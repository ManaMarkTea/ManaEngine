# ManaRenderer

C# .NET 10 renderer / game engine using Silk.NET. Target platforms are **macOS (arm64)** and **Windows**. **WebGPU** (wgpu via Silk.NET) is the graphics backend on every platform. The legacy OpenGL backend has been removed.

## Projects

| Project | Role |
|---|---|
| `ManaCore` | Shared interfaces and data types (no graphics dependency) |
| `ManaGraphics` | WebGPU backend implementation, WGSL shaders |
| `Mana` | Engine layer — scene graph, components, renderers, game modes |
| `ManaUI` | Retained-mode MVVM UI framework (no graphics dependency — headless-testable) |
| `ManaTests` | Headless test suite for all systems; suites live in `Code/<System>/` |
| `ManaVoxel` | Voxel editor app (the main runnable executable) |
| `Games/<Name>` | One project per game (`Games/TrainYardWars`): rules, view and level game mode, on the engine only; ManaVoxel references each and registers it (`ManaVoxel/Code/Levels/ManaGames.cs`) |
| `ManaVoxel.iOS` | The game natively on iPhone/iPad: `VoxelPlayGameMode` on wgpu-native's Metal backend (`Tools/Mobile/build-wgpu-ios.sh` builds the library Silk.NET does not ship), a platform window and touch controls through `IManaWebGpuNativeHost`, and `Game/` in the bundle (`Tools/Mobile/game_assets.py`). Deploy with `-App ManaVoxel.iOS`; `Documents/commands.json` on the phone runs as `--command-file` (`@@DOCUMENTS@@` for paths) |
| `ManaLidar` / `ManaLidar.Core` | iPhone LiDAR room scanner (`net10.0-ios`, built on the Mac; `ManaLidar/README.md`) and its Apple-free half -- ARKit mesh (or fused ARCore depth) plus keyframe photos to a simplified, textured, unlit GLB that Mana opens |

**Runnable entry point:** `ManaVoxel`

## Build & Run

```bash
dotnet build
dotnet run --project ManaVoxel

# The game, standalone: one level, no editor UI
dotnet run --project ManaVoxel -- --play "Content/Levels/Sample Course.manalevel"

# The editor, opened on a level
dotnet run --project ManaVoxel -- --level "Content/Levels/Sample Course.manalevel"

# Train Yard Wars (couch game prototype; Plans/Couch-Co-op-GameDesignDocuments/TrainYardWars.md) -- a level whose game mode
# builds the world; or open it in the editor and press F5. --command-file works with --play too.
dotnet run --project ManaVoxel -- --play "Games/TrainYardWars/Content/Levels/Train Yard Wars.manalevel"

# Test suite — no GPU or window required; exits non-zero on failure
dotnet run --project ManaTests

# One system's suites only
dotnet run --project ManaTests -- ManaUI

# Results in a window, drawn with ManaUI itself (filter, failures-only, re-run)
dotnet run --project ManaTests -- -gui
```

Add coverage by dropping a suite in `ManaTests/Code/<System>/` and registering it in the suite list in
`ManaTests/Code/TestRun.cs`. Suite files end in `Tests.cs`, and live under the system they exercise --
asset and model logic under its own project, not under ManaUI because a panel happens to call it.

- **Unit tests stay fast:** test one rule with a small set-up, never by playing a whole match or level out (that is
  what the `--command-file` scripts are for). The run ends with its slowest suites; one over a second or two is a smell.
- **Suites run in parallel.** Never change process-wide state in one (`ManaContentAssets.Root`, the working directory,
  a static setting) -- it leaks into whatever else is running. Build a fresh `ManaWorld`/`ManaContext` per suite.
- **When the normal output is locked** (a debugger holding `Artifacts/Bin/ManaVoxel`), build the tests elsewhere and
  run the dll: `dotnet build ManaTests -o Artifacts/testcheck` then `dotnet Artifacts/testcheck/ManaTests.dll`.
- **Content checks are strict:** `ContentAssetChecks` loads every prefab and every bundled level and fails on any load
  warning; bundled level roots must sit at x and z >= 32 (the scene offset) with cameras clear of the zero-axis walls;
  every picture a `.manasheet`/`.manaslice` names must exist in its pack.

## Testing levels with scripts

The editor can be driven by a JSON script instead of a person: `ManaVoxel --command-file <script.json>` runs the
steps one per frame (`Mana/Code/Automation/ManaCommandRunner.cs` holds the commands every app has and a `Register(type, handler)` hook, format in `ManaCommandFile.cs`; the editor-only ones -- open_level, prefabs, panels, grid maps, bow -- are registered by `ManaVoxel/Code/Automation/VoxelCommands.cs`) and is how
anything visual or interactive gets checked -- in-app screenshots, not desktop captures (those can show the user's
other windows). `Tools/Automation/run-commands.ps1` does the plumbing: writes the script, runs the editor from the
repo root, waits for it to quit and prints what it reported. `@@OUT@@` in a path becomes `Artifacts/Automation/<Name>.png`;
`@@PHOTO@@` becomes `DevPhotos/<today>` (`-PhotoDir` sends those somewhere else for a look first).

```powershell
dotnet build ManaVoxel -o Artifacts/AutomationBin      # a separate output, so a running editor or debugger is no problem
./Tools/Automation/run-commands.ps1 -Name course -Commands '[
  {"type":"open_level","path":"Content/Levels/2D Sample.manalevel"}, {"type":"wait","frames":240},
  {"type":"play"}, {"type":"wait","frames":90},
  {"type":"teleport_player","position":[100,1.3,40]}, {"type":"wait","frames":45},
  {"type":"log_player","name":"desert"}, {"type":"screenshot","path":"@@OUT@@"},
  {"type":"quit"}]'
```

