import torch
import torch.nn as nn

class SlayTheSpireBrain(nn.Module):
    def __init__(self, input_size=14, output_size=5):
        super(SlayTheSpireBrain, self).__init__()

        self.brain_pipeline = nn.Sequential(
            # Layer 1: Expand inputs to a solid processing space
            nn.Linear(input_size, 256),
            nn.ReLU(),
            
            # Layer 2: Deeper relationships (e.g., combining Vulnerable debuff + Attack damage)
            nn.Linear(256, 256),
            nn.ReLU(),
            
            # Layer 3: Start squeezing the logic down
            nn.Linear(256, 128),
            nn.ReLU(),
            
            # Layer 4: Squeeze down to your exact action choices
            nn.Linear(128, output_size)
        )

        # Define the layers of your neural network
        # Linear layer means "fully connected"
        self.input_layer = nn.Linear(input_size, 32)
        self.exit = nn.Linear(16, output_size)

        self.activation = nn.ReLU()

    def forward(self, x):
        # This defines how data flows through the network when making a decision
        x = self.brain_pipeline(x)  # Pass through the defined layers
        return x # Returns the raw "scores" (logits) for each action
    
if __name__ == "__main__":
    # Example usage
    brain = SlayTheSpireBrain()
    example_input = torch.tensor([50, 100, 5, 3], dtype=torch.float32)  # Example game state
    output = brain(example_input)
    print(output)