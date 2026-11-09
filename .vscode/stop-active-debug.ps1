$ErrorActionPreference = 'SilentlyContinue'

try {
    $processes = @(Get-CimInstance Win32_Process | Where-Object {
        ($_.Name -ieq 'vsdbg.exe') -or
        (
            ($_.Name -ieq 'dotnet.exe') -and
            $_.CommandLine -and
            (
                ($_.CommandLine -like '*Mana.dll*') -or
                ($_.CommandLine -like '*ManaEditor.dll*')
            )
        )
    })

    foreach ($process in $processes) {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    }
}
catch {
    # Pre-build cleanup should never block launching or building.
}

exit 0