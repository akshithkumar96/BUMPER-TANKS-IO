#!/usr/bin/env python3
"""Exercise the authenticated crash report API without sending exception text."""
import json
import sys
import urllib.error
import urllib.request


BASE = sys.argv[1].rstrip("/")


def call(path, method="GET", body=None, token=None):
    data = json.dumps(body).encode() if body is not None else None
    headers = {"Content-Type": "application/json"} if data is not None else {}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    request = urllib.request.Request(BASE + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(request) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.read()


def expect_status(status, expected, label):
    if status != expected:
        raise RuntimeError(f"{label}: expected HTTP {expected}, received {status}")


status, raw = call("/v1/identity/anonymous", "POST", {})
expect_status(status, 200, "identity")
identity = json.loads(raw)
player_id, token = identity["playerId"], identity["accessToken"]
report = {"playerId": player_id, "fingerprint": "a" * 64, "buildVersion": "1.0.0", "platform": "WindowsPlayer"}

expect_status(call("/v1/crash-reports", "POST", report)[0], 401, "unauthorized")
wrong_player = dict(report, playerId="0" * 32)
expect_status(call("/v1/crash-reports", "POST", wrong_player, token)[0], 403, "identity mismatch")
invalid = dict(report, fingerprint="raw exception stack text")
expect_status(call("/v1/crash-reports", "POST", invalid, token)[0], 400, "invalid fingerprint")
status, raw = call("/v1/crash-reports", "POST", report, token)
expect_status(status, 202, "accepted report")
if json.loads(raw).get("duplicate") is not False:
    raise RuntimeError("first report should not be a duplicate")
status, raw = call("/v1/crash-reports", "POST", report, token)
expect_status(status, 202, "duplicate report")
if json.loads(raw).get("duplicate") is not True:
    raise RuntimeError("repeated report should be deduplicated")
print("Crash report API authorization, validation, acceptance, and dedupe passed.")
