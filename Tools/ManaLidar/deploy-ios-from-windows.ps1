<#
.SYNOPSIS
    Builds ManaLidar on the Mac and installs it on the iPhone, from Windows: the command-line version of Visual
    Studio's Pair to Mac. Finds the Mac on the network, logs in over SSH, pulls this branch there and runs
    deploy-ios.sh.

.DESCRIPTION
    Finds the Mac by itself: a Mac with Remote Login on (System Settings > General > Sharing) announces itself
    over Bonjour, and the first one that answers is used. The first run asks for the Mac's user name (remembered)
    and, once, its password, to install this PC's SSH key so later runs need none.

    The phone must be plugged into the Mac, or paired over Wi-Fi once (Xcode > Window > Devices and Simulators >
    Connect via network).

    -Mac (or MANA_MAC) names a Mac instead, as user@host or just host; MANA_MAC_REPO if the checkout on the Mac is
    not ~/Code/ManaRenderer.

.EXAMPLE
    ./Tools/ManaLidar/deploy-ios-from-windows.ps1
.EXAMPLE
    ./Tools/ManaLidar/deploy-ios-from-windows.ps1 -Configuration Debug -UnlockKeychain
#>
param(
    [string] $Mac = $env:MANA_MAC,
    [string] $Repo = $(if ($env:MANA_MAC_REPO) { $env:MANA_MAC_REPO } else { "~/Code/ManaRenderer" }),
    [ValidateSet("Debug", "Release")] [string] $Configuration = "Release",
    # Signing needs the Mac's login keychain, which an SSH session finds locked ("errSecInternalComponent").
    # This asks for the Mac password and unlocks it for the build.
    [switch] $UnlockKeychain,
    # Push this branch first, so the Mac builds what is here.
    [switch] $Push
)

$ErrorActionPreference = "Stop"
$settingsDir = Join-Path $env:LOCALAPPDATA "ManaRenderer"
$userFile = Join-Path $settingsDir "mac-user.txt"

# ---------------------------------------------------------------- which Mac
if (-not $Mac) {
    Add-Type -Path (Join-Path $PSScriptRoot "FindMac.cs")
    Write-Host "Looking for a Mac with Remote Login on..."
    $found = [ManaFindMac]::Find(2500)
    if ($found.Count -eq 0) {
        throw "No Mac answered. On the Mac: System Settings > General > Sharing > Remote Login on, and both on the same network. Or pass -Mac you@host."
    }
    $first = $found[0]
    # The name when Windows can resolve it (it survives the Mac's address changing), else the address.
    $macHost = if ($first.Host -and (Resolve-DnsName $first.Host -ErrorAction SilentlyContinue)) { $first.Host } else { $first.Address }
    Write-Host "Found $($first.Name) ($macHost)" -ForegroundColor Cyan
    $Mac = $macHost
}
if ($Mac -notlike "*@*") {
    $user = if (Test-Path $userFile) { (Get-Content $userFile -Raw).Trim() } else { "" }
    if (-not $user) {
        $user = Read-Host "User name on the Mac (as in its Users folder)"
        New-Item -ItemType Directory -Force $settingsDir | Out-Null
        Set-Content $userFile $user
    }
    $Mac = "$user@$Mac"
}

# ---------------------------------------------------------------- passwordless login, set up once
$sshOptions = @("-o", "StrictHostKeyChecking=accept-new", "-o", "ConnectTimeout=8")
ssh @sshOptions -o BatchMode=yes $Mac "true" 2>$null
if ($LASTEXITCODE -ne 0) {
    $key = Join-Path $env:USERPROFILE ".ssh\id_ed25519"
    if (-not (Test-Path "$key.pub")) {
        Write-Host "Making an SSH key for this PC..."
        New-Item -ItemType Directory -Force (Split-Path $key) | Out-Null
        ssh-keygen -t ed25519 -N "" -q -f $key
    }
    Write-Host "Installing this PC's key on the Mac -- enter the Mac password once; later runs will not ask." -ForegroundColor Yellow
    Get-Content "$key.pub" | ssh @sshOptions $Mac "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"
    ssh @sshOptions -o BatchMode=yes $Mac "true"
    if ($LASTEXITCODE -ne 0) {
        if (Test-Path $userFile) { Remove-Item $userFile }
        throw "Could not log in to $Mac. Check the user name (asked again next run) and that Remote Login is on."
    }
}

# ---------------------------------------------------------------- what to build
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")
$branch = (git -C $repoRoot rev-parse --abbrev-ref HEAD).Trim()
if ($Push) {
    git -C $repoRoot push origin $branch
}
# The Mac pulls from the remote, so anything only here would be silently left out.
$ahead = (git -C $repoRoot rev-list --count "origin/$branch..HEAD" 2>$null)
if ($ahead -and [int]$ahead -gt 0) {
    throw "$ahead commit(s) on $branch are not pushed yet, so the Mac would build without them. Push, or run with -Push."
}

# ---------------------------------------------------------------- build and install, on the Mac
# A login shell (zsh -l), or dotnet is not on the PATH: a plain SSH command skips the Mac's profile.
$steps = "cd $Repo && git fetch --quiet && git checkout --quiet $branch && git pull --ff-only --quiet && Tools/ManaLidar/deploy-ios.sh $Configuration"
if ($UnlockKeychain) {
    $secure = Read-Host "Mac login password (unlocks the signing keychain)" -AsSecureString
    $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
    # Read from stdin on the Mac, so the password is never on a command line.
    $steps = "read -rs PW && security unlock-keychain -p `"`$PW`" ~/Library/Keychains/login.keychain-db && unset PW && $steps"
    $password | ssh @sshOptions -o BatchMode=yes $Mac "zsh -lc '$steps'"
}
else {
    ssh @sshOptions -o BatchMode=yes $Mac "zsh -lc '$steps'"
}

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "The deploy on the Mac failed (exit $LASTEXITCODE). If signing failed with errSecInternalComponent, run again with -UnlockKeychain." -ForegroundColor Yellow
    exit $LASTEXITCODE
}
Write-Host "ManaLidar is on the phone." -ForegroundColor Green
