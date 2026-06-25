from pipeline.neural_network import SlayTheSpireBrain
from socketing.socket import making_socket, receiving_message, send_message
from pipeline.carddb import boot_database, encode_raw_state, flatten_game_state
import torch

device = torch.device("cuda" if torch.cuda.is_available() else "cpu")


def MainLoop(mode):
    making_socket(mode)  # Start the socket server and wait for a client to connect
    brain = SlayTheSpireBrain()
    brain = brain.to(device)  # Move the neural network to the appropriate device (CPU or GPU)
    db = boot_database()  # Load the database into RAM

    while True:
        # 1. Get raw state from the game socket
        current_game_state = receiving_message()  
        
        # 2. Swap text strings out for clean numbers using RAM database
        clean_gamestate_frame = encode_raw_state(db, current_game_state.__dict__)
        
        # 3. Strip keys, flatten to fixed-size list, and create your GPU tensor
        flat_list = flatten_game_state(clean_gamestate_frame) 
        state_tensor = torch.tensor(flat_list, dtype=torch.float32).to(device)
        
        # 4. Feed the raw numbers to the brain to get decisions
        action_scores = brain(state_tensor)
        
        # 5. Extract the index of the highest score
        chosen_action = torch.argmax(action_scores).item()
        
        # 6. Command the game to act
        send_message(f"Chosen action: {chosen_action}")
        print(f"Chosen action: {chosen_action}")