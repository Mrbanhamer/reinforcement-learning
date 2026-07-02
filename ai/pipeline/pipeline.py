from pipeline.neural_network import SlayTheSpireBrain
from socketing.socket import making_socket, receiving_message, send_message
from pipeline.carddb import boot_database, encode_raw_state, flatten_game_state, generate_action_mask
import torch
import time

device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
# gpu integration is crucial for performance, especially when processing large game states or running complex neural networks.

def MainLoop(mode):
    # initializations
    making_socket(mode)  # Start the socket server and wait for a client to connect
    brain = SlayTheSpireBrain() # Imports the actual brain for the ai
    brain = brain.to(device)  # Move the neural network to the appropriate device (CPU or GPU)
    db = boot_database()  # Load the database into RAM

    while True:
        # 1. Get raw state from the game socket
        current_game_state = receiving_message()  
        
        # 2. Swap text strings out for clean numbers using RAM database
        clean_gamestate_frame = encode_raw_state(db, current_game_state.__dict__)
        #reward_factor = rewards(clean_gamestate_frame)  # Placeholder for any reward shaping logic you might want to implement
        
        # 3. Strip keys, flatten to fixed-size list, and create your GPU tensor
        flat_list = flatten_game_state(clean_gamestate_frame) 

        # 4. Feed the tensor a action mask to avoid illegal moves (e.g., trying to play a card when your hand is empty)
        action_mask = generate_action_mask(flat_list)
        state_tensor = torch.tensor(action_mask, dtype=torch.float32).to(device)
        
        # 5. Feed the raw numbers to the brain to get decisions
        action_scores = brain(state_tensor)
        
        # 7. Extract the index of the highest score
        chosen_action = torch.argmax(action_scores).item()
        
        # 8. Command the game to act
        send_message(f"Chosen action: {chosen_action}")
        print(f"Chosen action: {chosen_action}")
        time.sleep(0.05)  # Add a small delay to prevent overwhelming the game with actions