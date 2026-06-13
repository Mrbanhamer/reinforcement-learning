using System;
using System.Linq;
using System.Reflection;

class Program {
    private const string GameInstallPath = @"E:\SteamLibrary\steamapps\common\Slay the Spire 2";
    static void Main() {
        var asm = Assembly.LoadFrom($@"{GameInstallPath}\data_sts2_windows_x86_64\sts2.dll");
        Console.WriteLine("=== Types containing Reward ===");
        foreach (var type in asm.GetTypes().Where(t => t.FullName != null && t.FullName.Contains("Reward"))) {
            Console.WriteLine(type.FullName);
        }
        Console.WriteLine("\n=== Types containing Map ===");
        foreach (var type in asm.GetTypes().Where(t => t.FullName != null && t.FullName.Contains("Map"))).Take(200) {
            Console.WriteLine(type.FullName);
        }
    }
}
