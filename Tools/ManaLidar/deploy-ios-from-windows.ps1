<#
.SYNOPSIS
    Builds ManaLidar on the Mac and installs it on the iPhone, from Windows: the command-line version of Visual
    Studio's Pair to Mac. Logs into the Mac over SSH, pulls this branch there and runs deploy-ios.sh.

.DESCRIPTION
    One-time setup:
      Mac:     System Settings > General > Sharing > Remote Login on.
      Windows: ssh-keygen   (Enter to accept the defaults), then once:
               type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh you@your-mac.local "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys"
      Phone:   plugged into the Mac, or paired over Wi-Fi once (Xcode > Window > Devices and Simulators >
               Connect via network) -- after that it deploys without the cable.
    Set MANA_MAC=you@your-mac.local (and MANA_MAC_REPO if the checkout is not ~/Code/ManaRenderer) to skip -Mac.

.EXAMPLE
    ./Tools/ManaLidar/deploy-ios-from-windows.ps1 -Mac mark@marks-macbook.local
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
if (-not $Mac) {
    throw "Which Mac? Pass -Mac you@your-mac.local, or set MANA_MAC (see the comment at the top of this script)."
}

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

# A login shell (zsh -l), or dotnet is not on the PATH: a plain SSH command skips the Mac's profile.
$steps = "cd $Repo && git fetch --quiet && git checkout --quiet $branch && git pull --ff-only --quiet && Tools/ManaLidar/deploy-ios.sh $Configuration"
if ($UnlockKeychain) {
    $secure = Read-Host "Mac login password (unlocks the signing keychain)" -AsSecureString
    $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
    # Read from stdin on the Mac, so the password is never on a command line.
    $steps = "read -rs PW && security unlock-keychain -p `"`$PW`" ~/Library/Keychains/login.keychain-db && unset PW && $steps"
    $password | ssh -o BatchMode=yes $Mac "zsh -lc '$steps'"
}
else {
    ssh -o BatchMode=yes $Mac "zsh -lc '$steps'"
}

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "The deploy on the Mac failed (exit $LASTEXITCODE). If signing failed with errSecInternalComponent, run again with -UnlockKeychain." -ForegroundColor Yellow
    exit $LASTEXITCODE
}
Write-Host "ManaLidar is on the phone." -ForegroundColor Green
