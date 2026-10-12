# ManaLidar

An iPhone/iPad LiDAR room scanner. It uses ARKit's scene reconstruction to build the room's mesh while you walk
around, keeps small camera photos with LiDAR depth as it goes, and exports one coloured, unlit `.glb` (metres,
Y up, floor at y = 0, room centred on the origin). Mana opens the GLB as it is.

- `ManaLidar/`: the iOS app (`net10.0-ios`): camera with a live wireframe, Start/Pause, New, Export, and a
  Scans list.
- `ManaLidar.Core/`: plain .NET with no Apple code. It merges the mesh, colours the vertices from the photos
  (skipping a photo wherever the LiDAR saw something in front) and writes the glTF. It is tested by ManaTests on
  any desktop (`dotnet run --project ManaTests -- ManaLidar`).

Needs a LiDAR device: iPhone 12 Pro or later Pro, or an iPad Pro from 2020 on.

## One-time Mac setup

1. **Xcode**: install it from the App Store, open it once to accept the licence and let it install its
   components, then run `sudo xcode-select -s /Applications/Xcode.app`. .NET 10's iOS workload wants the Xcode
   version it was built against; if the build says the Xcode is too old or new, `dotnet workload update` usually
   fixes it, or install the Xcode it names from developer.apple.com/download/all.
2. **.NET 10 SDK** (arm64) from dot.net, then install the iOS workload:
   ```bash
   sudo dotnet workload install ios
   ```
3. **Signing**: in Xcode > Settings > Accounts, add your Apple ID. With a paid developer account Xcode manages
   the certificate. Check it is there:
   ```bash
   security find-identity -p codesigning -v     # expect "Apple Development: <you> (<TEAMID>)"
   ```
4. **A provisioning profile for the app id.** The quickest way is to let Xcode make one: File > New > Project >
   iOS App, set the Bundle Identifier to `com.manarenderer.manalidar` (or your own id; see below) and your
   Team, plug the phone in and press Run once. That registers the device and the id, and leaves a profile .NET
   finds by itself. Then delete the throwaway project.
5. **The phone**: plug it in by cable and trust the Mac. Turn on Settings > Privacy & Security > Developer Mode
   and restart when asked.

To use your own bundle id, change `ApplicationId` in `ManaLidar.csproj`, or pass `-p:ApplicationId=com.you.manalidar`.

## Build and run

```bash
git pull
dotnet build ManaLidar -c Debug                # builds and signs the .app
dotnet build ManaLidar -t:Run -c Debug         # also installs it on the plugged-in phone and launches it
```

With more than one device attached, pick one with `-p:_DeviceName=<UDID>` (`xcrun devicectl list devices`).
The first launch asks for camera access. If iOS says the developer is untrusted, go to Settings > General >
VPN & Device Management and trust your certificate.

A release build (faster colouring on export) is `dotnet build ManaLidar -t:Run -c Release`.

You can also open the folder in VS Code with the C# Dev Kit and .NET MAUI extensions, which can debug on the
phone. Rider works too.

## Scanning

1. **Start**, then walk the room slowly with the phone at chest height. Point it at every wall, the floor,
   the ceiling and round the furniture until the cyan wireframe covers it. Hold surfaces at 1-3 m for the
   best colour.
2. A hint under the counts says when tracking struggles ("Move slower", "more light").
3. **Pause** freezes the scan; **Resume** carries on once the phone recognises where it is.
4. **Export** names the scan, colours it (a few seconds) and opens the share sheet. AirDrop it to the Mac.
   Every export also stays in Files > On My iPhone > ManaLidar and in the app's **Scans** list (tap to share,
   swipe to delete).

## Viewing in Mana

Drop the `.glb` onto the ManaVoxel editor window (`dotnet run --project ManaVoxel`) to place it in the open
level. Scans are unlit, because their colours come from photos that already hold the room's light. Any glTF
viewer opens them too, including Blender and macOS Preview/Quick Look.

## Why Mana doesn't render on the phone (yet)

Mana draws through wgpu-native, whose NuGet package ships Windows, macOS and Linux binaries but none for iOS,
and its windows come from GLFW, which iOS doesn't have. So the scanner draws its live view with SceneKit, and
Mana takes over once the scan is a GLB. Two ways to get Mana onto the phone later: build wgpu-native as an iOS
static library and give `ManaGraphics` a `CAMetalLayer` surface, or use the web build. Safari on iOS 26 has
WebGPU, so `ManaVoxel.Web` could view scans in the browser today.
