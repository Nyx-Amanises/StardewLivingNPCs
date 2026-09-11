"""Bounded, opt-in live latency probe using the model in a local mod config.

Reads credentials only in memory. Reports omit the endpoint, key, and hidden
reasoning. Fixtures are synthetic: never replay player logs implicitly.
Example:
  python tools/benchmark_dialogue_latency.py --config PATH --output REPORT.json
  python tools/benchmark_dialogue_latency.py --config PATH --fixtures CASES.json --output REPORT.json
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import time
import unicodedata
from datetime import datetime, timezone

import requests


def chat_endpoint(address: str) -> str:
    address = address.strip().rstrip("/")
    if address.lower().endswith("/chat/completions"):
        return address
    if address.lower().endswith("/v1"):
        return address + "/chat/completions"
    return address + "/v1/chat/completions"


def screening_cases() -> list[dict]:
    system = (
        "You write Chinese dialogue for the Stardew Valley villager Haley. "
        "Keep her recognizable: interested in photography and fashion, initially "
        "aloof but capable of warmth. Use only supplied facts. "
        "Output one short NPC line starting with '- ', then two short possible "
        "player replies on separate lines starting with '%'. No analysis or JSON."
    )
    prompt = (
        "Synthetic benchmark, not a real save. Year 1, spring 24, sunny, 14:00, "
        "Town. Farmer: 小雪. Friendship: 2 hearts. No promises, conflict, outing, "
        "active help request, or gift opportunity. Player says: "
        "今天阳光真好，你准备去哪里拍照？ Reply naturally in about 40-70 Chinese characters."
    )
    # ABBA order reduces the effect of a warming connection/cache or load drift.
    return [
        {"name": "short-dialogue", "effort": effort, "system": system,
         "user": prompt, "stream": True, "max_tokens": 2048}
        for effort in ("low", "none", "none", "low")
    ]


def call(session: requests.Session, endpoint: str, model: str,
         case: dict, timeout: int) -> dict:
    thinking_type = case.get("thinking_type")
    if thinking_type not in (None, "enabled", "disabled"):
        raise ValueError("thinking_type must be enabled or disabled when supplied.")
    token_field = "max_completion_tokens" if (
        "gpt-5" in model.lower() or "gpt5" in model.lower()
        or any(len(part) > 1 and part[0].lower() == "o" and part[1].isdigit()
               for part in model.split("/"))
    ) else "max_tokens"
    body = {
        "model": model,
        "messages": [{"role": "system", "content": case["system"]},
                     {"role": "user", "content": case["user"]}],
        token_field: case.get("max_tokens", 2048),
        "reasoning_effort": case["effort"],
    }
    # A fixture may carry the explicit switch exported from production DeepSeek settings.
    # Keep it separate from effort: omitting a disabled switch can re-enable thinking.
    if thinking_type is not None:
        body["thinking"] = {"type": thinking_type}
        if thinking_type == "disabled":
            body.pop("reasoning_effort")
    stream = case.get("stream", False)
    if stream:
        body.update(stream=True, stream_options={"include_usage": True})
    if case.get("json_mode"):
        body["response_format"] = {"type": "json_object"}
    result = {
        "name": case["name"], "effort": case["effort"], "stream": stream,
        "thinking_type": thinking_type,
        "max_output_tokens": body[token_field], "output_budget_field": token_field,
        "prompt_chars": len(case["system"]) + len(case["user"]),
        "prompt_sha256": hashlib.sha256(
            (case["system"] + "\n" + case["user"]).encode("utf-8")).hexdigest(),
        "started_utc": datetime.now(timezone.utc).isoformat(),
        "usage": {}, "content": "", "first_content_ms": None,
        "finish_reason": None, "status": "error", "received_done": False,
        "event_count": 0, "response_keys": [],
    }
    started = time.perf_counter()

    def redact(value: str) -> str:
        auth = session.headers.get("Authorization", "").removeprefix("Bearer ")
        if auth:
            value = value.replace(auth, "[credential]")
        value = value.replace(endpoint, "[endpoint]")
        value = re.sub(r'https?://[^\s"<>]+', "[endpoint]", value, flags=re.I)
        return re.sub(r'\bsk-[A-Za-z0-9_-]+', "[credential]", value, flags=re.I)

    def safe_keys(value: dict) -> list[str]:
        return sorted({redact(str(key))[:80] for key in value})[:25]

    def sanitize(value: object) -> object:
        # Apply the same protection to echoed model names, usage, content and keys.
        if isinstance(value, str):
            return redact(value)
        if isinstance(value, dict):
            return {redact(str(key))[:80]: sanitize(item) for key, item in value.items()}
        if isinstance(value, list):
            return [sanitize(item) for item in value]
        return value

    def record_error(error: object) -> None:
        # Some compatible gateways return an error envelope inside HTTP 200 or SSE.
        # Record only a bounded, redacted explanation, never the whole response.
        result["error_type"] = "provider-error"
        message = str(error.get("message", "")) if isinstance(error, dict) else str(error)
        result["error_message"] = redact(message)[:400]

    def invalid_response() -> None:
        # Preserve an earlier provider error if malformed chunks follow it.
        result.setdefault("error_type", "invalid-response")

    def consume(data: object, streamed: bool) -> None:
        result["event_count"] += 1
        if not isinstance(data, dict):
            invalid_response()
            return
        result["response_keys"] = sorted(set(result["response_keys"]) | set(safe_keys(data)))[:25]
        if data.get("error") is not None and data["error"] is not False:
            record_error(data["error"])
            return
        if isinstance(data.get("usage"), dict) and data["usage"]:
            result["usage"] = data["usage"]
        if isinstance(data.get("model"), str) and data["model"]:
            result["resolved_model"] = data["model"]
        choices = data.get("choices")
        if choices is None:
            return
        if not isinstance(choices, list):
            invalid_response()
            return
        if not choices:
            return
        choice = choices[0]
        if not isinstance(choice, dict):
            invalid_response()
            return
        message = choice.get("delta" if streamed else "message")
        if message is None:
            message = {}
        if not isinstance(message, dict):
            invalid_response()
            return
        content = message.get("content") or choice.get("text") or ""
        if not isinstance(content, str):
            invalid_response()
            return
        if isinstance(content, str) and content:
            if result["first_content_ms"] is None and content.strip():
                result["first_content_ms"] = round((time.perf_counter() - started) * 1000)
            result["content"] += content
        elif not streamed:
            result["empty_content_shape"] = {
                "response_keys": safe_keys(data), "choice_keys": safe_keys(choice),
                "message_keys": safe_keys(message),
                "hidden_reasoning_chars": len(message["reasoning_content"])
                if isinstance(message.get("reasoning_content"), str) else 0,
            }
        if isinstance(choice.get("finish_reason"), str) and choice["finish_reason"]:
            result["finish_reason"] = choice["finish_reason"]

    try:
        with session.post(endpoint, json=body, stream=stream,
                          timeout=(min(15, timeout), timeout)) as response:
            result["http_status"] = response.status_code
            result["headers_ms"] = round((time.perf_counter() - started) * 1000)
            result["content_type"] = response.headers.get("Content-Type", "").split(";")[0]
            if response.status_code != 200:
                # Do not persist arbitrary gateway errors which may echo secrets.
                result["error_type"] = "http-error"
                try:
                    error = response.json().get("error", {})
                    record_error(error)
                    result["error_type"] = "http-error"
                except (ValueError, AttributeError):
                    pass
            elif stream and "text/event-stream" in response.headers.get("Content-Type", ""):
                for raw in response.iter_lines(chunk_size=1):
                    if time.perf_counter() - started > timeout:
                        result["error_type"] = "total-timeout"
                        break
                    if not raw.startswith(b"data:"):
                        continue
                    payload = raw[5:].strip()
                    if payload == b"[DONE]":
                        result["received_done"] = True
                        break
                    if payload:
                        consume(json.loads(payload), True)
                        if result.get("error_type") in ("provider-error", "invalid-response"):
                            break
            else:
                consume(response.json(), False)
                if stream:
                    result["stream_returned_json"] = True
    except (requests.RequestException, ValueError) as error:
        result["error_type"] = type(error).__name__

    result["elapsed_ms"] = round((time.perf_counter() - started) * 1000)
    completed = result["finish_reason"] == "stop" or (
        stream and result["received_done"] and result["finish_reason"] is None)
    result["status"] = "ok" if (
        result.get("http_status") == 200 and result["content"].strip()
        and completed and "error_type" not in result
    ) else "error"
    return sanitize(result)


def metadata_followup(template: dict, raw: str) -> dict:
    """Fill exported classifier data blocks from this actual main reply.

    This deliberately requires the canonical one-line NPC format used by the
    synthetic fixtures. It fails rather than timing a classifier on invented text.
    """
    visible = re.split(r"!+LIVINGNPCS_META", raw, maxsplit=1, flags=re.I)[0]
    lines = [line.strip() for line in visible.splitlines() if line.strip()]
    dialogue = [line[1:].strip() for line in lines if line.startswith("-")]
    if len(dialogue) != 1 or any(not line.startswith(("-", "%")) for line in lines):
        raise ValueError("The live reply was not canonical one-line dialogue.")
    options = [line.lstrip("%").strip() for line in lines if line.startswith("%")]

    def escape(value: str) -> str:
        value = value.replace("\r\n", "\n").replace("\r", "\n")
        value = "".join(ch for ch in value if ch in "\n\t" or unicodedata.category(ch) not in ("Cc", "Cf"))
        value = value.replace("<", "＜").replace(">", "＞")
        return re.sub("!LIVINGNPCS_META", "[metadata marker removed]", value, flags=re.I).strip() or "(empty)"

    case = dict(template)
    prompt = case["user"]
    for source, value in (("metadata_npc_reply", dialogue[0]),
                          ("metadata_farmer_options", "\n".join(options) or "(none)")):
        pattern = rf'(<untrusted_data source="{source}">)\r?\n.*?\r?\n(</untrusted_data>)'
        prompt, count = re.subn(pattern, lambda match: match[1] + "\n" + escape(value) + "\n" + match[2],
                                prompt, flags=re.S)
        if count != 1:
            raise ValueError("Missing or repeated exported classifier data block.")
    case["user"] = prompt
    return case


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--fixtures", type=Path)
    parser.add_argument("--timeout", type=int, default=85)
    args = parser.parse_args()
    config = json.loads(args.config.read_text(encoding="utf-8-sig"))
    if config.get("Provider", "").lower() not in ("openaicompatible", "openai"):
        raise SystemExit("This probe supports OpenAI-compatible configurations only.")
    model = config["ModelName"]
    endpoint = chat_endpoint(config.get("ServerAddress") or "https://api.openai.com")
    cases = json.loads(args.fixtures.read_text(encoding="utf-8-sig")) if args.fixtures else screening_cases()
    planned = len(cases) + sum("metadata_followup" in case for case in cases)
    if not 1 <= planned <= 24 or not 2 <= args.timeout <= 120:
        raise SystemExit("Use 1-24 cases and a timeout of 2-120 seconds.")
    all_cases = cases + [case["metadata_followup"] for case in cases if "metadata_followup" in case]
    if any(case["effort"] not in ("low", "none") for case in all_cases):
        raise SystemExit("The probe compares only low and none on the configured model.")
    if any(case.get("thinking_type") not in (None, "enabled", "disabled") for case in all_cases):
        raise SystemExit("thinking_type must be enabled or disabled when supplied.")
    if any(not 2 <= case.get("timeout_seconds", args.timeout) <= 120 for case in all_cases):
        raise SystemExit("Each case timeout must be 2-120 seconds.")
    report = {"requested_model": model, "results": []}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    if args.output.exists():
        raise SystemExit("Choose a new output file; reports are never overwritten.")
    print(json.dumps({"model": model, "planned_requests": planned,
                      "timeout_seconds": args.timeout}, ensure_ascii=False), flush=True)
    with requests.Session() as session:
        key = config.get("ApiKey", "")
        if key:
            session.headers["Authorization"] = "Bearer " + key.strip()
        def record(result: dict) -> None:
            report["results"].append(result)
            args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
            summary = {k: v for k, v in result.items()
                       if k in ("name", "effort", "status", "elapsed_ms", "first_content_ms",
                                "http_status", "finish_reason", "usage", "error_type", "error_message")}
            print(json.dumps({"request": len(report["results"]), **summary}, ensure_ascii=False), flush=True)

        for case in cases:
            result = call(session, endpoint, model, case, case.get("timeout_seconds", args.timeout))
            record(result)
            if (result.get("http_status") in (401, 403, 429, 502, 503, 504)
                    or result.get("error_type") in ("provider-error", "invalid-response")):
                print("Stopping on provider availability/authentication error; no automatic retries.", flush=True)
                break
            if "metadata_followup" in case and result["status"] == "ok":
                followup = metadata_followup(case["metadata_followup"], result["content"])
                extra = call(session, endpoint, model, followup, followup.get("timeout_seconds", args.timeout))
                extra["parent_case"] = case["name"]
                extra["pipeline_ms"] = result["elapsed_ms"] + extra["elapsed_ms"]
                record(extra)
                if (extra.get("http_status") in (401, 403, 429, 502, 503, 504)
                        or extra.get("error_type") in ("provider-error", "invalid-response")):
                    print("Stopping on provider availability/authentication error; no automatic retries.", flush=True)
                    break


if __name__ == "__main__":
    main()
