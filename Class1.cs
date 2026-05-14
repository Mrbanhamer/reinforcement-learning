using Godot;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models.Characters; 
using MegaCrit.Sts2.Core.Entities.Players; // Added for PlayerCombatState
using HarmonyLib; 
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DefectAIBridge;

[ModInitializer("OnModLoaded")]
public static class MyMod {
    private static UdpClient _udpClient = new UdpClient();
    private static IPEndPoint _remoteEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5005);

    public static void OnModLoaded() {
        var harmony = new Harmony("com.leona.defectai");
        harmony.PatchAll();
        GD.Print(">>> AI BRIDGE LOADED: Tripwires active!");
    }

    public static void SendData(string message) {
        try {
            byte[] data = Encoding.UTF8.GetBytes(message);
            _udpClient.Send(data, data.Length, _remoteEndPoint);
        } catch (System.Exception e) {
            GD.Print(">>> AI BRIDGE ERROR: " + e.Message);
        }
    }
}

[HarmonyPatch(typeof(PlayerCombatState), "StartTurn")] 
public static class BattleStateScraper {
    public static void Postfix(PlayerCombatState __instance) {
        // 1. ACCESSING PRIVATE DATA (The Lockpick)
        // Since '_energy' is private, we use Traverse to find it by its name string.
        int currentEnergy = Traverse.Create(__instance).Field("_energy").GetValue<int>();

        // 2. NAVIGATING THE PATH
        // We go from the Player -> into the Creature room -> to the CurrentHp variable.
        int hp = __instance.Creature.CurrentHp;
        int maxHp = __instance.Creature.MaxHp;

        // 3. PACKAGING
        string json = $"{{\"hp\": {hp}, \"maxHp\": {maxHp}, \"energy\": {currentEnergy}}}";

        MyMod.SendData(json);
        GD.Print(">>> AI BRIDGE: Data sent!");
    }
}

string json = $"{{\"hp\": {hp}, \"maxHp\": {maxHp}, \"energy\": {currentEnergy}}}";