| Command | Fields | Does |
|---|---|---|
| `open_level` | `path` | Opens a level in the editor, as File > Open does |
| `wait` | `frames` | Holds that many rendered frames (loading, physics settling, an animation) |
| `move_camera` | `position`, `target`, `frames`, `background`, `fov` | Puts the editor camera somewhere, facing a point; with `frames`, glides there eased (`background` lets the script go on meanwhile); `fov` sets the vertical field of view in degrees (55 by default) |
| `select_node` | `name` | Selects a node by name (brings up its Inspector and gizmo), or a scene by name (its level settings and game modes) |
| `screenshot` | `path`, `clean` | Writes the next frame to a PNG (WebGPU only); `clean` leaves out every panel and the HUD -- the scene across the whole window |
| `wind` | `position` (speed m/s, direction degrees, gustiness) | Sets the world wind (`ManaWind`): 0 is a glassy pond, 2.5 the default breeze, 4 a fresh one, 12+ chop |
| `gizmos` | `visible` | Editor helpers (axes, markers) on or off -- off for pictures |
| `wireframe` | `visible`, `name` | The View menu's wireframe overlay on or off (toggles without `visible`); `name: "normals"` does the vertex normals |
| `record` / `stop_record` | `path` (folder), `frames` (fps, 30), `clean`, `visible` | Saves every frame as `<path>/00000.jpg..` at a fixed step (1/fps a frame, so a slow save never speeds the game up); `record` with no path changes `clean`, and `visible: false` holds capture without ending the take. `Tools/Automation/sizzle.py` joins the takes of `Content/Scripts/Sizzle Reel.json` into an MP4 |
| `play` / `stop_play` | | Enters and leaves play mode (F5) |
| `teleport_player` | `position` | While playing, moves the character -- to look at a stretch of a level without walking there |
| `log_player` | `name` | While playing, logs `Command file: player <name> at <feet>, grounded <bool>` |
| `sky` | `name`, `skyDateTime`, `timeOfDay`, `frames`, `background` | Sky mode by name (`Day`, `DayNightCycle`, `Space`, `Photo`); `skyDateTime` is an ISO-8601 instant, `timeOfDay` holds a selected hour on that date, and with `frames` the day runs forward to it |
| `dirty_report` | `path` | Writes each open level's saved snapshot and a fresh capture (`<path>.saved.json`, `.now.json`) -- identical means it opens unchanged |
| `open_prefab` / `open_asset` / `review_changes` | `path` / `path` / `name` | Edit Prefab; a sheet or slice in the Sprite Asset window (or rules in the Generator); Review Changes on an instance |
| `set_theme` | `name` (`Dark`, `Light`, `LCARS`, `Kenney`) | Switches the active ManaUI root theme; available in every ManaUI-enabled app |
| `open_panel` / `open_tools` | `name` / | Brings up a panel by id; drops down the viewport bar's snap options |
| `emission` | `name`, `position` (rgb), `frames` (strength x100) | Sets emission on every mesh under a node |
| `dump_environment` | `path` | Saves the reflection cube's six faces as `<path>.face0..5.png` (+X -X +Y -Y +Z -Z) |
| `material` | `name`, `position` (metallic, roughness) | Sets metallic and roughness on every mesh under a node -- `[1, 0]` is a mirror |
| `drop_to_floor` | | Drops the selection onto whatever is below it (the End key) |
| `gizmo_mode` / `scale_box` | `name` / `name`, `position` | Translate, Rotate or Scale; drags a Scale box handle of the selection (`+x`, `-y`, a corner `-x+y`) to a world point and logs the node's position and scale |
| `viewport_menu` | `position`, `name` | The viewport's right-click Add menu at a world point; with `name` (an entry, e.g. `Cube`) picks it and logs the new node's position and scene |
| `grid_autotile` / `grid_generate` | see `ManaCommandFile` | Paints a road preset through cells; runs the WFC generator |
| `planet_detail` / `planet_status` / `planet_merge` | `name` (planet, default `Earth`); detail: `position` (longitude, latitude), `count` (target zoom, default 7); merge: `position` (root face, row, column) | Desktop planet checks: request detail, log streaming counters, or merge a root's refined children. Fixture: `Tools/Automation/Checks/planet-streaming.json` |
| `bow` | `name` (`hold`, `hold_up`, `release`) | Holds or lets go of the Bow's fire key while playing, so a script can shoot |
| `import_filter` | `name` | Types into the Import panel's filter (e.g. a pack name) to bring its models into view |
| `profile_reset` / `profile_dump` | `path` | Frame profiler: start fresh; write the per-scope report |
| `gpu_report` | `path` (optional) | Live GPU object counts by kind from wgpu-native's registry (buffers, textures, views, bind groups, pipelines, command buffers), and what moved since the last call; desktop/mobile only. Run it twice with a still scene between: a climbing count is a leak. Blind to wrapper handles such as pass encoders. `Tools/Automation/Checks/gpu-leak.json` |
| `audio_test` / `audio_status` | | Plays a diagnostic tone and logs device availability/volume; reports source, playing-loop and loading-error counts. Pass `-Sound` to the automation runner to hear playback. Startup logs the selected desktop output device. |
| `terrain` | `name`, `position`, `target` ([radius, strength, layer]), `count` | `sculpt`/`off` turns sculpting on the selected (or first) terrain on and off; a brush name (`raise`, `lower`, `smooth`, `flatten`, `noise`, `paint`) dabs it `count` times at a world `position` (flatten levels to its y), one undo step. Fixture: `Tools/Automation/Checks/terrain.json` |
| `save_as` | `path` | Saves the open level to `path` as File > Save Level As does (renamed to match, editing carries on in the copy); without `path`, drops the Save As box down |
| `debug_view` | `name` | The PBR debug view by name or number (Scene Inspector > PBR debug): `Light Count`, `Light Cells`, `Shadow Cascades`, `View Depth`, `World Normal`... `Off` for the shaded picture. Fixture: `Tools/Automation/Checks/debug-views.json` |
| `measure` | `name`, `position` | The measuring tool: `distance`/`radius`/`path`/`ray` (turn it on in that mode; `ray_out`/`ray_up`/`ray_down` set the ray), `click`/`hover` at a world `position` (projected to the screen and snapped exactly as the pointer would be; logs what it snapped to and the last measurement), `finish`, `clear`, `stay`/`free`, `metric`/`imperial`, `off` |
| `quit` | | Closes the editor (always end with it, or the runner times out) |

Reference scenes for rendering checks live in `Content/Levels/Tests/` (not in the web selector), with the scripts that
drive them in `Tools/Automation/Checks/` -- `./Tools/Automation/run-commands.ps1 -Name shadows -File Tools/Automation/Checks/shadows.json`.
`Shadow Contact` (shadows meeting their casters, objects in shadow, low sun) and `Reflections` (a mirror slab and ball --
their material is in the level; compare each shot with the matching `-sky` one), `Sprite Normals` (lit Kenney sprites
under a raking lamp: made normal maps on the left, flat on the right; `sprite-normals.json`), `Terrain` (hills, a path and a
pond; opens clean, footing on the slopes, a scripted sculpt; `terrain.json`). Add a scene and a
script there whenever a rendering bug is fixed.
`train-yard-wars.json` checks a game mode instead: it runs with `-ExtraArgs "--play","Games/TrainYardWars/Content/Levels/Train Yard Wars.manalevel"`, joins two stand-in gamepads (`tyw_pad`), lets bots play, and logs the score.

Patterns that work:
- **Does a level open clean?** `open_level`, `wait`, `dirty_report`, then compare the two files -- the same means
  nothing about it changes just by being opened (a level that reads as modified on open is a bug).
- **Is the footing right?** In play, `teleport_player` above each platform, `wait`, `log_player`; a respawn back at
  the start or `grounded False` is a fall-through. Restart play (`stop_play`, `play`) before each moving platform --
  they keep moving along their path.
- **A picture of play:** `play`, `wait` ~120 frames, `screenshot`. Editor screenshots include the panels: crop to
  the viewport, whose rectangle depends on the layout (open the PNG once and read it off).
- Scripts worth keeping -- tours, highlight reels, demos -- live in `Content/Scripts/` (`Highlights/Showcase Levels.json`,
  `Highlights/Sample Course Tour.json` shoot DevPhotos with `@@PHOTO@@`); one-off scripts and every output belong in
  `Artifacts/` or the session scratchpad.

## Benchmarks

Big third-party scenes live in `Library/Benchmarks/` (gitignored), fetched by a script, never committed.
`python Tools/Benchmarks/bistro.py` downloads NVIDIA ORCA's Amazon Lumberyard Bistro (CC-BY 4.0, ~900 MB), converts its
FBX with Godot's FBX2glTF, rebuilds the materials from the pack's texture naming (glTF pointing at the original DDS; only
the BC5 normals are rewritten, green flipped in their blocks) and writes `Bistro Exterior`/`Bistro Interior.manalevel`
there, lit as Falcor lights it: the pack's HDR as the Photo sky (`sky.photo` in a level names the picture; the HDR is
turned to put its sun where the FBX's sun light comes from) and that light as a directional `Sun` node. The ORCA site's
renders (`Library/Benchmarks/Bistro/reference/`) are the 2017 Lumberyard ones, lit differently.
`Tools/Benchmarks/bistro-benchmark.ps1` builds Release and reports load time, GPU memory and fps with the profiler's
top scopes from pbrt-v4's three Bistro cameras and an overview (`-NoBlockCompression` for the RGBA comparison, `-Capped` for the editor's normal pacing).
`MANA_UNCAPPED=1` turns off vsync and the 90 fps limiter; `MANA_NO_BC=1` decodes DDS as a GPU without BC would.
The Bistro mesh node sets `mergeParts`: on load its 1,600 parts become one mesh per material (`ManaMeshBatch.MergeInPlace`;
there is no frustum culling, so nothing is lost) -- 52 fps to 340 at the vespa camera, every draw being CPU-bound.

