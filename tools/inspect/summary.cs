using System;
using System.Text;
using Mono.Cecil;

class Summary
{
    static StringBuilder sb = new StringBuilder();
    static void Main(string[] args)
    {
        var asm = AssemblyDefinition.ReadAssembly(args[0]);
        foreach (var mod in asm.Modules)
        {
            Walk(mod.Types);
        }
        Console.Write(sb.ToString());
    }

    static void Walk(System.Collections.Generic.IEnumerable<TypeDefinition> types)
    {
        foreach (var t in types)
        {
            sb.AppendLine("T " + t.FullName + " " + t.Methods.Count + " " + t.Fields.Count);
            Walk(t.NestedTypes);
        }
    }
}
