using System;
using System.Linq;
using Mono.Cecil;

class Inspect
{
    static void Dump(TypeDefinition t)
    {
        Console.WriteLine("=== TYPE: " + t.FullName + " (base: " + (t.BaseType != null ? t.BaseType.FullName : "null") + ")");
        Console.WriteLine("-- FIELDS --");
        foreach (var f in t.Fields)
            Console.WriteLine("  " + f.FieldType.Name + " " + f.Name + (f.IsStatic ? " [static]" : "") + (f.IsPublic ? " [public]" : " [nonpublic]"));
        Console.WriteLine("-- PROPERTIES --");
        foreach (var p in t.Properties)
            Console.WriteLine("  " + p.PropertyType.Name + " " + p.Name);
        Console.WriteLine("-- METHODS --");
        foreach (var m in t.Methods)
            if (!m.IsGetter && !m.IsSetter)
                Console.WriteLine("  " + m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", m.Parameters.Select(p => p.ParameterType.Name + " " + p.Name)) + ")" + (m.IsPublic ? " [public]" : ""));
    }

    static void Main(string[] args)
    {
        var asm = AssemblyDefinition.ReadAssembly(args[0]);
        string typeName = args[1];
        bool search = args.Length > 2 && args[2] == "search";

        foreach (var mod in asm.Modules)
        {
            Action<TypeDefinition> walk = null;
            walk = t =>
            {
                if (search)
                {
                    if (t.Name.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0)
                        Console.WriteLine("  " + t.FullName);
                }
                else if (t.Name == typeName || t.FullName == typeName)
                {
                    Dump(t);
                }
                foreach (var n in t.NestedTypes) walk(n);
            };
            foreach (var t in mod.Types) walk(t);
        }
    }
}
