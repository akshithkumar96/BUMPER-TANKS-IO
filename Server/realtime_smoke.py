#!/usr/bin/env python3
"""Package-free WebSocket integration smoke test for the ASP.NET realtime hub."""

import base64
import json
import math
import os
import queue
import socket
import struct
import sys
import threading
import time
import urllib.request


class WebSocketClient:
    def __init__(self, address, origin=None):
        host, port = address.rsplit(":", 1)
        self.socket = socket.create_connection((host, int(port)), timeout=5)
        self.socket.settimeout(5)
        self.buffer = bytearray()
        self.messages = queue.Queue()
        self.reading = True
        key = base64.b64encode(os.urandom(16)).decode("ascii")
        request = (
            "GET /v1/realtime HTTP/1.1\r\n"
            f"Host: {address}\r\n"
            "Upgrade: websocket\r\n"
            "Connection: Upgrade\r\n"
            f"Sec-WebSocket-Key: {key}\r\n"
            "Sec-WebSocket-Version: 13\r\n"
        )
        if origin:
            request += f"Origin: {origin}\r\n"
        self.socket.sendall((request + "\r\n").encode("ascii"))
        while b"\r\n\r\n" not in self.buffer:
            self.buffer.extend(self.socket.recv(4096))
        header, remainder = self.buffer.split(b"\r\n\r\n", 1)
        self.buffer = bytearray(remainder)
        if b" 101 " not in header.split(b"\r\n", 1)[0]:
            status = header.split(b"\r\n", 1)[0].decode("ascii", errors="replace")
            self.socket.close()
            raise ConnectionError(f"WebSocket upgrade rejected: {status}")
        self.reader = threading.Thread(target=self._read_messages, daemon=True)
        self.reader.start()

    def send(self, message):
        payload = json.dumps(message, separators=(",", ":")).encode("utf-8") + b"\n"
        mask = os.urandom(4)
        length = len(payload)
        if length < 126:
            header = bytes((0x81, 0x80 | length))
        elif length < 65536:
            header = bytes((0x81, 0x80 | 126)) + struct.pack("!H", length)
        else:
            header = bytes((0x81, 0x80 | 127)) + struct.pack("!Q", length)
        masked = bytes(value ^ mask[index % 4] for index, value in enumerate(payload))
        self.socket.sendall(header + mask + masked)

    def receive(self):
        try:
            return self.messages.get(timeout=5)
        except queue.Empty as error:
            raise socket.timeout() from error

    def _read_messages(self):
        while self.reading:
            try:
                message = self._receive_frame()
            except socket.timeout:
                continue
            except (EOFError, OSError):
                self.messages.put({"type": "close"})
                return
            self.messages.put(message)
            if message.get("type") == "close":
                return

    def _receive_frame(self):
        first = self._read_exact(2)
        opcode = first[0] & 0x0F
        length = first[1] & 0x7F
        if length == 126:
            length = struct.unpack("!H", self._read_exact(2))[0]
        elif length == 127:
            length = struct.unpack("!Q", self._read_exact(8))[0]
        if first[1] & 0x80:
            mask = self._read_exact(4)
        else:
            mask = None
        payload = self._read_exact(length)
        if mask:
            payload = bytes(value ^ mask[index % 4] for index, value in enumerate(payload))
        if opcode == 8:
            code = struct.unpack("!H", payload[:2])[0] if len(payload) >= 2 else None
            reason = payload[2:].decode("utf-8", errors="replace") if len(payload) > 2 else ""
            return {"type": "close", "code": code, "reason": reason}
        if opcode == 9:
            self._send_control(10, payload)
            return self._receive_frame()
        if opcode != 1:
            return self._receive_frame()
        return json.loads(payload.decode("utf-8").rstrip("\r\n"))

    def _send_control(self, opcode, payload):
        mask = os.urandom(4)
        masked = bytes(value ^ mask[index % 4] for index, value in enumerate(payload))
        self.socket.sendall(bytes((0x80 | opcode, 0x80 | len(payload))) + mask + masked)

    def _read_exact(self, count):
        while len(self.buffer) < count:
            chunk = self.socket.recv(max(4096, count - len(self.buffer)))
            if not chunk:
                raise EOFError("WebSocket closed before a complete frame")
            self.buffer.extend(chunk)
        result = bytes(self.buffer[:count])
        del self.buffer[:count]
        return result

    def close(self):
        self.reading = False
        try:
            self._send_control(8, struct.pack("!H", 1000))
        except OSError:
            pass
        self.socket.close()


