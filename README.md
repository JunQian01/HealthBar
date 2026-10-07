# EnemyHPBar — 空洞骑士怪物血条模组

在《空洞骑士》(Hollow Knight) 怪物头顶显示实时血条与具体数字（如 `37/60`），
Boss 同样显示。游戏内按 **F8** 随时开/关。

专为 **Steam 新版构建（Unity 6 引擎，游戏内版本号 1.5.1262x）** 定制，
官方 Modding API 尚未支持该构建，因此本模组采用**自包含注入方案，不依赖任何外部模组加载器**。

## 效果

- 白色血条（黑底白描边），宽度自动匹配怪物体型
- 怪物受击时血条短暂闪红
- 头顶像素风数字：当前血量 / 最大血量（内置 3×5 点阵字模渲染，不依赖游戏字体）
- Boss、竞技场与后续刷新的敌人全覆盖
- F8 全局开关（旧/新两套输入系统自适应）

## 工作原理

1. `EnemyHPBar.dll`：模组本体（纯 C#，只读游戏内存中的 `HealthManager.hp`，
   不修改存档与游戏数据）。
2. 补丁版 `Assembly-CSharp.dll`：用 [Mono.Cecil](https://github.com/jbevain/cecil)
   在游戏 `GameManager.Awake()` 开头注入一行 `call EnemyHPBar.Bootstrap::Start()`，
   启动时由 CLR 按程序集引用自动加载模组。
   **除这一行外与原版逐字节一致**（构建时做过结构级比对验证）。

之所以不用官方 Modding API：该构建与 API 支持的版本（1.5.78.11833 及更早）
存在约 1400 个类型的代码差异，直接安装会整体替换游戏程序集导致不可预知的损坏。

## 仓库结构

```
src/EnemyHPBar.cs        模组源码（单文件，无第三方依赖）
build.sh                 编译模组（csc + 游戏自身的 Managed 引用）
patch.sh                 给 Assembly-CSharp.dll 打注入补丁
tools/patch.cs           注入器源码（Mono.Cecil）
tools/inspect/           IL 结构检查工具（开发期用于版本比对/验证）
tools/moddingapi/        仅保留 Mono.Cecil.dll（MIT），供上述工具编译
```

`build.sh` / `patch.sh` 中的路径按本机环境写死（游戏 Managed 目录位于
`C:\workspace\file\Managed`），换机器时自行调整。

## 构建

前置要求：

- .NET Framework 自带的 csc（`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`）
- 对应游戏版本的 `Managed` 目录（需包含 `Assembly-CSharp.dll`、
  `UnityEngine*.dll`、`Unity.InputSystem.dll`、`netstandard.dll`）

```bash
./build.sh   # 编译模组 + 生成补丁版 Assembly-CSharp.dll，产物在 output/
```

## 安装（对应游戏目录 `.../Hollow Knight/Hollow Knight_Data/Managed/`）

1. 把原版 `Assembly-CSharp.dll` 复制一份改名为 `Assembly-CSharp.dll.bak` 备份；
2. 将 `output/` 中的 `Assembly-CSharp.dll`（补丁版）与 `EnemyHPBar.dll` 复制进 `Managed/`；
3. 启动游戏。

卸载：还原 `.bak`，或 Steam「验证文件完整性」后删除 `EnemyHPBar.dll`。

游戏更新会覆盖补丁文件使模组失效（安全设计），届时用新版游戏的
`Assembly-CSharp.dll` 重新执行一次 `build.sh` 即可。

## 免责声明

- 仅供学习交流与个人合法持有的游戏副本使用，请勿分发补丁版
  `Assembly-CSharp.dll`（含 Team Cherry 原始代码衍生内容）；
- 本项目与 Team Cherry 无关；《空洞骑士》版权归 Team Cherry 所有。
- Mono.Cecil.dll 遵循其 [MIT 许可证](https://github.com/jbevain/cecil/blob/master/LICENSE.txt)。
