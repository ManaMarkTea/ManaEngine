# Builds ManaEditor's FBX reader (ufbx + mana_fbx.c) for Windows x64 with MSVC into ManaEditor/Native/Fbx/runtimes.
# The Mac's is built by build-mana-fbx.sh. Commit the result: the editor loads it from there, no compiler needed.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
$source = Join-Path $root 'ManaEditor/Native/Fbx'
$output = Join-Path $source 'runtimes/win-x64'
New-Item -ItemType Directory -Force $output | Out-Null

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'No Visual Studio with the C++ x64 tools.' }
$vcvars = Join-Path $vs 'VC/Auxiliary/Build/vcvars64.bat'

$objects = Join-Path $env:TEMP 'mana_fbx_build'
New-Item -ItemType Directory -Force $objects | Out-Null
# /MT: no VC++ redistributable needed beside the dll.
$compile = "cl /nologo /O2 /MT /W3 /LD /Fo`"$objects\\`" `"$source\mana_fbx.c`" `"$source\ufbx\ufbx.c`" /Fe`"$output\mana_fbx.dll`" /link /NOIMPLIB /NOEXP"
cmd /c "`"$vcvars`" >nul && $compile"
if ($LASTEXITCODE -ne 0) { throw "cl failed ($LASTEXITCODE)" }
Remove-Item -Recurse -Force $objects
Get-Item (Join-Path $output 'mana_fbx.dll') | Select-Object Name, Length
