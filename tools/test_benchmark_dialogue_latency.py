"""Offline checks for probe privacy, malformed gateways and paid-call boundaries."""

import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import benchmark_dialogue_latency as probe


KEY = "synthetic-current-credential"
ENDPOINT = "https://gateway.invalid/v1/chat/completions"
CASE = {"name": "synthetic", "effort": "low", "system": "Rules", "user": "Hi", "stream": True}


class Response:
    def __init__(self, payload, *, stream=False, status=200):
        self.status_code = status
        self.headers = {"Content-Type": "text/event-stream" if stream else "application/json"}
        self.body = payload if isinstance(payload, str) else json.dumps(payload)

    def __enter__(self):
        return self

    def __exit__(self, *args):
        pass

    def json(self):
        return json.loads(self.body)

    def iter_lines(self, chunk_size):
        yield from self.body.encode().splitlines()


class Session:
    def __init__(self, *responses):
        self.headers = {"Authorization": "Bearer " + KEY}
        self.responses = list(responses)
        self.posts = []

    def __enter__(self):
        return self

    def __exit__(self, *args):
        pass

    def post(self, endpoint, **kwargs):
        self.posts.append(kwargs["json"])
        return self.responses.pop(0)


def call(response, stream=True):
    return probe.call(Session(response), ENDPOINT, "gpt-5.6-sol", dict(CASE, stream=stream), 2)


class ProbeTests(unittest.TestCase):
    def test_deepseek_fixture_keeps_explicit_thinking_switch_and_output_budget(self):
        for thinking_type in ("enabled", "disabled"):
            with self.subTest(thinking_type=thinking_type):
                session = Session(Response({"choices": [{"message": {"content": "Hello"}, "finish_reason": "stop"}]}))
                result = probe.call(session, ENDPOINT, "deepseek-flash", dict(
                    CASE, stream=False, thinking_type=thinking_type, max_tokens=16000), 2)
                body = session.posts[0]
                self.assertEqual({"type": thinking_type}, body["thinking"])
                self.assertEqual(16000, body["max_tokens"])
                self.assertEqual(16000, result["max_output_tokens"])
                if thinking_type == "enabled":
                    self.assertEqual("low", body["reasoning_effort"])
                else:
                    self.assertNotIn("reasoning_effort", body)

    def test_invalid_thinking_switch_fails_before_sending_a_request(self):
        session = Session()
        with self.assertRaises(ValueError):
            probe.call(session, ENDPOINT, "deepseek-flash", dict(CASE, thinking_type="arbitrary"), 2)
        self.assertEqual([], session.posts)

    def test_error_messages_redact_credentials_and_case_insensitive_urls(self):
        for streamed in (False, True):
            with self.subTest(streamed=streamed):
                error = {"error": {"message": f"{KEY} {ENDPOINT} HTTPS://UPPER.invalid/private sk-synthetic-secret " + "x" * 600}}
                response = Response("data: " + json.dumps(error) + "\n", stream=True) if streamed else Response(error)
                result = call(response, streamed)
                self.assertEqual("provider-error", result["error_type"])
                self.assertEqual("error", result["status"])
                self.assertLessEqual(len(result["error_message"]), 400)
                self.assert_redacted(result)

    def test_echoed_metadata_and_key_names_are_also_redacted(self):
        result = call(Response({
            KEY: "ignored",
            "model": KEY,
            "usage": {ENDPOINT: KEY},
            "choices": [{"sk-synthetic-secret": 1, "message": {
                ENDPOINT: 1, KEY: 1, "content": "", "reasoning_content": "PRIVATE_REASONING"
            }}]
        }), False)
        self.assert_redacted(result)
        self.assertNotIn("PRIVATE_REASONING", json.dumps(result))
        self.assertIn("empty_content_shape", result)
        self.assertTrue(all(len(key) <= 80 for key in result["response_keys"]))

    def assert_redacted(self, result):
        serialized = json.dumps(result)
        for value in (KEY, ENDPOINT, "UPPER.invalid", "sk-synthetic-secret"):
            self.assertNotIn(value, serialized)

    def test_malformed_json_shapes_become_recordable_errors(self):
        for data in ([], [1], None, {"choices": {}}, {"choices": [None]},
                     {"choices": [{"message": []}]}, {"choices": [{"message": {"content": ["bad"]}}]}):
            for streamed in (False, True):
                with self.subTest(data=data, streamed=streamed):
                    if streamed and isinstance(data, dict) and data.get("choices") and isinstance(data["choices"], list):
                        data = json.loads(json.dumps(data).replace('"message"', '"delta"'))
                    response = Response("data: " + json.dumps(data) + "\n", stream=True) if streamed else Response(data)
                    result = call(response, streamed)
                    self.assertEqual("invalid-response", result["error_type"])
                    self.assertEqual("error", result["status"])

    def test_done_without_finish_reason_accepts_real_text_but_not_whitespace(self):
        for content, expected in (("- Hello", "ok"), (" \n", "error"), ("", "error")):
            with self.subTest(content=content):
                chunk = {"choices": [{"delta": {"content": content}}]}
                result = call(Response("data: " + json.dumps(chunk) + "\ndata: [DONE]\n", stream=True))
                self.assertEqual(expected, result["status"])
                self.assertTrue(result["received_done"])
                self.assertEqual(bool(content.strip()), result["first_content_ms"] is not None)

    def test_length_finish_remains_an_error_even_with_done(self):
        chunk = {"choices": [{"delta": {"content": "- Partial"}, "finish_reason": "length"}]}
        result = call(Response("data: " + json.dumps(chunk) + "\ndata: [DONE]\n", stream=True))
        self.assertEqual("error", result["status"])

    def test_http_error_message_is_redacted(self):
        result = call(Response({"error": {"message": KEY + " " + ENDPOINT}}, status=503), False)
        self.assertEqual("http-error", result["error_type"])
        self.assert_redacted(result)

    def test_http_200_provider_error_stops_before_the_next_paid_request(self):
        session = Session(Response({"error": {"message": "Our servers are currently overloaded."}}))
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            config, fixtures, output = (root / name for name in ("config.json", "fixtures.json", "result.json"))
            config.write_text(json.dumps({"Provider": "OpenAiCompatible", "ModelName": "gpt-5.6-sol",
                                          "ServerAddress": ENDPOINT, "ApiKey": KEY}), encoding="utf-8")
            fixtures.write_text(json.dumps([CASE, CASE]), encoding="utf-8")
            with patch.object(probe.requests, "Session", return_value=session), patch("sys.argv", [
                "probe", "--config", str(config), "--fixtures", str(fixtures), "--output", str(output)
            ]), contextlib.redirect_stdout(io.StringIO()):
                probe.main()
            report = json.loads(output.read_text(encoding="utf-8"))
        self.assertEqual(1, len(session.posts))
        self.assertEqual(1, len(report["results"]))
        self.assertEqual("provider-error", report["results"][0]["error_type"])


if __name__ == "__main__":
    unittest.main()
