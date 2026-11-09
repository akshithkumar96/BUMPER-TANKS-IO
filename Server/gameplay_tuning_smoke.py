#!/usr/bin/env python3
"""Verify the public gameplay tuning payload, cache headers, and ETag handling."""
import json
import sys
import urllib.error
import urllib.request


base = sys.argv[1].rstrip("/")
request = urllib.request.Request(base + "/v1/gameplay/tuning")
with urllib.request.urlopen(request) as response:
    payload = json.loads(response.read())
    etag = response.headers.get("ETag")
    cache_control = response.headers.get("Cache-Control", "")
    if response.status != 200:
        raise RuntimeError(f"expected HTTP 200, got {response.status}")
    if not etag or "max-age=60" not in cache_control:
        raise RuntimeError(f"missing cache policy or ETag: {cache_control}, {etag}")

expected = {
    "revision": 1,
    "speedMultiplier": 1.0,
    "projectileCooldownMultiplier": 1.0,
    "knockbackMultiplier": 1.0,
}
if len(sys.argv) == 5:
    expected["speedMultiplier"] = float(sys.argv[2])
    expected["projectileCooldownMultiplier"] = float(sys.argv[3])
    expected["knockbackMultiplier"] = float(sys.argv[4])
for key, value in expected.items():
    if payload.get(key) != value:
        raise RuntimeError(f"{key}: expected {value}, received {payload.get(key)}")

conditional = urllib.request.Request(base + "/v1/gameplay/tuning", headers={"If-None-Match": etag})
try:
    urllib.request.urlopen(conditional)
    raise RuntimeError("matching ETag should return HTTP 304")
except urllib.error.HTTPError as error:
    if error.code != 304:
        raise

print("Gameplay tuning payload, cache headers, and ETag validation passed.")