DDS (`ManaDds`, `ManaCore/Imaging/`): BC1-5 and 32-bit RGBA, read with every mip. Where the device has
`TextureCompressionBC` the texture cache uploads the blocks as they are (flipped in place, as decoded pictures are);
elsewhere -- the browser, phones, thumbnails -- `ManaImageCodec.Load` decodes level 0. Normal maps may be two-channel
(BC5): `pbr_irradiance.wgsl` rebuilds z from x and y for every map.

## Testing the web build

GPU particle stress testing: `Tools/Automation/benchmark-gpu-particles.ps1` builds Release and sweeps the
`GPU Fountain` in `Content/Levels/GPU Particles.manalevel` from zero to 10 million. The `gpu_particles`
command takes `name` and `count`; `profile_dump` includes GPU emitter/particle counts. `GpuParticleEmitter`
is an additive, procedural WebGPU script with fixed lifetimes, sampled birth poses and world-space gravity;
see `Plans/GpuParticles.md`. `transform_node` takes a node `name` and optional `position`/`rotation` (Euler radians);
the moving-emitter check is `Tools/Automation/Checks/gpu-particles-world.json`.

Publish (`dotnet publish ManaVoxel.Web/ManaVoxel.Web.csproj -c Release`, AOT, a few minutes), serve it
(`python ManaVoxel.Web/serve.py 8123`), then drive headless Chrome with `node Tools/WebAssets/webdrive.mjs <out dir>
'<steps>'` -- `go`, `wait`, `waitFor` (console text), `shot`, `press`/`down`/`up` (key codes), `eval` (page
JavaScript). The page console is the game's log: `[BrowserPlay] Spawned` means a level is up, and the browser
logs the score (`[BrowserPlay] Coins x/y`) for scripts; the HUD itself is ManaUI, as on desktop. Things that only break there: the trimmed build
strips reflection metadata (types made by JSON need `DynamicDependency` and parameterless constructors), assets come
from the catalog rather than a checkout, and files can be locked by Defender mid-build (just build again).

`ManaVoxel.Web` is the combined browser-wasm/WebGPU host: its default page is the play selector, and `?mode=editor` starts the full editor. Publish once with `dotnet publish ManaVoxel.Web/ManaVoxel.Web.csproj -c Release -m:1`, serve with `python ManaVoxel.Web/serve.py 8123`, and use the same Chrome driver for both modes. A successful build alone is not enough: Chrome's WebGPU validation catches shader and pipeline issues that the desktop backend can miss.

A slow machine: the console's `[WebGPU] adapter:` line says which GPU the page got (a laptop with two should get the fast
one -- we ask for `high-performance` -- and "software fallback" means the CPU is drawing). The page's settings go lighter
for weaker cards: Resolution 1080p/720p (`?resolution=`, a pixel budget the browser scales up). Full by default, and
it lowers itself when frames stay slow (over 45 ms for 2 s: 75%, 55%, 40%; back up after 20 s of quick ones; logged as
`[Quality]`, shown on the page's GPU mark; `?resolution=full` keeps it full),
Graphics Phone (`?rendering=mobile`), Sky Simple (`?sky=simple`). `[Hitch]` lines come at most every 30 frames, so a
steady low frame rate shows as one line every few seconds.

Browser-specific lessons and limits:
- The WebGPU C shim (`ManaGraphics/Native/mana_webgpu.c`) targets the Emscripten SDK's bundled `webgpu.h`; Silk.NET.WebGPU enum values can differ from that header. Recheck mappings when either version changes, and remap local descriptor copies, never caller-owned memory, because descriptors may be reused.
- Adapter/device requests use the JS Promise and `[JSExport]` bridge in `ManaBrowserWebGpuCallbackBridge`; routing those callbacks through the legacy Emscripten WebGPU callback path fails in this .NET wasm setup. Await `ManaBrowserInterop.InitializeAsync()` before constructing browser graphics.
- WGSL fragment operations requiring uniform derivatives (`textureSample`, `fwidth`) must run before fragment-varying branches. Naga's uniformity analysis can also reject derivative-dependent code textually after a divergent branch, even when it is hoisted out of the branch body.
- RID-specific web hosts need referenced projects to declare `RuntimeIdentifiers=browser-wasm`, and their `ProjectReference`s need `SetTargetFramework` for that RID. Do not rely on `-r` propagation to select the browser target.
- Mesh import is glTF/GLB only through SharpGLTF on every platform. The Planet Showcase level is intentionally excluded from the browser selector; the rigged glTF skinning/animation path still lacks a validated browser fixture.

## Deploying ManaLidar to the phone

`ManaLidar` (iOS) builds only on the Mac, and `ManaLidar.Android` deploys from the Mac too. On Windows, check the iOS
app compiles first -- `dotnet build ManaLidar/ManaLidar.csproj -t:Compile` type-checks it against the real iOS
bindings without a Mac (the `ios` workload is installed here) -- then deploy:

```powershell
./Tools/ManaLidar/deploy-ios-from-windows.ps1        # or the VS Code task "Deploy: ManaLidar to iPhone (from Windows, via the Mac)"
```

It finds the Mac by itself (Bonjour `_sftp-ssh._tcp`, `Tools/ManaLidar/FindMac.cs` -- the first Mac with Remote Login
on; here, ManaPotion), pushes this PC's **committed** HEAD over SSH into a deploy copy on the Mac
(`~/.mana-deploy/ManaRenderer`, branch `incoming`, built detached) and runs `Tools/ManaLidar/deploy-ios.sh` there,
which builds, signs, installs and launches on the iPhone (plugged into the Mac or Wi-Fi paired). Commit first:
uncommitted changes are not sent. No GitHub is involved, and the Mac's own checkout (another session may be working
in it) is never touched. On the Mac itself: `Tools/ManaLidar/deploy-ios.sh [Debug|Release]`, and
`Tools/ManaLidar/deploy-android.sh` for the Android app.

- **One-time setup:** the Mac account name is kept in `%LOCALAPPDATA%\ManaRenderer\mac-user.txt` and this PC's SSH
  key is installed on the Mac. Signing uses a keychain of its own on the Mac (`~/.mana-deploy/signing/`, made by
  `setup-mac-signing.sh` on the first deploy, which asks the Mac's login password once to copy the signing identity
  out of the login keychain); its random password never leaves the Mac. `-ResetSigning` redoes it (a renewed
  certificate). `-App ManaVoxel.iOS` deploys the engine app instead of ManaLidar, `-Device` picks an iPhone or iPad,
  `-Console 60` prints a minute of the app's own output after launch.
- **Why each piece is there:** an SSH session finds the Mac's login keychain locked, which breaks both codesign
  (`errSecInternalComponent`) and Git's GitHub login (`-25308`) -- hence the signing keychain, and pushing straight
  to the Mac instead of pulling. Commands run in `zsh -lc` or `dotnet`/`git-lfs` are not on the PATH. A push into a checked-out
  branch is refused, hence `incoming` and the detached build.
