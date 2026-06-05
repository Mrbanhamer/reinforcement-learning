using Godot;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Entities.Players; 
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;

namespace DefectAIBridge;

[ModInitializer("OnModLoaded")]
public static class MyMod {
    private static readonly string StateFilePath = Path.Combine(OS.GetUserDataDir(), "sts2_state.json");
    // Additional debug copy in Documents to make it easy to find while testing
    private static readonly string DebugStateFilePath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "sts2_state_debug.json");

    public static void OnModLoaded() {
        // Hook natively into the game's global combat lifecycle events if available,
        // or register a hook listener. For basic telemetry updates, subscribe to action execution.
        GD.Print($">>> AI BRIDGE LOADED: state file will be written to {StateFilePath}");

        try {
            var executor = RunManager.Instance?.ActionExecutor;
            if (executor != null) {
                executor.AfterActionExecuted += OnGameActionExecuted;
                GD.Print(">>> AI BRIDGE: subscribed to ActionExecutor.AfterActionExecuted");
            } else {
                GD.Print(">>> AI BRIDGE WARNING: ActionExecutor instance unavailable");
            }
        } catch (Exception e) {
            GD.Print(">>> AI BRIDGE ERROR: could not subscribe to action executor - " + e.Message);
        }
    }

    // Call this from the game action executor or action completion hook.
    public static void OnActionExecuted(PlayerCombatState stateInstance) {
        var combatState = CombatManager.Instance?.DebugOnlyGetState();
        ProcessCombatTelemetry(stateInstance, combatState);
    }

    private static void OnGameActionExecuted(GameAction action) {
        try {
            var combatState = CombatManager.Instance?.DebugOnlyGetState();
            var playerState = combatState?.Players?.FirstOrDefault()?.PlayerCombatState;
            if (playerState != null) {
                OnActionExecuted(playerState);
            }
        } catch (Exception e) {
            GD.Print(">>> AI BRIDGE ERROR: OnGameActionExecuted failed - " + e.Message);
        }
    }

    private static void WriteStateFile(string json) {
        try {
            File.WriteAllText(StateFilePath, json, Encoding.UTF8);
            // Also write a debug copy to Documents so it's easy to locate while testing
            try { File.WriteAllText(DebugStateFilePath, json, Encoding.UTF8); } catch { }
            GD.Print($">>> AI BRIDGE: state file updated ({StateFilePath}) and debug copy ({DebugStateFilePath})");
        } catch (Exception e) {
            GD.Print(">>> AI BRIDGE ERROR: " + e.Message);
        }
    }

    /// <summary>
    /// This core scraper loop can be safely called directly by your game loop hook handlers
    /// whenever a PlayerCombatState instance updates its turn boundaries.
    /// </summary>
    public static void ProcessCombatTelemetry(PlayerCombatState stateInstance, CombatState combatState) {
        if (stateInstance == null) return;

        try {
            // 1. Core Resources
            int currentEnergy = stateInstance.Energy;
            int maxEnergy = stateInstance.MaxEnergy;

            // 2. Extract Card Piles
            List<string> handList = new List<string>();
            if (stateInstance.Hand?.Cards != null) {
                foreach (var card in stateInstance.Hand.Cards) {
                    if (card != null) handList.Add($@"""{card.Id}""");
                }
            }

            List<string> drawList = new List<string>();
            if (stateInstance.DrawPile?.Cards != null) {
                foreach (var card in stateInstance.DrawPile.Cards) {
                    if (card != null) drawList.Add($@"""{card.Id}""");
                }
            }

            List<string> discardList = new List<string>();
            if (stateInstance.DiscardPile?.Cards != null) {
                foreach (var card in stateInstance.DiscardPile.Cards) {
                    if (card != null) discardList.Add($@"""{card.Id}""");
                }
            }

            // 3. Scan for Osty (Pets list)
            int ostyHp = -1;
            int ostyMaxHp = -1;
            if (stateInstance.Pets != null && stateInstance.Pets.Count > 0) {
                var osty = stateInstance.Pets[0];
                if (osty != null) {
                    ostyHp = osty.CurrentHp;
                    ostyMaxHp = osty.MaxHp;
                }
            }

            // 4. Extract Live Orbs
            List<string> orbList = new List<string>();
            int orbCount = stateInstance.OrbQueue?.Orbs?.Count ?? 0;
            int orbCapacity = stateInstance.OrbQueue?.Capacity ?? 0;

            if (stateInstance.OrbQueue?.Orbs != null) {
                foreach (var orb in stateInstance.OrbQueue.Orbs) {
                    if (orb != null) orbList.Add($@"""{orb.Id}"""); 
                }
            }

            // 5. Build Player Creature State
            var playerCreature = combatState?.PlayerCreatures?.FirstOrDefault();
            string playerCreatureJson = SerializeCreature(playerCreature);

            // 6. Loop through Room Enemies to Scrape Health, Powers, and Intent
            List<string> enemyJsonList = new List<string>();
            var enemies = combatState?.Enemies;
            if (enemies != null) {
                foreach (var enemy in enemies) {
                    if (enemy != null && enemy.IsAlive) {
                        enemyJsonList.Add(SerializeCreature(enemy));
                    }
                }
            }

            // 7. Extract round number from combat state
            int roundNumber = combatState?.RoundNumber ?? 0;

            // 8. Flatten arrays to clean strings
            string handJson = string.Join(",", handList);
            string drawJson = string.Join(",", drawList);
            string discardJson = string.Join(",", discardList);
            string orbsJson = string.Join(",", orbList);
            string enemiesJson = string.Join(",", enemyJsonList);

            // 9. Serialize rewards and map state if available
            string rewardsJson = SerializeRewards();
            string mapJson = SerializeMapState();
            string relicsJson = SerializeRelics();
            string potionsJson = SerializePotions();
            string restSiteOptionsJson = SerializeRestSiteOptions();

            // 10. Assemble the Final JSON String using safe verbatim literals
            string json = $@"{{""round"": {roundNumber}, ""energy"": {currentEnergy}, ""max_energy"": {maxEnergy}, ""osty_hp"": {ostyHp}, ""osty_max_hp"": {ostyMaxHp}, ""orb_count"": {orbCount}, ""orb_capacity"": {orbCapacity}, ""orbs"": [{orbsJson}], ""hand"": [{handJson}], ""draw_pile"": [{drawJson}], ""discard_pile"": [{discardJson}], ""relics"": {relicsJson}, ""potions"": {potionsJson}, ""player_stats"": {playerCreatureJson}, ""enemies"": [{enemiesJson}], ""rewards"": {rewardsJson}, ""map"": {mapJson}, ""rest_site_options"": {restSiteOptionsJson}}}";

            // 11. Write the state JSON file so external AI can read the latest game state
            WriteStateFile(json);
        }
        catch (Exception ex) {
            GD.Print($"> don't break logic loop error: {ex.Message}");
        }
    }

    private static string SerializeRelics() {
        try {
            var runState = GetPrivateField(RunManager.Instance, "_runState");
            if (runState == null) return "[]";
            var playerCollection = GetPropertyOrFieldValue(runState, "PlayerCollectionProvider");
            if (playerCollection == null) {
                // Fallback: try to get player directly from combat manager
                var combatState = CombatManager.Instance?.DebugOnlyGetState();
                var players = combatState?.Players;
                var player = players?.FirstOrDefault();
                if (player != null) {
                    return SerializeRelicsFromPlayer(player);
                }
                return "[]";
            }
            // Try to get the first/active player
            var getPlayersMethod = playerCollection?.GetType().GetMethod("GetPlayers", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (getPlayersMethod != null) {
                var players = getPlayersMethod.Invoke(playerCollection, null) as IEnumerable;
                if (players != null) {
                    foreach (var p in players) {
                        return SerializeRelicsFromPlayer(p);
                    }
                }
            }
            return "[]";
        } catch {
            return "[]";
        }
    }

    private static string SerializeRelicsFromPlayer(object player) {
        if (player == null) return "[]";
        var relics = GetPropertyOrFieldValue(player, "Relics") as IEnumerable;
        if (relics == null) return "[]";
        var relicList = new List<string>();
        foreach (var relic in relics) {
            if (relic == null) continue;
            string relicId = GetPropertyOrFieldValue(relic, "Id")?.ToString() ?? "Unknown";
            relicList.Add(JsonSerializer.Serialize(relicId));
        }
        return $"[{string.Join(",", relicList)}]";
    }

    private static string SerializePotions() {
        try {
            var combatState = CombatManager.Instance?.DebugOnlyGetState();
            var players = combatState?.Players;
            var player = players?.FirstOrDefault();
            if (player == null) return "[]";
            var potions = GetPropertyOrFieldValue(player, "PotionSlots") as IEnumerable;
            if (potions == null) return "[]";
            var potionList = new List<string>();
            foreach (var potion in potions) {
                if (potion == null) continue;
                string potionId = GetPropertyOrFieldValue(potion, "Id")?.ToString() ?? "Empty";
                potionList.Add(JsonSerializer.Serialize(potionId));
            }
            return $"[{string.Join(",", potionList)}]";
        } catch {
            return "[]";
        }
    }

    private static string SerializeRestSiteOptions() {
        try {
            var runState = GetPrivateField(RunManager.Instance, "_runState");
            if (runState == null) return "[]";
            var currentRoom = GetPropertyOrFieldValue(runState, "CurrentRoom");
            if (currentRoom == null) return "[]";
            // Check if it's a RestSiteRoom
            if (!currentRoom.GetType().Name.Equals("RestSiteRoom", StringComparison.OrdinalIgnoreCase)) return "[]";
            var options = GetPropertyOrFieldValue(currentRoom, "Options") as IEnumerable;
            if (options == null) return "[]";
            var optionList = new List<string>();
            foreach (var option in options) {
                if (option == null) continue;
                string optionId = GetPropertyOrFieldValue(option, "OptionId")?.ToString() ?? "Unknown";
                string optionType = option.GetType().Name;
                optionList.Add($@"{{""type"": {JsonSerializer.Serialize(optionType)}, ""id"": {JsonSerializer.Serialize(optionId)}}}" );
            }
            return $"[{string.Join(",", optionList)}]";
        } catch {
            return "[]";
        }
    }

    private static string SerializeRewards() {
        try {
            var sync = RunManager.Instance?.RewardsSetSynchronizer;
            if (sync == null) return "null";
            var localPlayer = GetPropertyOrFieldValue(sync, "LocalPlayer");
            if (localPlayer == null) return "null";
            var getStateMethod = sync.GetType().GetMethod("GetRewardStateForPlayer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var playerRewardState = getStateMethod?.Invoke(sync, new[] { localPlayer });
            if (playerRewardState == null) return "[]";

            var rewardStateField = playerRewardState.GetType().GetField("rewardsStack", BindingFlags.NonPublic | BindingFlags.Instance);
            var rewardStateStack = rewardStateField?.GetValue(playerRewardState) as IEnumerable;
            if (rewardStateStack == null) return "[]";

            var rewardSetJson = new List<string>();
            foreach (var item in rewardStateStack) {
                if (item == null) continue;
                var setField = item.GetType().GetField("set", BindingFlags.NonPublic | BindingFlags.Instance);
                var rewardSet = setField?.GetValue(item);
                if (rewardSet == null) continue;
                rewardSetJson.Add(SerializeRewardSet(rewardSet));
            }

            return $"[{string.Join(",", rewardSetJson)}]";
        } catch {
            return "null";
        }
    }

    private static string SerializeRewardSet(object rewardSet) {
        if (rewardSet == null) return "null";

        object idObj = GetPropertyOrFieldValue(rewardSet, "Id");
        string idJson = idObj?.ToString() ?? "0";
        var room = GetPropertyOrFieldValue(rewardSet, "Room");
        string roomName = room?.GetType().Name ?? "Unknown";
        var rewards = GetPropertyOrFieldValue(rewardSet, "Rewards") as IEnumerable;

        var rewardJson = new List<string>();
        if (rewards != null) {
            foreach (var reward in rewards) {
                rewardJson.Add(SerializeReward(reward));
            }
        }

        return $@"{{""id"": {idJson}, ""room"": ""{roomName}"", ""rewards"": [{string.Join(",", rewardJson)}]}}";
    }

    private static string SerializeReward(object reward) {
        if (reward == null) return "null";

        var type = reward.GetType();
        string typeName = type.Name;
        string rewardType = GetPropertyOrFieldValue(reward, "RewardType")?.ToString() ?? "Unknown";
        string description = GetPropertyOrFieldValue(reward, "Description")?.ToString() ?? string.Empty;
        string iconPath = GetPropertyOrFieldValue(reward, "IconPath")?.ToString() ?? string.Empty;
        string iconPosition = GetPropertyOrFieldValue(reward, "IconPosition")?.ToString() ?? string.Empty;
        string detailsJson = SerializeRewardDetails(reward);

        return $@"{{""type"": {JsonSerializer.Serialize(typeName)}, ""reward_type"": {JsonSerializer.Serialize(rewardType)}, ""description"": {JsonSerializer.Serialize(description)}, ""icon_path"": {JsonSerializer.Serialize(iconPath)}, ""icon_position"": {JsonSerializer.Serialize(iconPosition)}, ""details"": {detailsJson}}}";
    }

    private static string SerializeRewardDetails(object reward) {
        if (reward == null) return "{}";
        var type = reward.GetType();
        var details = new Dictionary<string, string>();
        var candidateNames = new[] { "Card", "Relic", "Potion", "GoldAmount", "Amount", "CardId", "RelicId", "PotionId", "Description" };

        foreach (var name in candidateNames) {
            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null) continue;
            var value = prop.GetValue(reward);
            if (value == null) continue;
            if (value is string strValue) {
                details[name] = strValue;
            } else {
                details[name] = value.ToString();
            }
        }

        if (details.Count == 0) return "{}";

        var pairs = details.Select(kvp => $"{JsonSerializer.Serialize(kvp.Key)}: {JsonSerializer.Serialize(kvp.Value)}");
        return $"{{{string.Join(",", pairs)}}}";
    }

    private static string SerializeMapState() {
        try {
            var mapSync = RunManager.Instance?.MapSelectionSynchronizer;
            if (mapSync == null) return "null";
            var runState = GetPrivateField(mapSync, "_runState");
            if (runState == null) return "null";

            var map = GetPropertyOrFieldValue(runState, "Map");
            var currentCoord = GetPropertyOrFieldValue(runState, "CurrentMapCoord");
            var currentPoint = GetPropertyOrFieldValue(runState, "CurrentMapPoint");
            var currentRoom = GetPropertyOrFieldValue(runState, "CurrentRoom");
            var actFloor = GetPropertyOrFieldValue(runState, "ActFloor") ?? 0;
            var act = GetPropertyOrFieldValue(runState, "Act");
            string currentRoomName = currentRoom?.GetType().Name ?? "Unknown";
            string actName = act?.GetType().Name ?? "Unknown";

            string pointJson = SerializeMapPoint(currentPoint);
            string currentCoordJson = SerializeMapCoord(currentCoord);
            string pointsJson = SerializeMapPoints(map);

            return $@"{{""act"": {JsonSerializer.Serialize(actName)}, ""act_floor"": {actFloor}, ""current_room"": {JsonSerializer.Serialize(currentRoomName)}, ""current_coord"": {currentCoordJson}, ""current_point"": {pointJson}, ""points"": {pointsJson}}}";
        } catch {
            return "null";
        }
    }

    private static string SerializeMapPoints(object actMap) {
        if (actMap == null) return "[]";
        var method = actMap.GetType().GetMethod("GetAllMapPoints", BindingFlags.Public | BindingFlags.Instance);
        if (method == null) return "[]";
        var points = method.Invoke(actMap, null) as IEnumerable;
        if (points == null) return "[]";

        var pointJson = new List<string>();
        foreach (var point in points) {
            pointJson.Add(SerializeMapPoint(point));
        }
        return $"[{string.Join(",", pointJson)}]";
    }

    private static string SerializeMapPoint(object point) {
        if (point == null) return "null";
        var coord = GetPropertyOrFieldValue(point, "Coord");
        var pointType = GetPropertyOrFieldValue(point, "PointType")?.ToString() ?? "Unknown";
        var canBeModified = GetPropertyOrFieldValue(point, "CanBeModified") as bool? ?? false;
        var children = GetPropertyOrFieldValue(point, "Children") as IEnumerable;

        var childCoords = new List<string>();
        if (children != null) {
            foreach (var child in children) {
                var childCoord = GetPropertyOrFieldValue(child, "Coord");
                childCoords.Add(SerializeMapCoord(childCoord));
            }
        }

        return $@"{{""coord"": {SerializeMapCoord(coord)}, ""point_type"": {JsonSerializer.Serialize(pointType)}, ""can_be_modified"": {(canBeModified ? "true" : "false")}, ""children"": [{string.Join(",", childCoords)}]}}";
    }

    private static string SerializeMapCoord(object coord) {
        if (coord == null) return "null";
        int row = 0;
        int col = 0;

        var rowField = coord.GetType().GetField("row", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var colField = coord.GetType().GetField("col", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (rowField != null) row = rowField.GetValue(coord) as int? ?? 0;
        if (colField != null) col = colField.GetValue(coord) as int? ?? 0;

        return $@"{{""row"": {row}, ""col"": {col}}}";
    }

    private static object GetPrivateField(object target, string fieldName) {
        if (target == null) return null;
        return target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target);
    }

    private static object GetPropertyOrFieldValue(object target, string name) {
        if (target == null) return null;
        var type = target.GetType();
        var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (prop != null) return prop.GetValue(target);
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(target);
    }

    private static string SerializeCreature(Creature creature) {
        if (creature == null) return "null";

        string name = creature.Name?.Replace("\"", "\\\"") ?? "Unknown";
        string id = creature.ModelId?.ToString() ?? "Unknown";
        string type = creature.IsPlayer ? "Player" : "Monster";
        
        List<string> powerStrings = new List<string>();
        if (creature.Powers != null) {
            foreach (PowerModel power in creature.Powers) {
                if (power == null) continue;
                string pId = power.Id?.ToString() ?? power.GetType().Name;
                powerStrings.Add($@"{{""id"": ""{pId}"", ""amount"": {power.Amount}}}");
            }
        }
        string powersJson = string.Join(",", powerStrings);

        string intentJson = "null";
        if (creature.IsMonster && creature.Monster?.NextMove != null) {
            string moveId = creature.Monster.NextMove.Id ?? "Unknown";
            bool isAttacking = false;
            List<string> intentTypes = new List<string>();

            if (creature.Monster.NextMove.Intents != null) {
                foreach (AbstractIntent intent in creature.Monster.NextMove.Intents) {
                    if (intent == null) continue;
                    string intentName = intent.GetType().Name;
                    intentTypes.Add($@"""{intentName}""");

                    if (intentName.Contains("Attack") || intentName.Contains("Damage")) {
                        isAttacking = true;
                    }
                }
            }

            if (creature.IsStunned) {
                moveId = "STUNNED";
                isAttacking = false;
            }

            string combinedTypes = string.Join(",", intentTypes);
            intentJson = $@"{{""move_id"": ""{moveId}"", ""is_attacking"": {(isAttacking ? "true" : "false")}, ""types"": [{combinedTypes}]}}";
        }

        return $@"{{""name"": ""{name}"", ""id"": ""{id}"", ""type"": ""{type}"", ""is_alive"": {(creature.IsAlive ? "true" : "false")}, ""hp"": {creature.CurrentHp}, ""max_hp"": {creature.MaxHp}, ""block"": {creature.Block}, ""is_stunned"": {(creature.IsStunned ? "true" : "false")}, ""powers"": [{powersJson}], ""intent"": {intentJson}}}";
    }
}