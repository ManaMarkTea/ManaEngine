<#
.SYNOPSIS
    Builds ManaLidar on the Mac and installs it on the iPhone, from Windows: the command-line version of Visual
    Studio's Pair to Mac. Finds the Mac on the network, sends this PC's current commit to a deploy copy there over
    SSH (no GitHub needed, the Mac's own checkout untouched) and runs deploy-ios.sh in it.

.DESCRIPTION
    Finds the Mac by itself: a Mac with Remote Login on (System Settings > General > Sharing) announces itself
    over Bonjour, and the first one that answers is used. The first run asks for the Mac's user name (remembered)
    and, once, its password, to install this PC's SSH key so later runs need none.

    The phone must be plugged into the Mac, or paired over Wi-Fi once (Xcode > Window > Devices and Simulators >
    Connect via network).

    -Mac (or MANA_MAC) names a Mac instead, as user@host or just host; MANA_MAC_REPO if the checkout on the Mac is
    not the one the script finds by itself.

.EXAMPLE
    ./Tools/ManaLidar/deploy-ios-from-windows.ps1
.EXAMPLE
    ./Tools/ManaLidar/deploy-ios-from-windows.ps1 -Configuration Debug
#>
param(
    [string] $Mac = $env:MANA_MAC,
    # The checkout on the Mac; found by itself (and remembered) when not given.
    [string] $Repo = $env:MANA_MAC_REPO,
    [ValidateSet("Debug", "Release")] [string] $Configuration = "Release",
    # Which app: the scanner, or Mana itself (the engine on the phone or iPad).
    [ValidateSet("ManaLidar", "ManaVoxel.iOS")] [string] $App = "ManaLidar",
    # An iPhone or iPad by name or UDID, when several are paired with the Mac.
    [string] $Device = $env:MANA_IOS_DEVICE,
    # Unused: the keychain is now always unlocked with the password kept from the first run.
    [switch] $UnlockKeychain,
    # Unused: the commit now goes straight to the Mac. Kept so older command lines still run.
    [switch] $Push
)

$ErrorActionPreference = "Stop"
$settingsDir = Join-Path $env:LOCALAPPDATA "ManaRenderer"
$userFile = Join-Path $settingsDir "mac-user.txt"
$repoFile = Join-Path $settingsDir "mac-repo.txt"

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
        Write-Host "Your Mac's account name -- the short one, like 'mark', not your full name. On the Mac: run 'whoami'" -ForegroundColor Yellow
        Write-Host "in Terminal, or see Finder > Go > Home (the house folder's name)." -ForegroundColor Yellow
        $user = Read-Host "Mac account name"
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
    Write-Host "Installing this PC's key on the Mac. At '$Mac's password:' type your Mac login password (the one" -ForegroundColor Yellow
    Write-Host "that unlocks the Mac) and press Enter -- nothing shows as you type. Later runs will not ask." -ForegroundColor Yellow
    Get-Content "$key.pub" | ssh @sshOptions $Mac "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"
    ssh @sshOptions -o BatchMode=yes $Mac "true"
    if ($LASTEXITCODE -ne 0) {
        if (Test-Path $userFile) { Remove-Item $userFile }
        throw "Could not log in to $Mac. The account name or password was not right; the name will be asked again next run."
    }
}

# ---------------------------------------------------------------- the Mac's checkout, to seed a deploy copy from
if (-not $Repo -and (Test-Path $repoFile)) {
    $Repo = (Get-Content $repoFile -Raw).Trim()
    ssh @sshOptions -o BatchMode=yes $Mac "test -f $Repo/ManaLidar.Core/ManaLidar.Core.csproj" 2>$null
    if ($LASTEXITCODE -ne 0) { $Repo = "" }
}
if (-not $Repo) {
    # Wherever it was cloned: by the project file, since build output has folders of the same name.
    $project = ssh @sshOptions -o BatchMode=yes $Mac 'find ~ -maxdepth 7 -name ManaLidar.Core.csproj -not -path "*/Library/*" -not -path "*/.Trash/*" -not -path "*/.mana-deploy/*" -not -path "*/Artifacts/*" 2>/dev/null | head -1'
    if (-not $project) {
        throw "No ManaRenderer checkout found on the Mac. Clone it there, or pass -Repo ~/path/to/ManaRenderer."
    }
    $Repo = $project.Trim() -replace "/ManaLidar\.Core/ManaLidar\.Core\.csproj$", ""
    New-Item -ItemType Directory -Force $settingsDir | Out-Null
    Set-Content $repoFile $Repo
}

