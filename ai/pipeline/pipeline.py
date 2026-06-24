from ai.pipeline import carddb
from pipeline.neural_network import SlayTheSpireBrain
from socketing.socket import making_socket, receiving_message, send_message
from pipeline.carddb import boot_database, encode_raw_state
import torch

def MainLoop(mode):
    making_socket(mode)  # Start the socket server and wait for a client to connect
    brain = SlayTheSpireBrain()
    db = boot_database()  # Load the database into RAM
    #initializations
    while True:
        # Example game state (these values would come from the vision module in a real implementation)
        current_game_state = receiving_message()  # Receive the current game state from the socket
        clean_gamestate_frame = encode_raw_state(db, current_game_state.__dict__)
        
        # Convert the game state to a tensor and get the action scores from the neural network
        state_tensor = clean_gamestate_frame.to_tensor()
        action_scores = brain(state_tensor)
        
        # Choose the action with the highest score
        # output score
        chosen_action = torch.argmax(action_scores).item()
        
        # Here you would send the chosen action to the game using the vision module
        send_message(f"Chosen action: {chosen_action}")
        print(f"Chosen action: {chosen_action}")