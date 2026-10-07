#!/bin/bash
set -e
cd /c/workspace/mod/tools
MSYS2_ARG_CONV_EXCL="*" "/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe" -nologo -out:"C:/workspace/mod/tools/patch.exe" -r:"C:/workspace/mod/tools/moddingapi/Mono.Cecil.dll" patch.cs && ./patch.exe "C:/workspace/file/Managed/Assembly-CSharp.dll" "C:/workspace/mod/output/EnemyHPBar.dll" "C:/workspace/mod/output/Assembly-CSharp.dll"
