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

def encode_raw_state(db_obj: dict, game_state: dict) -> dict:
    """
    Takes the whole raw game state, pulls out ONLY the lists that match our 
    database namespaces, translates strings to integers via RAM, and ignores everything else.
    """
    # Create a copy so we don't accidentally mutate the original socket frame data
    encoded_state = game_state.copy()
    
    # Loop over the namespaces specified in your configs
    for ns in DEFAULT_NAMESPACES:
        # If the incoming game state has a matching key and it's a list (e.g. game_state["cards"] = [...])
        if ns in game_state and isinstance(game_state[ns], list):
            encoded_list = []
            
            for item in game_state[ns]:
                # Hand it off to your optimized lookup function
                item_id = get_or_create_id(db_obj, ns, str(item))
                encoded_list.append(item_id)
            
            # Replace the old raw text list with your clean integer list
            encoded_state[ns] = encoded_list
            
    return encoded_state