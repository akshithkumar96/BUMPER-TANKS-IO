#!/usr/bin/env python3
"""Exercise LevelPlay challenge binding, MD5 verification, and idempotent payout."""
import hashlib
import hmac
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
ATTESTATION_SECRET = os.environ.get("Rewards__MatchAttestationSecret", "ci-only-match-attestation-secret-32-bytes-minimum")

# The repository-level test advances the challenge date without sleeping for a day, and
# reproduces an already-signed event whose unsigned custom challengeId is changed to B.
test_project = pathlib.Path(__file__).resolve().parent.parent / "Tests" / "AdRewardRepositorySmoke" / "AdRewardRepositorySmoke.csproj"
subprocess.run(["dotnet", "run", "--configuration", "Release", "--no-build", "--project", str(test_project)], check=True)


def request(path, method="GET", body=None, token=None, extra_headers=None):
    data = json.dumps(body).encode() if body is not None else None
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    headers.update(extra_headers or {})
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

match_id = "b" * 32
match_body = {"playerId": player, "matchId": match_id, "gems": 12, "eliminations": 1}
status, _ = request("/v1/rewards/ad/match/challenge", "POST", {"playerId": player, "matchId": match_id}, token)
assert status == 409, ("an unfinished match was eligible for doubling", status)
payload = f"{player}\n{match_id}\n12\n1".encode()
match_attestation = hmac.new(ATTESTATION_SECRET.encode(), payload, hashlib.sha256).hexdigest()
status, raw = request("/v1/matches/complete", "POST", match_body, token,
                      {"X-Match-Attestation": match_attestation})
assert status == 200, (status, raw)
match_result = json.loads(raw)
assert match_result["award"] == 22, match_result
starting_balance = match_result["profile"]["battleCoins"]

status, raw = request("/v1/rewards/ad/match/challenge", "POST", {"playerId": player, "matchId": match_id}, token)
assert status == 200, (status, raw)
match_challenge = json.loads(raw)
assert match_challenge["award"] == 22 and match_challenge["challengeId"].startswith("mb"), match_challenge
status, raw = request("/v1/rewards/ad/match/challenge", "POST", {"playerId": player, "matchId": match_id}, token)
assert status == 200 and json.loads(raw)["challengeId"] == match_challenge["challengeId"], (status, raw)
status, raw = request("/v1/rewards/ad/match/status/" + match_challenge["challengeId"], token=token)
assert status == 200 and not json.loads(raw)["completed"], (status, raw)
status, _ = request("/v1/rewards/ad/match/challenge", "POST", {"playerId": "0" * 32, "matchId": match_id}, token)
assert status == 403, status

# The S2S event used by the separate daily-spin flow cannot be rebound to the match bonus.
match_mutation = dict(params, custom_challengeId=match_challenge["challengeId"])
status, _ = request("/v1/rewards/ad/levelplay-callback?" + urllib.parse.urlencode(match_mutation))
assert status == 409, status

match_event = "ci-match-levelplay-event-0001"
match_timestamp = time.strftime("%Y%m%d%H%M", time.gmtime())
match_callback = {"appUserId": player, "rewards": "1", "eventId": match_event, "timestamp": match_timestamp,
                  "custom_challengeId": match_challenge["challengeId"]}
match_callback["signature"] = hashlib.md5(
    (match_timestamp + match_event + player + "1" + KEY).encode()).hexdigest()
status, raw = request("/v1/rewards/ad/levelplay-callback?" + urllib.parse.urlencode(match_callback))
assert status == 200, (status, raw)
status, raw = request("/v1/rewards/ad/levelplay-callback?" + urllib.parse.urlencode(match_callback))
assert status == 200, (status, raw)
status, raw = request("/v1/rewards/ad/match/status/" + match_challenge["challengeId"], token=token)
assert status == 200, (status, raw)
match_status = json.loads(raw)
assert match_status["completed"] and match_status["award"] == 22, match_status
assert match_status["profile"]["battleCoins"] == starting_balance + 22, match_status
status, _ = request("/v1/rewards/ad/match/challenge", "POST", {"playerId": player, "matchId": match_id}, token)
assert status == 409, status
print("LevelPlay match bonus bound to finalized reward, doubled once, and kept separate from the daily spin")
