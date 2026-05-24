using System;
using System.Linq;
using System.Reflection;

class Program {
    static void Main() {
        var asm = Assembly.LoadFrom(@"E:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2.dll");
        foreach (var type in asm.GetTypes().Where(t => t.Name.Contains("Intent") || t.FullName.Contains("Intent"))) {
            Console.WriteLine(type.FullName);
        }
    }
}
