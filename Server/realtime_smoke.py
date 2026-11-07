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
            return {"type": "close"}
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


def player_id(index):
    return f"{index:032x}"


def join(address, index, origin=None, archetype=None):
    client = WebSocketClient(address, origin=origin)
    message = {"type": "join", "playerId": player_id(index), "displayName": f"Smoke {index}"}
    if archetype is not None:
        message["archetype"] = archetype
    client.send(message)
    response = client.receive()
    assert response["type"] == "joined", response
    assert response["tickRate"] == 30, response
    assert isinstance(response["playerId"], int), response
    assert len(response["roomId"]) == 32, response
    return client, response


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
        time.sleep(0.035)
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


def fire_until_hit(client, snapshot, shooter_id, target_id, previous_health, last_client_tick):
    """Keep aim updated against the moving authoritative bots until a shot lands."""
    deadline = time.monotonic() + 3
    current = snapshot
    while time.monotonic() < deadline:
        shooter = next(player for player in current["players"] if player["playerId"] == shooter_id)
        target = next(player for player in current["players"] if player["playerId"] == target_id)
        aim_x = target["position"]["x"] - shooter["position"]["x"]
        aim_y = target["position"]["y"] - shooter["position"]["y"]
        length = max(0.0001, math.hypot(aim_x, aim_y))
        input_tick = max(last_client_tick + 1, current["tick"] + 1)
        time.sleep(0.02)  # Respect the server's 60 Hz per-player input limit.
        client.send({
            "type": "input", "tick": input_tick,
            "movement": {"x": 0, "y": 0},
            "aim": {"x": aim_x / length, "y": aim_y / length},
            "fire": True, "ability": False,
        })
        last_client_tick = input_tick
        current = wait_for(client, lambda message: message.get("type") == "snapshot" and
                           message.get("tick", 0) > current["tick"], timeout=1)
        target = next((player for player in current["players"] if player["playerId"] == target_id), None)
        if target is None or target["health"] < previous_health:
            return current, target, last_client_tick
    raise AssertionError(f"Timed out waiting for moving bot to be hit; last snapshot: {current}")


def main():
    address = sys.argv[1].removeprefix("http://").removeprefix("ws://").rstrip("/")
    clients = []
    first_room_player_ids = []
    try:
        try:
            WebSocketClient(address, origin="https://untrusted.invalid")
            raise AssertionError("WebSocket accepted an origin outside Cors__AllowedOrigins")
        except ConnectionError as rejected:
            assert "403" in str(rejected), rejected

        first, first_join = join(address, 1, origin="http://localhost:5173")
        clients.append(first)
        first_room_player_ids.append(first_join["playerId"])
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

        # Use the Spinner near the right side so authoritative recoil moves it
        # back into the arena while the bots keep moving.
        combat_shooter_id = first_room_player_ids[2]
        target_id = clear_shot_target(moved, combat_shooter_id)["playerId"]
        previous_health = next(player["health"] for player in moved["players"] if player["playerId"] == target_id)
        spinner = clients[2]
        hit, target_state, spinner_last_client_tick = fire_until_hit(
            spinner, moved, combat_shooter_id, target_id, previous_health, 19)
        impulse_speed = math.hypot(target_state["velocity"]["x"], target_state["velocity"]["y"])
        assert target_state["health"] < previous_health and impulse_speed > 0.5, target_state
        last_client_tick = 19

        second.send({
            "type": "input", "tick": 20,
            "movement": {"x": 0, "y": 0}, "aim": {"x": 0, "y": 1},
            "fire": False, "ability": True,
        })
        heavy_shield = wait_for(second, lambda message: message.get("type") == "snapshot" and
                                next((player["shielded"] for player in message["players"]
                                      if player["playerId"] == second_join["playerId"]), False))
        assert next(player for player in heavy_shield["players"] if player["playerId"] == second_join["playerId"])["shielded"], heavy_shield

        spinner_id = first_room_player_ids[2]
        spinner.send({
            "type": "input", "tick": spinner_last_client_tick + 1,
            "movement": {"x": 0, "y": 0}, "aim": {"x": 0, "y": 1},
            "fire": False, "ability": True,
        })
        whirlwind = wait_for(spinner, lambda message: message.get("type") == "snapshot" and
                             next((player.get("whirling", False) for player in message["players"]
                                   if player["playerId"] == spinner_id), False))
        assert next(player for player in whirlwind["players"] if player["playerId"] == spinner_id)["archetype"] == "RicochetSpinner", whirlwind

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

        # Close the eighth real client in the first room and verify it disappears from snapshots.
        last_real_player = first_room_players[-1]["playerId"]
        clients[7].close()
        clients[7] = None
        after_leave = wait_for(first, lambda message: message.get("type") == "snapshot" and
                               all(player["playerId"] != last_real_player for player in message["players"]))
        assert len([player for player in after_leave["players"] if player["playerId"] > 0]) == 7, after_leave

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

        print("Realtime WebSocket smoke passed: matchmaking, 8-player cap, 30 Hz snapshots, server gem/power-up collection, Nitro/Mega-Bullet/Magnet effects, 12-second respawn, doubled sudden-death gems, authoritative combat, boundary steps, bots, disconnect, and invalid-input rejection.")
    finally:
        for client in clients:
            if client is not None:
                client.close()


if __name__ == "__main__":
    main()