def create_identity(address):
    request = urllib.request.Request(f"http://{address}/v1/identity/anonymous", data=b"", method="POST")
    with urllib.request.urlopen(request, timeout=5) as response:
        identity = json.loads(response.read().decode("utf-8"))
    assert len(identity["playerId"]) == 32, identity
    assert len(identity["accessToken"]) == 64, identity
    return identity


def join(address, index, origin=None, archetype=None):
    identity = create_identity(address)
    return join_identity(address, index, identity, origin=origin, archetype=archetype)


def join_identity(address, index, identity, origin=None, archetype=None):
    client = WebSocketClient(address, origin=origin)
    client._identity_player_id = identity["playerId"]
    client._identity_access_token = identity["accessToken"]
    message = {"type": "join", "playerId": identity["playerId"], "accessToken": identity["accessToken"],
               "displayName": f"Smoke {index}"}
    if archetype is not None:
        message["archetype"] = archetype
    client.send(message)
    response = client.receive()
    assert response["type"] == "joined", response
    assert response["tickRate"] == 30, response
    assert isinstance(response["playerId"], int), response
    assert len(response["roomId"]) == 32, response
    response["_externalPlayerId"] = identity["playerId"]
    response["_accessToken"] = identity["accessToken"]
    return client, response


def assert_join_rejected(address, player_id, token=None):
    client = WebSocketClient(address)
    message = {"type": "join", "playerId": player_id, "displayName": "Unauthenticated"}
    if token is not None:
        message["accessToken"] = token
    try:
        client.send(message)
        response = client.receive()
        assert response.get("type") == "error" and response.get("code") == "invalid_credentials", response
    finally:
        client.close()


def assert_duplicate_join_rejected(address, identity):
    client = WebSocketClient(address)
    try:
        client.send({"type": "join", "playerId": identity["playerId"], "accessToken": identity["accessToken"],
                     "displayName": "Duplicate Socket"})
        response = client.receive()
        assert response.get("type") == "error" and response.get("code") == "player_already_connected", response
    finally:
        client.close()


def wait_for(client, predicate, timeout=5, allow_error=False):
    deadline = time.monotonic() + timeout
    last_message = None
    while time.monotonic() < deadline:
        try:
            message = client.receive()
        except socket.timeout:
            continue
        last_message = message
        if message.get("type") == "error" and not allow_error:
            raise AssertionError(f"Realtime server rejected smoke input: {message}")
        if predicate(message):
            return message
    raise AssertionError(f"Timed out waiting for realtime server message; last message: {last_message}")


def assert_no_snapshot(client, delay=0.35):
    try:
        message = client.messages.get(timeout=delay)
        if message.get("type") == "snapshot":
            raise AssertionError("Underfilled room started before the 2.5-second backfill window")
        client.messages.put(message)
    except queue.Empty:
        pass


def smoke_reconnect(address, grace_seconds):
    identity = create_identity(address)
    original, joined = join_identity(address, "resume", identity)
    initial = wait_for(original, lambda message: message.get("type") == "snapshot" and
                       any(player["playerId"] == joined["playerId"] for player in message.get("players", [])), timeout=8)
    initial_state = next(player for player in initial["players"] if player["playerId"] == joined["playerId"])
    for tick in range(1, 9):
        original.send({"type": "input", "tick": tick, "movement": {"x": 1, "y": 0}, "aim": {"x": 0, "y": 1},
                       "fire": False, "ability": False})
        time.sleep(0.05)
    changed = wait_for(original, lambda message: message.get("type") == "snapshot" and
                       message.get("tick", 0) > initial.get("tick", 0), timeout=2)
    before = next(player for player in changed["players"] if player["playerId"] == joined["playerId"])
    assert before["health"] < 100 or before["gems"] > 0 or math.hypot(before["velocity"]["x"], before["velocity"]["y"]) > 0.5, (initial_state, before)
    original.close()
    resumed, resumed_join = join_identity(address, "resume", identity)
    assert resumed_join["roomId"] == joined["roomId"] and resumed_join["playerId"] == joined["playerId"], resumed_join
    resumed_state = wait_for(resumed, lambda message: message.get("type") == "snapshot" and
                             any(player["playerId"] == joined["playerId"] for player in message.get("players", [])))
    player_state = next(player for player in resumed_state["players"] if player["playerId"] == joined["playerId"])
    assert player_state["gems"] == before["gems"] and player_state["archetype"] == before["archetype"] and player_state["health"] <= before["health"], (before, player_state)
    resumed.send({"type": "input", "tick": 1, "movement": {"x": 0, "y": 1}, "aim": {"x": 0, "y": 1},
                  "fire": False, "ability": False})
    restarted_sequence = wait_for(resumed, lambda message: message.get("type") == "snapshot" and
                                  message.get("tick", 0) > resumed_state.get("tick", 0) and
                                  next((player["velocity"]["y"] for player in message.get("players", [])
                                        if player["playerId"] == joined["playerId"]), 0) > 0.5, timeout=2)
    assert restarted_sequence["tick"] > resumed_state["tick"], restarted_sequence
    resumed.close()
    time.sleep(grace_seconds + 0.3)
    fresh, fresh_join = join_identity(address, "resume", identity)
    try:
        assert fresh_join["roomId"] != joined["roomId"] and fresh_join["playerId"] != joined["playerId"], fresh_join
    finally:
        fresh.close()
    print("Realtime reconnect smoke passed: identity and slot resume with retained state, restarted input ticks, duplicate sockets rejected, grace expiry creates a fresh room and slot.")


