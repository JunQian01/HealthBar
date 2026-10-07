// 用 Mono.Cecil 给游戏的 Assembly-CSharp.dll 打补丁：
// 在 GameManager.Awake() 方法体开头插入一行 `call EnemyHPBar.Bootstrap::Start()`，
// 其余字节逻辑完全不动。游戏启动时 CLR 会按程序集引用自动从
// Managed 目录加载 EnemyHPBar.dll 并执行入口。
using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

class Patcher
{
    static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("用法: patch.exe <原版Assembly-CSharp.dll> <EnemyHPBar.dll> <输出.dll>");
            return 1;
        }

        string src = args[0];
        string modDll = args[1];
        string dst = args[2];

        string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(src));
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(dir);

        var game = AssemblyDefinition.ReadAssembly(src, new ReaderParameters { AssemblyResolver = resolver });
        var mod = AssemblyDefinition.ReadAssembly(modDll, new ReaderParameters { AssemblyResolver = resolver });

        TypeDefinition gm = game.MainModule.GetType("GameManager");
        if (gm == null)
        {
            Console.WriteLine("错误: 找不到 GameManager 类型");
            return 1;
        }

        MethodDefinition awake = gm.Methods.FirstOrDefault(m =>
            m.Name == "Awake" && !m.IsStatic && m.Parameters.Count == 0 && m.HasBody);
        if (awake == null)
        {
            Console.WriteLine("错误: 找不到 GameManager.Awake()");
            return 1;
        }

        TypeDefinition bootstrap = mod.MainModule.GetType("EnemyHPBar.Bootstrap");
        if (bootstrap == null)
        {
            Console.WriteLine("错误: EnemyHPBar.dll 中找不到 Bootstrap 类型");
            return 1;
        }

        MethodDefinition start = bootstrap.Methods.FirstOrDefault(m =>
            m.Name == "Start" && m.IsStatic && m.Parameters.Count == 0 &&
            m.ReturnType.FullName == "System.Void");
        if (start == null)
        {
            Console.WriteLine("错误: 找不到 Bootstrap.Start() 方法");
            return 1;
        }

        MethodReference imported = game.MainModule.ImportReference(start);

        ILProcessor il = awake.Body.GetILProcessor();
        Instruction call = il.Create(OpCodes.Call, imported);
        if (awake.Body.Instructions.Count > 0)
        {
            il.InsertBefore(awake.Body.Instructions[0], call);
        }
        else
        {
            il.Append(call);
            il.Append(il.Create(OpCodes.Ret));
        }

        game.Write(dst);
        Console.WriteLine("补丁完成: " + dst);
        Console.WriteLine("注入点: GameManager.Awake -> " + imported.FullName);
        return 0;
    }
}
