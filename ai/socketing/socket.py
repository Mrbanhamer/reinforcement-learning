import socket
from ai.paramaters.configs import DEFAULT_HOST, DEFAULT_PORT

def making_socket():
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind((DEFAULT_HOST, DEFAULT_PORT))
    sock.listen(1)
    print("Server is listening...")

    client_socket, client_address = sock.accept()
    return client_socket, client_address


def receiving_message(client_socket):
    buffer = "" # 🟢 ADDED: Crucial for holding split TCP data streams
    while True:
        # 🟢 CHANGED TO .recv() because TCP doesn't use recvfrom()
        data = client_socket.recv(4096)
        if not data:
            print("Game disconnected.")
            break
            
        buffer += data.decode('utf-8')
        
        # 🟢 ADDED: Safely unpack full JSON chunks separated by '\n'
        while "\n" in buffer:
            message, buffer = buffer.split("\n", 1)
            if message.strip():
                game_state = json.loads(message)
                print(f"Received game state! Current HP: {game_state.get('hp')}")

def send_message(client_socket, message):
    client_socket.sendall(message.encode('utf-8'))