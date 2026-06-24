import argparse
import json
import os
import sys
import time

_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
_REPO_ROOT = os.path.dirname(os.path.dirname(_SCRIPT_DIR))
if _REPO_ROOT not in sys.path:
    sys.path.insert(0, _REPO_ROOT)
if _SCRIPT_DIR in sys.path:
    sys.path.remove(_SCRIPT_DIR)

from ai.paramaters.configs import DEFAULT_HOST, DEFAULT_PORT
import socket


def parse_args():
    parser = argparse.ArgumentParser(
        description="Slay the Spire 2 socket observer test server (compatible with DefectAI C# mod)."
    )
    parser.add_argument("--host", default=DEFAULT_HOST, help="Host to bind to")
    parser.add_argument("--port", default=DEFAULT_PORT, type=int, help="Port to bind to")
    parser.add_argument(
        "--mode",
        choices=["training", "test"],
        default="test",
        help="Mode sent to the C# mod during handshake",
    )
    parser.add_argument(
        "--raw",
        action="store_true",
        help="Print raw incoming lines instead of parsed summary",
    )
    return parser.parse_args()


def summarize_state(payload: dict) -> str:
    round_num = payload.get("round", "?")
    energy = payload.get("energy", "?")
    hand_count = len(payload.get("hand", []) or [])
    room = (payload.get("map") or {}).get("current_room", "?")
    encounter = payload.get("current_encounter", "?")
    return f"round={round_num} energy={energy} hand={hand_count} room={room} encounter={encounter}"


def recv_lines(sock: socket.socket, buffer: str):
    data = sock.recv(65536)
    if not data:
        return None, buffer
    buffer += data.decode("utf-8", errors="replace")
    lines = []
    while "\n" in buffer:
        line, buffer = buffer.split("\n", 1)
        line = line.strip()
        if line:
            lines.append(line)
    return lines, buffer


def run_observer(host: str, port: int, mode: str, raw: bool):
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as server:
        server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        server.bind((host, port))
        server.listen(1)

        print(f"Listening on {host}:{port}")
        print("Waiting for the Slay the Spire 2 mod to connect...")
        client, addr = server.accept()

        with client:
            print(f"Connected: {addr[0]}:{addr[1]}")
            client.settimeout(0.25)
            buffer = ""
            message_count = 0
            confirmed = False
            last_send = 0.0

            while not confirmed:
                now = time.monotonic()
                if now - last_send >= 0.25:
                    client.sendall((mode + "\n").encode("utf-8"))
                    print(f"Handshake sent: {mode}")
                    last_send = now

                try:
                    lines, buffer = recv_lines(client, buffer)
                except socket.timeout:
                    continue

                if lines is None:
                    print("Disconnected before handshake confirmation.")
                    return

                for line in lines:
                    if line.startswith("SOCKET_READY") or line.startswith("ACK") or line == "READY":
                        print(f"Handshake confirmed by mod: {line}")
                        confirmed = True
                        break
                    if raw:
                        print(f"[handshake] {line}")

            client.settimeout(None)
            print("Handshake complete; receiving live state.")

            while True:
                try:
                    lines, buffer = recv_lines(client, buffer)
                except socket.timeout:
                    continue

                if lines is None:
                    print("Disconnected by mod.")
                    break

                for line in lines:
                    message_count += 1
                    if raw:
                        print(f"[{message_count}] {line}")
                        continue

                    try:
                        payload = json.loads(line)
                        print(f"[{message_count}] {summarize_state(payload)}")
                    except json.JSONDecodeError:
                        print(f"[{message_count}] (non-json) {line}")


if __name__ == "__main__":
    args = parse_args()
    run_observer(args.host, args.port, args.mode, args.raw)