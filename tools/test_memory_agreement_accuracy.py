"""Offline tests for paid-call boundaries, evidence grading and synthetic fixture integrity."""
import argparse
import contextlib
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import benchmark_memory_agreement_accuracy as probe


KEY = "synthetic-accuracy-secret"
CONTROLS = {"AssemblySha256": "A" * 64, "Parameters": {}, "OutputBudgetField": "max_tokens"}
PARSER_ASSEMBLY = os.environ.get("LIVINGNPCS_ACCURACY_TEST_ASSEMBLY")
PARSER_CAPTURE = os.environ.get("LIVINGNPCS_ACCURACY_TEST_CAPTURE")


class Response:
    status_code = 200
    headers = {"Content-Type": "application/json"}

    def __init__(self, payload):
        self.payload = payload

    def __enter__(self):
        return self

    def __exit__(self, *args):
        pass

    def json(self):
        return self.payload


class Session:
    def __init__(self, payloads):
        self.headers, self.posts, self.payloads = {}, [], list(payloads)

    def __enter__(self):
        return self

    def __exit__(self, *args):
        pass

    def post(self, endpoint, **kwargs):
        self.posts.append(kwargs["json"])
        return Response(self.payloads.pop(0))


class AccuracyTests(unittest.TestCase):
    def capture(self, folder, *, variant="candidate", fixture_hash=None):
        case = probe.read_json(probe.FIXTURES)["Cases"][0]
        row = {"Id": case["Id"], "System": "Synthetic system", "User": "Synthetic production capture stand-in for transport tests only.",
               "InputUnchanged": True, "UnresolvedNameTokens": 0, "SourceEvidence": []}
        row["PromptSha256"] = probe.sha(row["System"] + "\n" + row["User"])
        capture = {"SchemaVersion": 1, "SyntheticOnly": True, "Variant": variant,
                   "FixtureSha256": fixture_hash or probe.file_sha(probe.FIXTURES), "AssemblySha256": "A" * 64,
                   "AssetSha256": {"prompts": "B" * 64}, "Cases": [row]}
        path = folder / (variant + ".json")
        probe.write_json(path, capture)
        return path, case["Id"]

    def test_default_mode_never_reads_config_or_opens_network(self):
        with tempfile.TemporaryDirectory() as name:
            directory = Path(name)
            # This is intentionally not JSON. Reading it would fail the default-mode test.
            config = directory / "config.json"
            config.write_text("not a configuration", encoding="utf-8")
            with patch.object(probe.transport.requests, "Session", side_effect=AssertionError("network")), patch("sys.argv", [
                "probe", "--config", str(config), "--output-directory", str(directory / "prepared")
            ]), contextlib.redirect_stdout(io.StringIO()):
                probe.main()
            prepared = probe.read_json(directory / "prepared/prepared.json")
            self.assertFalse(prepared["ConfigRead"])
            self.assertEqual(0, prepared["NetworkCalls"])

    def test_fixture_mismatch_and_request_cap_reject_before_live_mode(self):
        with tempfile.TemporaryDirectory() as name:
            path, case_id = self.capture(Path(name), fixture_hash="0" * 64)
            with self.assertRaises(ValueError):
                probe.prepare(probe.FIXTURES, [path], [case_id], 20)
            with self.assertRaises(ValueError):
                probe.prepare(probe.FIXTURES, [], [case_id], 25)

    def test_actual_production_controls_override_probe_defaults(self):
        for parameters in ({}, {"thinking": {"type": "disabled"}}, {"thinking": {"type": "enabled"}, "reasoning_effort": "high"}):
            with self.subTest(parameters=parameters):
                session = Session([{}])
                configured = probe.ConfiguredSession(session, dict(CONTROLS, Parameters=parameters, OutputBudgetField="max_completion_tokens"))
                configured.post("https://synthetic.invalid", json={"max_tokens": 16000, "reasoning_effort": "none", "thinking": {"type": "enabled"}})
                self.assertEqual({"max_completion_tokens": 16000, **parameters}, session.posts[0])

    def test_production_validation_mode_never_reads_config_or_opens_network(self):
        with tempfile.TemporaryDirectory() as name:
            directory = Path(name)
            config = directory / "config.json"
            config.write_text("not a configuration", encoding="utf-8")
            with patch.object(probe.transport.requests, "Session", side_effect=AssertionError("network")), patch.object(
                probe, "validate_production"
            ) as validate, patch("sys.argv", [
                "probe", "--validate-production", "--config", str(config), "--report", str(directory / "report.json"),
                "--captures", str(directory / "capture.json"), "--assembly-directory", str(directory),
                "--output-directory", str(directory / "parsed")
            ]):
                probe.main()
            validate.assert_called_once_with(directory / "report.json", directory / "capture.json", directory, directory / "parsed", "pwsh")

    @unittest.skipUnless(PARSER_ASSEMBLY and PARSER_CAPTURE, "Set the two LIVINGNPCS_ACCURACY_TEST_* paths for real DLL integration.")
    def test_real_production_parsers_preserve_strict_acceptance_and_rejection(self):
        capture_path, assembly = Path(PARSER_CAPTURE), Path(PARSER_ASSEMBLY)
        capture = probe.read_json(capture_path)
        # These are synthetic parser probes, never provider calls or semantic accuracy samples.
        probes = [
            ("valid", "- 这是合成格式检查。\n% 好的。\n!LIVINGNPCS_META {\"complete\":true}", True, True, False),
            ("lowercase-marker", "- 已经取消了。\n!livingnpcs_meta {\"complete\":true}", True, False, False),
            ("trailing-content", "- 在下层。\n!LIVINGNPCS_META {\"complete\":true}\n多余说明", True, False, False),
            ("empty", "", False, False, False),
            ("invalid-json", "- 我记得。\n!LIVINGNPCS_META {\"complete\":true,}", True, False, False),
            ("recovered-prefix", "这是一条没有前缀的合成回复。\n!LIVINGNPCS_META {\"complete\":true}", True, True, False),
            ("hidden-option", "- 我没有这段记录。\n% LIVINGNPCS_META is hidden\n% 好吧。\n!LIVINGNPCS_META {\"complete\":true}", True, True, False),
            ("overlong", "- " + "x" * 210 + "\n!LIVINGNPCS_META {\"complete\":true}", False, False, False),
            ("incomplete", "- 木材已经交齐了。\n!LIVINGNPCS_META {}", True, False, False),
            ("hidden-tag", "- <think>synthetic marker probe</think>\n!LIVINGNPCS_META {\"complete\":true}", True, True, True),
            ("invalid-type", "- 格式检查。\n!LIVINGNPCS_META {\"complete\":true,\"rapportDelta\":\"wrong\"}", True, False, False),
        ]
        with tempfile.TemporaryDirectory(prefix="LivingNPCs-parser-integration-") as name:
            directory = Path(name)
            replies = []
            for index, (label, content, _, _, _) in enumerate(probes):
                row = capture["Cases"][index % len(capture["Cases"])]
                replies.append({"RequestId": capture["Variant"] + "/parser-probe-" + label, "CaseId": row["Id"],
                                "Variant": capture["Variant"], "AssemblySha256": capture["AssemblySha256"],
                                "PromptSha256": row["PromptSha256"],
                                "Transport": {"status": "ok", "content": content, "prompt_sha256": row["PromptSha256"].lower()}})
            report_path = directory / "synthetic-parser-probes.json"
            report = {"SchemaVersion": 1, "SyntheticOnly": True, "FixtureSha256": capture["FixtureSha256"], "Results": replies}
            probe.write_json(report_path, report)
            output = directory / "解析结果"
            stdout = io.StringIO()
            with contextlib.redirect_stdout(stdout):
                probe.validate_production(report_path, capture_path, assembly, output, "pwsh")
            summary = json.loads(stdout.getvalue())
            self.assertEqual(str(output / (capture["Variant"] + "-parser-validation.json")), summary["OutputPath"])
            validation = probe.read_json(output / (capture["Variant"] + "-parser-validation.json"))
            self.assertEqual(capture["AssemblySha256"], validation["AssemblySha256"])
            self.assertEqual(0, validation["NetworkCalls"])
            self.assertFalse(validation["ClassifierFallbackInvoked"])
            self.assertFalse(validation["GameActionsExecuted"])
            self.assertEqual(len(probes), len(validation["Results"]))
            for parsed, (label, _, body_ok, inline_ok, leaked) in zip(validation["Results"], probes):
                with self.subTest(probe=label):
                    self.assertEqual(body_ok, parsed["BodyParser"]["Success"])
                    self.assertEqual(inline_ok, parsed["InlineMetadata"]["Success"])
                    self.assertEqual(leaked, bool(parsed["VisibleHiddenMarkerLeaks"]))
                    self.assertEqual(body_ok and inline_ok and not leaked, parsed["StrictPipelineParseSuccess"])
                    self.assertTrue(parsed["ParserInputsUnchanged"])
                    if not body_ok:
                        self.assertTrue(parsed["BodyParser"]["FailureReason"])
                    if not inline_ok:
                        self.assertTrue(parsed["InlineMetadata"]["FailureReason"])
            self.assertFalse(validation["Results"][5]["BodyParser"]["RawStartsWithDialoguePrefix"])
            self.assertEqual(["好吧。"], validation["Results"][6]["BodyParser"]["Options"])
            for field in ("AssemblySha256", "prompt_sha256"):
                with self.subTest(tampered_hash=field):
                    tampered = json.loads(json.dumps(report))
                    target = tampered["Results"][0] if field == "AssemblySha256" else tampered["Results"][0]["Transport"]
                    target[field] = "0" * 64
                    tampered_path, rejected_output = directory / (field + ".json"), directory / ("rejected-" + field)
                    probe.write_json(tampered_path, tampered)
                    with self.assertRaisesRegex(ValueError, "Offline production parser validation failed"):
                        probe.validate_production(tampered_path, capture_path, assembly, rejected_output, "pwsh")
                    self.assertFalse(rejected_output.exists())

    def test_denied_advisory_word_is_never_a_fact_pass_or_failure(self):
        definition = {"NoNewActions": True, "AdvisoryTerms": ["torn"]}
        result = {"content": '- I do not remember any torn pages.\n!LIVINGNPCS_META {"complete":true}'}
        inspected = probe.inspect_reply(result, definition)
        self.assertEqual(["torn"], inspected["AdvisoryMentions"])
        self.assertEqual([], inspected["ObjectiveFactualFailures"])
        self.assertEqual("unreviewed", inspected["AccuracyVerdict"])
        self.assertFalse(inspected["ProductionParserValidated"])

    def test_structured_unauthorized_action_is_objective_failure(self):
        content = '- That was cancelled.\n!LIVINGNPCS_META {"complete":true,"actions":[{"type":"companion_outing"}]}'
        inspected = probe.inspect_reply({"content": content}, {"NoNewActions": True, "AdvisoryTerms": []})
        self.assertIn("unexpected_new_effect:actions", inspected["ObjectiveFactualFailures"])
        self.assertEqual("fail", inspected["AccuracyVerdict"])

    def test_live_failure_stops_and_never_persists_key_or_truth_in_request(self):
        with tempfile.TemporaryDirectory() as name:
            directory = Path(name)
            first, case_id = self.capture(directory, variant="baseline")
            second, _ = self.capture(directory, variant="candidate")
            suite, jobs = probe.prepare(probe.FIXTURES, [first, second], [case_id], 20)
            config = directory / "config.json"
            probe.write_json(config, {"Provider": "OpenAiCompatible", "ModelName": "synthetic-model",
                                     "ServerAddress": "https://synthetic.invalid/v1", "ApiKey": KEY,
                                     "ChatThinkingLevel": "Auto", "UseStreamingDialogueTransport": False})
            args = argparse.Namespace(config=config, assembly_directory=directory, pwsh="pwsh", timeout=2, fixtures=probe.FIXTURES)
            session = Session([{"error": {"message": "Provider echoed " + KEY}}])
            output = directory / "results"
            output.mkdir()
            stdout = io.StringIO()
            with patch.object(probe, "production_controls", return_value=CONTROLS), patch.object(
                probe.transport.requests, "Session", return_value=session
            ), contextlib.redirect_stdout(stdout):
                probe.run(args, suite, jobs, output)
            self.assertEqual(1, len(session.posts))
            self.assertNotIn(KEY, stdout.getvalue())
            saved = (output / "run-report.json").read_text(encoding="utf-8")
            self.assertNotIn(KEY, saved)
            self.assertNotIn("synthetic.invalid", saved)
            sent = json.dumps(session.posts[0], ensure_ascii=False)
            self.assertNotIn("GroundTruth", sent)
            self.assertNotIn(jobs[0]["Definition"]["Criteria"][0]["PassCondition"], sent)

    def test_fixture_source_references_are_complete_and_unique(self):
        suite = probe.read_json(probe.FIXTURES)
        initial_path = probe.FIXTURES.with_name("memory_agreement_accuracy.initial.json")
        self.assertEqual("8EBD702B9519536E97D6BF7F8EC1AF6C72496DBA817A67062124DD1BE25733CE", probe.file_sha(initial_path))
        initial = probe.read_json(initial_path)
        self.assertEqual(10, len(initial["Cases"]))
        self.assertEqual(initial["Defaults"], suite["Defaults"])
        self.assertEqual(11, len(suite["Cases"]))
        self.assertEqual(11, len({case["Id"] for case in suite["Cases"]}))
        current = {case["Id"]: case for case in suite["Cases"]}
        self.assertEqual({case["Id"] for case in initial["Cases"]} | {"current-dated-reschedule"}, set(current))
        for original in initial["Cases"]:
            revised = current[original["Id"]]
            if original["Id"] in {"current-pronoun-reschedule", "two-independent-agreements"}:
                # Only evaluator wording changes: do not leak the corrected oracle into dialogue.
                for key in set(original) - {"Title", "GroundTruth", "Criteria"}:
                    self.assertEqual(original[key], revised[key], (original["Id"], key))
            else:
                self.assertEqual(original, revised, original["Id"])
        dated = current["current-dated-reschedule"]
        dated_source = "\n".join(block.get("Player", "") + "\n" + block.get("Npc", "") for block in dated["Conversation"]) + dated["Query"]
        self.assertIn("第2年夏季16日", dated_source)
        self.assertNotIn("17", dated_source)
        self.assertNotIn("后天", dated_source)
        controls = probe.read_json(probe.FIXTURES.with_name("memory_agreement_accuracy.controls.json"))
        self.assertTrue(controls["SyntheticOnly"])
        self.assertEqual(2, len(controls["Cases"]))
        self.assertEqual(2, len({case["Id"] for case in controls["Cases"]}))
        self.assertFalse(set(current) & {case["Id"] for case in controls["Cases"]})
        for case in suite["Cases"] + controls["Cases"]:
            sources = ["current-player"]
            for block in case["Conversation"]:
                if "Filler" not in block:
                    sources += [block["Id"] + "-player", block["Id"] + "-npc"]
            sources += [turn["Id"] for record in case["History"] for turn in record["Turns"]]
            sources += ["state:" + entry["AuditId"] for entries in case["State"].values() for entry in entries]
            self.assertEqual(len(sources), len(set(sources)), case["Id"])
            referenced = case["RequiredEvidence"] + case.get("ExpectedOmissions", []) + [source for item in case["GroundTruth"] + case["Criteria"] for source in item["Sources"]]
            self.assertFalse(set(referenced) - set(sources), case["Id"])
            self.assertGreaterEqual(len(case["Criteria"]), 2)
        with tempfile.TemporaryDirectory() as name:
            capture_path, _ = self.capture(Path(name))
            capture = probe.read_json(capture_path)
            template = capture["Cases"][0]
            capture["Cases"] = [dict(template, Id=case["Id"]) for case in suite["Cases"]]
            probe.write_json(capture_path, capture, update=True)
            _, jobs = probe.prepare(probe.FIXTURES, [capture_path], None, 20)
            self.assertEqual(11, len(jobs))

    def test_review_needs_exact_reply_citations_and_every_criterion(self):
        with tempfile.TemporaryDirectory() as name:
            directory = Path(name)
            capture, case_id = self.capture(directory)
            _, jobs = probe.prepare(probe.FIXTURES, [capture], [case_id], 20)
            job = jobs[0]
            result = {"status": "ok", "content": '- 比原定时间往后一天，具体日期和钟点还需要确认。\n!LIVINGNPCS_META {"complete":true}'}
            template = probe.review_item(job, result)
            report = {"ReviewTemplate": {"Items": [template]}, "Results": [{"RequestId": job["RequestId"],
                      "Transport": result, "Inspection": probe.inspect_reply(result, job["Definition"])}]}
            report_path = directory / "report.json"
            probe.write_json(report_path, report)
            review = json.loads(json.dumps(template))
            review["Reviewer"] = "offline test reviewer"
            for item in review["Criteria"]:
                item.update(Verdict="pass", ResponseQuotes=[""], Reason="The quoted reply was checked against the stated evidence.")
            review_path = directory / "review.json"
            probe.write_json(review_path, {"Items": [review]})
            with self.assertRaises(ValueError):
                probe.apply_review(report_path, review_path, directory)
            for item in review["Criteria"]:
                item["ResponseQuotes"] = ["比原定时间往后一天，具体日期和钟点还需要确认。"]
            for quotes in ("天一", [1], ["天一"], [" "]):
                with self.subTest(invalid_response_quotes=quotes):
                    tampered = json.loads(json.dumps(review))
                    tampered["Criteria"][0]["ResponseQuotes"] = quotes
                    probe.write_json(review_path, {"Items": [tampered]}, update=True)
                    with self.assertRaisesRegex(ValueError, "nonempty literal reply quotes"):
                        probe.apply_review(report_path, review_path, directory)
            for field, value in (("PassCondition", "Changed scoring condition."), ("FailureExamples", ["Changed example."]),
                                 ("Severity", "advisory"), ("Sources", ["different-source"]), ("UnexpectedRubricField", "added")):
                with self.subTest(changed_rubric_field=field):
                    tampered = json.loads(json.dumps(review))
                    tampered["Criteria"][0][field] = value
                    probe.write_json(review_path, {"Items": [tampered]}, update=True)
                    with self.assertRaisesRegex(ValueError, "original fixture rubric"):
                        probe.apply_review(report_path, review_path, directory)
            with self.subTest(removed_rubric_field="PassCondition"):
                tampered = json.loads(json.dumps(review))
                del tampered["Criteria"][0]["PassCondition"]
                probe.write_json(review_path, {"Items": [tampered]}, update=True)
                with self.assertRaisesRegex(ValueError, "original fixture rubric"):
                    probe.apply_review(report_path, review_path, directory)
            for tampered in ({"Items": [dict(review, ExtraReviewField="added")]}, {"Items": [review], "ExtraReviewField": "added"}):
                with self.subTest(added_review_metadata=sorted(tampered)):
                    probe.write_json(review_path, tampered, update=True)
                    with self.assertRaisesRegex(ValueError, "original template"):
                        probe.apply_review(report_path, review_path, directory)
            probe.write_json(review_path, {"Items": [review]}, update=True)
            with contextlib.redirect_stdout(io.StringIO()):
                probe.apply_review(report_path, review_path, directory)
            reviewed = probe.read_json(directory / "reviewed-report.json")
            self.assertEqual("pass", reviewed["Results"][0]["Inspection"]["AccuracyVerdict"])

    def test_empty_provider_reply_can_only_be_reviewed_as_uncertain(self):
        with tempfile.TemporaryDirectory() as name:
            directory = Path(name)
            capture, case_id = self.capture(directory)
            _, jobs = probe.prepare(probe.FIXTURES, [capture], [case_id], 20)
            job, result = jobs[0], {"status": "error", "content": ""}
            template = probe.review_item(job, result)
            report = {"ReviewTemplate": {"Items": [template]}, "Results": [{"RequestId": job["RequestId"],
                      "Transport": result, "Inspection": probe.inspect_reply(result, job["Definition"])}]}
            report_path, review_path = directory / "report.json", directory / "review.json"
            probe.write_json(report_path, report)
            review = json.loads(json.dumps(template))
            review["Reviewer"] = "offline test reviewer"
            for item in review["Criteria"]:
                item.update(Verdict="uncertain", ResponseQuotes=[], Reason="The provider returned no reply.")
            probe.write_json(review_path, {"Items": [review]})
            with contextlib.redirect_stdout(io.StringIO()):
                probe.apply_review(report_path, review_path, directory)
            self.assertEqual("uncertain", probe.read_json(directory / "reviewed-report.json")["Results"][0]["Inspection"]["AccuracyVerdict"])


if __name__ == "__main__":
    unittest.main()
