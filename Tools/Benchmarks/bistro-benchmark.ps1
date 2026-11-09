<#
.SYNOPSIS
    Benchmarks the Amazon Lumberyard Bistro exterior (2.8M triangles, 400 textures): load time, frame rate per viewpoint,
    the frame profiler's scopes, and GPU memory. The checked-in level downloads/imports its assets on first open.
.EXAMPLE
    ./Tools/Benchmarks/bistro-benchmark.ps1
    ./Tools/Benchmarks/bistro-benchmark.ps1 -NoBlockCompression   # DDS decoded to RGBA, for comparison
    ./Tools/Benchmarks/bistro-benchmark.ps1 -Night                # 135 point lights from the emissive surfaces
#>
param(
    [string] $Exe,
    [string] $Name = 'bistro',
    # Decode DDS to RGBA as a GPU without BC sampling would (MANA_NO_BC=1).
    [switch] $NoBlockCompression,
    # The night level: no sun, a point light at every glowing thing (clustered lighting under load).
    [switch] $Night,
    # Leave vsync and the 90 fps limiter on, as the editor normally runs.
    [switch] $Capped,
    [int] $Frames = 600,
    [int] $TimeoutSeconds = 600
)
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$level = 'Content/Levels/Downloaded/Bistro/Bistro Exterior.manalevel'
if ($Night) { $level = 'Content/Levels/Downloaded/Bistro/Bistro Exterior Night.manalevel' }
$out = Join-Path $repo 'Artifacts/Benchmarks'
New-Item -ItemType Directory -Force $out | Out-Null
if (-not $Exe) {
    $Exe = Join-Path $repo 'Artifacts/BenchmarkRelease/ManaEditor.exe'
    dotnet build (Join-Path $repo 'ManaEditor') -c Release -o (Split-Path $Exe) | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark build failed.' }
}
if ($NoBlockCompression) { $Name = "$Name-rgba" }
if ($Night) { $Name = "$Name-night" }

# World positions (the level puts the scene at (100, 8, 100)). The first three are the cameras of Matt Pharr's pbrt-v4
# port (github.com/mmp/pbrt-v4-scenes, bistro/*.pbrt): eye, target and vertical field of view as written there.
$views = [ordered]@{
    vespa       = @{ position = @(90.3, 10.5, 104.3); target = @(92.5, 9.6, 99.75); fov = 45 }
    cafe        = @{ position = @(88.3, 10.5, 106.5); target = @(91.5, 10.8, 108.0); fov = 53 }
    boulangerie = @{ position = @(108.0, 11.9, 65.0); target = @(109.6, 11.9, 75.0); fov = 60 }
    overview    = @{ position = @(205.0, 85.0, 15.0); target = @(125.0, 8.0, 105.0); fov = 55 }
}
$commands = [System.Collections.Generic.List[object]]::new()
$commands.Add(@{ type = 'open_level'; path = $level })
$commands.Add(@{ type = 'gizmos'; visible = $false })
# Loading streams meshes over a few seconds; the log's "deferred=0" marks the end, read back below.
$commands.Add(@{ type = 'wait'; frames = 900 })
$commands.Add(@{ type = 'gpu_report'; path = "@@OUT@@.gpu.txt" })
foreach ($view in $views.Keys) {
    $commands.Add(@{ type = 'move_camera'; position = $views[$view].position; target = $views[$view].target; fov = $views[$view].fov })
    $commands.Add(@{ type = 'wait'; frames = 120 })
    $commands.Add(@{ type = 'profile_reset' })
    $commands.Add(@{ type = 'wait'; frames = $Frames })
    $commands.Add(@{ type = 'profile_dump'; path = "@@OUT@@.$view.txt" })
    $commands.Add(@{ type = 'screenshot'; path = "@@OUT@@.$view.png"; clean = $true })
}
$commands.Add(@{ type = 'quit' })
$script = ConvertTo-Json -InputObject $commands.ToArray() -Depth 5 -Compress

$env:MANA_UNCAPPED = if ($Capped) { '' } else { '1' }
$env:MANA_NO_BC = if ($NoBlockCompression) { '1' } else { '' }
# GPU memory in use, sampled while it runs (the whole card, so close other GPU-heavy apps).
$baseline = [int](nvidia-smi --query-gpu=memory.used --format=csv,noheader,nounits)
$sampler = Start-Job { param($seconds) $end = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $end) { [int](nvidia-smi --query-gpu=memory.used --format=csv,noheader,nounits); Start-Sleep -Milliseconds 500 } } -ArgumentList $TimeoutSeconds
$started = Get-Date
try {
    & (Join-Path $repo 'Tools/Automation/run-commands.ps1') -Name $Name -Commands $script -Exe $Exe -Out $out -TimeoutSeconds $TimeoutSeconds
}
finally {
    Remove-Item Env:MANA_UNCAPPED, Env:MANA_NO_BC -ErrorAction SilentlyContinue
}
Stop-Job $sampler
$peakMb = [int](Receive-Job $sampler | Measure-Object -Maximum).Maximum
Remove-Job $sampler

$log = Get-Content (Join-Path $out "$Name.log")
function Stamp($line) { [datetime]::ParseExact(($line -replace '^\[([0-9:.]+).*', '$1'), 'HH:mm:ss.fff', $null) }
$opened = $log | Where-Object { $_ -match 'Opened level' } | Select-Object -First 1
$resident = $log | Where-Object { $_ -match 'deferred=0,' } | Select-Object -First 1
$number = '[0-9]+(?:\.[0-9]+)?'
"Bistro exterior$(if ($Night) { ' at night' }), $(if ($NoBlockCompression) { 'DDS decoded to RGBA8' } else { 'DDS as BC blocks' }), $(if ($Capped) { 'capped' } else { 'uncapped' })"
if ($opened -and $resident) { "  load: level opened to every mesh resident {0:0.0} s" -f ((Stamp $resident) - (Stamp $opened)).TotalSeconds }
"  GPU memory: {0} MB in use at peak, {1} MB above the idle card" -f $peakMb, ($peakMb - $baseline)
$results = foreach ($view in $views.Keys) {
    # @@OUT@@ is <Name>.png, so the reports are <Name>.png.<view>.txt.
    $path = Join-Path $out "$Name.png.$view.txt"
    if (-not (Test-Path $path) -or (Get-Item $path).LastWriteTime -lt $started) { throw "Missing fresh report: $path" }
    $report = Get-Content -Raw $path
    $fps = [double]::Parse([regex]::Match($report, "fps=($number)").Groups[1].Value, [cultureinfo]::InvariantCulture)
    $top = (Get-Content $path | Select-Object -Skip 4 -First 4 | ForEach-Object { ($_ -split ' \| ')[0..1] -join ' ' }) -join ', '
    [pscustomobject]@{
        View = $view
        FPS = $fps
        FrameMs = [math]::Round(1000 / $fps, 2)
        WorstMs = [regex]::Match($report, "worstFrameMs=($number)").Groups[1].Value
        Hitches = [regex]::Match($report, 'hitches=([0-9]+)').Groups[1].Value
        TopScopesMs = $top
    }
}
$results | Format-Table -AutoSize -Wrap
