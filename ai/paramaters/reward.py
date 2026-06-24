
def beatboss(floor):
    if floor == 18:
        return 250
    if floor == 34:
        return 500
    # if ai gives up after floor 34 then floor 47 doesnt actually exist.
    if floor == 47:
        return 2000
    
def normalencounter(encounter):
    if encounter == "elite" and enemy["hp"] == 0:
        return 20
    if encounter == "normal":
        return 10

def death(hp):
    if hp == 0:
        return -100
    else:
        return 0