def drive_toward_pickups(clients, player_ids, snapshot, pickup_ids):
    pickups = {pickup["pickupId"]: pickup["position"] for pickup in snapshot["powerUps"]}
    vectors = []
    for player_id_value, pickup_id in zip(player_ids, pickup_ids):
        player = next(player for player in snapshot["players"] if player["playerId"] == player_id_value)
        target = pickups[pickup_id]
        dx = target["x"] - player["position"]["x"]
        dy = target["y"] - player["position"]["y"]
        length = math.hypot(dx, dy)
        vectors.append((dx / length, dy / length))

    # Keep input cadence below the server's 60 Hz per-player limit while giving
    # each tank enough movement frames to cross its seeded pickup location.
    for tick in range(1, 19):
        for client, (x, y) in zip(clients, vectors):
            client.send({"type": "input", "tick": tick, "movement": {"x": x, "y": y},
                         "aim": {"x": 0, "y": 1}, "fire": False, "ability": False})
        time.sleep(0.05)
    for client in clients:
        client.send({"type": "input", "tick": 19, "movement": {"x": 0, "y": 0},
                     "aim": {"x": 0, "y": 1}, "fire": False, "ability": False})


def clear_shot_target(snapshot, shooter_id):
    shooter = next(player for player in snapshot["players"] if player["playerId"] == shooter_id)
    sx, sy = shooter["position"]["x"], shooter["position"]["y"]
    candidates = []
    for target in snapshot["players"]:
        if target["playerId"] == shooter_id or not target.get("alive") or target["health"] < 60:
            continue
        dx = target["position"]["x"] - sx
        dy = target["position"]["y"] - sy
        distance = math.hypot(dx, dy)
        if not 0.4 < distance <= 12:
            continue
        aim_x, aim_y = dx / distance, dy / distance
        blocked = False
        for other in snapshot["players"]:
            if other["playerId"] in (shooter_id, target["playerId"]) or not other.get("alive"):
                continue
            ox = other["position"]["x"] - sx
            oy = other["position"]["y"] - sy
            along = ox * aim_x + oy * aim_y
            perpendicular = math.hypot(ox - aim_x * along, oy - aim_y * along)
            if 0.4 <= along < distance and perpendicular <= 0.55:
                blocked = True
                break
        if not blocked:
            candidates.append((distance, target))
    if not candidates:
        raise AssertionError(f"No clear combat target in range: {snapshot}")
    return min(candidates, key=lambda item: item[0])[1]


def clear_three_bounce_aim(snapshot, shooter_id):
    shooter = next(player for player in snapshot["players"] if player["playerId"] == shooter_id)
    origin = shooter["position"]
    targets = [player["position"] for player in snapshot["players"]
               if player["playerId"] != shooter_id and player.get("alive")]
    for degree in range(360):
        angle = math.radians(degree)
        dx, dy = math.cos(angle), math.sin(angle)
        aim_x, aim_y = dx, dy
        x, y, remaining = origin["x"], origin["y"], 48.0
        segments = 0
        blocked = False
        while segments < 4 and remaining > 0.05:
            tx = ((7.4 - x) / dx if dx > 0 else (-7.4 - x) / dx) if abs(dx) > 0.0001 else math.inf
            ty = ((5.4 - y) / dy if dy > 0 else (-5.4 - y) / dy) if abs(dy) > 0.0001 else math.inf
            tx = tx if tx >= 0 else math.inf
            ty = ty if ty >= 0 else math.inf
            wall = min(tx, ty)
            length = min(remaining, wall)
            for target in targets:
                ox, oy = target["x"] - x, target["y"] - y
                along = ox * dx + oy * dy
                perpendicular = abs(ox * dy - oy * dx)
                if 0.2 <= along <= length and perpendicular <= 0.55:
                    blocked = True
                    break
            if blocked:
                break
            x, y = x + dx * length, y + dy * length
            remaining -= length
            segments += 1
            if wall > length + 0.001 or segments == 4:
                break
            if abs(tx - wall) < 0.001:
                dx = -dx
            if abs(ty - wall) < 0.001:
                dy = -dy
            x += dx * 0.05
            y += dy * 0.05
        if not blocked and segments == 4:
            return aim_x, aim_y
    raise AssertionError("No clear path for a three-bounce Spinner laser")


