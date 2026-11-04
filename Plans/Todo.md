[x] - A 2D UI node for HUDs and menus (2026-10-30): `UiCanvas` on level/prefab nodes hosts a `.manaui`
    layout over the viewport, bound to the mode's `HudViewModel` (MVVM, see below), with custom-widget slots.
    Inspector opens the layout in the UI Designer. Contour's cockpit, title, briefing/debrief and map overlay
    moved to Games/Contour/Content/UI/Contour HUD.manaui; dynamic menu contents, radar and health bars stay widgets.
    Weapon button edges now survive render frames between simulation ticks, including while firing.
    Bindings are MVVM now (`UiMarkupBinding`): INotifyPropertyChanged, OneWay/TwoWay/OneTime/OneWayToSource, commands,
    nested paths, `context`, typos reported. Contour's `ContourHudViewModel` is the example, checked in the web build too.
    [x] Lists (2026-11-04): `ItemsControl` + `itemTemplate` in markup; Contour's menu and mission list are now in the layout.
    Follow-ups: `UiImage` pictures
    in markup, canvas layouts rebuilding when the theme changes, and a way to see a canvas in the editor's viewport
    before pressing play (today it shows only in play and in the designer's preview).

[x] - Contour port (2026-10-30): the 2010 XNA space shooter (GhostTap/OldWork/.../XNA/Source/Contour) as Games/Contour,
    a level game mode. Its 20 FBX models converted to GLB (Games/Contour/Content/Models), its sounds and the Contour
    Method track to Audio; every ship, turret, station, rock and the planet is a prefab carrying a ContourUnit script
    (the old stats XML), the Brigantine a variant of the Sloop, the wormhole now GPU particles; the seven Ryser missions
    are .manalevel files of those prefabs plus ContourSpawn points, briefed from the design folder's mission briefs.
    Defend/Attack/Survival/Explore rules, waves, turrets, the four rounds (EMP reworked: 6 s cooldown), aggro AI,
    HUD/radar/debrief in ManaUI, pad support; ManaTests Contour suite. Games/Contour/README.md.
    [x] Linked up (2026-10-30): Contour.manalevel is the whole game -- title over an autopilot battle, menu, the Ryser
        system map (Ryser System.manalevel, ContourWaypoint bodies on their orbits) and the campaign mission to mission,
        each loaded behind a fade; progress saved (contour.txt). Health bars over ships you have hit.
    [x] Store (2026-11-04): hull, shields, engines and turret-rack upgrades bought with credits (ContourUpgrades).
    Follow-ups: the rest of the store's stock (weapons, skills, crew); the galaxy
    view above the system map (Menu_GalaxyView: Ryser and Sol); the other music mixes (WAV only, so they need a smaller
    format first); the unused models (mine, Phoenix, the Battlecruiser/Carrier concepts in the design folder);
    a web build check of the Games/*/Content/Models catalog path.

[x] - Editable Bistro imports (2026-10-26): keep the glTF hierarchy and Mesh Renderer components in the editor;
    `ManaStaticMeshBatcher` caches compatible geometry in the renderer. `meshParts` saves imported transforms,
    names, materials, components and deletions; `Tools/Automation/Checks/bistro-editable.json` checks the real scene.
    Follow-up: persist reparenting/duplication and authored nodes nested inside imported parts; improve lighting
    against the ORCA references. Static batching opts in through the mesh root's `mergeParts` flag.
    Checked-in Bistro variants now live in `Content/Levels/Downloaded/Bistro/`; their download declarations fetch/import
    source packs on desktop and their converted assets download through the web catalog. See that folder's README.

[] - Foundry factory builder: plan and roadmap in Plans/Foundry.md; game lives in Games/Foundry.
    [x] First playable (2026-10-20): finite mining, belts/backpressure, crafting, power, science goal,
        responsive ManaUI board, manual recovery/feeding, example layout, desktop save/load and automation check.
        Play Content/Levels/Foundry.manalevel; controls and recipes in Games/Foundry/README.md.
    [x] 3D and depth (2026-10-20): rebuilt in 3D on the Kenney factory kit with a class per building, smooth belts
        with curves and side-loading, splitters/sorting, day/night with solar, coal and batteries, assembler recipes,
        a tech tree with red/green science, guided goals and a Space Beacon finale. Games/Foundry/README.md.
    [x] Factorio concepts (2026-10-20): inserters (filter, fast, long), chests, splitters, mergers, undergrounds,
        pipette, copy/paste settings, undo, blueprints, area deconstruction, production stats, minimap, alerts,
        repeatable research, autosave, sounds; HUD rebuilt from ManaUI slot widgets; code split into classes.
    [x] Power poles and more (2026-10-21): pole and substation networks; fluids (pipes, pumps, boilers, steam
        engines, tanks); oil (pumpjacks, refineries, chemical plants, plastic); blue science and modules; logistic
        robots (roboports, provider/requester chests); trains (rails, stops, a two-car train, an outpost in the start
        factory); Alt view, fast-replace, lamps, inserter capacity, an electricity graph. Plans/Foundry.md.
    Next: two-lane belts, train schedules and signals, browser and macOS checks.

[x] - Code Screensaver: split terminal windows scroll random real repository source, with soft typing.
    Built in Content/Levels/Code Screensaver.manalevel; native-size pixel-snapped text, configurable speed/density/audio.
    Standalone Pages route: /code-screensaver/ (Tools/WebAssets/screensaver), with a right-side sound toggle.
    Terminals tile the viewport flush to its borders, with no cropped panes or positional drift.
    Green terminal borders separate the panes. Engine implementation lives in Games/CodeScreensaver.
    Each pane types its newest line character by character, then scrolls, with its own pace and cursor.
    Line fitting uses the actual rasterized glyph widths, so text fills each pane at any display scale.
    Full-width top status bar holds Pause/Stop/Sound; the HTML full-screen button is centered above the panes.
    The top bar also shows the current local time, updating each second even while sessions are paused.
    “Finance department” pane tracks simulated token usage/spend from typing, with a throughput graph,
    per-agent costs and rotating quips; clearly labeled fictional tariffs, no API calls.
    Cached C#/WGSL syntax colors distinguish keywords, types, methods, strings, numbers, comments and directives.
    Optional GitHub repository (2026-10-19): owner/name or URL in the bar (or the mode's Repository), code files only, any
    language; last + 10 recent kept (code-screensaver.json on desktop, localStorage + ?repo= on the web). GitHubSources.cs.

[] - Project code cleanup: Plans/CodeCleanup.md.
    [x] Test ownership (2026-10-18): moved 40 domain assertions from ManaUI to Mana/ManaVoxel suites; all 3,512 checks pass.
    [x] Renderer ownership audit (2026-10-18): Plans/RendererResourceOwnership.md maps resources, callbacks and shutdown across all 16 context partials.
    [x] Readback/thumbnail lifetime repair (2026-10-18): safe callback IDs, staging retirement and thumbnail rollback; 3,523 checks plus native Metal and browser rendering checks pass.
    [x] Environment lifetime repair (2026-10-18): CPU-only preparation, stale-generation rejection and render-thread GPU upload/activation; 3,538 checks plus native Metal, browser and iOS compile checks pass.
    [x] Local renderer ownership repair (2026-10-18): pending capture cancellation, height teardown, partial-construction rollback and browser initialization request IDs; 3,550 checks plus native Metal/browser/iOS compile checks pass.
    Next: specify binding-wrapper lifetime and extract reviewed renderer resource owners, reviewing remaining frame-local failure paths (CodeCleanup section 7).
    Cody (2026-10-16): asset and prefab tests now use explicit paths and per-world content roots; no tests change the working directory.
    Panel metadata and factories now live together in EditorPanelRegistry; saved panel IDs and default layout behavior are preserved.
    EditorWorkspaceLayout now owns layout capture/restoration, presets, upgrades and floating-window persistence.
    Renamed the production class to EditorWorkspace; panel requests, controllers, tool positions, location search and subscriptions now belong to each UI root.
    EditorAssetServices now owns each editor world's browser and thumbnail worker; thumbnail image state belongs to its atlas, and native pop-outs share the owner's services.
    ManaLevelLoader now delegates to ManaMeshAssetLoader, ManaAssetPaths and ManaLevelAssetPreloader; sprite dependencies are collected from declared fields and typed sheet/slice assets instead of regex over JSON.
    PlanetOceanController now owns the far shell and near-wave patch, including camera updates and synchronized bathymetry rebuilds; check: Tools/Automation/Checks/planet-ocean.json.
    PlanetTileStream, PlanetHeightSampler and PlanetRebuildScheduler now separate request sharing, GPU admission and background/completion scheduling; upres metadata/snapshots return through a budgeted main-thread handoff.
    PlanetPatchPlanner now owns geographic coverage, exact-zoom selection and recursive source-matched subdivision.
    Planets now retire on permanent level removal/reload and app shutdown, discarding late terrain results while active readbacks retain their slots; reparenting stays live.
    PlanetPatchGeometry now owns patch bounds, baseline overlap/source measurements and height application, with scene mutation and retirement checks retained in PlanetNode.
    TywGroundRenderer now owns Train Yard Wars ground chunks, territory ground tint, sea and coastal scenery; board-owned scene callbacks preserve replacement mesh lifetimes.
    TywTrainRenderer now owns animated train state and cargo indicators, preserving unchanged wagon instances and route history.
    Fixed the dark Train Yard Wars board: SetLighting now updates the astronomical date/time with the chosen hour, so the sun follows the game setting and the L-key lighting choices.
    TywInteractionOverlay now owns hover, per-seat route/ready/cursor feedback and territory/siege geometry; all three Train Yard Wars visual extractions are complete.
    Yacht Jousting now lives in Games/YachtJousting; ManaGames supplies model services at registration, preserving the serialized level type and settings. Its tests live under Code/YachtJousting.
    Follow-ups: other mutable test hooks and renderer resource ownership; register wasm-tools for SDK 10.0.402 (requires administrator privileges).

[] - Auto-Normal looks really bad.
    Claude (2026-10-07, not changed): looked at the Sprite Normals test scene (Tools/Automation/Checks/sprite-normals.json);
    it reads fine at night there, so I can't tell which sprite looks wrong. Tell me one (a level and a sprite) and I'll
    tune ManaSpriteNormals against it. Auto Normal Map is still off by default.


[] - Terrain support?  Would be nice to have rolling hills, or dirt paths, sculpting, etc. Can also be marching cube style voxel terrain to support caves and cliffs?
    Plan: Plans/Terrain.md. Claude (2026-10-07, not started -- a design choice for you): I'd build it in two layers. (1) A heightfield terrain node:
    a grid of heights drawn as one mesh, with sculpt brushes (raise, lower, smooth, flatten), a splat map for grass / dirt /
    path painted the same way, and a heightfield collider -- covers hills and paths cheaply, and the existing Grid Map and
    Voxel tools show how a floating tool window plus a brush works. (2) Caves and cliffs: a marching-cubes mesh over a density
    grid, reusing the voxel node's chunking, as a second node kind. Say if you want (1) first.
    Claude (2026-10-23): layer (1) is built. Add > Terrain makes a `ManaTerrainNode` (Mana/Code/Nodes): heights centred on the
    node, four paint layers (grass, dirt, rock, sand) blended in the vertex colours, drawn and collided as 32-cell patches
    so a brush rebuilds only what it touches. Selecting it brings up the Terrain window (Sculpt; Raise, Lower, Smooth,
    Flatten, Noise, Paint; size, strength, falloff; Ctrl swaps raise/lower, Shift smooths; one undo per stroke) with a
    ring on the ground under the pointer. Saved as deflated base64 blobs (`ManaLevelTerrain`). Reference level
    `Content/Levels/Tests/Terrain.manalevel` (hills, a dirt path, a pond), check `Tools/Automation/Checks/terrain.json`,
    tests in `ManaTests/Code/ManaVoxel/TerrainTests.cs`, DevPhotos/2026-10-23 terrain.png.
    Open: the path spline tool (flatten + paint along a draggable curve); layer (2), caves and cliffs.
    Triplanar colour on steep ground is done (2026-10-28): cliffs no longer smear down their faces.
    Claude (2026-10-28): the layers are textured -- grass, soil, granite and sand pictures (CC0 packs already in
    Assets/Materials) in one 2 x 2 atlas, tiled every 4 m and blended by paint weight sharpened by brightness, through
    a `MANA_TERRAIN` build of the PBR shader; Inspector > Level JSON > Layers turns it off. Every picture texture now
    has mips (ManaMipChain) and the sampler 8x anisotropy, which also stops distant textures shimmering everywhere.
    Checked in Chrome. DevPhotos/2026-10-28 terrain-ground-pictures.png. Tried on the web build (2026-10-28): terrain, footing and
    scatter all work in Chrome (84 fps on the Terrain level).
    Claude (2026-10-28): scatter is built. A `Scatter` script on a terrain strews models (picked in the Inspector's new
    model picker, `[ModelPath]`) where one paint layer is, at a density a square metre, off slopes past a limit, turned,
    sized and leaned from a seed; copies are editor-transient, regrown a moment after a sculpt or paint stroke. The
    Terrain test level now grows grass, a mixed wood and rocks from the survival kit. ManaScatterComponent,
    ModelPicker, ScatterTests.cs, DevPhotos/2026-10-28 terrain-scatter.png. Follow-ups: ~2,900 copies cost ~4 ms of
    CPU a frame walking and collecting nodes -- a scatter should hand the renderer its transforms directly (one
    instanced batch per model) instead of a node per copy; distance fade for grass; colliders for trees in play.

[] - Is there some kind of couch co-op 4 - 8 player game we could make using this stuff
    Plan: Plans/Couch-Coop.md. Claude (2026-10-07, ideas only): what exists is a side-on platformer pawn, bows and arrows, enemies, coins, checkpoints and
    moving platforms. A good fit is a "Coin Scramble" arena: 2-8 players on one screen, a fixed camera framing everyone, coins
    spawn, arrows stun, most coins when the clock ends wins; or a co-op "race the flood" run through the 2D Sample. What it
    needs first: more than one pawn (the world has a single GameState.Player), one gamepad per player (Silk.NET.Input
    handles several; nothing reads them yet apart from the flying mount), a camera that frames all players, and a join screen.
    Each game should be its own project to keep things seperate where posssible.  The editor prolly needs to depend on them all though to function?
    Claude (2026-10-08): yes -- Train Yard Wars is now its own project, `Games/TrainYardWars/` (engine only, no editor
    code). ManaVoxel references each game and registers its level game mode in `ManaVoxel/Code/Levels/ManaGames.cs`,
    lending it what only the app has (mesh import), so the editor depends on the games and never the other way round.
    Its tests are their own system: `dotnet run --project ManaTests -- TrainYardWars`. Yacht Jousting
    (`ManaVoxel/Code/Gameplay/YachtJousting/`) could move to `Games/` the same way.
    Codex (2026-10-10): Train Yard Wars performance and bot fixes are complete; review: Plans/TrainYardWarsReview.md.
    Codex (2026-10-11): Couch stockpiles now depend on owned yards: 10 of each good and 20 Steel per depot/station,
    with munitions capped at 24. Player cards show held/capacity; full yards keep cargo aboard until room is made.
    Couch controls now choose claim or menu from the hex, with mouse drag / hold-A movement for rows of claims;
    no Interact/Claim switching or card indicators. Check: Tools/Automation/Checks/train-yard-wars-context.json.
    Follow-ups: reduce overlapping world gauges in eight-player matches; improve bot spending when one scarce
    resource blocks expansion; human playtest recovery after losing territory and full-edition pacing.
    Rendering follow-up (2026-10-10): quitting immediately with recording readbacks pending can crash in
    ManaWebGpuBufferReadback.OnBufferMapped; cancellation frees callback userdata before native teardown callbacks.

[x] - Add support for GPU particles
    Codex (2026-10-08): GPU Particle Emitter on WebGPU -- additive procedural discs, shader-side ballistic motion/drag,
    up to 10 million per emitter, without per-particle CPU work. Test scene: Content/Levels/Particle Garden.manalevel (GPU Fountain).
    Run Tools/Automation/benchmark-gpu-particles.ps1 for a count sweep with a zero-particle baseline. Details: Plans/GpuParticles.md.
    Moving/rotating emitters now preserve each particle's birth pose, with world-space gravity; no per-particle CPU state.
    Optional bounce against two world-space infinite planes, with restitution and drag. Demo: the garden's GPU Bounce.
    2026-10-29: the GPU demos joined Particle Garden, with swirl/pull, turbulence, arms, bursts, streaks and colour
    variety -- tornado, galaxy, black hole, comet, aurora, fireworks, volcano, boats and a Rocket prefab (Plans/GpuParticles.md).


[x] - UI Editor is not working and not used, needs updating - low priority
    Claude (2026-10-30): rebuilt. It could not edit any real layout: it took a file's wrapper for the root element, so
    Contour's HUD showed one dead row. Now an outline (drag to rearrange), a preview at a chosen screen size (click,
    drag, handles; hidden elements show while selected), typed properties, bindings and commands picked from the view
    model, undo, Save As, and layouts as Project panel assets. DevPhotos/2026-10-30 ui-designer.png.
    2026-10-31: every property row has its own bind button, placement included. Still code-only: lists that need an item
    template (ItemsControl, TreeView, VirtualWrapPanel), so those lists are not offered for binding yet.

[x] Shadows seem to be slightly off, they don't touch the ground at the same spot that the actor does.
    Claude (2026-10-07): could not reproduce. In play on the Sample Course at a low sun the character's shadow meets its feet,
    and the Shadow Contact scene's cubes, pillar and disk all touch their shadows (the bias is already in metres, from an
    earlier fix). If you still see it, tell me the level and the time of day -- or does it show only at a distance, where
    the shadow cascades get coarser?

[] - Big step!!  Rename ManaVoxel, its no longer just a voxel editor
    Claude (2026-10-23, not started -- needs a name from you): the rename touches 4 projects (ManaVoxel, ManaVoxel.Web,
    ManaVoxel.Editor.Web, ManaVoxel.iOS), ~850 mentions in ~330 files (283 .cs, mostly `ManaVoxel.Code.*` namespaces),
    the sln, build-web.yml, .vscode launch/tasks, run-commands.ps1 and the other Tools scripts, the iOS deploy scripts
    and AGENTS.md. Plan: `git mv` the folders and csproj files, rewrite namespaces and paths in one scripted pass, build
    everything plus the web publish and an iOS compile, run the suite and a check script, one commit -- on a day no
    other agent has work in flight. Name ideas: ManaEditor (the app is the editor plus `--play`), ManaStudio, or just
    Mana (with the engine layer becoming Mana.Engine). Watch: the Pages URLs and the web host's file names are user-visible.


[x] - Lights:  I dont like the limits, games like minecraft seem to have unlimited light sources. Unreal has MegaLights, etc.  How can we increase the light amounts?  Forward+ rendering?  Fake lights? etc
    Claude (2026-10-22): clustered (Forward+) lighting. Up to 4096 placed lights (was 64): ManaLightClusters bins them
    into a 16x9x24 grid over the view each frame, and the PBR, grid and water shaders light each pixel with only the
    lights in its cell. Content/Levels/Tests/Many Lights.manalevel has 1,024 lamps (114 fps in Chrome here); old scenes
    look the same. Follow-ups: shadows from point lights; binning costs ~2 ms for 1,000 lights seen up close (fine
    for a normal level, worth a look before 4,000-light scenes).
    Claude (2026-10-23): the binning now runs on the GPU (light_clusters.wgsl, one workgroup a cell; the CPU's
    ManaLightClusters.Build is the same algorithm, kept for the tests): 0.04 ms of CPU a frame for Many Lights, was ~2 ms,
    picture unchanged. Debugging: Scene > PBR debug gains Light Count (lights per cell, over the tile grid), Light Cells,
    Shadow Cascades and View Depth (`debug_view` script command, Tools/Automation/Checks/debug-views.json), and
    Panels > Debugging > Render Targets shows the HDR scene, depth, the three shadow cascades and lights per tile live.
    Seen with them: the nearest shadow cascade covers only a few metres in front of the camera. Still open: shadows from
    point lights; no depth pre-pass (overdraw pays full lighting); a cell lists at most 256 lights.
    Claude (2026-10-23): Many Lights runs at 120 fps (was 60-70). It was CPU-bound on draw calls -- 1,024 lamps drawn once
    and three times more for the shadow cascades. Meshes alike now go out as one instanced draw, in the material pass and
    the shadow pass; shadow cascades are cut to the casters and cull what is outside them. Frame CPU 12.8 ms -> 4.7 ms.

[x] - Do we have cloth simulation in bepu? If so can we set it up to work with ManaWind on some of the cloths (tents, flags, etc)
    Claude (2026-10-28): BEPU has no cloth (only rigid bodies and joints, far too heavy for a flag), so cloth is its own
    script: `Cloth` (`ManaClothComponent`, Mana/Code/Components) -- a sheet of points and springs solved in world space,
    held along its left edge, top edge or top corners, pushed by ManaWind's speed, direction and gusts with a flutter,
    drawn as a mesh rebuilt each frame and lit from both sides (new per-object two-sided lighting). Moving the node drags
    it. Reference: Content/Levels/Tests/Cloth.manalevel (two flags, a banner, a sheet on a line), check
    Tools/Automation/Checks/cloth.json, tests ManaTests/Code/Mana/ClothTests.cs, DevPhotos/2026-10-28 cloth-in-the-wind.png.
    Follow-ups: tents and awnings (collision with a box or the ground, more pin shapes); ~0.2 ms a frame per cloth in
    Release, fine for a handful -- sleep cloths out of view if a level wants dozens; untried on the web build.
    Claude (2026-10-28): capes -- the Character game mode's `cape` (and `capeColor`) hangs one on the player
    (`ManaCape`, Mana/Code/Components): a Cloth on a node beside the pawn, held behind the neck by `HangFrom`, curled
    round the body and kept out of a capsule round it (`CollideWith`), fitted to the model's bounds; skipped side-on.
    Cloth gained `wrap`, `weight` and `inertia` (Unity's world-velocity scale: the pawn goes 0 to 7 m/s at once, which
    flings a fully world-space cape over its head), damping now settles toward the holder's motion rather than a
    standstill (it was a hidden air brake), and still air drags. Tests in ClothTests (setting off and a sharp turn keep
    the hem below the neck); `walk_player` script command; DevPhotos/2026-10-28 character-cape.png.
    Follow-ups: pin to the chest bone (no bone attach yet, so it hangs from the root and ignores the animation);
    legs can poke through when they swing back; turned on in no bundled level yet.
