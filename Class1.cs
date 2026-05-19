using Godot;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Entities.Players; 
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using HarmonyLib; 
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;

namespace DefectAIBridge;

[ModInitializer("OnModLoaded")]
public static class MyMod {
    private static UdpClient _udpClient = new UdpClient();
    private static IPEndPoint _remoteEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5005);

    public static void OnModLoaded() {
        var harmony = new Harmony("com.leona.defectai");
        harmony.PatchAll();
        GD.Print(">>> AI BRIDGE LOADED: Full state & intent monitoring active!");
    }

    public static void SendData(string message) {
        try {
            byte[] data = Encoding.UTF8.GetBytes(message);
            _udpClient.Send(data, data.Length, _remoteEndPoint);
        } catch (Exception e) {
            GD.Print(">>> AI BRIDGE ERROR: " + e.Message);
        }
    }
}

[HarmonyPatch(typeof(PlayerCombatState), "StartTurn")] 
// You can stack multiple patches to run the same code!
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Cards.CardQueue), "UseCard")] // Generic example name for STS2 card usage
public static class BattleStateScraper {
    public static void Postfix(PlayerCombatState __instance) {
        try {
            // 1. Core Resources
            int currentEnergy = __instance.Energy;
            int maxEnergy = __instance.MaxEnergy;

            // 2. Extract Card Piles
            List<string> handList = new List<string>();
            foreach (var card in __instance.Hand.Cards) {
                if (card != null) handList.Add($"\"{card.Id}\"");
            }

            List<string> drawList = new List<string>();
            foreach (var card in __instance.DrawPile.Cards) {
                if (card != null) drawList.Add($"\"{card.Id}\"");
            }

            List<string> discardList = new List<string>();
            foreach (var card in __instance.DiscardPile.Cards) {
                if (card != null) discardList.Add($"\"{card.Id}\"");
            }

            // 3. Scan for Osty (Pets list)
            int ostyHp = -1;
            int ostyMaxHp = -1;
            if (__instance.Pets != null && __instance.Pets.Count > 0) {
                var osty = __instance.Pets[0];
                if (osty != null) {
                    ostyHp = osty.CurrentHp;
                    ostyMaxHp = osty.MaxHp;
                }
            }

            // 4. Extract Live Orbs
            List<string> orbList = new List<string>();
            int orbCount = __instance.OrbQueue?.Orbs?.Count ?? 0;
            int orbCapacity = __instance.OrbQueue?.Capacity ?? 0;

            if (__instance.OrbQueue?.Orbs != null) {
                foreach (var orb in __instance.OrbQueue.Orbs) {
                    if (orb != null) orbList.Add($"\"{orb.Id}\""); 
                }
            }

            // 5. Build Player Creature State (Health, Block, Powers)
            string playerCreatureJson = SerializeCreature(__instance);

            // 6. Loop through Room Enemies to Scrape Health, Powers, and Intent
            List<string> enemyJsonList = new List<string>();
            if (__instance.Enemies != null) {
                foreach (var enemy in __instance.Enemies) {
                    if (enemy != null && enemy.IsAlive) {
                        enemyJsonList.Add(SerializeCreature(enemy));
                    }
                }
            }

            // 7. Flatten arrays to clean strings for JSON assembly
            string handJson = string.Join(",", handList);
            string drawJson = string.Join(",", drawList);
            string discardJson = string.Join(",", discardList);
            string orbsJson = string.Join(",", orbList);
            string enemiesJson = string.Join(",", enemyJsonList);

            // 8. Assemble the Final JSON String
            string json = $"{{" +
                $"\"energy\": {currentEnergy}, " +
                $"\"max_energy\": {maxEnergy}, " +
                $"\"osty_hp\": {ostyHp}, " +
                $"\"osty_max_hp\": {ostyMaxHp}, " +
                $"\"orb_count\": {orbCount}, " +
                $"\"orb_capacity\": {orbCapacity}, " +
                $"\"orbs\": [{orbsJson}], " +
                $"\"hand\": [{handJson}], " +
                $"\"draw_pile\": [{drawJson}], " +
                $"\"discard_pile\": [{discardJson}], " +
                $"\"player_stats\": {playerCreatureJson}, " +
                $"\"enemies\": [{enemiesJson}]" +
                $"}}";

            // 9. Blast to Python over the socket
            MyMod.SendData(json);
            GD.Print(">>> AI BRIDGE: Packaged state & enemy intents sent to network pipeline!");
        }
        catch (Exception ex) {
            GD.Print($">>> AI BRIDGE ERROR IN POSTFIX: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Helper method to format Slay the Spire 2 Creature data directly into JSON format.
    /// </summary>
    private static string SerializeCreature(Creature creature) {
        if (creature == null) return "null";

        string name = creature.Name?.Replace("\"", "\\\"") ?? "Unknown";
        string id = creature.ModelId?.ToString() ?? "Unknown";
        string type = creature.IsPlayer ? "Player" : "Monster";
        
        // Handle powers/buffs stringification
        List<string> powerStrings = new List<string>();
        if (creature.Powers != null) {
            foreach (PowerModel power in creature.Powers) {
                if (power == null) continue;
                string pId = power.Id?.ToString() ?? power.GetType().Name;
                powerStrings.Add($"{{\\"id\\": \\"{pId}\\", \\"amount\\": {power.Amount}}}");
            }
        }
        string powersJson = string.Join(",", powerStrings);

        // Intent generation (Monsters only)
        string intentJson = "null";
        if (creature.IsMonster && creature.Monster?.NextMove != null) {
            string moveId = creature.Monster.NextMove.Id ?? "Unknown";
            bool isAttacking = false;
            List<string> intentTypes = new List<string>();

            if (creature.Monster.NextMove.Intents != null) {
                foreach (AbstractIntent intent in creature.Monster.NextMove.Intents) {
                    if (intent == null) continue;
                    string intentName = intent.GetType().Name;
                    intentTypes.Add($"\\\"{intentName}\\\"");

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
            intentJson = $"{{" +
                $"\\\"move_id\\": \\"{moveId}\\\", " +
                $"\\\"is_attacking\\": {(isAttacking ? "true" : "false")}, " +
                $"\\\"types\\": [{combinedTypes}]" +
                $"}}";
        }

        // Format creature blocks block 
        return $"{{" +
            $"\"name\": \"{name}\", " +
            $"\"id\": \"{id}\", " +
            $"\"type\": \"{type}\", " +
            $"\"is_alive\": {(creature.IsAlive ? "true" : "false")}, " +
            $"\"hp\": {creature.CurrentHp}, " +
            $"\"max_hp\": {creature.MaxHp}, " +
            $"\"block\": {creature.Block}, " +
            $"\"is_stunned\": {(creature.IsStunned ? "true" : "false")}, " +
            $"\"powers\": [{powersJson}], " +
            $"\"intent\": {intentJson}" +
            $"}}";
    }
}