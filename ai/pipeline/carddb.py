import json
from pathlib import Path
from typing import Any
from paramaters.configs import DEFAULT_NAMESPACES

DEFAULT_DB_PATH = Path(__file__).with_name("carddb.json")

def save_database(db_obj: dict[str, Any], db_path: str | Path = DEFAULT_DB_PATH) -> None:
    """
    Call this whenever you want to write your RAM database object back to disk.
    """
    db_file = Path(db_path)
    db_file.parent.mkdir(parents=True, exist_ok=True)
    with db_file.open("w", encoding="utf-8") as file:
        json.dump(db_obj, file, indent=2, sort_keys=True)

def get_or_create_id(db_obj: dict, namespace: str, name: str, db_path: str | Path = DEFAULT_DB_PATH) -> int:
    """
    Looks up a name in your RAM database object.
    If it doesn't exist, it logs it, saves the file to disk, updates the RAM object, 
    and returns the new ID. Completely skips disk I/O if the name is already known.
    """
    # 1. Point straight to the right bucket in your RAM object
    ns = db_obj["namespaces"][namespace]
    values = ns["values"]
    
    # 2. FAST PATH: If it's already in RAM, return it instantly (No disk slowdown!)
    if name in values:
        return int(values[name])
        
    # 3. SLOW PATH: It's brand new. Calculate the new ID
    new_id = int(ns["next_id"])
    
    # 4. Update the live RAM object instantly
    values[name] = new_id
    ns["next_id"] = new_id + 1
    
    # 5. Write the updated RAM object to the JSON file so it's permanently saved
    save_database(db_obj, db_path)
    print(f"✨ Discovered new {namespace}: '{name}' locked to ID {new_id} and saved to disk.")
    
    return new_id
    
def boot_database(db_path: str | Path = DEFAULT_DB_PATH) -> dict[str, Any]:
    """
    Call this when the AI boots up. 
    It reads the JSON file and returns the entire database object to be saved in RAM.
    """
    db_file = Path(db_path)
    
    # If the file doesn't exist yet, return a clean blueprint structure
    if not db_file.exists():
        return {
            "version": 2,
            "namespaces": {name: {"next_id": 1, "values": {}} for name in DEFAULT_NAMESPACES}
        }
        
    # If it does exist, read it from disk and hand it directly back
    with db_file.open("r", encoding="utf-8") as file:
        return json.load(file)
    
def flatten_game_state(clean_frame: dict) -> list[float]:
    """
    Takes your clean integer dictionary and squashes it into a 
    flat, fixed-size list of numbers that PyTorch can understand.
    """
    vector = []
    
    # 1. Add raw numeric stats first (Safely extraction based on structural keys)
    vector.append(float(clean_frame.get("round", 0)))
    vector.append(float(clean_frame.get("energy", 0)))
    
    # Extract embedded numbers out of the nested player_stats dictionary
    player_stats = clean_frame.get("player_stats", {})
    vector.append(float(player_stats.get("hp", 0)))
    vector.append(float(player_stats.get("block", 0)))
    
    # Extract maps numbers
    map_data = clean_frame.get("map", {})
    vector.append(float(map_data.get("act", 0))) # Integer ID of the current Act
    vector.append(float(map_data.get("act_floor", 0)))

    # 2. Process Hand Cards (Looking for "hand", fixed to 10 slots)
    hand_cards = clean_frame.get("hand", [])
    max_hand_slots = 10
    for i in range(max_hand_slots):
        if i < len(hand_cards):
            card_dict = hand_cards[i]
            # Safely grab the integer ID out of the nested dictionary
            vector.append(float(card_dict.get("id", 0)))
        else:
            vector.append(0.0)

    # 3. Process Enemies (Fixed to max 3 enemies on screen to keep length rigid)
    enemies = clean_frame.get("enemies", [])
    max_enemies = 3
    for i in range(max_enemies):
        if i < len(enemies):
            enemy = enemies[i]
            vector.append(float(enemy.get("id", 0)))
            vector.append(float(enemy.get("hp", 0)))
            vector.append(float(enemy.get("block", 0)))
        else:
            # Pad missing enemy slots with 3 zeros (ID, HP, Block)
            vector.extend([0.0, 0.0, 0.0])
            
    return vector