- **iOS ahead-of-time code is not the desktop's:** the iPhone build got `Vector2` arithmetic wrong (V shifted, U
  right), tearing textures until `ScanAtlas`/`ScanTextureBaker`/measure snapping moved to plain floats. Code that
  runs on the phone avoids `Vector2` maths; `ScanAtlas.Verify` refuses a layout off the texture rather than saving it.
  It also reads a `Vector3`/`Vector2` array element with a 16-byte load, past the array's end: an array ending at an
  unmapped page crashes (`EXC_BAD_ACCESS` at a page-aligned address, now and then). Hot loops over vector arrays read
  them as floats (`MemoryMarshal.Cast<Vector3, float>`, as `ManaWebGpuMesh.BuildVertices` does). Symbolicate a phone
  crash with `atos -o <app>/ManaVoxel.iOS -l <base>` on the Mac (`devicectl device copy from --domain-type systemCrashLogs`).

## What assets there are

`Assets/` holds every binary asset: the model and picture packs (`*.zip`) and the engine's own (`Audio/`, `Fonts/`,
`Materials/`, `Models/`, `Planet/`, `Textures/`; `Mana.csproj` copies those into the output as `Assets/...`). Asset
references are `Assets/...` paths (an old `Mana/Assets/...` still resolves). `Assets/Catalog.md` lists every model the
project can place -- each pack in `Assets/*.zip` and the loose `Assets/Models` -- with its size in metres (and its origin when not centred on the ground) and its animation clips,
plus the picture packs and `Content/`'s own prefabs and sprite assets. Read it before laying out a level or a prefab
rather than opening packs. It is generated: `python Tools/Assets/catalog_assets.py` after adding or changing a pack
(the content checks fail on a pack it is missing); `--sheets` also draws each pack's previews into
`Library/AssetPreviews/<pack>.png`, to see what the pieces look like.

## Thumbnails

`EditorAssetServices` owns the browser library and thumbnail worker per editor world; the editor disposes them on close. Workspace panels and native pop-outs use that owner's services. `MeshThumbnailImages` keeps thumbnail coordinates and pixels per image atlas, shared only by roots using the same atlas.

The editor's Import and Project panels make a model's or asset's thumbnail the first time its tile is on screen and keep it in
`Library/Thumbnails/` (gitignored, one PNG per asset; the cache version in `MeshThumbnailCache` forces a redo). To fill it all at
once -- a fresh checkout, a new pack -- run `dotnet run --project ManaVoxel -- --precache-thumbnails` (headless, all cores, a few
seconds; repeat runs only make what is missing). These are the same CPU renders the web previews in `Assets/Generated/Thumbnails/`
are baked from; the live editor draws new ones with the GPU instead.

## Working conventions

- **DevPhotos:** when something visual works for the first time, save a screenshot of it to
  `DevPhotos/<yyyy-mm-dd> <what-it-is>.png` (e.g. `2026-09-30 2d-biome-castle.png`) -- an in-app capture, cropped
  to the viewport, of the feature at its best. It is the project's visual record; one per milestone, not per tweak.
- **Plans/Todo.md** is the user's list (finished items go to Plans/Todo-Finished.md). Mark items done (`[x]`) with a short note of what was built and where, and add any
  open follow-ups under the item, so the state of things can be read there between sessions.
- **The Sample Course is the source of truth:** patch `Content/Levels/Sample Course.manalevel` by hand or by script,
  never regenerate it -- it carries authored audio and tuning that a rebuild would lose. Other levels too: edit by
  node id, keep every id.
- **Other agents edit this repo at the same time.** Files can change under you: edit by anchor (the surrounding
  text), not by line number, and stop any running `ManaVoxel.exe` before building it. Commit only your own files
  (`git add <paths>`, never `git add -A`) -- the working tree can hold someone else's unfinished change.
- **Code comments:** one or two lines, saying why, not what; no essays.
- **Commit and push** working steps as they land, with a message saying what changed and why.
- **GitHub Actions:** do not manually trigger workflows unless the user explicitly asks. Let the daily deployment run to save compute.

## Architecture

- **Scene graph:** `ManaWorld` → `ManaScene[]` → `ManaSceneNode` tree → `ManaComponent[]`
- **Cameras:** one `CameraComponent` (`World.MainCamera`) flies in the editor; anything else that drives it is an
  `IManaCameraRig` attached with `AttachRig` (evaluated after the scene moves; an attached camera takes no fly input) and
  let go with `DetachRig(rig)`. The character's `ManaSpringArmComponent` is one (`IManaCameraLookRig`: it takes mouse look);
  a game mode that frames the shot itself uses `ManaComputedCameraRig` (Train Yard Wars, Yacht Jousting) or its own rig
  (`RunnerCamera`), and detaches it on `Suspend` (F8) -- never by borrowing `ShouldCapture*Input`.
- **Engine pieces for games** (use these before writing a game's own): `ManaModelLibrary`/`ManaModel` (models and
  prefabs, read once, placed as shared copies, "Owner" parts painted), `ManaShapeMesh` (many coloured shapes as one mesh),
  `ManaMeshBatch` (static scenery merged into one mesh per texture), `ManaStrategyCamera` (top-down view: zoom to the
  pointer, drag, quarter turns, bounds; its own rig), `ManaFixedStepClock` (fixed ticks, speed, pause, alpha),
  `ManaSoundBank` and `ManaMusicPlayer` (sounds by path, music crossfades).
- **Render loop:** `BaseGameMode` drives update + render. `ManaGameModeGraphicsBridge` finalizes material texture data for WebGPU. `ManaWorld.CreateRenderScene()` collects mesh instances each frame.
- **WebGPU frame:** `ManaWebGpuContext` owns the frame skeleton and shutdown order; `ManaWebGpuNativeDevice` the native
  handles and their (desktop or browser) bring-up, `ManaWebGpuWindowSurface` the swap chain
  and depth targets, `ManaWebGpuSceneRenderer` the scene half of the frame. Each piece of that is a
  `ManaWebGpuRenderPass` that owns its pipelines and overrides the hooks it draws in (`Prepare` before the scene pass,
  `DrawScene` inside it, `AfterScene` after it), registered in frame order with `AddPass` in `CreateScenePipelines`:
  environment capture, shadows, sky, materials, particles, the debug overlays, outline. A new effect is a new pass
  class, not more context code. Passes read the frame through `ManaWebGpuFrame`; shared layouts, samplers and the frame
  uniform are `ManaWebGpuSceneBindings`, resident meshes `ManaWebGpuSceneMeshes`, reflection cubes
  `ManaWebGpuEnvironment`; thumbnails, captures and the ManaUI compositor are services of their own. Meshes alike are
  instanced: the material pass draws opaque PBR meshes with the same geometry (`ManaMeshCpuData.GeometryHash`) and
  textures as one draw (`pbr_irradiance.wgsl` built with `MANA_INSTANCED` reads each one's object uniform from an
  array), and the shadow pass does the same for opaque casters (`shadow_depth_instanced.wgsl`); per-frame instance data
  goes through `ManaWebGpuInstanceBuffer`. `ManaWgslPreprocessor` takes `#if NAME` blocks for such variants. Every owner of GPU objects derives from
  `ManaWebGpuDeviceObject` (borrowed `Api`/`Device`/`Queue`, `Dispose`); handles their owner releases do not. Folders are
  namespaces: frame stages in `WebGPU/Pipelines/` (`.Pipelines`, overlays `.Pipelines.Overlays`), GPU data in `Resources/`
  (`.Resources`), things that draw or compute on request in `Renderers/` (`.Renderers`); browser interop is in
  `Graphics/Browser/`.