def fire_until_hit(client, snapshot, shooter_id, target_id, last_client_tick, retarget=False):
    """Keep aim current and confirm the intended live target actually took the shot."""
    deadline = time.monotonic() + 5
    current = snapshot
    while time.monotonic() < deadline:
        while True:
            try:
                queued = client.messages.get_nowait()
            except queue.Empty:
                break
            if queued.get("type") == "error":
                raise AssertionError(f"Realtime server rejected smoke input: {queued}")
            if queued.get("type") == "snapshot" and queued.get("tick", 0) > current.get("tick", 0):
                current = queued

        shooter = next(player for player in current["players"] if player["playerId"] == shooter_id)
        if retarget:
            try:
                target = clear_shot_target(current, shooter_id)
                target_id = target["playerId"]
            except AssertionError:
                time.sleep(0.05)
                continue
        else:
            target = next((player for player in current["players"] if player["playerId"] == target_id), None)
            if target is None or not target.get("alive"):
                raise AssertionError(f"Combat target {target_id} was eliminated before the shot: {current}")
        aim_x = target["position"]["x"] - shooter["position"]["x"]
        aim_y = target["position"]["y"] - shooter["position"]["y"]
        length = max(0.0001, math.hypot(aim_x, aim_y))
        health_before_shot = target["health"]
        input_tick = max(last_client_tick + 1, current["tick"] + 2)
        time.sleep(0.04)  # Leave margin below the server's 60 Hz per-player input limit.
        client.send({
            "type": "input", "tick": input_tick,
            "movement": {"x": 0, "y": 0},
            "aim": {"x": aim_x / length, "y": aim_y / length},
            "fire": True, "ability": False,
        })
        last_client_tick = input_tick
        current = wait_for(client, lambda message: message.get("type") == "snapshot" and
                           message.get("tick", 0) > input_tick, timeout=1)
        target_after = next((player for player in current["players"] if player["playerId"] == target_id), None)
        confirmed_hit = any(shot.get("ownerPlayerId") == shooter_id and shot.get("hitPlayerId") == target_id and
                            shot.get("hit") for shot in current.get("shots", []))
        if confirmed_hit and target_after is not None and target_after["health"] < health_before_shot:
            return current, target_after, health_before_shot, last_client_tick
    raise AssertionError(f"Timed out waiting for moving bot to be hit; last snapshot: {current}")


def fire_until_shield_blocked(shooter_client, shield_client, snapshot, shooter_id, shield_id,
                              shooter_tick, shield_tick, timeout=2.5):
    """Track the moving target while firing inside the Heavy shield's short active window."""
    deadline = time.monotonic() + timeout
    current = snapshot
    while time.monotonic() < deadline:
        shooter = next(player for player in current["players"] if player["playerId"] == shooter_id)
        shield = next(player for player in current["players"] if player["playerId"] == shield_id)
        if not shield["shielded"]:
            raise AssertionError(f"Heavy shield expired before a reflected shot: {current}")
        dx = shield["position"]["x"] - shooter["position"]["x"]
        dy = shield["position"]["y"] - shooter["position"]["y"]
        length = max(0.0001, math.hypot(dx, dy))
        time.sleep(0.04)
        shooter_tick = max(shooter_tick + 1, current["tick"] + 2)
        shield_tick = max(shield_tick + 1, current["tick"] + 2)
        shield_client.send({"type": "input", "tick": shield_tick, "movement": {"x": 0, "y": 0},
                            "aim": {"x": -dx / length, "y": -dy / length},
                            "fire": False, "ability": False})
        shooter_client.send({"type": "input", "tick": shooter_tick, "movement": {"x": 0, "y": 0},
                             "aim": {"x": dx / length, "y": dy / length},
                             "fire": True, "ability": False})
        current = wait_for(shooter_client, lambda message: message.get("type") == "snapshot" and
                           message.get("tick", 0) >= shooter_tick, timeout=0.5)
        if any(shot.get("ownerPlayerId") == shooter_id and shot.get("hitPlayerId") == shield_id and
               shot.get("shieldBlocked") for shot in current.get("shots", [])):
            return current, shooter_tick, shield_tick
    raise AssertionError(f"Timed out waiting for a Heavy shield reflection: {current}")


