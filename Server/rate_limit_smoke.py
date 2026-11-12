import json
import sys
import urllib.error
import urllib.request


def request(address, path, token=None, method="GET", body=None):
    headers = {}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    data = None
    if body is not None:
        headers["Content-Type"] = "application/json"
        data = json.dumps(body).encode("utf-8")
    req = urllib.request.Request(address + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=3) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.read()


def create_identity(address):
    req = urllib.request.Request(address + "/v1/identity/anonymous", method="POST")
    with urllib.request.urlopen(req, timeout=3) as response:
        identity = json.loads(response.read())
    return identity["playerId"], identity["accessToken"]


def main():
    address = sys.argv[1].rstrip("/")
    first_id, first_token = create_identity(address)
    second_id, second_token = create_identity(address)
    third_id, third_token = create_identity(address)

    # Different invalid bearer strings must not create fresh partitions. They
    # share the source-IP budget until a credential is actually authenticated.
    for index in range(60):
        invalid_token = f"{index + 1:064x}"
        status, _ = request(address, f"/v1/profiles/{first_id}", invalid_token)
        assert status == 401, f"invalid token {index + 1} unexpectedly returned {status}"
    invalid_token = f"{61:064x}"
    status, _ = request(address, f"/v1/profiles/{first_id}", invalid_token)
    assert status == 429, f"invalid token spam escaped the source-IP budget: {status}"

    path = f"/v1/profiles/{first_id}"
    for index in range(60):
        status, _ = request(address, path, first_token)
        assert status == 200, f"first client request {index + 1} unexpectedly returned {status}"

    status, _ = request(address, path, first_token)
    assert status == 429, f"first client's request limit was not enforced: {status}"

    status, body = request(address, f"/v1/profiles/{second_id}", second_token)
    assert status == 200, f"one client's limit blocked a separate client: {status} {body!r}"

    # Anonymous identity creation uses its own source-IP partition, not the
    # exhausted authenticated-player partition.
    status, _ = request(address, f"/v1/profiles/{third_id}", third_token)
    assert status == 200, f"anonymous identity creation was blocked by another client's limit: {status}"

    crash_path = "/v1/crash-reports"
    for index in range(10):
        report = {"playerId": first_id, "fingerprint": f"{index + 1:064x}",
                  "buildVersion": "smoke.1", "platform": "test"}
        status, _ = request(address, crash_path, first_token, "POST", report)
        assert status == 202, f"first client's crash request {index + 1} unexpectedly returned {status}"
    report = {"playerId": first_id, "fingerprint": f"{11:064x}",
              "buildVersion": "smoke.1", "platform": "test"}
    status, _ = request(address, crash_path, first_token, "POST", report)
    assert status == 429, f"first client's crash-report limit was not enforced: {status}"
    report["playerId"] = second_id
    status, _ = request(address, crash_path, second_token, "POST", report)
    assert status == 202, f"one client's crash-report limit blocked another client: {status}"
    print("Rate limit smoke passed: per-player API budgets isolate clients and anonymous identity creation.")


if __name__ == "__main__":
    main()