- **ManaUI is the only UI framework.** ImGui was removed entirely (package references, controllers, the `Draw*` hooks and every `*PanelView.cs`); a mode with `EnableManaUi` off now has no UI at all rather than an ImGui fallback. Retained-mode, two-pass WPF-style measure/arrange, lambda-based bindings, docking. Everything draws as one instanced SDF primitive, so a frame is a single draw call. The engine layer owns the tree (`Mana/Code/UI/ManaUi/ManaUiHost.cs`), laid out at the window size over the display scale (`UiRoot.ScreenScale`, from `ManaDpiScale`; `MANA_UI_SCALE=1.5` multiplies it to try a scaled layout -- pointer input is divided by it, and anything mapping UI positions to window pixels multiplies) and hands `ManaWebGpuContext.UiRoot` a finished draw list, which the backend submits in the tone-map pass. Views live under each project's `Code/UI/ManaUi/` folder. Game inventories and toolbars use the slot widgets
  (`ManaUI/Code/Controls/Slots/`): `ItemSlot` (bevelled well, icon, shortened count, hotkey, selected/unavailable/progress),
  `SlotGrid` (fixed columns, empty wells to `MinimumRows`), `Hotbar` (numbered rows), `PanelFrame` (titled frame with an
  inset well), `ItemIcon` (an atlas picture, or stock vector shapes until there is art) and `SlotStyle` (industrial
  palette or `FromTheme`); Foundry's HUD is the example.
- **Levels:** `.manalevel` JSON in `Content/Levels/` (an optional `sky` block -- `mode`, `dateTime`, `timeSpeed`, `constellationLines` -- sets the sky on open; see `Night Sky` and `Particle Garden`). A level holds only what somebody placed -- a node is saved when it carries a `ManaLevelSource` (`Mana/Code/Levels/`), which `ManaShapeFactory` and the mesh import paths set as they build. Demo scenery (grid planes, planet, borders) has none and is never captured, because it comes back from the code that generates it. Voxel grids go in as a palette plus one base64 blob. Capture/load live in `ManaVoxel/Code/Levels/`.
- **Grid maps:** `ManaGridMapNode` (`ManaVoxel/Code/GridMaps/`) is Godot's GridMap -- cells of (palette index, quarter turns) painted from a palette of mesh assets. Each palette asset loads once and its tiles share its meshes; tiles are editor-transient children, so a level saves the palette and one cell blob, never the tiles. Painting lives in `VoxelEditorGameMode.GridMap.cs`, switched on from the Grid Map tool window (`GridMapToolManaView`), which floats while a grid map is selected and exists only then (`GridMapEditing` holds the brush; its Blank brush clears). Each palette item has its own `GridMapItemSettings` -- cells covered, fit-or-fixed scale, turn, align (Model, as authored, by default). Maps have layers; the cell API works on `ActiveLayer`, and a hidden layer builds nothing. `ManaEditorGizmos.Visible` (the viewport bar's gizmo toggle) hides editor-only scene helpers. The editor room (256 units, centred on the origin, with the world axes drawn on its floor by `grid.wgsl`) shows only its floor; its walls, ceiling and coves come up while drawing voxels (`SyncGridRoomWalls`) or with `ManaEditorGizmos.ShowRoom` (the viewport bar's room toggle). Auto-tile sets (`GridMapAutoTile.cs`) are roads and the like: palette items tagged with the sides they join on, re-picked with their neighbours as cells change; `GridMapAutoTilePresets` holds the Kenney defaults, all editable in the window.
- **Terrain:** `ManaTerrainNode` (`Mana/Code/Nodes/`) is a heightfield centred on its node: `SamplesX` x `SamplesZ` heights
  `Spacing` apart, four paint layers (weights per sample, blended into the vertex colours; the material's albedo is white),
  drawn and collided as editor-transient patches of 32 cells, each with its own triangle-mesh body -- `Apply(dab)` rebuilds
  only the patches a brush touched. Saved as `ManaLevelTerrain` (deflated base64 heights and paint, `ManaLevelTerrainCodec`).
  The Terrain window (`TerrainToolManaView`) floats while one is selected; brush state is `TerrainEditing`, pointer input
  `TerrainInputController`, the ring `TerrainBrushOverlay`. Reference: `Content/Levels/Tests/Terrain.manalevel`.
- **Generator (WFC):** `GridMapWfc` is wave function collapse over grid map cells: tiles with per-cell socket labels on each side, multi-cell tiles cut into parts that only join each other. Rules are `.manawfc` JSON in `Content/Generators/` (`Dungeon.manawfc` is Kenney's modular dungeon kit), edited in the Generator window (`GridMapWfcManaView`, opened by Generate... in the Grid Map window) and run into an area of the selected map by `GridMapWfcGenerator`. Big rooms of any size are built from pieces (`GridMapWfcBigRooms`) and laid first. Props rules (`Kind: props`, e.g. `Dungeon Props.manawfc`, named by the dungeon rules' `Then`) furnish the result on a props layer, reading each cell's tag and walls off the map (`ReadDungeon`); `Pairs` lets labels join others, and a `>` label never joins itself.
- **Emoji:** ManaUI draws colour emoji from an `IManaColorGlyphSource` -- Noto's CBDT tables (`ManaColorBitmapTable`) on desktop; on the web the browser's own emoji font, drawn on a canvas (`ManaBrowserEmoji`, `drawEmoji` in `mana_interop.js`), so the 10 MB font is not downloaded. The web loading screen shows each one as it is drawn.
- **Themes and skins:** `UiTheme` tokens (Dark, Light, LCARS, Kenney); a theme may carry a `UiSkin` of named nine-slice pictures (`button`, `primary`, `panel`, `titleBar`, `track`, `fill`) that Button, UiWindow, the dock panels, Slider and ProgressBar draw instead of flat colour (`ManaUiDrawList.DrawNineSlice`). `UiRoot.RequestTheme` owns theme switching and initializes skin artwork before raising `ThemeChanged`; application views use that event to rebuild captured styles. LCARS artwork is built into ManaUI (`LcarsUiSkin`). The host registers `KenneyUiSkin.For` with `UiRoot.RegisterSkinFactory` to supply the external Space UI pack without an engine dependency in ManaUI. The viewport grid's checker contrast is `GridCheckerRatio`.
- **UI from JSON:** `UiMarkup` (`ManaUI/Code/Markup/`) builds a ManaUI tree from a `.manaui` document -- properties by name, `children`/`child`, `grid`/`dock`/`canvas` slots, `bind` (polled values) and `on` (events) against a `UiMarkupData`, colours as hex or `@ThemeToken`. Layouts live in `UI/`; Panels > UI Designer (`UiDesignerManaView`) edits them with an outline, a live preview (click to select) and a properties panel, all through the JSON.
- **Prefabs:** reusable authored trees in `Content/Prefabs/*.manaprefab` (browse them in the Project panel; see `Content/README.md` for the editor workflow). Model: `ManaVoxel/Code/Assets/ManaPrefab.cs`; loading/instances: `ManaLevelLoader` (`Prefab` kind) and `ManaPrefabRuntime`. Grid map presets are `*.managridpreset` beside them (`ManaGridMapPreset`). Edit Prefab is a prefab stage: the Outliner shows only the prefab (`ProjectAssetViewModel.EditingScenes`) under a Save / Save & Back bar. `PrefabOverrideMarks` marks each Inspector row an instance -- or the variant being edited -- changes from its source, with a revert arrow; a widget opts a row in with `PrefabOverrideMarks.Mark(context, row, node, component, "savedFieldName")`.

- **Viewport bar and navigation gizmo:** the bar over the viewport (`ViewportToolbarManaView.BuildBar`) is Blender's header -- Camera/Display/Focus menus, then snapping (magnet, and the steps and gizmo size dropped down from it) and the gizmo, room and wireframe toggles. The top-right gizmo (`NavigationGizmo` in `ViewportCameraGizmoManaView.cs`) is Blender's: axis balls (click one to look down it, again to turn round; drag to orbit, `CameraComponent.Orbit`) over round zoom, pan and projection buttons (`Dolly`, `Pan`).
- **Measuring:** the ruler on the toolbar (or M) turns on `MeasureTool` (`ManaVoxel/Code/Measure/`, held by `VoxelEditorViewModel.Measure`): Distance, Radius, Path and Ray (from a clicked spot to the next surface, out of the surface or straight up/down, through the host's `CastRay`), snapped by screen distance to the vertices, real edges (not a flat face's diagonal), edge midpoints and face of the triangle under the pointer (`ManaScenePicker.PickTriangle`), or to the ground grid. "Stay on the first surface" (or Shift) drops later points onto the first point's plane, to measure along a floor. Input in `VoxelEditorGameMode.Measure.cs`, drawing in `MeasureOverlay`, options in the Measure tool window (`MeasureToolManaView`).
- **Voxel tools:** the tools, their settings and the work plane float in the Voxel Tools window (`VoxelToolPaletteManaView.BuildWindow`) while a voxel node is selected or a voxel tool is on; the toolbar keeps one toggle for it. Selecting a voxel node makes it the one the tools draw into (`VoxelEditorViewModel.ActivateVoxelNode`). The tool windows come and go in `EditorWorkspace.SyncToolWindow`.
- **Play mode:** F5 flips the editor into `PlayModeController` -- editor UI collapsed by `ManaUiPlayOverlay`, the level's game modes started. A level with the `Character` game mode gets the character spawned at the `ManaPlayerStartNode`, possessed, with the follow camera; one without is played under a free debug camera (the world runs, nobody spawns). F8 (or the faint Inspect button top-right) ejects into the editor with the match frozen, to select and inspect its objects, and F8 or Resume goes back (`PlayModeController.IsEjected`; modes get `Suspend`/`Resume`; the editor's `IsPlaying` is false while ejected, the `eject` script command toggles it). Ctrl+F5 saves the level and launches `--play` in a second process, which runs `VoxelPlayGameMode`: ground, level, character, no editor.
- **Level game modes:** a level's rules, like Unreal's per-level GameMode -- `ManaLevelGameMode` (`Mana/Code/GameModes/`), not to be confused with `BaseGameMode`, which is the app around the world. A level lists them under `gameModes` (`{ type, properties }`; `ManaLevelGameModes` in `ManaVoxel/Code/Levels/` lists the engine's own and loads/saves each mode's settings -- its public read-write properties, `[Range]` for Inspector limits; a game in its own project under `Games/` is registered by `ManaGames` in ManaVoxel, which also lends it what only the app has, such as mesh import (`ManaModel.Loader`); a mode inside ManaVoxel can register itself with a `[ModuleInitializer]` calling `ManaLevelGameModes.Register`). The built-in `Character` mode (`ManaCharacterGameMode`, `maxJumps`) is what asks for the character; every bundled level with a Player Start has it (a content check enforces that). Every open level's modes run together on F5, Play In New Window and `--play`: `BeginPlay`/`Tick`/`EndPlay`, an optional HUD, script commands via `ManaPlaySession.Commands`, and flags -- `WantsPlayerCharacter` (off by default), `ShowsPlayHud`, `HandlesExitKeys` (Esc/R are the mode's), `KeepsEditorFloor`. A mode that builds its world in code can override `EditorPreview(context, visible)`: the editor calls it every frame while the level is open and not playing, so the level shows what play would build (in a scene of its own, never saved); Train Yard Wars previews its board for the level's players and seed. Select a level's scene to add, remove and set them in the Inspector (`GameModesWidget`). A mode builds what it needs in a scene of its own and removes it on `EndPlay`, so nothing of a match is ever saved into the level. Example: `TrainYardWarsMode` (`Content/Levels/Train Yard Wars*.manalevel`).
- **Sky:** `ManaIrradianceSettings.Sky` picks Day, Day and Night (`TimeOfDay`, `TimeSpeed` in sky seconds a second, or `RealTime`; the menu bar's sky clock, `SkyClockManaView`, sets them), Space (the LCARS theme's default) or Photo, from Appearance > Sky. `ManaSky.Evaluate` works out the sun and the daylight once a frame; `skybox.wgsl` draws from it (a scattering atmosphere after Sebastian Lague's MIT-licensed Solar System, notice in the file; the moon is NASA's CGI Moon Kit map, `Mana/Assets/Textures/Sky/moon_lroc_1k.jpg`, wrapped onto its disc as a sphere), and the fill lights and editor grid dim with the daylight, so placed lamps carry a night scene. Day and Night's stars are real: `include/constellations.wgsl` (written by `Tools/Sky/make_constellations.py` -- ~130 bright stars and the figures joining them) is turned by `ManaSky.CelestialFrame` with the date, hour and observer, with the figures shown by `ConstellationLines` (Scene Inspector > Night sky; the `sky` command's `visible`). The world frame is -Z south, +Z north, -X east (so facing the default view you look south, east on your left). On WebGPU the same shader is drawn into the reflection and irradiance cubes whenever the sky moves (`DrawSkyIntoEnvironmentIfNeeded` in `ManaWebGpuContext.Environment.cs`), so reflections show the sky on screen; the 4K photo is decoded into them only for Photo (and fetched from the asset catalog on the web). A scene with no directional light of its own is lit by the sky's sun, then the moon (`ManaSky.Light`), which casts the cascaded shadows (`ManaWebGpuContext.Shadows.cs`; the editor grid receives but never casts). Placed point lights (`ManaRenderScene.LocalLights`, up to 4096; lamps, torches, glows) are clustered: `ManaLightClusters` cuts the view into 16x9 tiles by 24 depth slices and lists, per cell, the lights whose range reaches it (a compute pass at the start of each frame, `light_clusters.wgsl`, one workgroup a cell, at most 256 lights a cell;
  `ManaLightClusters.Build` is the same binning on the CPU for the tests -- keep them alike); the PBR, grid and water shaders find their pixel's cell and shade only those (`include/point_lights.wgsl`, storage bindings 3-5 of the frame group). Stress scene: `Content/Levels/Tests/Many Lights.manalevel` (1,024 lamps), `Tools/Automation/Checks/many-lights.json`; the four `LightPositions` slots are the far fill lights only.