# ---------------------------------------------------------------- send this PC's commit to a deploy copy on the Mac
# A copy of its own, so the Mac's working tree (someone may be working in it) is never touched, and GitHub is not
# involved: an SSH session cannot use the Mac's keychain for GitHub. The copy is seeded from the Mac's checkout
# (a local clone, quick), then this PC pushes its commit straight into it.
$deploy = ".mana-deploy/ManaRenderer"
ssh @sshOptions -o BatchMode=yes $Mac "test -d ~/$deploy/.git || GIT_LFS_SKIP_SMUDGE=1 git clone --quiet --no-checkout '$Repo' ~/$deploy"
if ($LASTEXITCODE -ne 0) {
    throw "Could not make the deploy copy on the Mac."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")
$commit = (git -C $repoRoot rev-parse --short HEAD).Trim()
if (git -C $repoRoot status --porcelain -- $App ManaLidar.Core ManaVoxel/Code/Measure ManaGraphics Mana ManaCore ManaUI Tools/Mobile) {
    Write-Host "Uncommitted changes to the app are not sent: building the last commit, $commit." -ForegroundColor Yellow
}
Write-Host "Sending $commit to the Mac..."
# Large files tracked by Git LFS (planet textures) are not needed by the phone app and have nowhere to go.
$env:GIT_LFS_SKIP_PUSH = "1"
# Into a branch that is never checked out (the build below detaches at it), or the next push is refused.
git -C $repoRoot push --quiet --force "$($Mac):$deploy" HEAD:refs/heads/incoming
Remove-Item Env:GIT_LFS_SKIP_PUSH
if ($LASTEXITCODE -ne 0) {
    throw "Could not send the commit to the Mac."
}

# ---------------------------------------------------------------- build and install, on the Mac
# Signing needs the login keychain, which an SSH session finds locked (errSecInternalComponent). Its password is
# asked once and kept encrypted for this Windows account only (DPAPI), then sent over stdin -- never on a command
# line. Once, codesign is also let use the key without the click-to-allow dialog nobody is at the Mac to answer.
$secretFile = Join-Path $settingsDir "mac-password.xml"
if (Test-Path $secretFile) {
    $secure = Import-Clixml $secretFile
}
else {
    Write-Host "To sign the app over SSH the Mac's keychain must be unlocked. Your Mac login password is asked once and" -ForegroundColor Yellow
    Write-Host "kept encrypted for your Windows account only ($secretFile)." -ForegroundColor Yellow
    $secure = Read-Host "Mac login password" -AsSecureString
    New-Item -ItemType Directory -Force $settingsDir | Out-Null
    $secure | Export-Clixml $secretFile
}
$password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))

# A login shell (zsh -l), or dotnet is not on the PATH: a plain SSH command skips the Mac's profile.
$keychain = "~/Library/Keychains/login.keychain-db"
$steps = "read -rs PW; security unlock-keychain -p `"`$PW`" $keychain 2>~/.mana-deploy/unlock.err || exit 77; " +
    # Best effort, once: it can report "passphrase not correct" over one odd item even with the right password,
    # and signing works without it where Xcode already let codesign use the key.
    "[ -f ~/.mana-deploy/.signing-ready ] || { security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k `"`$PW`" $keychain >/dev/null 2>&1; touch ~/.mana-deploy/.signing-ready; }; " +
    "unset PW; cd ~/$deploy && GIT_LFS_SKIP_SMUDGE=1 git checkout --quiet --force --detach incoming && APP=$App DEVICE=`"$Device`" Tools/ManaLidar/deploy-ios.sh $Configuration"
$password | ssh @sshOptions -o BatchMode=yes $Mac "zsh -lc '$steps'"
$code = $LASTEXITCODE
$password = $null

if ($code -eq 77) {
    # Only a refused passphrase means the stored one is wrong; anything else is kept and reported.
    $reason = (ssh @sshOptions -o BatchMode=yes $Mac "cat ~/.mana-deploy/unlock.err 2>/dev/null") -join " "
    if ($reason -match "passphrase|password") {
        Remove-Item $secretFile
        Write-Host "The Mac did not accept the stored password ($reason). It was forgotten; the next run asks again." -ForegroundColor Yellow
    }
    else {
        Write-Host "Unlocking the Mac's keychain failed: $reason" -ForegroundColor Yellow
    }
    exit 1
}
if ($code -ne 0) {
    Write-Host ""
    Write-Host "The deploy on the Mac failed (exit $code)." -ForegroundColor Yellow
    exit $code
}
Write-Host "$App ($commit) is on the device." -ForegroundColor Green
