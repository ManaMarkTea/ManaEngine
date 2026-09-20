<#
.SYNOPSIS
    Captures a CPU trace of a running ManaVoxel.exe and prints/saves a hot-function report,
    with no AI involvement required.

.DESCRIPTION
    Wraps the dotnet-trace workflow used to diagnose the render-loop bottleneck (per-mesh WebGPU
    native calls dominating frame time): collect a trace, convert it to speedscope JSON, then walk
    the "evented" call-stack format (Open/Close events, not sample/weight arrays) to rank functions
    by self time and inclusive/total time on the render thread.

    Requires the dotnet-trace global tool:
        dotnet tool install --global dotnet-trace

.PARAMETER DurationSeconds
    How long to sample. Default 15.

.PARAMETER Launch
    If ManaVoxel.exe isn't already running, build+launch it (dotnet run --project ManaVoxel),
    wait -WarmupSeconds for it to reach steady state, capture, then stop the instance this script
    started (an already-running instance found without -Launch is left alone either way).

.PARAMETER WarmupSeconds
    Only used with -Launch. Time to let the app finish startup/loading before tracing. Default 25.

.PARAMETER OutDir
    Where trace + report files land. Default <repo root>\PerfTraces (gitignored).

.PARAMETER TopCount
    How many functions to list in each ranking of the report. Default 25; raise it to see below the wrappers
    that sit at the top of every render-thread trace.

.PARAMETER TraceFile
    Skip capture and just re-report on an existing .nettrace file.

.EXAMPLE
    # App already running:
    .\tools\Profiling\Run-PerfTrace.ps1

.EXAMPLE
    # Build, launch, warm up, trace, and shut down automatically:
    .\tools\Profiling\Run-PerfTrace.ps1 -Launch -DurationSeconds 20

.EXAMPLE
    # Re-analyze a previous capture:
    .\tools\Profiling\Run-PerfTrace.ps1 -TraceFile PerfTraces\trace_2026-08-05_180512.nettrace
