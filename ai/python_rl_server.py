import argparse
import json
import socket
import sys

DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 12345


def parse_args():
    parser = argparse.ArgumentParser(description="TCP RL server for Slay the Spire 2 mod handshake and JSON state ingestion.")
    parser.add_argument("--host", default=DEFAULT_HOST, help="Host to bind to")
    parser.add_argument("--port", default=DEFAULT_PORT, type=int, help="Port to bind to")
    parser.add_argument("--mode", choices=["training", "test"], help="Initial mode to send to the game client")
    return parser.parse_args()


def choose_mode(args):
    if args.mode:
        return args.mode
    while True:
        value = input("Enter mode ('training' or 'test'): ").strip().lower()
        if value in ("training", "test"):
            return value
        print("Invalid mode. Please enter 'training' or 'test'.")


def handle_client(conn, address, mode):
    print(f"Client connected from {address}")
    try:
        conn.sendall((mode + "\n").encode("utf-8"))
        print(f"Sent init mode: {mode}")
        with conn.makefile("r", encoding="utf-8", newline="\n") as reader:
            received = 0
            while True:
                line = reader.readline()
                if not line:
                    print("Connection closed by client.")
                    break
                line = line.strip()
                if not line:
                    continue

                received += 1
                try:
                    state = json.loads(line)
                except json.JSONDecodeError as err:
                    print(f"[WARN] Received invalid JSON on line {received}: {err}")
                    continue

                print(f"[STATE {received}] round={state.get('round')} energy={state.get('energy')} hand_count={len(state.get('hand', []))}")

    except (ConnectionResetError, BrokenPipeError):
        print("Client disconnected unexpectedly.")
    except Exception as err:
        print(f"[ERROR] Client handler failed: {err}")
    finally:
        conn.close()


def run_server(host, port, mode):
    print(f"Starting RL server on {host}:{port}")
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as server_socket:
        server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        server_socket.bind((host, port))
        server_socket.listen(1)
        print("Waiting for game client to connect...")

        while True:
            try:
                conn, address = server_socket.accept()
                handle_client(conn, address, mode)
                print("Waiting for a new client connection...")
            except KeyboardInterrupt:
                print("Server interrupted by user. Shutting down.")
                return
            except Exception as err:
                print(f"[ERROR] Server error: {err}")
                print("Restarting accept loop...")


if __name__ == "__main__":
    args = parse_args()
    selected_mode = choose_mode(args)
    try:
        run_server(args.host, args.port, selected_mode)
    except KeyboardInterrupt:
        print("Server stopped.")
        sys.exit(0)
