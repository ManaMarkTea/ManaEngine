<#
.SYNOPSIS
    Runs the editor on a --command-file script and waits for it to quit: open a level, move the camera, play,
    screenshot, check what changed. See "Testing levels with scripts" in AGENTS.md for the commands.

.EXAMPLE
    ./Tools/Automation/run-commands.ps1 -Name sample -Commands '[{"type":"open_level","path":"Content/Levels/2D Sample.manalevel"},{"type":"wait","frames":240},{"type":"play"},{"type":"wait","frames":120},{"type":"screenshot","path":"@@OUT@@"},{"type":"quit"}]'

    Writes Artifacts/Automation/sample.png (the screenshot), sample.json (the script) and sample.log (the editor's
    console). @@OUT@@ in the script becomes Artifacts/Automation/<Name>.png; add a suffix for several shots
    ("@@OUT@@.left.png").

.EXAMPLE
    ./Tools/Automation/run-commands.ps1 -Name highlights -File Content/Scripts/Highlights/Levels.json

    @@PHOTO@@ becomes DevPhotos/<today's date>, so "@@PHOTO@@ flight-helmet.png" is a DevPhotos entry. Pass
    -PhotoDir to send them elsewhere for a look first (-PhotoDir Artifacts/Automation/photos).
#>
param(
    [Parameter(Mandatory)] [string] $Name,
    # The script as JSON text, or -File for one already on disk.
    [string] $Commands,
    [string] $File,
    # The editor to run. Build it somewhere other than its usual output when that one is in use (a debugger):
    #   dotnet build ManaEditor -o Artifacts/AutomationBin
    [string] $Exe,
    [string] $Out,
    [string] $PhotoDir,
    # More arguments for the app, ahead of the script: -ExtraArgs "--train-yard-wars" runs that game, not the editor.
    [string[]] $ExtraArgs = @(),
    [int] $TimeoutSeconds = 300,
    # Scripted runs are silent; -Sound to hear one (a check of the music or the sounds themselves).
    [switch] $Sound
)

$repo = Resolve-Path (Join-Path $PSScriptRoot "../..")
if (-not $Out) { $Out = Join-Path $repo "Artifacts/Automation" }
if (-not $Exe) {
    $Exe = @("Artifacts/AutomationBin/ManaEditor.exe", "Artifacts/Bin/ManaEditor/Debug/net10.0/ManaEditor.exe") |
        ForEach-Object { Join-Path $repo $_ } | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Exe -or -not (Test-Path $Exe)) { throw "No ManaEditor.exe found; build it first (dotnet build ManaEditor -o Artifacts/AutomationBin)." }
New-Item -ItemType Directory -Force $Out | Out-Null

$script = if ($File) { Get-Content -Raw $File } else { $Commands }
if (-not $script) { throw "Pass -Commands (JSON) or -File." }
$outPath = (Join-Path $Out $Name) -replace '\\', '/'
# Case-sensitive: "screenshot" must not be mistaken for a placeholder.
$script = $script.Replace('@@OUT@@', "$outPath.png")
if (-not $PhotoDir) { $PhotoDir = Join-Path $repo "DevPhotos" }
New-Item -ItemType Directory -Force $PhotoDir | Out-Null
$photoPrefix = (Join-Path (Resolve-Path $PhotoDir) (Get-Date -Format "yyyy-MM-dd")) -replace '\\', '/'
$script = $script.Replace('@@PHOTO@@', $photoPrefix)
$scriptPath = Join-Path $Out "$Name.json"
$logPath = Join-Path $Out "$Name.log"
Set-Content -Path $scriptPath -Value $script

# The editor runs from the repo root so level and asset paths in the script resolve as they do in a checkout.
# Start-Process joins its arguments with spaces, so a level path with spaces in it needs quotes of its own.
$appArgs = @($ExtraArgs | ForEach-Object { if ($_ -match '\s' -and $_ -notmatch '^"') { "`"$_`"" } else { $_ } })
if (-not $Sound) { $appArgs += "--mute" }
$process = Start-Process -FilePath $Exe -ArgumentList ($appArgs + @("--command-file", "`"$scriptPath`"")) -WorkingDirectory $repo `
    -RedirectStandardOutput $logPath -PassThru -NoNewWindow
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $process.Id -Force
    Write-Warning "Timed out after $TimeoutSeconds s; the script may be missing a quit."
}

# What the script reported (log_player, open_prefab...), and anything that went wrong.
Select-String -Path $logPath -Pattern "Command file|xception|warning\(s\)" | ForEach-Object { $_.Line }
Get-ChildItem -Path $Out -Filter "$Name*.png" | ForEach-Object { "shot: $($_.FullName)" }
if ($script.Contains($photoPrefix)) {
    Get-ChildItem -Path $PhotoDir -Filter "*.png" | Where-Object { $_.LastWriteTime -gt $process.StartTime } |
        ForEach-Object { "photo: $($_.FullName)" }
}
