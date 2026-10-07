#!/bin/bash
# 一键构建：编译模组 + 给 Assembly-CSharp.dll 打补丁（源码在 src/，用户游戏文件在 /c/workspace/file/Managed/）
set -e
cd /c/workspace/mod
mkdir -p output
cd src
MSYS2_ARG_CONV_EXCL="*" "/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe" -nologo -target:library -optimize+ \
  -out:"C:/workspace/mod/output/EnemyHPBar.dll" \
  -r:"C:/workspace/file/Managed/Assembly-CSharp.dll" \
  -r:"C:/workspace/file/Managed/UnityEngine.dll" \
  -r:"C:/workspace/file/Managed/UnityEngine.CoreModule.dll" \
  -r:"C:/workspace/file/Managed/UnityEngine.Physics2DModule.dll" \
  -r:"C:/workspace/file/Managed/UnityEngine.InputLegacyModule.dll" \
  -r:"C:/workspace/file/Managed/Unity.InputSystem.dll" \
  -r:"C:/workspace/file/Managed/netstandard.dll" \
  EnemyHPBar.cs
cd /c/workspace/mod
./patch.sh
echo "== 全部完成，成品在 output/ =="