#>
param(
    [int]$DurationSeconds = 15,
    [switch]$Launch,
    [int]$WarmupSeconds = 25,
    [int]$TopCount = 25,
    [string]$OutDir = "$PSScriptRoot\..\..\PerfTraces",
    [string]$TraceFile = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path "$PSScriptRoot\..\.."
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Test-DotnetTrace {
    if (-not (Get-Command dotnet-trace -ErrorAction SilentlyContinue)) {
        Write-Error "dotnet-trace is not installed. Install it with:`n    dotnet tool install --global dotnet-trace"
        exit 1
    }
}

function Get-FrameName($frames, $idx) {
    return $frames[$idx].name
}

function Write-Report {
    param($speedscopePath, $reportPath)

    Write-Host "Parsing $speedscopePath ..."
    $data = Get-Content $speedscopePath -Raw | ConvertFrom-Json -Depth 64
    $frames = $data.shared.frames
    $profiles = $data.profiles

    $renderProfile = $null
    foreach ($p in $profiles) {
        foreach ($e in $p.events) {
            if ($e.type -eq 'O' -and (Get-FrameName $frames $e.frame) -like '*GameSilkWebGpu.Render*') {
                $renderProfile = $p
                break
            }
        }
        if ($renderProfile) { break }
    }

    if (-not $renderProfile) {
        Write-Warning "Could not find the render thread (no frame matched '*GameSilkWebGpu.Render*'). Falling back to the busiest thread by event count."
        $renderProfile = $profiles | Sort-Object { $_.events.Count } -Descending | Select-Object -First 1
    }

    $captureMs = [double]$renderProfile.endValue - [double]$renderProfile.startValue
    $selfTime = @{}
    $totalTime = @{}
    $stack = New-Object System.Collections.Generic.Stack[psobject]
    $prevTime = $null

    foreach ($e in $renderProfile.events) {
        $t = [double]$e.at
        if ($stack.Count -gt 0 -and $null -ne $prevTime -and $t -gt $prevTime) {
            $topName = Get-FrameName $frames $stack.Peek().frame
            if (-not $selfTime.ContainsKey($topName)) { $selfTime[$topName] = 0.0 }
            $selfTime[$topName] += ($t - $prevTime)
        }
        $prevTime = $t

        if ($e.type -eq 'O') {
            $stack.Push([pscustomobject]@{ frame = $e.frame; at = $t })
        } elseif ($stack.Count -gt 0) {
            $top = $stack.Pop()
            $name = Get-FrameName $frames $top.frame
            if (-not $totalTime.ContainsKey($name)) { $totalTime[$name] = 0.0 }
            $totalTime[$name] += ($t - $top.at)
        }
    }

    # ClearFrame runs exactly once per rendered frame, so its close-event count is the frame count.
    $frameCount = ($renderProfile.events | Where-Object { $_.type -eq 'C' -and (Get-FrameName $frames $_.frame) -like '*ClearFrame*' }).Count
    $fps = if ($frameCount -gt 0) { [math]::Round($frameCount / ($captureMs / 1000.0), 1) } else { 0 }

    $topSelf = $selfTime.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First $TopCount
    $topTotal = $totalTime.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First $TopCount

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# ManaVoxel perf trace report")
    $lines.Add("")
    $lines.Add("Captured: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
    $lines.Add("Thread: $($renderProfile.name)")
    $lines.Add(("Capture window: {0:N1} ms ({1} frames, ~{2} fps)" -f $captureMs, $frameCount, $fps))
    $lines.Add("")
    $lines.Add("> The sample profiler only resolves managed frames. Time spent inside native/P-Invoke code")
    $lines.Add("> (e.g. wgpu_native.dll) collapses into the generic `CPU_TIME`/`UNMANAGED_CODE_TIME` buckets")
    $lines.Add("> below regardless of which C# method triggered it -- so **self time by name is not a useful")
    $lines.Add("> ranking on this codebase's native-heavy render path.** Use the TOTAL/inclusive table: it")
    $lines.Add("> correctly attributes native execution time to whichever managed method was on the stack.")
    $lines.Add("")
    $lines.Add("## Top $TopCount by total/inclusive time (ms, and ms/frame) -- start here")
    $lines.Add("")
    $lines.Add("| Total ms | ms/frame | % of capture | Function |")
    $lines.Add("|---:|---:|---:|---|")
    foreach ($row in $topTotal) {
        $perFrame = if ($frameCount -gt 0) { $row.Value / $frameCount } else { 0 }
        $pct = ($row.Value / $captureMs) * 100
        $lines.Add(("| {0:N2} | {1:N3} | {2:N1}% | {3} |" -f $row.Value, $perFrame, $pct, $row.Key))
    }
    $lines.Add("")
    $lines.Add("## Top $TopCount by self time (ms, and ms/frame)")
    $lines.Add("")
    $lines.Add("| Self ms | ms/frame | % of capture | Function |")
    $lines.Add("|---:|---:|---:|---|")
    foreach ($row in $topSelf) {
        $perFrame = if ($frameCount -gt 0) { $row.Value / $frameCount } else { 0 }
        $pct = ($row.Value / $captureMs) * 100
        $lines.Add(("| {0:N2} | {1:N3} | {2:N1}% | {3} |" -f $row.Value, $perFrame, $pct, $row.Key))
    }

    $lines | Set-Content -Path $reportPath -Encoding utf8

    $managedSelf = 0.0
    $nativeSelf = 0.0
    foreach ($k in $selfTime.Keys) {
        if ($k -eq 'CPU_TIME') { $managedSelf += $selfTime[$k] }
        elseif ($k -eq 'UNMANAGED_CODE_TIME') { $nativeSelf += $selfTime[$k] }
    }
    $nativePct = if ($captureMs -gt 0) { ($nativeSelf / $captureMs) * 100 } else { 0 }

    Write-Host ""
    Write-Host "=== $($fps) fps over $frameCount frames ($('{0:N1}' -f $captureMs) ms capture) ===" -ForegroundColor Cyan
    Write-Host ("~{0:N0}% of sampled time was inside native/unmanaged code (P/Invoke calls the profiler can't see past). Self-time-by-name is NOT useful here -- total/inclusive time below correctly attributes that native time to whichever C# method triggered it." -f $nativePct) -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "Top 10 by TOTAL (inclusive) time -- this is the actionable ranking:" -ForegroundColor Cyan
    foreach ($row in ($topTotal | Select-Object -First 10)) {
        $perFrame = if ($frameCount -gt 0) { $row.Value / $frameCount } else { 0 }
        "{0,8:N2} ms  ({1,6:N3} ms/frame)  {2}" -f $row.Value, $perFrame, $row.Key
    }
    Write-Host ""
    Write-Host "Full report (self + total tables) written to $reportPath"
}

if ($TraceFile -ne "") {
    Test-DotnetTrace
    $traceFullPath = (Resolve-Path $TraceFile).Path
    $speedscopeOut = [System.IO.Path]::ChangeExtension($traceFullPath, "speedscope.json")
    if (-not (Test-Path $speedscopeOut)) {
        dotnet-trace convert $traceFullPath --format speedscope -o $speedscopeOut
        $speedscopeOut = [System.IO.Path]::ChangeExtension($speedscopeOut, "speedscope.json")
    }
    $reportOut = [System.IO.Path]::ChangeExtension($traceFullPath, "report.md")
    Write-Report -speedscopePath $speedscopeOut -reportPath $reportOut
    return
}

Test-DotnetTrace

$launchedIt = $false
$proc = Get-Process -Name "ManaVoxel" -ErrorAction SilentlyContinue | Select-Object -First 1

if (-not $proc) {
    if (-not $Launch) {
        Write-Error "ManaVoxel.exe isn't running. Start it first, or pass -Launch to have this script build+launch it for you."
        exit 1
    }

    Write-Host "ManaVoxel.exe not found; building and launching (dotnet run --project ManaVoxel)..."
    Push-Location $repoRoot
    try {
        dotnet build ManaVoxel/ManaVoxel.csproj | Out-Null
        Start-Process -FilePath "dotnet" -ArgumentList "run","--project","ManaVoxel","--no-build" -WindowStyle Minimized
    } finally {
        Pop-Location
    }

    Write-Host "Waiting $WarmupSeconds s for startup/loading to settle..."
    $deadline = (Get-Date).AddSeconds(30)
    while (-not $proc -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 1
        $proc = Get-Process -Name "ManaVoxel" -ErrorAction SilentlyContinue | Select-Object -First 1
    }
    if (-not $proc) {
        Write-Error "ManaVoxel.exe never appeared after launch."
        exit 1
    }
    Start-Sleep -Seconds $WarmupSeconds
    $launchedIt = $true
}

$timestamp = Get-Date -Format "yyyy-MM-dd_HHmmss"
$traceOut = Join-Path $OutDir "trace_$timestamp.nettrace"
$speedscopeOut = Join-Path $OutDir "trace_$timestamp.speedscope.json"
$reportOut = Join-Path $OutDir "trace_$timestamp.report.md"

Write-Host "Capturing $DurationSeconds s from PID $($proc.Id) ..."
dotnet-trace collect -p $proc.Id --providers Microsoft-DotNETCore-SampleProfiler -o $traceOut --duration "00:00:$DurationSeconds"

Write-Host "Converting to speedscope..."
dotnet-trace convert $traceOut --format speedscope -o $speedscopeOut
# dotnet-trace convert appends its own ".speedscope.json" suffix on top of whatever -o was given.
$actualSpeedscopeOut = [System.IO.Path]::ChangeExtension($speedscopeOut, "speedscope.json")
if (-not (Test-Path $actualSpeedscopeOut)) { $actualSpeedscopeOut = $speedscopeOut }

Write-Report -speedscopePath $actualSpeedscopeOut -reportPath $reportOut

if ($launchedIt) {
    Write-Host "Stopping the ManaVoxel.exe instance this script launched (PID $($proc.Id))..."
    Stop-Process -Id $proc.Id -Force
}
