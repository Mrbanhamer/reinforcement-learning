using System;
using System.Linq;
using System.Reflection;

class Program {
    private const string GameInstallPath = @"E:\SteamLibrary\steamapps\common\Slay the Spire 2";
    static void Main() {
        var asm = Assembly.LoadFrom($@"{GameInstallPath}\data_sts2_windows_x86_64\sts2.dll");
        var combatState = asm.GetType("MegaCrit.Sts2.Core.Combat.CombatState");
        Console.WriteLine("=== CombatState members with 'Round' ===");
        foreach (var m in combatState.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(x => x.Name.Contains("Round", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Name)) {
            Console.WriteLine(m.MemberType + " " + m.Name + " " + (m is PropertyInfo pi ? pi.PropertyType.FullName : m is FieldInfo fi ? fi.FieldType.FullName : ""));
        }
        Console.WriteLine("\n=== CombatState public properties (first 20) ===");
        foreach (var p in combatState.GetProperties(BindingFlags.Public | BindingFlags.Instance).Take(20).OrderBy(x => x.Name)) {
            Console.WriteLine("Property " + p.Name + " " + p.PropertyType.FullName);
        }
    }
}
