<#
.SYNOPSIS
    Runs the GPU particle count sweep, then prints FPS, slowdown and frame hitches.
.EXAMPLE
    ./Tools/Automation/benchmark-gpu-particles.ps1
#>
param(
    [string] $Exe,
    [string] $Name = 'gpu-particles',
    [int] $TimeoutSeconds = 300
)
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$out = Join-Path $repo 'Artifacts/Automation'
if (-not $Exe) {
    $Exe = Join-Path $repo 'Artifacts/GpuParticleRelease/ManaEditor.exe'
    dotnet build (Join-Path $repo 'ManaEditor') -c Release -o (Split-Path $Exe)
    if ($LASTEXITCODE -ne 0) { throw 'GPU particle benchmark build failed.' }
}
$benchmarkStarted = Get-Date
& (Join-Path $PSScriptRoot 'run-commands.ps1') -Name $Name -File (Join-Path $PSScriptRoot 'Checks/gpu-particles.json') `
    -Exe $Exe -Out $out -TimeoutSeconds $TimeoutSeconds

$baselinePath = Join-Path $out "$Name.png.0.txt"
if (-not (Test-Path -LiteralPath $baselinePath)) { throw 'Benchmark did not produce its baseline report.' }
if ((Get-Item -LiteralPath $baselinePath).LastWriteTime -lt $benchmarkStarted) { throw 'Benchmark did not write a fresh baseline report.' }
$number = '[0-9]+(?:\.[0-9]+)?'
$baselineText = Get-Content -Raw -LiteralPath $baselinePath
$baselineFps = [double]::Parse([regex]::Match($baselineText, "fps=($number)").Groups[1].Value, [cultureinfo]::InvariantCulture)
$results = foreach ($count in @(0, 10000, 100000, 250000, 500000, 1000000, 2000000, 4000000, 8000000, 10000000, 'baseline-after')) {
    $path = Join-Path $out "$Name.png.$count.txt"
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing benchmark report: $path" }
    if ((Get-Item -LiteralPath $path).LastWriteTime -lt $benchmarkStarted) { throw "Benchmark did not write a fresh report: $path" }
    $report = Get-Content -Raw -LiteralPath $path
    $fps = [double]::Parse([regex]::Match($report, "fps=($number)").Groups[1].Value, [cultureinfo]::InvariantCulture)
    [pscustomobject]@{
        Particles = $count
        FPS = $fps
        SlowdownPercent = [math]::Round([math]::Max(0, 100 * (1 - $fps / $baselineFps)), 1)
        WorstFrameMs = [regex]::Match($report, "worstFrameMs=($number)").Groups[1].Value
        Hitches = [regex]::Match($report, 'hitches=([0-9]+)').Groups[1].Value
    }
}
$results | Format-Table -AutoSize
