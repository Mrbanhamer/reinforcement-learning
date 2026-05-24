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
using System.Collections.Generic;

namespace DefectAIBridge;

[ModInitializer("OnModLoaded")]
public static class MyMod {
    private static readonly string StateFilePath = Path.Combine(OS.GetUserDataDir(), "sts2_state.json");

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
            GD.Print(">>> AI BRIDGE: state file updated");
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

            // 7. Flatten arrays to clean strings
            string handJson = string.Join(",", handList);
            string drawJson = string.Join(",", drawList);
            string discardJson = string.Join(",", discardList);
            string orbsJson = string.Join(",", orbList);
            string enemiesJson = string.Join(",", enemyJsonList);

            // 8. Assemble the Final JSON String using safe verbatim literals
            string json = $@"{{""energy"": {currentEnergy}, ""max_energy"": {maxEnergy}, ""osty_hp"": {ostyHp}, ""osty_max_hp"": {ostyMaxHp}, ""orb_count"": {orbCount}, ""orb_capacity"": {orbCapacity}, ""orbs"": [{orbsJson}], ""hand"": [{handJson}], ""draw_pile"": [{drawJson}], ""discard_pile"": [{discardJson}], ""player_stats"": {playerCreatureJson}, ""enemies"": [{enemiesJson}]}}";

            // 9. Write the state JSON file so external AI can read the latest game state
            WriteStateFile(json);
        }
        catch (Exception ex) {
            GD.Print($"> don't break logic loop error: {ex.Message}");
        }
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