"""Bounded synthetic evidence-grounding evaluation; preparation is offline by default.

Prepare: python tools/benchmark_memory_agreement_accuracy.py --captures BASE.json NEW.json
Run: add --run --config PATH --assembly-directory ISOLATED_BUILD
Review: --apply-review REVIEW.json --report RUN.json
Production parser validation (offline): --validate-production --report RUN.json
  --captures ONE_CAPTURE.json --assembly-directory MATCHING_ISOLATED_BUILD

Only captured production System/User strings reach the provider. Ground truth and review
criteria remain local. No automatic judging call, retry, deployment or player-data import.
Replies, parsed metadata and evidence review templates are retained in Temp for inspection.
"""
from __future__ import annotations

import argparse
from copy import deepcopy
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile
from typing import Any

import benchmark_dialogue_latency as transport


ROOT = Path(__file__).resolve().parent
FIXTURES = ROOT / "fixtures" / "memory_agreement_accuracy.json"
EXPORTER = ROOT / "export_memory_agreement_accuracy.ps1"
MARKER = re.compile(r"!+LIVINGNPCS_META\s*", re.I)


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest().upper()


def file_sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def output_directory(path: Path | None) -> Path:
    result = path.resolve() if path else Path(tempfile.mkdtemp(prefix="LivingNPCs-memory-accuracy-"))
    if not result.is_relative_to(Path(tempfile.gettempdir()).resolve()):
        raise ValueError("Raw synthetic review artifacts must stay under Temp.")
    result.mkdir(parents=True, exist_ok=True)
    return result


def write_json(path: Path, value: object, *, update: bool = False) -> None:
    if path.exists() and not update:
        raise ValueError("Choose a new output directory; prior artifacts are not overwritten.")
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def prepare(fixtures: Path, captures: list[Path], selected: list[str] | None, maximum: int) -> tuple[dict, list[dict]]:
    suite = read_json(fixtures)
    if suite.get("SchemaVersion") != 1 or suite.get("SyntheticOnly") is not True:
        raise ValueError("Expected synthetic accuracy fixtures, schema 1.")
    definitions = {case["Id"]: case for case in suite["Cases"]}
    wanted = selected or list(definitions)
    if len(set(wanted)) != len(wanted) or set(wanted) - definitions.keys():
        raise ValueError("Case IDs must be known and unique.")
    if not 1 <= maximum <= 24:
        raise ValueError("Use a request cap between 1 and 24.")
    loaded = [read_json(path) for path in captures]
    if len(loaded) > 2 or len({capture["Variant"] for capture in loaded}) != len(loaded):
        raise ValueError("Use at most two captures with distinct variant names.")
    for capture in loaded:
        if capture.get("SyntheticOnly") is not True or capture.get("SchemaVersion") != 1:
            raise ValueError("Only synthetic production captures, schema 1, are accepted.")
        if capture["FixtureSha256"].upper() != file_sha(fixtures):
            raise ValueError("Capture and evaluator must use identical fixture bytes.")
        if capture["AssetSha256"] != loaded[0]["AssetSha256"]:
            raise ValueError("Baseline/candidate assets differ; recapture with identical assets.")
    jobs = []
    for index, case_id in enumerate(wanted):
        # Alternate pair order to avoid consistently favoring one variant by request order.
        for capture in (loaded if index % 2 == 0 else list(reversed(loaded))):
            found = [row for row in capture["Cases"] if row["Id"] == case_id]
            if len(found) != 1:
                raise ValueError("Each capture must contain exactly one row per selected case.")
            row = found[0]
            if sha(row["System"] + "\n" + row["User"]) != row["PromptSha256"].upper():
                raise ValueError("Captured prompt hash mismatch.")
            if not row["InputUnchanged"] or row["UnresolvedNameTokens"]:
                raise ValueError("Capture changed its input or has unresolved name templates.")
            if any(not value for value in row.get("Checks", {}).values()) or not 1 <= row.get("MaxTokens", 16000) <= 16000:
                raise ValueError("Capture failed production accounting/budget checks.")
            jobs.append({"RequestId": capture["Variant"] + "/" + case_id, "CaseId": case_id,
                         "Variant": capture["Variant"], "AssemblySha256": capture["AssemblySha256"],
                         "Capture": row, "Definition": definitions[case_id]})
    if len(jobs) > maximum:
        raise ValueError("Planned requests exceed --max-requests; select fewer cases or raise the bounded cap.")
    return suite, jobs