def encode_raw_state(db_obj: dict, game_state: dict) -> dict:
    """
    Takes the whole raw game state and processes flat lists, 
    nested player dictionaries, complex enemies, and the map structure.
    """
    encoded_state = game_state.copy()
    
    # --- STEP 1: Process Flat and Structured Card Lists ---
    card_namespaces = ["hand", "draw_pile", "discard_pile"]
    for ns in card_namespaces:
        if ns in game_state and isinstance(game_state[ns], list):
            encoded_cards = []
            for item in game_state[ns]:
                if isinstance(item, dict):  # NEW: Handle your C# dictionary structure!
                    item_copy = item.copy()
                    if "id" in item_copy:
                        item_copy["id"] = get_or_create_id(db_obj, "cards", str(item_copy["id"]))
                    encoded_cards.append(item_copy)
                else:
                    # Fallback for simple strings (like relics or potions if they remain strings)
                    encoded_cards.append(get_or_create_id(db_obj, "cards", str(item)))
            encoded_state[ns] = encoded_cards

    # (Other lists like relics, potions, orbs can remain using your old string loop)
    other_flat_namespaces = ["relics", "potions", "orbs"]
    for ns in other_flat_namespaces:
        if ns in game_state and isinstance(game_state[ns], list):
            encoded_state[ns] = [
                get_or_create_id(db_obj, ns, str(item)) for item in game_state[ns]
            ]

    # --- STEP 2: Process Nested Player Stats ---
    if "player_stats" in game_state and isinstance(game_state["player_stats"], dict):
        player = game_state["player_stats"].copy()
        if "id" in player:
            player["id"] = get_or_create_id(db_obj, "characters", player["id"])
            
        if "powers" in player and isinstance(player["powers"], list):
            encoded_powers = []
            for p in player["powers"]:
                if isinstance(p, dict) and "id" in p:
                    p_copy = p.copy()
                    p_copy["id"] = get_or_create_id(db_obj, "powers", p["id"])
                    encoded_powers.append(p_copy)
            player["powers"] = encoded_powers
        encoded_state["player_stats"] = player

    # --- STEP 3: Process Nested Enemy Lists ---
    if "enemies" in game_state and isinstance(game_state["enemies"], list):
        encoded_enemies = []
        for enemy in game_state["enemies"]:
            if isinstance(enemy, dict):
                enemy_copy = enemy.copy()
                if "id" in enemy_copy:
                    enemy_copy["id"] = get_or_create_id(db_obj, "enemies", enemy_copy["id"])
                
                if "intent" in enemy_copy and isinstance(enemy_copy["intent"], dict):
                    intent_copy = enemy_copy["intent"].copy()
                    if "move_id" in intent_copy:
                        intent_copy["move_id"] = get_or_create_id(db_obj, "enemy_moves", intent_copy["move_id"])
                    enemy_copy["intent"] = intent_copy
                
                if "powers" in enemy_copy and isinstance(enemy_copy["powers"], list):
                    encoded_enemy_powers = []
                    for p in enemy_copy["powers"]:
                        if isinstance(p, dict) and "id" in p:
                            p_copy = p.copy()
                            p_copy["id"] = get_or_create_id(db_obj, "powers", p["id"])
                            encoded_enemy_powers.append(p_copy)
                    enemy_copy["powers"] = encoded_enemy_powers
                encoded_enemies.append(enemy_copy)
        encoded_state["enemies"] = encoded_enemies

    # --- STEP 4: Process the Map Structure ---
    if "map" in game_state and isinstance(game_state["map"], dict):
        map_copy = game_state["map"].copy()
        if "act" in map_copy:
            map_copy["act"] = get_or_create_id(db_obj, "acts", map_copy["act"])
        if "current_room" in map_copy:
            map_copy["current_room"] = get_or_create_id(db_obj, "encounters", map_copy["current_room"])
            
        # Optional: Clean up the point_type strings inside the room matrix if you track them
        if "current_point" in map_copy and isinstance(map_copy["current_point"], dict):
            cp = map_copy["current_point"].copy()
            if "point_type" in cp:
                cp["point_type"] = get_or_create_id(db_obj, "encounters", cp["point_type"])
            map_copy["current_point"] = cp
            
        encoded_state["map"] = map_copy
            
    return encoded_state

def generate_action_mask(clean_frame: dict) -> list[int]:
    """
    Returns a list of 11 integers (1 for legal, 0 for illegal).
    Uses the game's native 'is_playable' property!
    """
    mask = [0] * 11
    mask[10] = 1  # End Turn is always legal
    
    hand_cards = clean_frame.get("hand", [])
    
    for i in range(10):
        if i < len(hand_cards):
            card_dict = hand_cards[i]
            
            # Look directly at what the C# engine told us!
            if card_dict.get("is_playable", False) == True:
                mask[i] = 1  # Legal move!
            else:
                mask[i] = 0  # Illegal move according to game rules
        else:
            mask[i] = 0  # No card in this slot
            
    return mask