- **Seeing inside a frame:** Scene Inspector > PBR debug (`ManaIrradianceSettings.DebugModeNames`, numbered as
  `pbr_irradiance.wgsl` numbers them) swaps the shaded picture for one term -- normals, albedo, IBL, direct light, lights
  per cell, the cell grid, shadow cascades, depth. Panels > Debugging > Render Targets (`RenderTargetsManaView`) shows the
  frame's targets live: `ManaWebGpuTargetPreview` draws them small into one texture (`target_preview.wgsl`, layout in
  `ManaRenderTargetPreview`) only on frames whose UI draws from it -- a ManaUI batch can draw from a backend texture by id
  (`ManaUiDrawList.SetTexture`, registered with the UI renderer's `SetTexture`).
- **Wind:** `ManaIrradianceSettings.WindSpeed`/`WindDirection`/`Gustiness` (Scene Inspector > world settings > Wind); `ManaWind.Tick` gusts it once a frame and accumulates the drift, which reaches every shader as `frame.wind`/`frame.wind_motion`. Water swell, ripples and the darker gust patches follow it.
- **Sky clock:** `ManaIrradianceSettings.SkyDateTime` is the simulated local date/time; `ManaLunarPhase` computes mean lunar phase in UTC from it. The reusable `ManaDateTimePickerWidget` (ManaUI) takes optional phase providers and extra popup content; `SkyClockManaView` supplies the Solar/Sidereal selector (`86,164.0905` seconds per cycle); `TimeSpeed` advances the calendar as well as the sky phase and may be negative.
- **User-made assets:** everything a person sets up once and reuses is a JSON file in `Content/` with `format`, `version`, `id` and `name`, referenced by `{ assetId, assetPath }` (`ManaAssetRef`; the id wins when a file moves). A game keeps its own in `Games/<Name>/Content/` (a content mount, `ManaContentMounts`), referenced as
`@<Name>/...` (`@Foundry/Prefabs/Conveyor.manaprefab`); lookup by id searches every mount, the Project panel shows
each game's folder as a root of its own, the content checks load them, and the web and phone builds bundle them.
A game's levels live there too (`Games/<Name>/Content/Levels/`, pictures in `Levels/Pictures/`): `ManaLevelLibrary.Folders`
lists them after `Content/Levels`, the Scenes panel and level menus show them under Games/<Name>, and the web selector
and phone start screen offer them. `Content/` keeps only what is not one game's.
`ManaContentAssets` (`Mana/Code/Content/`) finds, reads (cached until the file changes) and saves them; a `[AssetKind(".ext")]` `ManaAssetRef` property gets an asset picker in the Inspector. Every kind is one entry in `ManaContentKinds` (`ManaVoxel/Code/Assets/`): the Project panel lists, filters, badges and offers New for them from it. Kinds today: prefabs (own instance/override code), sprite sheets, 9-slices, grid map presets, generator rules. Sheets and slices open in the Sprite Asset window (`SpriteAssetManaView`, state in `SpriteAssetEditing`; each change saves at once so the scene updates live), and drag into the scene like prefabs.
- **Sprites in the Import panel:** PNGs are a format there (`MeshAssetFormats.Png`) -- a pack's pictures when it has no models (not its previews, `Spritesheets/` or Kenney's `Double/` copies), loose ones in a `Sprites` folder. Dragging one in makes a Sprite; right-click a selection for Make Sprite Sheet (frames in order) or Make 9-Slice (pieces placed by their names, `SpriteAssetFactory`).
- **2D:** three sprite scripts on a shared base (`ManaSpriteBase`, Look: flip, tint, pixel art, unlit): `Sprite` (one picture, optional `repeatSize`), `SpriteSheet` (a `.manasheet` asset -- frames as separate pictures or a grid, pixels per unit, pivot, named animations -- played by `animation` name) and `Sprite9Slice` (a `.manaslice` asset of nine pieces, laid out across `size` in whole tiles), plus `PolygonCollider`. A `Bow` script (`BowComponent`, on the Player Start beside `Platformer2D`) arms the 2D player: hold F to draw, release to loose, W/Up for a high shot; arrows fall under gravity, defeat the first enemy they cross (`EnemyComponent.TryShoot`) and stick in solids. Bow and arrow pictures are drawn by `Tools/Sprites/make_bows.py` into `Content/Sprites/Items/Bows/` (Wooden, Golden, Crystal sheets in `Content/Sprites/Items/`). A sprite is a quad facing +Z drawn by a mesh renderer on its own node, lit (and normal-mapped) by the PBR pass. A `Platformer2D` script (on the Player Start) makes play side-on: the pawn is held to its plane (`ManaPawnNode.PlaneZ`), drawn by a sprite sheet instead of its model, with the boom fixed looking down -Z. Sprite pictures can live in a pack (`zip:kenney_new-platformer-pack-1.1.zip/Sprites/...`, copied out once by `ManaSpritePictures`). `unlit` shows flat art as drawn (the PBR shader inverts the tone map). A `[PicturePath]` string or string[] property gets the Inspector's picture picker (`PicturePicker`: Content, pack pictures, material textures). Sample: `Content/Levels/2D Sample.manalevel`, a five-biome run (grass, desert, snow, mushroom, stone castle) built from Kenney's New Platformer Pack. The pack's pieces are assets in `Content/Sprites/` -- `Characters/` (five aliens, every pose), `Enemies/` (18), `Items/` (coins, flags, torch, spring), `Terrain/` (block, platform and cloud slices for six grounds, water, lava) -- and the things placed again and again are 2D prefabs (`Coin 2D`, `Gem 2D`, `Spring 2D`, `Spikes 2D`, `Checkpoint Flag 2D`).
- **Web build:** `ManaVoxel.Web` opens on a level selector (`levels.json`, written by `Tools/WebAssets/publish_assets.py` with pictures from each levels folder's `Pictures/`) and plays the chosen level; Escape is a pause menu. A level is listed only when its `web` block says `publish` (scene Inspector > Publishing; `description` and `order` too). Level game modes run there (Games/ projects are referenced by ManaVoxel.Web, registered by the linked `ManaGames.cs`); a mode that builds in code lists its models, pictures and sounds in `ManaLevelGameMode.Assets` so they are fetched before play. The Pages deploy (`.github/workflows/build-web.yml`) puts the shared build in `app/v/<commit>/` and names it in `app/version.json` (`editor/` and `play/` redirect there with the startup mode), which `index.html` reads uncached -- file names carry no hash and Pages caches for 10 minutes, so deploying over the same paths mixed builds. Assets come from the web asset catalog on demand (`ManaAssetCatalog`; `ManaAssetPaths.Resolve` and sprite packs look there first). Model previews in the catalog are baked offline into `Assets/Generated/Thumbnails/` (left out of the published `Assets/`, attached to the catalog instead) (`python Tools/WebAssets/bake_thumbnails.py` after adding or changing models; it re-renders only what changed -- commit the result); a model without a current one is drawn on demand in the browser. Pictures are read and written through `ManaImageCodec` (`ManaCore/Imaging/`), never a decoder directly: StbImageSharp / StbImageWriteSharp on desktop, the browser's own decoder on the web (neither is in the web builds). `ManaLevelAssetPreloader` prepares the level's dependencies; `ManaSpriteAssetDependencies` reads declared sprite fields and typed sheet/slice assets, including defaults and ID-based references. That decoder is asynchronous, so the catalog's `EnsureAsync` decodes a reference's pictures ahead (a model's textures, a pack picture) and the synchronous load picks them up; anything loaded without an `EnsureAsync` first misses (logged as `[Images] ... loaded before it was decoded`). In the browser `SavePng` writes raw pixels (`ManaImageCodec.Size` reads either). Testing it: see "Testing the web build" below.
- **Shaders:** WGSL shaders at `ManaGraphics/Assets/Shaders/WebGpu/*.wgsl`, copied to output on build.
- **Physics:** BepuPhysics v2.

