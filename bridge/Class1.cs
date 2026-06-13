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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DefectAIBridge;

[ModInitializer("OnModLoaded")]
public static class MyMod {
    // Default paths (may be overridden at runtime to the mod folder)
    private static string StateFilePath = Path.Combine(OS.GetUserDataDir(), "sts2_state.json");
    // Additional debug copy in Documents to make it easy to find while testing
    private static string DebugStateFilePath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "sts2_state_debug.json");

    // Socket bridge state
    private static readonly object SocketLock = new object();
    private static TcpClient socketClient;
    private static StreamWriter socketWriter;
    private static CancellationTokenSource socketCts;
    private static string socketMode = "test";
    private const string SocketHost = "127.0.0.1";
    private const int SocketPort = 12345;
    private static readonly TimeSpan SocketReconnectDelay = TimeSpan.FromSeconds(2);

    public static void OnModLoaded() {
        // Try to set the state file paths to the folder where this mod DLL resides
        try {
            string modFolder = null;
            try { modFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); } catch { }
            if (string.IsNullOrEmpty(modFolder)) {
                try { modFolder = AppDomain.CurrentDomain.BaseDirectory; } catch { }
            }
            if (!string.IsNullOrEmpty(modFolder)) {
                StateFilePath = Path.Combine(modFolder, "sts2_state.json");
                DebugStateFilePath = Path.Combine(modFolder, "sts2_state_debug.json");
            }

            GD.Print($">>> AI BRIDGE LOADED: state file will be written to {StateFilePath}");
        } catch (Exception e) {
            GD.Print($">>> AI BRIDGE: failed to set mod-folder paths, using defaults ({StateFilePath}) - {e.Message}");
        }

        // Start the socket bridge and subscribe to game action events when ready.
        StartSocketBridge();
        StartExecutorWatcher();
    }

    private static void StartSocketBridge() {
        socketCts?.Cancel();
        socketCts = new CancellationTokenSource();
        Task.Run(async () => {
            while (!socketCts.IsCancellationRequested) {
                try {
                    await ConnectAndHandshakeAsync(socketCts.Token);
                    while (!socketCts.IsCancellationRequested && socketClient?.Connected == true) {
                        await Task.Delay(1000, socketCts.Token).ContinueWith(_ => { });
                    }
                } catch (OperationCanceledException) {
                    break;
                } catch (Exception e) {
                    GD.PrintErr($">>> AI BRIDGE: Socket bridge failed: {e.Message}");
                }

                if (socketCts.IsCancellationRequested) break;
                CloseSocketConnection();
                await Task.Delay(SocketReconnectDelay, socketCts.Token).ContinueWith(_ => { });
            }
        }, socketCts.Token);
    }

    private static async Task ConnectAndHandshakeAsync(CancellationToken cancellationToken) {
        CloseSocketConnection();
        var client = new TcpClient();
        await client.ConnectAsync(SocketHost, SocketPort).WaitAsync(cancellationToken);
        var stream = client.GetStream();
        using (var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true)) {
            string modeLine = await reader.ReadLineAsync().WaitAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(modeLine)) {
                throw new InvalidOperationException("Socket handshake failed: empty mode string.");
            }

            var mode = modeLine.Trim().ToLowerInvariant();
            if (mode != "training" && mode != "test") {
                throw new InvalidOperationException($"Socket handshake failed: unsupported mode '{mode}'.");
            }

            ApplySocketMode(mode);
            var writer = new StreamWriter(stream, Encoding.UTF8, 4096, leaveOpen: true) { NewLine = "\n", AutoFlush = true };
            lock (SocketLock) {
                socketClient = client;
                socketWriter = writer;
                socketMode = mode;
            }

            GD.Print($">>> AI BRIDGE: Connected to socket server at {SocketHost}:{SocketPort} with mode '{mode}'.");
        }
    }

    private static void StartExecutorWatcher() {
        Task.Run(async () => {
            while (!socketCts?.IsCancellationRequested ?? false) {
                if (TrySubscribeActionExecutor()) break;
                await Task.Delay(1000, socketCts.Token).ContinueWith(_ => { });
            }
        });
    }

    private static bool TrySubscribeActionExecutor() {
        try {
            var executor = RunManager.Instance?.ActionExecutor;
            if (executor == null) return false;
            executor.AfterActionExecuted += OnGameActionExecuted;
            GD.Print(">>> AI BRIDGE: Subscribed to ActionExecutor.AfterActionExecuted.");
            return true;
        } catch (Exception e) {
            GD.PrintErr($">>> AI BRIDGE: Executor subscribe failed - {e.Message}");
            return false;
        }
    }

    private static void ApplySocketMode(string mode) {
        if (mode == "training") {
            Engine.TimeScale = 10.0f;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Minimized);
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled, 0);
        } else {
            Engine.TimeScale = 1.0f;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Enabled, 0);
        }

        GD.Print($">>> AI BRIDGE: Applied socket mode '{mode}'. TimeScale={Engine.TimeScale}, VSync={(mode == "training" ? "disabled" : "enabled")}");
    }

    private static void CloseSocketConnection() {
        lock (SocketLock) {
            if (socketWriter != null) {
                try { socketWriter.Dispose(); } catch { }
                socketWriter = null;
            }
            if (socketClient != null) {
                try { socketClient.Close(); } catch { }
                socketClient = null;
            }
        }
    }

    private static void SendStateOverSocket(string json) {
        lock (SocketLock) {
            if (socketWriter == null) return;
            try {
                socketWriter.WriteLine(json);
            } catch (Exception e) {
                GD.PrintErr($">>> AI BRIDGE: Socket send failed - {e.Message}");
                CloseSocketConnection();
            }
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
        // Disabled: JSON file writing removed. This mod only handles socket transport now.
        // Keep method as a no-op so other code that may call it compiles.
        try { /* no-op */ } catch { }
    }

    /// <summary>
    /// This module no longer performs JSON serialization. Callers that need to
    /// send serialized state should call `SendSerializedState` below.
    /// </summary>
    public static void ProcessCombatTelemetry(PlayerCombatState stateInstance, CombatState combatState) {
        // No-op: telemetry serialization removed from socket bridge module.
    }

    // Public API: send a pre-serialized string (JSON or other) over the socket bridge.
    // Call this from other game code that performs serialization.
    public static void SendSerializedState(string serializedState) {
        if (string.IsNullOrEmpty(serializedState)) return;
        SendStateOverSocket(serializedState);
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