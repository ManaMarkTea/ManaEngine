#!/usr/bin/env bash
# Once, on the Mac: copies the Apple Development signing identity out of the login keychain into a keychain of its
# own, locked with a random password kept only on this Mac (~/.mana-deploy/signing/password, readable by this user
# only). Deploys over SSH then unlock that keychain instead of the login one, so the Mac's login password is never
# stored elsewhere or asked for again. Reads the login password from stdin (it unlocks the login keychain to export).
# Re-run it if the certificate is renewed.
#
# macOS only lets a private key be exported after a click on the Mac's own screen, so the first time this has to run
# there, in Terminal:  bash ~/.mana-deploy/setup-mac-signing.sh   (Allow the dialog that asks to export the key).
# Over SSH the export is refused, which exits 3.
set -euo pipefail
dir="$HOME/.mana-deploy/signing"
keychain="$dir/mana-signing.keychain-db"
login="$HOME/Library/Keychains/login.keychain-db"
mkdir -p "$dir" && chmod 700 "$dir"

if [ -t 0 ]; then
  printf 'Mac login password: ' >&2
fi
read -rs LOGIN_PW
[ -t 0 ] && echo >&2
security unlock-keychain -p "$LOGIN_PW" "$login" || exit 2

password="$(openssl rand -hex 24)"
export_pw="$(openssl rand -hex 16)"
p12="$dir/identity.p12"
trap 'rm -f "$p12"' EXIT
# The key's access list already lets Apple's tools use it (set once by the first deploy), so this needs no click.
if ! security export -k "$login" -t identities -f pkcs12 -P "$export_pw" -o "$p12"; then
  echo "The signing key can only be exported from the Mac's own screen: in Terminal there, run" >&2
  echo "  bash ~/.mana-deploy/setup-mac-signing.sh" >&2
  exit 3
fi

security delete-keychain "$keychain" 2>/dev/null || true
security create-keychain -p "$password" "$keychain"
# No auto-lock timeout and no lock on sleep: each deploy unlocks it anyway.
security set-keychain-settings "$keychain"
security unlock-keychain -p "$password" "$keychain"
security import "$p12" -k "$keychain" -P "$export_pw" -T /usr/bin/codesign -T /usr/bin/security >/dev/null
# Lets codesign use the key without the click-to-allow dialog nobody is at the Mac to answer.
security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$password" "$keychain" >/dev/null

# First in the search list, so codesign finds the identity here, unlocked, rather than the locked login copy.
existing=$(security list-keychains -d user | tr -d '"' | grep -v "$keychain" || true)
security list-keychains -d user -s "$keychain" $existing

umask 077
printf '%s' "$password" > "$dir/password"
unset LOGIN_PW password export_pw
security find-identity -v -p codesigning "$keychain" | head -1
echo "Signing keychain ready: $keychain"