## Authoring prefabs (by hand or from code)

A prefab is plain JSON, so one can be authored without the editor -- write the file and run the suite: `ContentAssetChecks` loads and places every prefab in `Content/` and fails on any warning.

```json
{
  "format": "mana-prefab", "version": 1,
  "id": "<32 hex chars, unique per asset>",
  "name": "Coin",
  "root": {
    "id": "<32 hex>", "name": "Coin", "kind": "Group",
    "position": [0, 0, 0], "rotation": [0, 0, 0], "scale": [1, 1, 1],
    "scripts": [
      { "id": "<32 hex>", "type": "Trigger", "properties": { "size": [0.8, 0.8, 0.8], "offset": [0, 0.4, 0] } },
      { "id": "<32 hex>", "type": "Collectible", "properties": { "value": 1, "spinDegreesPerSecond": 120 } }
    ],
    "children": [
      { "id": "<32 hex>", "name": "Coin Model", "kind": "Mesh", "position": [0, 0.15, 0], "rotation": [0, 0, 0], "scale": [1, 1, 1],
        "asset": "zip:kenney_mini-dungeon.zip/Models/GLB format/coin.glb" }
    ]
  },
  "properties": [
    { "id": "<32 hex>", "name": "Value", "nodeId": "<root id>", "componentId": "<Collectible id>", "property": "value", "minimum": 1, "maximum": 100 }
  ]
}
```