def production_controls(directory: Path, model: str, level: str, pwsh: str) -> dict:
    completed = subprocess.run([pwsh, "-NoLogo", "-NoProfile", "-NonInteractive", "-File", str(EXPORTER),
                                "-AssemblyDirectory", str(directory), "-TransportOnly",
                                "-TransportModelName", model, "-TransportThinkingLevel", level],
                               capture_output=True, text=True, encoding="utf-8", timeout=30,
                               creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    if completed.returncode:
        # Do not forward arbitrary process text which might contain supplied configuration.
        raise ValueError("Production transport-profile extraction failed; no request sent.")
    controls = json.loads(completed.stdout)
    if controls["OutputBudgetField"] not in ("max_tokens", "max_completion_tokens"):
        raise ValueError("Unsupported production output-budget field.")
    if set(controls["Parameters"]) - {"thinking", "reasoning_effort"}:
        raise ValueError("Unexpected fields in production transport profile.")
    return controls


def validate_production(report: Path, capture: Path, directory: Path, output: Path, pwsh: str) -> None:
    """Invoke real production parsers without any provider session or classifier fallback."""
    completed = subprocess.run([pwsh, "-NoLogo", "-NoProfile", "-NonInteractive", "-File", str(EXPORTER),
                                "-AssemblyDirectory", str(directory), "-ValidateReportPath", str(report),
                                "-CapturePath", str(capture), "-OutputDirectory", str(output)],
                               capture_output=True, text=True, encoding="utf-8", timeout=30,
                               creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    if completed.returncode:
        raise ValueError("Offline production parser validation failed; no request sent.")
    summary = json.loads(completed.stdout)
    print(json.dumps({key: summary[key] for key in (
        "Status", "Replies", "BodyPassed", "InlinePassed", "StrictPipelinePassed", "NetworkCalls", "OutputPath"
    )}, ensure_ascii=False))


class ConfiguredSession:
    """Reuse the tested streaming/redaction collector with exact production thinking fields."""
    def __init__(self, session: Any, controls: dict):
        self.session, self.controls, self.headers = session, controls, session.headers

    def post(self, endpoint: str, *, json: dict, **kwargs: Any) -> Any:
        body = dict(json)
        budget = body.pop("max_tokens", body.pop("max_completion_tokens", 16000))
        body.pop("reasoning_effort", None)
        body.pop("thinking", None)
        body[self.controls["OutputBudgetField"]] = budget
        body.update(deepcopy(self.controls["Parameters"]))
        return self.session.post(endpoint, json=body, **kwargs)


def inspect_reply(result: dict, definition: dict) -> dict:
    """Only structural violations can fail automatically; word matches are review pointers."""
    raw = result.get("content", "")
    matches = list(MARKER.finditer(raw))
    visible, metadata = raw, None
    format_failures, factual_failures = [], []
    if len(matches) != 1:
        format_failures.append("expected_exactly_one_final_metadata_object")
    else:
        visible = raw[:matches[0].start()].strip()
        try:
            metadata = json.loads(raw[matches[0].end():].strip())
            if not isinstance(metadata, dict):
                metadata = None
                format_failures.append("metadata_is_not_an_object")
        except (ValueError, TypeError):
            format_failures.append("metadata_invalid_or_not_final")
    lines = [line.strip() for line in visible.splitlines() if line.strip()]
    dialogue = [line[1:].strip() for line in lines if line.startswith("-")]
    if len(dialogue) != 1 or any(not line.startswith(("-", "%")) for line in lines):
        format_failures.append("noncanonical_dialogue_or_options")
    if metadata is not None:
        if metadata.get("complete") is not True:
            format_failures.append("metadata_not_certified_complete_no_recovery_call_made")
        if definition["NoNewActions"]:
            for key in ("actions", "helpRequests", "helpRequestUpdates"):
                effects = metadata.get(key)
                if effects is not None and not isinstance(effects, list):
                    format_failures.append(key + "_must_be_an_array")
                elif effects:
                    factual_failures.append("unexpected_new_effect:" + key)
            travel = metadata.get("travelDecision")
            if isinstance(travel, dict) and travel.get("consent") == "accepted_now":
                factual_failures.append("unsupported_immediate_travel_consent")
    return {"VisibleReply": visible, "Dialogue": dialogue, "Metadata": metadata,
            "FormatFailures": format_failures, "ObjectiveFactualFailures": factual_failures,
            "ProductionParserValidated": False,
            "AdvisoryMentions": [word for word in definition["AdvisoryTerms"] if word.casefold() in visible.casefold()],
            "AccuracyVerdict": "fail" if factual_failures else "unreviewed",
            "Policy": "Python envelope inspection does not establish production parser acceptance. Advisory mentions never pass or fail facts; denial and quotations require semantic review."}


def review_item(job: dict, result: dict) -> dict:
    definition = job["Definition"]
    criteria = [dict(item, Verdict="unreviewed", ResponseQuotes=[], Reason="") for item in definition["Criteria"]]
    criteria.append({"Id": "whole-reply-grounded", "PassCondition": "Every fact/commitment asserted in dialogue, player options or metadata (including proposed memories) is supported; no invented events, details, motives or actor reversals.",
                     "Sources": sorted({source for item in definition["GroundTruth"] for source in item["Sources"]}),
                     "Severity": "critical", "Verdict": "unreviewed", "ResponseQuotes": [], "Reason": ""})
    return {"RequestId": job["RequestId"], "ReplySha256": sha(result.get("content", "")),
            "Reviewer": "", "Criteria": criteria}


def apply_review(report_path: Path, review_path: Path, output: Path) -> None:
    report, review = read_json(report_path), read_json(review_path)
    if {key: value for key, value in review.items() if key != "Items"} != {
        key: value for key, value in report["ReviewTemplate"].items() if key != "Items"
    }:
        raise ValueError("Review metadata must match its original template.")
    expected = {item["RequestId"]: item for item in report["ReviewTemplate"]["Items"]}
    rows = {item["RequestId"]: item for item in report["Results"]}
    items = review.get("Items", [])
    if len({item["RequestId"] for item in items}) != len(items) or set(rows) != {item["RequestId"] for item in items}:
        raise ValueError("Review must contain each recorded request exactly once.")
    for item in items:
        row = rows[item["RequestId"]]
        template = expected[item["RequestId"]]
        if {key: value for key, value in item.items() if key not in ("Reviewer", "Criteria")} != {
            key: value for key, value in template.items() if key not in ("Reviewer", "Criteria")
        }:
            raise ValueError("Review request metadata must match its original template.")
        if not item.get("Reviewer") or item["ReplySha256"] != sha(row["Transport"].get("content", "")):
            raise ValueError("Review needs a reviewer and must match the exact reply hash.")
        original = {criterion["Id"]: criterion for criterion in template["Criteria"]}
        if len(item["Criteria"]) != len(original) or {c["Id"] for c in item["Criteria"]} != set(original):
            raise ValueError("Review criteria may not be dropped or duplicated.")
        for criterion in item["Criteria"]:
            editable = ("Verdict", "ResponseQuotes", "Reason")
            if {key: value for key, value in criterion.items() if key not in editable} != {
                key: value for key, value in original[criterion["Id"]].items() if key not in editable
            }:
                raise ValueError("Review criteria and source references must match the original fixture rubric.")
            if criterion["Verdict"] not in ("pass", "fail", "uncertain") or not criterion.get("Reason"):
                raise ValueError("Each criterion needs a reason and pass/fail/uncertain verdict.")
            no_reply = not row["Transport"].get("content", "") and row["Transport"]["status"] != "ok"
            quotes = criterion.get("ResponseQuotes", [])
            if no_reply and criterion["Verdict"] != "uncertain":
                raise ValueError("A missing provider reply can only receive an uncertain review.")
            if (not isinstance(quotes, list) or (not no_reply and not quotes)
                    or any(not isinstance(quote, str) or not quote.strip()
                           or quote not in row["Transport"].get("content", "") for quote in quotes)):
                raise ValueError("Each review criterion must cite a list of nonempty literal reply quotes.")
        verdicts = [criterion["Verdict"] for criterion in item["Criteria"]]
        final = "fail" if "fail" in verdicts or row["Inspection"]["ObjectiveFactualFailures"] else (
            "pass" if all(value == "pass" for value in verdicts) and row["Transport"]["status"] == "ok" else "uncertain")
        row["EvidenceReview"], row["Inspection"]["AccuracyVerdict"] = item, final
    report["ReviewCompleted"] = True
    write_json(output / "reviewed-report.json", report)
    print(json.dumps({"status": "review applied", "results": len(rows), "output": str(output)}, ensure_ascii=False))


def run(args: argparse.Namespace, suite: dict, jobs: list[dict], output: Path) -> None:
    if not jobs or args.config is None or args.assembly_directory is None:
        raise ValueError("Live mode requires captured jobs, --config and --assembly-directory.")
    # Nothing is persisted from config. In particular, never attach config to a report/error.
    config = read_json(args.config)
    provider = str(config.get("Provider", "")).lower()
    if provider not in ("openaicompatible", "openai", "deepseek"):
        raise ValueError("This runner supports OpenAI-compatible/OpenAI/DeepSeek configurations only.")
    model = str(config.get("ModelName", "")).strip()
    if not model:
        raise ValueError("The configured model name is required.")
    controls = production_controls(args.assembly_directory, model, str(config.get("ChatThinkingLevel", "Auto")), args.pwsh)
    address = "https://api.deepseek.com" if provider == "deepseek" else (
        "https://api.openai.com" if provider == "openai" else str(config.get("ServerAddress") or "https://api.openai.com"))
    endpoint = transport.chat_endpoint(address)
    timeout = args.timeout or int(config.get("QueryTimeout", 85))
    if not 2 <= timeout <= 120:
        raise ValueError("Use a timeout between 2 and 120 seconds.")
    stream = bool(config.get("UseStreamingDialogueTransport", False))
    report = {"SchemaVersion": 1, "SyntheticOnly": True, "FixtureSha256": file_sha(args.fixtures),
              "PlannedRequests": len(jobs), "TransportProfile": controls, "ConfiguredModelSha256": sha(model),
              "ReviewCompleted": False, "Results": [], "ReviewTemplate": {"Items": []},
              "Policy": "No automatic fact pass. Use literal reply citations and source-linked semantic review. No automatic retry or judge call."}
    path = output / "run-report.json"
    if path.exists() or (output / "review.json").exists():
        raise ValueError("Use a new output directory for this run.")
    with transport.requests.Session() as session:
        key = str(config.get("ApiKey", "")).strip()
        if key:
            session.headers["Authorization"] = "Bearer " + key
        configured = ConfiguredSession(session, controls)
        for job in jobs:
            capture = job["Capture"]
            request = {"name": job["RequestId"], "system": capture["System"], "user": capture["User"],
                       "effort": controls["Parameters"].get("reasoning_effort"), "max_tokens": capture.get("MaxTokens", 16000), "stream": stream,
                       "thinking_type": controls["Parameters"].get("thinking", {}).get("type")}
            result = transport.call(configured, endpoint, model, request, timeout)
            result["output_budget_field"] = controls["OutputBudgetField"]
            row = {"RequestId": job["RequestId"], "CaseId": job["CaseId"], "Variant": job["Variant"],
                   "AssemblySha256": job["AssemblySha256"], "PromptSha256": capture["PromptSha256"],
                   "SourceEvidence": capture["SourceEvidence"], "GroundTruth": job["Definition"]["GroundTruth"],
                   "Transport": result, "Inspection": inspect_reply(result, job["Definition"])}
            report["Results"].append(row)
            report["ReviewTemplate"]["Items"].append(review_item(job, result))
            write_json(path, report, update=True)
            write_json(output / "review.json", report["ReviewTemplate"], update=True)
            print(json.dumps({"request": len(report["Results"]), "id": job["RequestId"], "status": result["status"],
                              "error_type": result.get("error_type")}, ensure_ascii=False), flush=True)
            if result["status"] != "ok":
                # No retry, fallback classifier, or second paid request after a transport failure.
                break
    print(json.dumps({"status": "live collection finished; production parser validation and semantic review required", "recorded": len(report["Results"]),
                      "output": str(output)}, ensure_ascii=False))


def main() -> None:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fixtures", type=Path, default=FIXTURES)
    parser.add_argument("--captures", type=Path, nargs="*", default=[])
    parser.add_argument("--cases", nargs="*")
    parser.add_argument("--max-requests", type=int, default=20)
    parser.add_argument("--output-directory", type=Path)
    parser.add_argument("--run", action="store_true")
    parser.add_argument("--config", type=Path)
    parser.add_argument("--assembly-directory", type=Path)
    parser.add_argument("--pwsh", default="pwsh")
    parser.add_argument("--timeout", type=int)
    parser.add_argument("--apply-review", type=Path)
    parser.add_argument("--validate-production", action="store_true")
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    output = output_directory(args.output_directory)
    if args.validate_production:
        if args.run or args.apply_review or args.report is None or args.assembly_directory is None or len(args.captures) != 1:
            raise ValueError("Production validation is offline and requires --report, one --captures path and --assembly-directory.")
        validate_production(args.report, args.captures[0], args.assembly_directory, output, args.pwsh)
        return
    if args.apply_review:
        if args.run or args.report is None:
            raise ValueError("Review application is offline and requires --report.")
        apply_review(args.report, args.apply_review, output)
        return
    suite, jobs = prepare(args.fixtures, args.captures, args.cases, args.max_requests)
    if args.run:
        run(args, suite, jobs, output)
    else:
        plan = {"SyntheticOnly": True, "Mode": "offline preparation", "NetworkCalls": 0, "ConfigRead": False,
                "CaseIds": args.cases or [case["Id"] for case in suite["Cases"]], "PlannedRequests": len(jobs),
                "Order": [job["RequestId"] for job in jobs], "FixtureSha256": file_sha(args.fixtures),
                "GroundTruthAndCriteria": suite["Cases"], "Policy": "Evidence presence is not a factual pass; replies require source-linked semantic review."}
        write_json(output / "prepared.json", plan)
        print(json.dumps({key: plan[key] for key in ("Mode", "NetworkCalls", "ConfigRead", "CaseIds", "PlannedRequests")}, ensure_ascii=False))
        print(json.dumps({"output": str(output)}, ensure_ascii=False))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, KeyError, TypeError, subprocess.SubprocessError) as error:
        # Avoid echoing malformed configuration values, URLs, keys or arbitrary process output.
        raise SystemExit("Accuracy evaluation stopped before further requests (" + type(error).__name__ + ").") from None
