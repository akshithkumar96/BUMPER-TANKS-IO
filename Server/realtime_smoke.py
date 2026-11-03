#!/usr/bin/env python3
"""Package-free WebSocket integration smoke test for the ASP.NET realtime hub."""

import base64
import json
import os
import socket
import struct
import sys
import time


class WebSocketClient:
    def __init__(self, address, origin=None):
        host, port = address.rsplit(":", 1)
        self.socket = socket.create_connection((host, int(port)), timeout=5)
        self.socket.settimeout(5)
        self.buffer = bytearray()
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
            return self.receive()
        if opcode != 1:
            return self.receive()
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
        try:
            self._send_control(8, struct.pack("!H", 1000))
        except OSError:
            pass
        self.socket.close()


def player_id(index):
    return f"{index:032x}"


def join(address, index, origin=None):
    client = WebSocketClient(address, origin=origin)
    client.send({"type": "join", "playerId": player_id(index), "displayName": f"Smoke {index}"})
    response = client.receive()
    assert response["type"] == "joined", response
    assert response["tickRate"] == 30, response
    assert isinstance(response["playerId"], int), response
    assert len(response["roomId"]) == 32, response
    return client, response


def wait_for(client, predicate, timeout=5):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        message = client.receive()
        if predicate(message):
            return message
    raise AssertionError("Timed out waiting for realtime server message")


def assert_no_snapshot(client, delay=0.35):
    client.socket.settimeout(delay)
    try:
        message = client.receive()
        if message.get("type") == "snapshot":
            raise AssertionError("Underfilled room started before the 2.5-second backfill window")
    except socket.timeout:
        pass
    finally:
        client.socket.settimeout(5)


def main():
    address = sys.argv[1].removeprefix("http://").removeprefix("ws://").rstrip("/")
    clients = []
    try:
        try:
            WebSocketClient(address, origin="https://untrusted.invalid")
            raise AssertionError("WebSocket accepted an origin outside Cors__AllowedOrigins")
        except ConnectionError as rejected:
            assert "403" in str(rejected), rejected

        first, first_join = join(address, 1, origin="http://localhost:5173")
        clients.append(first)
        second, second_join = join(address, 2)
        clients.append(second)
        assert second_join["roomId"] == first_join["roomId"], second_join
        assert_no_snapshot(first)

        for index in range(3, 9):
            client, response = join(address, index)
            clients.append(client)
            assert response["roomId"] == first_join["roomId"], response

        ninth, ninth_join = join(address, 9)
        clients.append(ninth)
        assert ninth_join["roomId"] != first_join["roomId"], ninth_join

        first.send({
            "type": "input", "tick": 1,
            "movement": {"x": 1, "y": 0}, "aim": {"x": 0, "y": 1},
            "fire": False, "ability": False,
        })
        moved = wait_for(first, lambda message: message.get("type") == "snapshot" and
                         any(player["playerId"] == first_join["playerId"] and player["position"]["x"] > -3.75
                             for player in message["players"]))
        first_room_players = [player for player in moved["players"] if player["playerId"] > 0]
        assert len(first_room_players) == 8, moved

        # Close the eighth real client in the first room and verify it disappears from snapshots.
        last_real_player = first_room_players[-1]["playerId"]
        clients[7].close()
        clients[7] = None
        after_leave = wait_for(first, lambda message: message.get("type") == "snapshot" and
                               all(player["playerId"] != last_real_player for player in message["players"]))
        assert len([player for player in after_leave["players"] if player["playerId"] > 0]) == 7, after_leave

        bots = wait_for(ninth, lambda message: message.get("type") == "snapshot" and
                        len(message.get("players", [])) == 8, timeout=4)
        assert len(bots["players"]) == 8, bots

        tenth, tenth_join = join(address, 10)
        clients.append(tenth)
        eleventh, eleventh_join = join(address, 11)
        clients.append(eleventh)
        assert tenth_join["roomId"] == eleventh_join["roomId"], eleventh_join
        assert_no_snapshot(tenth)
        two_player_fill = wait_for(tenth, lambda message: message.get("type") == "snapshot" and
                                   len(message.get("players", [])) == 8, timeout=4)
        assert len([player for player in two_player_fill["players"] if player["playerId"] > 0]) == 2, two_player_fill
        assert len([player for player in two_player_fill["players"] if player["playerId"] < 0]) == 6, two_player_fill

        invalid, _ = join(address, 12)
        clients.append(invalid)
        invalid.send({
            "type": "input", "tick": -1,
            "movement": {"x": 0, "y": 0}, "aim": {"x": 0, "y": 1},
            "fire": False, "ability": False,
        })
        rejection = wait_for(invalid, lambda message: message.get("type") == "error", timeout=2)
        assert rejection["code"] == "invalid_input", rejection
        print("Realtime WebSocket smoke passed: matchmaking, 8-player cap, 30 Hz snapshots, movement, bot fill, disconnect, and invalid-input rejection.")
    finally:
        for client in clients:
            if client is not None:
                client.close()


if __name__ == "__main__":
    main()