- **Root and nodes** are `ManaLevelNode`s, exactly as in a `.manalevel` (camelCase JSON, enums as strings, rotation in radians). Kinds: `Group` (empty parent), `Shape` (`shape`: Cube/Sphere/Pyramid/Wedge/Disk/Torus/Icosahedron/Icosphere/Quad, `color`, `size`, and an optional `material` holding only what differs from the default -- `metallic`, `roughness`, `albedo`, `emission`, `emissionStrength`, `occlusion`, `normalStrength`, `opacity`, `opacityMode`, `alphaCutoff`, `uvScale`, `uvOffset`, `flatShaded`, `unlit`; no texture maps yet), `Mesh` (`asset`: a repo path like `Assets/Models/Kenny/candle.glb` or `zip:<pack>.zip/Models/GLB format/<model>.glb`), plus `Water`, `GridMap`, `Voxels`. One root; put everything else under it. No nested prefabs (`kind: Prefab` inside a prefab is rejected).
- **Every node and every script needs an `id`** -- 32 hex chars (`uuid4().hex`), unique within the prefab. Overrides and exposed properties key on these, never on names, so never reuse or regenerate them once instances exist.
- **Components** go in `scripts` by type name: `Trigger`, `Collectible`, `Hazard`, `Checkpoint`, `Goal`, `SpringPad`, `BoostPad`, `MovingPlatform`, `Enemy`, `AudioSource`, `TrainCar`, `Sprite` (`texture`/`normalMap` PNG or `zip:` pack paths, `pixelsPerUnit`, `pivot` [x, y], `repeatSize` [w, h] metres), `SpriteSheet` (`sheet` asset ref, `animation` name, `playing`, `frame`), `Sprite9Slice` (`slice` asset ref, `size` [w, h] metres, `pivot`) -- all three also take `flipX`, `tint`, `pixelArt`, `unlit` --, `PolygonCollider` (`points` as [x0, y0, x1, y1, ...] in the XY plane, `depth`; concave is fine; edited in the viewport while selected -- `VoxelEditorGameMode.Polygon.cs`), `Parallax` (`factor` [x, y]: share of the camera's travel a backdrop follows while playing), `Platformer2D` (`sheet` asset ref, `idleAnimation`/`runAnimation`/`jumpAnimation` names, `jumpHeight` (held jump while playing side-on), `cameraDistance`, `orthographic`, `viewHeight`), `Glow` (`color`, `strength` -- above 1 blooms, `lightRange`, `lightIntensity`, `lightOffset`, `pulseSpeed`; makes everything beneath glow and adds a small light -- see `Content/Prefabs/Glowing Coin.manaprefab`, a Coin variant that adds one), `ParticleEmitter` (`rate`, `burst`, `spreadDegrees`, `lifetime`, `speed`, `gravity`, `startSize`/`endSize`, `startColor`/`endColor`, `brightness`, `shape` Points|Streaks|Flames, `blendMode` Alpha|Additive, `useMidColor`/`midColor`/`midPosition`, `sizeBulge`, `colorSteps`, `drag`, `turbulence` -- see `Content/Levels/Particle Garden.manalevel`), `Light` (`color` `[r, g, b, a]`, `intensity`, `range` in metres, `lightType` Point, `flickerAmount`/`flickerSpeed`), `ColorCycle` (`speed` turns a second, `phase`, `spread` -- fans the lights beneath round the wheel, `saturation`, `brightness`, `pulseSpeed`/`pulseDepth`; runs every point `Light` and `Glow` on its node and below round the rainbow, except under a node with its own), `Motion` (`spinAxis`/`spinSpeed` deg/s, `swingAxis`/`swingAngle`/`swingSpeed`, `moveAxis`/`moveDistance`/`moveSpeed`, `phase` seconds; moves the node and everything beneath). Both animate only what is shown (a light's `Animate` colour, the node's `LocalTransform`), never a saved field, so a level stays unchanged while they play, in the editor too -- see `Content/Levels/Light Show.manalevel`. Their `properties` are the component's own serialized fields -- copy a working example from `Content/Levels/Sample Course.manalevel`.
- **Exposed properties** (`properties`) let each instance change one value: a component field is `nodeId` + `componentId` + the field name; a node field is `nodeId` + e.g. `position` (no `componentId`). `minimum`/`maximum` are optional.
- **Placing one in a level** is a node `{ "id": "<32 hex>", "name": "...", "kind": "Prefab", "position": [...], "prefab": { "assetId": "<prefab id>", "assetPath": "Prefabs/Coin.manaprefab", "overrides": [] } }` -- `assetPath` is relative to `Content/`; the id is what is trusted if the file moves. Levels are format version 2.
- **Per-instance changes** are `overrides`: `{ "path": [<nodeId>, "scripts", <scriptId>, "properties", "color"], "value": [1, 0.15, 0.1, 1] }` for a script field, `[<nodeId>, "position"]` for a node field. `Content/Levels/Prefab Lamps.manalevel` recolours one `Lamp` this way.
- **Variants** hold `base` instead of `root` -- `"base": { "assetId": "<Lamp id>", "assetPath": "Prefabs/Lamp.manaprefab", "overrides": [ ...only what changes... ] }` with `"properties": []` to show the base's -- and follow every later edit to their base. See `Content/Prefabs/Blue Lamp.manaprefab`; the Project panel's Create Variant makes one, and saving it writes only the diff. Variants of variants work (8 deep); a loop is refused. A variant can add a script: an override `{ "path": [<nodeId>, "scripts", <new script id>], "value": { "id", "type", "properties" }, "add": true }`.

## Code Style

- Always use curly braces `{}` on all control flow blocks — no single-line braceless if/for/foreach/while bodies.
- Avoid the ternary operator (`a ? b : c`): it is hard to read. Write an if/else (or a switch) instead.
- **Prefer prefabs to meshes built in code.** What a thing looks like -- a building, a prop, an enemy -- is a prefab in
  its content folder, editable in the editor; code places it and moves its parts by name. Foundry is the example: each
  building is `Games/Foundry/Content/Prefabs/<Name>.manaprefab`, its view calls `UsePrefab` (a `Body` group turns with
  the building's facing) and `Find("Pivot")` for the parts it animates, taking each part's authored place as its home.
  Every game reads models and prefabs through the engine's `ManaModelLibrary` (`Mana/Code/Models/`): each is read once
  (`ManaModel`, by the one loader the app registers, `ManaModel.Loader`) and placed as copies that keep its tree and share
  its meshes. A part named "Owner ..." is authored white and drawn in its player's colour (`ManaModelLibrary.Paint`, or a
  tint as it is batched -- Train Yard Wars' ground). Only what is drawn from live state stays
  in code: indicators recoloured or sized as the game runs (alerts, health bars, gauges' fill), wires between two points, items.
  Geometry that follows the board (ground, track, hexes, ore) is built with the engine's `ManaShapeMesh`
  (`Mana/Code/Shapes/`): many coloured boxes, rocks and hex pieces as one mesh, so a whole stretch is one draw.
- Nullable reference types enabled; avoid `!` suppression where possible.
- `AllowUnsafeBlocks` is on in ManaGraphics and Mana (needed for WebGPU raw pointer interop).
