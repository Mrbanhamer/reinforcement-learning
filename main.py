import socket
import json

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.bind(("127.0.0.1", 5005))

while True:
    data, addr = sock.recvfrom(4096)
    message = data.decode('utf-8')
    
    try:
        # Convert the text from the game into a Python Dictionary
        game_state = json.loads(message)
        
        # Simple Logic Test:
        if game_state['hp'] < 20:
            print("AI THINKS: We are dying! Play block cards!")
        else:
            print(f"AI THINKS: HP is {game_state['hp']}. Keep attacking.")
            
    except:
        print(f"Raw message from Spire: {message}")