#!/usr/bin/env python3
"""Exercise LevelPlay challenge binding, MD5 verification, and idempotent payout."""
import hashlib
import json
import os
import pathlib
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

BASE = sys.argv[1].rstrip("/")
KEY = os.environ["Rewards__LevelPlayPrivateKey"]

# The repository-level test advances the challenge date without sleeping for a day, and
# reproduces an already-signed event whose unsigned custom challengeId is changed to B.
test_project = pathlib.Path(__file__).resolve().parent.parent / "Tests" / "AdRewardRepositorySmoke" / "AdRewardRepositorySmoke.csproj"
subprocess.run(["dotnet", "run", "--configuration", "Release", "--no-build", "--project", str(test_project)], check=True)


def request(path, method="GET", body=None, token=None):
    data = json.dumps(body).encode() if body is not None else None
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    req = urllib.request.Request(BASE + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req) as response:
            return response.status, response.read().decode()
    except urllib.error.HTTPError as error:
        return error.code, error.read().decode()


_, raw = request("/v1/identity/anonymous", "POST", {})
identity = json.loads(raw)
player, token = identity["playerId"], identity["accessToken"]
status, raw = request("/v1/rewards/ad/challenge", "POST", {"playerId": player}, token)
assert status == 200, (status, raw)
challenge = json.loads(raw)["challengeId"]
timestamp, event_id, reward_units = time.strftime("%Y%m%d%H%M", time.gmtime()), "ci-levelplay-event-0001", "1"
params = {"appUserId": player, "rewards": reward_units, "eventId": event_id,
          "timestamp": timestamp, "custom_challengeId": challenge}
params["signature"] = hashlib.md5((timestamp + event_id + player + reward_units + KEY).encode()).hexdigest()
status, raw = request("/v1/rewards/ad/challenge", "POST", {"playerId": player}, token)
assert status == 200 and json.loads(raw)["challengeId"] == challenge, (status, raw)
status, raw = request("/v1/rewards/ad/status/" + challenge, token=token)
assert status == 200 and not json.loads(raw)["completed"], (status, raw)
mutated = dict(params, custom_challengeId="f" * 32)
status, _ = request("/v1/rewards/ad/levelplay-callback?" + urllib.parse.urlencode(mutated))
assert status == 409, status
status, raw = request("/v1/rewards/ad/status/" + challenge, token=token)
assert status == 200 and not json.loads(raw)["completed"], (status, raw)
bad = dict(params, signature="0" * 32)
status, _ = request("/v1/rewards/ad/levelplay-callback?" + urllib.parse.urlencode(bad))
assert status == 401, status
other_player = "0" * 32
wrong_owner = dict(params, appUserId=other_player)
wrong_owner["signature"] = hashlib.md5((timestamp + event_id + other_player + reward_units + KEY).encode()).hexdigest()
status, _ = request("/v1/rewards/ad/levelplay-callback?" + urllib.parse.urlencode(wrong_owner))
assert status == 409, status
status, raw = request("/v1/rewards/ad/status/" + challenge, token=token)
assert status == 200 and not json.loads(raw)["completed"], (status, raw)
status, body = request("/v1/rewards/ad/levelplay-callback?" + urllib.parse.urlencode(params))
assert status == 200 and body == event_id + ":OK", (status, body)
status, body = request("/v1/rewards/ad/levelplay-callback?" + urllib.parse.urlencode(params))
assert status == 200 and body == event_id + ":OK", (status, body)
status, raw = request("/v1/rewards/ad/status/" + challenge, token=token)
assert status == 200, (status, raw)
result = json.loads(raw)
assert result["completed"] and result["award"] in (25, 50, 75, 100), result
status, _ = request("/v1/rewards/ad/challenge", "POST", {"playerId": player}, token)
assert status == 409, status
print("LevelPlay S2S reward verified, bound, and idempotent")
