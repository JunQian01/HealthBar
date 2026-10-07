using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

class Verify
{
    static int Main(string[] args)
    {
        var asm = AssemblyDefinition.ReadAssembly(args[0]);
        var gm = asm.MainModule.GetType("GameManager");
        var awake = gm.Methods.FirstOrDefault2("Awake");

        Console.WriteLine("GameManager.Awake 前几条指令:");
        int n = Math.Min(6, awake.Body.Instructions.Count);
        for (int i = 0; i < n; i++)
        {
            Console.WriteLine("  IL_" + i + ": " + awake.Body.Instructions[i].OpCode + " " + awake.Body.Instructions[i].Operand);
        }

        Console.WriteLine("程序集引用:");
        foreach (var r in asm.MainModule.AssemblyReferences)
            if (r.Name == "EnemyHPBar")
                Console.WriteLine("  -> " + r.Name + " v" + r.Version);

        return 0;
    }
}

static class Ext
{
    public static MethodDefinition FirstOrDefault2(this System.Collections.Generic.IEnumerable<MethodDefinition> ms, string name)
    {
        foreach (var m in ms) if (m.Name == name && !m.IsStatic && m.Parameters.Count == 0) return m;
        return null;
    }
}
