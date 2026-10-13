# ManaLidar

An iPhone/iPad LiDAR room scanner. It uses ARKit's scene reconstruction to build the room's mesh while you walk
around, keeps camera photos with LiDAR depth as it goes, and exports one textured, unlit `.glb` (metres, Y up,
floor at y = 0, room centred on the origin). Mana opens the GLB as it is.

- `ManaLidar/`: the iOS app (`net10.0-ios`): camera with a live wireframe, Start/Pause, New, Export, and a
  Scans list.
- `ManaLidar.Core/`: plain .NET with no Apple code. It merges the mesh and simplifies it (flat areas become a
  few large triangles), lays it out on one texture and paints that from the photos (skipping a photo wherever the
  depth saw something in front, favouring sharp, head-on views), then writes the glTF. For phones without LiDAR
  it also fuses depth frames into one surface (`ScanTsdfVolume`). It is tested by ManaTests on any desktop
  (`dotnet run --project ManaTests -- ManaLidar`).
- `ManaLidar.Android/`: the ARCore version, for phones with depth but no LiDAR.

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
4. **Export** names the scan, bakes its texture (a few seconds) and opens the share sheet. AirDrop it to the Mac.
   **Detail** sets the photo size kept while scanning and, at export, the texture (2K, or 4K for High and Ultra)
   and how much fine relief the simplified mesh keeps.
   Every export also stays in Files > On My iPhone > ManaLidar and in the app's **Scans** list (tap to share,
   swipe to delete).

The status line shows what the scan holds (`Scan 140 MB`) and how much more memory iOS will give the app before
closing it (`1.8 GB left`). Photos are nearly all of it; when "left" runs low, export, or add the room to a
building (below).

## A building, a room at a time

Export > **Add Room to a Building** exports the room into a building GLB and lets go of its photos and mesh, so
memory drops back. The finished room stays on screen as a dim outline. Walk on and scan the next room, then
**Add Room** again. The phone keeps one coordinate frame the whole time, so the rooms line up. The building
file is rewritten as each room goes in. It holds a node per room under one building node, so dropping it into
Mana places the whole building, with each room selectable in the Outliner. **Finish Building** shares the file.

Keep the app open between rooms. **New** (or closing the app) starts a new coordinate frame, and later rooms
would no longer line up with the earlier ones.

## While scanning: what is covered

The mesh over the camera is see-through, so a hole shows as a gap straight through to the camera picture. Its
colour is photo coverage: **red** where no photo has seen it yet, **amber** where only from far off or at a
glancing angle, **green** once seen close and head-on. Sweep until the room is green; red parts come out grey
or blurry in the texture.

## Looking round a scan and measuring it (on the phone)

Tap a scan in **Scans** to open it (the (i) button shares it instead). Drag to orbit, pinch to zoom, two fingers
to pan. Tap to place points: Distance, Radius, Path, or Ray (tap a spot and a ray goes to the next surface: out of
the surface tapped -- a floor to its ceiling, one side of a cabinet to the other -- or straight Up or Down; the
last button picks which), with the same snapping as the Mana editor's measuring
tool (corners, real edges and their middles, a larger reach for a fingertip). **Floor** keeps later points on
the first point's surface; **m/ft** switches units; **Undo**, **Clear**, and **Done** to finish a path. The
measuring rules are the editor's own files (`ManaVoxel/Code/Measure/`), compiled into the app.

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