def main():
    address = sys.argv[1].removeprefix("http://").removeprefix("ws://").rstrip("/")
    if len(sys.argv) > 2 and sys.argv[2] == "--reconnect-only":
        smoke_reconnect(address, float(os.environ.get("REALTIME_DISCONNECT_GRACE_SECONDS", "30")))
        return
    clients = []
    first_room_player_ids = []
    try:
        idle_client = WebSocketClient(address)
        idle_close = idle_client.receive()
        assert idle_close.get("type") == "close" and idle_close.get("code") == 1008, idle_close
        idle_client.close()

        try:
            WebSocketClient(address, origin="https://untrusted.invalid")
            raise AssertionError("WebSocket accepted an origin outside Cors__AllowedOrigins")
        except ConnectionError as rejected:
            assert "403" in str(rejected), rejected

        first_identity = create_identity(address)
        second_identity = create_identity(address)
        assert_join_rejected(address, first_identity["playerId"])
        assert_join_rejected(address, first_identity["playerId"], "f" * 64)
        assert_join_rejected(address, second_identity["playerId"], first_identity["accessToken"])

        smoke_reconnect(address, float(os.environ.get("REALTIME_DISCONNECT_GRACE_SECONDS", "30")))

        first, first_join = join(address, 1, origin="http://localhost:5173")
        clients.append(first)
        first_room_player_ids.append(first_join["playerId"])
        assert_duplicate_join_rejected(address, {"playerId": first_join["_externalPlayerId"],
                                                  "accessToken": first_join["_accessToken"]})
        second, second_join = join(address, 2, archetype="HeavyDozer")
        clients.append(second)
        first_room_player_ids.append(second_join["playerId"])
        assert second_join["roomId"] == first_join["roomId"], second_join
        assert_no_snapshot(first)

        for index in range(3, 9):
            archetype = ("SpeedyBumper", "HeavyDozer", "RicochetSpinner")[(index - 1) % 3]
            client, response = join(address, index, archetype=archetype)
            clients.append(client)
            first_room_player_ids.append(response["playerId"])
            assert response["roomId"] == first_join["roomId"], response
            assert any(player["playerId"] == response["playerId"] and player["archetype"] == archetype
                       for player in response["players"]), response

        initial = wait_for(first, lambda message: message.get("type") == "snapshot" and
                           len(message.get("powerUps", [])) == 3, timeout=3)
        drive_toward_pickups([first, second, clients[2]], first_room_player_ids[:3], initial, [1, 2, 3])
        moved = wait_for(first, lambda message: message.get("type") == "snapshot" and
                         any(player["playerId"] == first_room_player_ids[0] and player.get("megaBulletActive")
                             for player in message["players"]) and
                         any(player["playerId"] == first_room_player_ids[1] and player.get("nitroActive")
                             for player in message["players"]) and
                         any(player["playerId"] == first_room_player_ids[2] and player.get("magnetActive")
                             for player in message["players"]) and
                         any(player["playerId"] == first_room_player_ids[2] and player.get("gems", 0) >= 1
                             for player in message["players"]))
        first_room_players = [player for player in moved["players"] if player["playerId"] > 0]
        assert len(first_room_players) == 8, moved
        assert moved["phase"] == "opening" and 0 <= moved["remainingSeconds"] <= 90, moved
        assert moved["arenaBounds"] == {"halfWidth": 7.4, "halfHeight": 5.4}, moved
        assert all("alive" in player and "eliminations" in player and "shielded" in player and "archetype" in player
                   for player in first_room_players), moved
        first_state = next(player for player in first_room_players if player["playerId"] == first_join["playerId"])
        second_state = next(player for player in first_room_players if player["playerId"] == second_join["playerId"])
        magnet_state = next(player for player in first_room_players if player["playerId"] == first_room_player_ids[2])
        assert first_state["archetype"] == "SpeedyBumper" and first_state["gems"] == 1 and first_state["megaBulletActive"], moved
        assert second_state["nitroActive"], moved
        assert magnet_state["magnetActive"] and magnet_state["gems"] >= 1, moved
        assert all(gem["pickupId"] not in (1, 2) for gem in moved["gems"]), moved
        assert not moved["powerUps"], moved

        # Verify a live shot before running the separate 2.5-second bot-fill scenario.
        combat_shooter_id = first_room_player_ids[0]
        target_id = clear_shot_target(moved, combat_shooter_id)["playerId"]
        hit, target_state, previous_health, shooter_last_client_tick = fire_until_hit(
            first, moved, combat_shooter_id, target_id, 19, retarget=True)
        impulse_speed = math.hypot(target_state["velocity"]["x"], target_state["velocity"]["y"])
        assert target_state["health"] < previous_health and impulse_speed > 0.5, target_state
        last_client_tick = shooter_last_client_tick
        spinner_last_client_tick = 19

        time.sleep(0.05)  # Let zero-movement frames settle before joining the next room.

        ninth, ninth_join = join(address, 9)
        clients.append(ninth)
        assert ninth_join["roomId"] != first_join["roomId"], ninth_join

        tenth, tenth_join = join(address, 10)
        clients.append(tenth)
        assert tenth_join["roomId"] == ninth_join["roomId"], tenth_join
        assert_no_snapshot(ninth)
        two_player_fill = wait_for(ninth, lambda message: message.get("type") == "snapshot" and
                                   len(message.get("players", [])) == 8, timeout=4)
        assert len([player for player in two_player_fill["players"] if player["playerId"] > 0]) == 2, two_player_fill
        assert len([player for player in two_player_fill["players"] if player["playerId"] < 0]) == 6, two_player_fill

        invalid, _ = join(address, 11)
        clients.append(invalid)
        invalid.send({
            "type": "input", "tick": -1,
            "movement": {"x": 0, "y": 0}, "aim": {"x": 0, "y": 1},
            "fire": False, "ability": False,
        })
        rejection = wait_for(invalid, lambda message: message.get("type") == "error", timeout=2, allow_error=True)
        assert rejection["code"] == "invalid_input", rejection

        # Move close before activating Heavy's three-second shield.
        heavy_shield = wait_for(second, lambda message: message.get("type") == "snapshot")
        shooter_state = next(player for player in heavy_shield["players"] if player["playerId"] == first_join["playerId"])
        heavy_state = next(player for player in heavy_shield["players"] if player["playerId"] == second_join["playerId"])
        for _ in range(36):
            dx = heavy_state["position"]["x"] - shooter_state["position"]["x"]
            dy = heavy_state["position"]["y"] - shooter_state["position"]["y"]
            distance = math.hypot(dx, dy)
            if distance <= 8.0:
                break
            input_tick = last_client_tick + 1
            first.send({"type": "input", "tick": input_tick, "movement": {"x": dx / distance, "y": dy / distance},
                        "aim": {"x": dx / distance, "y": dy / distance}, "fire": False, "ability": False})
            last_client_tick = input_tick
            prior_tick = heavy_shield["tick"]
            heavy_shield = wait_for(first, lambda message: message.get("type") == "snapshot" and
                                    message.get("tick", 0) > prior_tick, timeout=1)
            shooter_state = next(player for player in heavy_shield["players"] if player["playerId"] == first_join["playerId"])
            heavy_state = next(player for player in heavy_shield["players"] if player["playerId"] == second_join["playerId"])
            time.sleep(0.05)
        assert distance <= 8.0, (shooter_state, heavy_state)
        # Aim and activate once they are in range, then keep aim current and
        # retry shots until a reflection is observed or the shield expires.
        dx = shooter_state["position"]["x"] - heavy_state["position"]["x"]
        dy = shooter_state["position"]["y"] - heavy_state["position"]["y"]
        aim_length = max(0.0001, math.hypot(dx, dy))
        time.sleep(0.05)
        second.send({"type": "input", "tick": 22, "movement": {"x": 0, "y": 0},
                     "aim": {"x": dx / aim_length, "y": dy / aim_length}, "fire": False, "ability": True})
        facing_shield = wait_for(second, lambda message: message.get("type") == "snapshot" and
                                 next((player["shielded"] for player in message["players"]
                                       if player["playerId"] == second_join["playerId"]), False))
        reflected, last_client_tick, heavy_last_client_tick = fire_until_shield_blocked(
            first, second, facing_shield, first_join["playerId"], second_join["playerId"], last_client_tick, 22)
        reflected_shot = next(shot for shot in reflected["shots"] if shot["ownerPlayerId"] == first_join["playerId"])
        assert reflected_shot["shieldBlocked"] and not reflected_shot["hit"], reflected
        shielded_after_hit = next(player for player in reflected["players"] if player["playerId"] == second_join["playerId"])
        assert shielded_after_hit["health"] == heavy_state["health"], reflected

        # The shield covers only its front arc; a perpendicular shot still damages Heavy.
        shooter_state = next(player for player in reflected["players"] if player["playerId"] == first_join["playerId"])
        heavy_state = next(player for player in reflected["players"] if player["playerId"] == second_join["playerId"])
        dx = heavy_state["position"]["x"] - shooter_state["position"]["x"]
        dy = heavy_state["position"]["y"] - shooter_state["position"]["y"]
        aim_length = max(0.0001, math.hypot(dx, dy))
        time.sleep(0.05)
        heavy_last_client_tick = max(heavy_last_client_tick + 1, reflected["tick"] + 2)
        second.send({"type": "input", "tick": heavy_last_client_tick, "movement": {"x": 0, "y": 0},
                     "aim": {"x": -dy / aim_length, "y": dx / aim_length}, "fire": False, "ability": False})
        side_facing = wait_for(second, lambda message: message.get("type") == "snapshot" and
                               message.get("tick", 0) > reflected["tick"])
        before_side_health = next(player["health"] for player in side_facing["players"]
                                  if player["playerId"] == second_join["playerId"])
        hit_side, side_target, before_side_health, last_client_tick = fire_until_hit(
            first, side_facing, first_join["playerId"], second_join["playerId"], last_client_tick)
        assert side_target["health"] < before_side_health, hit_side

        spinner_id = first_room_player_ids[2]
        spinner = clients[2]
        spinner.send({
            "type": "input", "tick": spinner_last_client_tick + 1,
            "movement": {"x": 0, "y": 0}, "aim": {"x": 0, "y": 1},
            "fire": False, "ability": True,
        })
        whirlwind = wait_for(spinner, lambda message: message.get("type") == "snapshot" and
                             next((player.get("whirling", False) for player in message["players"]
                                   if player["playerId"] == spinner_id), False))
        assert next(player for player in whirlwind["players"] if player["playerId"] == spinner_id)["archetype"] == "RicochetSpinner", whirlwind
        laser_aim = clear_three_bounce_aim(whirlwind, spinner_id)
        time.sleep(0.05)  # Keep successive inputs under the server's 60 Hz limit.
        spinner.send({
            "type": "input", "tick": spinner_last_client_tick + 2,
            "movement": {"x": 0, "y": 0}, "aim": {"x": laser_aim[0], "y": laser_aim[1]},
            "fire": True, "ability": False,
        })
        spinner_laser = wait_for(spinner, lambda message: message.get("type") == "snapshot" and
                                 any(shot.get("ownerPlayerId") == spinner_id and shot.get("type") == "RicochetLaser"
                                     for shot in message.get("shots", [])))
        laser_id = next(shot["shotId"] for shot in spinner_laser["shots"] if shot["ownerPlayerId"] == spinner_id)
        laser_segments = sorted((shot for shot in spinner_laser["shots"] if shot["shotId"] == laser_id),
                                key=lambda shot: shot["segmentIndex"])
        assert len(laser_segments) == 4, spinner_laser
        assert [shot["segmentIndex"] for shot in laser_segments] == [0, 1, 2, 3], spinner_laser
        assert [shot["bounceIndex"] for shot in laser_segments] == [0, 1, 2, 3], spinner_laser

        dash_tick = last_client_tick + 1
        first.send({
            "type": "input", "tick": dash_tick,
            "movement": {"x": 0, "y": 0}, "aim": {"x": 0, "y": 1},
            "fire": False, "ability": True,
        })
        dash_snapshot = wait_for(first, lambda message: message.get("type") == "snapshot" and
                                 next((player["velocity"]["y"] for player in message["players"]
                                       if player["playerId"] == first_join["playerId"]), 0) > 5)
        assert next(player for player in dash_snapshot["players"] if player["playerId"] == first_join["playerId"])["velocity"]["y"] > 5, dash_snapshot
        time.sleep(0.02)
        first.send({
            "type": "input", "tick": dash_tick + 1,
            "movement": {"x": 0, "y": 0}, "aim": {"x": 0, "y": 1},
            "fire": False, "ability": False,
        })

        # Resume a dropped socket into its exact slot with its authoritative state intact.
        last_real_player = first_room_players[-1]["playerId"]
        before_disconnect = wait_for(first, lambda message: message.get("type") == "snapshot")
        prior_state = next(player for player in before_disconnect["players"] if player["playerId"] == last_real_player)
        resume_identity = {"playerId": clients[7]._identity_player_id,
                           "accessToken": clients[7]._identity_access_token}
        clients[7].close()
        clients[7] = None
        resumed, resumed_join = join_identity(address, 8, resume_identity)
        clients[7] = resumed
        assert resumed_join["roomId"] == first_join["roomId"] and resumed_join["playerId"] == last_real_player, resumed_join
        resumed_snapshot = wait_for(resumed, lambda message: message.get("type") == "snapshot" and
                                    any(player["playerId"] == last_real_player for player in message.get("players", [])))
        resumed_state = next(player for player in resumed_snapshot["players"] if player["playerId"] == last_real_player)
        assert resumed_state["archetype"] == prior_state["archetype"] and \
               resumed_state["health"] <= prior_state["health"] and \
               resumed_state["gems"] >= prior_state["gems"], (prior_state, resumed_state)
        resumed.close()
        clients[7] = None
        grace = float(os.environ.get("REALTIME_DISCONNECT_GRACE_SECONDS", "30"))
        time.sleep(grace + 0.3)
        after_expiry = wait_for(first, lambda message: message.get("type") == "snapshot" and
                                all(player["playerId"] != last_real_player for player in message["players"]))
        assert len([player for player in after_expiry["players"] if player["playerId"] > 0]) == 7, after_expiry
        fresh, fresh_join = join_identity(address, 8, resume_identity)
        clients[7] = fresh
        assert fresh_join["roomId"] != first_join["roomId"] and fresh_join["playerId"] != last_real_player, fresh_join

        # Keep only one first-room socket open while testing the long match clock,
        # so unread snapshots on idle clients do not apply TCP backpressure.
        for index in range(1, 7):
            clients[index].close()
            clients[index] = None

        respawn_elapsed = moved["elapsedSeconds"] + 12.2
        before_respawn = wait_for(first, lambda message: message.get("type") == "snapshot" and
                                  message.get("elapsedSeconds", 0) >= moved["elapsedSeconds"] + 11.5, timeout=15)
        assert all(power_up["pickupId"] != 1 for power_up in before_respawn["powerUps"]), before_respawn
        after_respawn = wait_for(first, lambda message: message.get("type") == "snapshot" and
                                 message.get("elapsedSeconds", 0) >= respawn_elapsed, timeout=4)
        respawned_mega = next(power_up for power_up in after_respawn["powerUps"] if power_up["pickupId"] == 1)
        assert respawned_mega["type"] == "MegaBullet", respawned_mega
        assert respawned_mega["position"] == {"x": 0, "y": -4}, respawned_mega
        assert {power_up["pickupId"] for power_up in after_respawn["powerUps"]} == {1, 2, 3}, after_respawn

        collapse = wait_for(first, lambda message: message.get("type") == "snapshot" and
                            message.get("elapsedSeconds", 0) >= 30, timeout=35)
        assert collapse["phase"] == "collapse", collapse
        assert 6.3 < collapse["arenaBounds"]["halfWidth"] < 6.5, collapse
        next_boundary = wait_for(first, lambda message: message.get("type") == "snapshot" and
                                 message.get("elapsedSeconds", 0) >= 35, timeout=7)
        assert next_boundary["phase"] == "collapse", next_boundary
        assert 5.3 < next_boundary["arenaBounds"]["halfWidth"] < 5.6, next_boundary
        assert next_boundary["arenaBounds"]["halfWidth"] < collapse["arenaBounds"]["halfWidth"], next_boundary

        sudden_death = wait_for(first, lambda message: message.get("type") == "snapshot" and
                                message.get("elapsedSeconds", 0) >= 60, timeout=27)
        assert sudden_death["phase"] == "sudden_death", sudden_death
        assert sudden_death["arenaBounds"] == {"halfWidth": 1.5, "halfHeight": 1.5}, sudden_death
        assert sudden_death["gems"] and all(gem["value"] == 2 for gem in sudden_death["gems"]), sudden_death

        finished = wait_for(first, lambda message: message.get("type") == "snapshot" and message.get("phase") == "finished", timeout=40)
        first_final = next(player for player in finished["players"] if player["playerId"] == first_join["playerId"])
        expected_award = min(200, first_final["gems"] + first_final["eliminations"] * 10)
        receipt = wait_for(first, lambda message: message.get("type") == "match_result", timeout=2)
        assert receipt["matchId"] == first_join["roomId"], receipt
        assert receipt["playerId"] == first_join["_externalPlayerId"], receipt
        assert receipt["gems"] == first_final["gems"] and receipt["eliminations"] == first_final["eliminations"], receipt
        assert receipt["award"] == expected_award, receipt
        assert receipt["profile"]["matchesPlayed"] == 1, receipt
        assert receipt["profile"]["battleCoins"] == expected_award, receipt
        duplicate_deadline = time.monotonic() + 0.35
        while time.monotonic() < duplicate_deadline:
            try:
                message = first.receive()
            except socket.timeout:
                continue
            assert message.get("type") != "match_result", message

        print("Realtime WebSocket smoke passed: pre-join timeout, authenticated identity/matchmaking, 8-player cap, 30 Hz snapshots, server gem/power-up collection, Nitro/Mega-Bullet/Magnet effects, 12-second respawn, doubled sudden-death gems, authoritative combat, idempotent server-issued match reward receipt, boundary steps, bots, disconnect, and invalid-input rejection.")
    finally:
        for client in clients:
            if client is not None:
                client.close()


if __name__ == "__main__":
    main()
