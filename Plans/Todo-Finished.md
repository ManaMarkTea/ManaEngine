# Finished

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

[x] - Editable Bistro imports (2026-10-26): keep the glTF hierarchy and Mesh Renderer components in the editor;
    `ManaStaticMeshBatcher` caches compatible geometry in the renderer. `meshParts` saves imported transforms,
    names, materials, components and deletions; `Tools/Automation/Checks/bistro-editable.json` checks the real scene.
    Follow-up: persist reparenting/duplication and authored nodes nested inside imported parts; improve lighting
    against the ORCA references. Static batching opts in through the mesh root's `mergeParts` flag.
    Checked-in Bistro variants now live in `Content/Levels/Downloaded/Bistro/`; their download declarations fetch/import
    source packs on desktop and their converted assets download through the web catalog. See that folder's README.

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

[x] - Big step!!  Rename ManaEditor, its no longer just a voxel editor
    Done (2026-11-09): renamed to ManaEditor (voxel types keep their names); the repo is ManaEngine. Plan was: the rename touches 4 projects (ManaEditor, ManaEditor.Web,
    ManaEditor.Editor.Web, ManaEditor.iOS), ~850 mentions in ~330 files (283 .cs, mostly `ManaEditor.Code.*` namespaces),
    the sln, build-web.yml, .vscode launch/tasks, run-commands.ps1 and the other Tools scripts, the iOS deploy scripts
    and AGENTS.md. Plan: `git mv` the folders and csproj files, rewrite namespaces and paths in one scripted pass, build
    everything plus the web publish and an iOS compile, run the suite and a check script, one commit -- on a day no
    other agent has work in flight. Name ideas: ManaEditor (the app is the editor plus `--play`), ManaStudio, or just
    Mana (with the engine layer becoming Mana.Engine). Watch: the Pages URLs and the web host's file names are user-visible.

[x] - Project code cleanup: Plans/CodeCleanup.md.
    [x] Test ownership (2026-10-18): moved 40 domain assertions from ManaUI to Mana/ManaEditor suites; all 3,512 checks pass.
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

[x] - Terrain support?  Would be nice to have rolling hills, or dirt paths, sculpting, etc. Can also be marching cube style voxel terrain to support caves and cliffs?
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
    tests in `ManaTests/Code/ManaEditor/TerrainTests.cs`, DevPhotos/2026-10-23 terrain.png.
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

[x] - Repository tools audit and downloadable scene content (2026-10-29): removed the empty, untracked ManaSceneViewer
    directory and obsolete Night Sky/Runner initial-authoring generators. Maintained tools and reasons are listed in
    `Tools/README.md`. Five Bistro levels are checked in under `Content/Levels/Downloaded/Bistro/` without large assets;
    source-pack declarations survive capture/save and download/import on desktop. Web publishing stages referenced
    converted assets in the lazy catalog, using existing 512 px DDS mips for browser memory and Pages size limits.
    Headless checks, desktop clean-open/command sequencing, Release AOT publishing and Chrome exterior/evening renders
    pass. Added the missing browser SMAA layout-lookup shim; web capture: `DevPhotos/2026-10-29 bistro-downloadable-web.png`.

[x] - Add inset material parallax (2026-10-23): existing PBR materials expose Inset depth in metres, saved with levels and applied to every map in the WebGPU shader. Reference: Content/Levels/Tests/Parallax.manalevel and Tools/Automation/Checks/parallax.json. Flat recess effect; height-map relief remains a possible extension.

[x] - Add audio routing to Diagnostics (2026-10-16): renamed Graphics Diagnostics in the panel menu/tab and About button, preserving its saved dock id. A live Audio section shows the output device, availability, playback unlock and app master volume through IManaAudio. Desktop build, 1796 ManaUI checks and 61 Audio checks pass; in-app capture: DevPhotos/2026-10-16 diagnostics-audio.png.

[x] - Make pawn audio a node component and log desktop audio routing (2026-10-16): CharacterSpawnService attaches ManaPawnAudioComponent; it ticks with the node and subscribes/unsubscribes to jump/landing events through BeginPlay/EndPlay. Added audio_test/audio_status automation and startup logging of the actual OpenAL output endpoint and explicit --mute state. All 3460 headless checks and the browser build pass; a sound-enabled Sample Course run reports an available device, an active voice and no loading errors.

[x] - Review partial classes and extract separate responsibilities (2026-10-16): audited all 31 original families; extracted 21 classes for editor tools, bindings, audio, importing, previews, game policy/camera/batching and renderer helpers. Six owners no longer need partial declarations; generated, platform and cohesive coordination partials remain. Decisions: Plans/PartialClassReview.md. All 3455 headless checks pass; desktop editor/game/zoom automation and Release browser AOT publish plus Chrome play/editor checks pass.

[x] - Retire the OpenGL backend (2026-10-15): WebGPU is the only renderer. Removed the legacy backend, GLSL assets, shader compiler, immediate mesh/texture paths, Silk.NET.OpenGL packages and VS Code backend choices. Material uniform types now belong to ManaCore, with their saved numeric values preserved. Desktop solution and iOS compile passed; all 3455 headless checks passed. Release browser AOT publish and Chrome play/editor smoke checks passed without WebGPU errors, with editor/play captures in Artifacts/Automation and DevPhotos/2026-10-15 webgpu-only.png.

[x] - Remove the standalone ManaSceneViewer (2026-10-15): deleted its project and unused SceneViewerMode demo, removed VS Code build/run/debug entries and browser exclusion, and cleaned up the OpenGL window title. Rendering remains available through ManaEditor. Desktop solution build passed.

[x] - Orthographic viewport clipping and camera settings (2026-10-14): the editor-only free camera retreats along its view direction as the ortho volume grows, preserving the pivot and restoring the perspective range on switching back. Camera settings open from the sliders button under the navigation gizmo or Camera > Settings (projection, FOV/ortho height, clips, move/pan speeds). CameraComponent, ViewportCameraGizmoManaView/ViewportToolbarManaView. Added Orthographic Navigation reference level and automation; visual before/after verified, full 3469 checks passed plus final 1796 ManaUI checks.

[x] - Detached panel resize/presentation mitigations (2026-10-14): present once per normal tick instead of twice in resize callbacks, choose supported Immediate/Mailbox presentation with FIFO fallback, retain a valid surface on acquisition timeout, and profile panel Acquire/Present independently. ManaUiDesktopWindows/ManaWebGpuContext.UiSurface; desktop_panel automation and Tools/Automation/Checks/desktop-panel-resize.json. Repeated shrink/expand check held 119.9 FPS, 11.2 ms worst frame, no settled hitches; all 1795 ManaUI checks passed.
    Follow-up: exact user-reported persistent 3 FPS/black-area failure was not reproduced; these mitigations need confirmation on that intermittent case.

[x] - Preserve macOS red close circles after docking (2026-10-14): DockTabGroup draws persistent left-side red circles for every closeable macOS tab, with a cross on hover. Docking close targets unchanged; in-app screenshot verified and all 1795 ManaUI checks passed.

[x] - macOS chrome and control styling (2026-10-14): neutral silver surfaces and gradient buttons; full-width segmented dock tabs with left close targets; centered floating-window titles and left traffic lights (inactive grey, hover symbols). Red closes, yellow collapses/restores the title bar, green zooms/restores; unavailable actions grey out. ManaUI UiWindow/DockTabGroup/UiTheme; focused pointer and restore tests, all 1795 ManaUI checks passed; Tools/Automation/Checks/macos-chrome.json and DevPhotos captures.

[x] - Windows 11 and macOS editor themes (2026-10-14): added to Appearance > Theme, with distinct neutral palettes, corner/scrollbar metrics, blue accents and drawn group chevrons (ManaUI UiTheme/Expander). In-app WebGPU captures verified; all 1789 ManaUI checks passed. Repeatable check: Tools/Automation/Checks/modern-themes.json.

[x] - Theme-specific group indicators (2026-10-14): Windows 3.1 uses a framed system-menu dash; LCARS uses an amber bar, horizontal expanded and vertical collapsed (ManaUI Expander). In-app captures verified; all 1789 ManaUI checks passed.

[x] - Windows 3.1 editor theme (2026-10-14): Appearance > Theme > Windows 3.1; built-in ManaUI palette and atlas skin with square grey bevels, recessed inputs, navy titles/tabs and grey scrollbar thumbs. Automated WebGPU screenshot verified; all 1789 ManaUI checks passed.

[x] - Train Yard Wars cargo transfer feedback and dock stops (2026-10-11): a moving yellow arrow points into
    the loading wagon; a teal arrow points from the unloading wagon toward the dock. Yards have a marked east-side
    cargo platform. Trains stop with each loaded wagon beside that platform, transfer one wagon, then pull forward
    one wagon length for the next; towns and ports also transfer wagons individually. Split town deliveries award
    prestige once, keeping unrelated cargo aboard. Rules: TywTrain/TywTrain.Cargo; visuals: TywBoardView.
    All 271 game checks and 137 focused checks passed. In-app WebGPU verification:
    Tools/Automation/Checks/train-yard-wars-cargo-transfer.json; DevPhotos/2026-10-11 train-yard-wars-cargo-transfer.png.

[x] - Train Yard Wars turnaround ownership preference (2026-10-11): automatic loops first search for a short
    route along owned ground or a ring around an owned hex, before falling back outside claimed land. Existing
    unused loops move onto newly suitable owned land; captured enemy loop rails cannot be reused as one's own.
    Rules: TywGame.Claim.cs; ownership, fallback and relocation regressions: MatchReviewTests.cs.
    All 258 game checks and 125 final focused checks passed; no flicker tests run.

[x] - Train Yard Wars mixed wagon cargo and side loading (2026-10-11): every wagon carries its own resource
    and quantity; dispatch fills spare space before delivering. Pickup routes position the loading wagon along
    the producer's edge when track allows, retaining a fallback for short dead-end sidings. Towns consume only
    the requested resource, depots bank every load, and ports trade every type. Loading respects other trains'
    reservations; unchanged wagon render instances survive load changes. Rules: TywTrain.Cargo.cs/TywTrain.cs;
    visuals: TywBoardView.cs. Full game suite: 252 checks passed; final focused coverage: 122 checks passed.
    Eight-player WebGPU capture: train-yard-wars-flicker.json, 151 consecutive frames.

[x] - Train Yard Wars enemy tile menu crash (2026-10-11): attack menus use HexOwner instead of SabotageVictim,
    which returns -1 for claimed territory without rival buildings or rails. Full-edition menus omit couch
    attacks. Regression coverage: MatchReviewTests.cs; fix committed as d3829a4.

[x] - Train Yard Wars preview pivots and transient gizmos (2026-10-11): generated batches use local vertices
    with pivots near their bounds; each depot has its own batch/pivot. Rebuilds preserve scene identities and update
    both editor Position and render transforms. Removed the editor's blanket gizmo ban on transient selections;
    previews retain their transient status and stay out of level saves. All 111 targeted geometry/match/serialization
    checks passed. In-app check: train-yard-wars-preview-pivots.json; DevPhotos/2026-10-11 train-yard-wars-preview-pivot.png.

[x] - Train Yard Wars ground rebuild flicker (2026-10-10): replaced four-frame overlapping terrain with stable
    scene instances per texture group, retaining the backend's old-buffer fallback under heavy upload queues.
    Three regressions plus consecutive-frame WebGPU stress capture: Tools/Automation/Checks/train-yard-wars-flicker.json.

[x] - Train Yard Wars terrain preservation (2026-10-10): edge rails no longer flatten mountain, hill or forest
    interiors. Mountain mines keep a full-height peak with a small working area against its front slope.
    In-app WebGPU check: Tools/Automation/Checks/train-yard-wars-mountains.json, standalone Full level;
    screenshot: DevPhotos/2026-10-10 train-yard-wars-mountain-mine.png. All 21 view/match regressions passed.

[x] - Train Yard Wars bot and late-match review (2026-10-10): fixed enemy attacks, border towers,
    defence priority, affordable expansion, interrupted cargo reservations, captured mine colours and ghost trains.
    Cached HUD icons: eight-player twelve-minute match 36 -> 75 FPS, UI layout 11.93 -> 0.29 ms locally.
    Fourteen regressions and repeatable bot-match automation; findings and remaining tuning: Plans/TrainYardWarsReview.md.

[x] - Train Yard Wars late-match performance (2026-10-10): settled rails batch by 8x8 map region,
    retaining construction animations and stable render instances during GPU uploads. Seed 42, four-player
    couch match after eight simulated minutes: 74 -> 106 FPS, 761 -> 529 rendered meshes on this machine.
    Code: Games/TrainYardWars/Code/View/TywBoardView.TrackBatches.cs. Seven headless rendering checks;
    repeatable comparison: Tools/Automation/Checks/train-yard-wars-perf.json (tyw_batch_tracks toggles batching).

[x] - Filled the seven remaining level previews (2026-10-10), including Planet Showcase,
    Train Yard Wars Couch and all five rendering test scenes. All 21 bundled levels have
    thumbnails; update-scene-thumbnails.py --missing captures absent previews in their level's Pictures folder.

[x] - Refreshed the 14 scene thumbnails with WebGPU FXAA (2026-10-10), with repeatable captures in
    Tools/Automation/update-scene-thumbnails.py. Consolidated material demos into PBR Gallery:
    removed Material Library, Material Packs and Flight Helmet levels; the gallery keeps all models
    and 21 material presets, with oak flooring, warm plinths and no backdrop wall.
    Showcase tours now shoot the helmet in the gallery.

[x] - WebGPU scene anti-aliasing (2026-10-10): FXAA filters the tone-mapped scene in
    ManaGraphics/Assets/Shaders/WebGpu/hdr_tonemap.wgsl; UI and selection outlines draw afterward.
    Check: Content/Levels/Tests/Anti Aliasing.manalevel; Tools/Automation/Checks/anti-aliasing.json.
    Follow-up: temporal anti-aliasing for motion shimmer and subpixel detail.

[x] - Compact zipped PBR material picker (2026-10-10): Material > Preset picks a ZIP with previews and search,
    assigning its maps together; maps and imported-mesh overrides survive level and prefab saves.
    Added 512px CC0 wood parquet, black brick and concrete packs (1.7 MiB total) under Mana/Assets/Materials/,
    with source/license notes and Tools/Materials/compact.py for resizing more packs. Removed UI icons,
    model images and demonstration art from the surface texture picker. Rendering fixture:
    Content/Levels/PBR Gallery.manalevel; Tools/Automation/Checks/pbr-material-library.json.
    Follow-up: displacement/height and specular workflows are not supported by the material pack loader.


[x] - Yacht Jousting from the supplied game images (2026-10-08): playable first-to-five boat duel against a bot,
    charged lances, directional shields, guard, lane steering, pause/rematch, and ManaUI score/health HUD.
    Uses Kenney ships, captains, rocks, islands, palms, docks and buoys plus the existing lighthouse.
    Level: Content/Levels/Yacht Jousting.manalevel; implementation: ManaEditor/Code/Gameplay/YachtJousting/;
    details and remaining polish: Plans/YachtJousting.md.

[x] - Version number in the app (bottom left, and in the About box); View > About did nothing; an emptied side column
    could only be got back by resetting the panels.
    Claude: the version is <Version> in Directory.Build.props (0.1.0; bump it for a release) plus the git commit, read by
    ManaCore's ManaVersion. View > About opens an About box (AboutBoxManaView) with build details and credits -- fonts,
    the emoji font in use, Kenney's packs, the sky photos, the libraries; sources not on record (the lighthouse, the zoo
    animals, the course audio) are left off until we know them. The old diagnostics panel is now Graphics Diagnostics.
    An emptied column keeps a 40 px band along its window edge to drop a panel into (DockHost.EmptyDropZone).

[x] - Web Level play:  ManaUi is not showing up at all. the player also moves much slower than the regular player.
    Claude: the browser play mode had ManaUI switched off; it now shows desktop play's HUD, results and death screens
    (Back to levels returns to the selector). The slowness was the character: it spawned the first one found at the
    default 1 m height, and speeds and jump scale with height; it now spawns the Player Start's character, as desktop.
    Also: Pages deploys go to <app>/v/<build>/, named by version.json, so a cached old file can no longer break a new
    deploy (the editor's 'Failed to fetch ManaAudio.wasm'). The first deploy after this can still trip a browser holding
    the old index.html for up to 10 minutes; a reload after that fixes it for good.

[x] - Can the moon match up to the real phase of the moon (New moon, half moon, full moon)
    `ManaLunarPhase` calculates the phase and illumination; the simulated date/time now drives the moon, its
    light, and reflections.

[x] - Add date/time picking and sidereal day steps to the sky clock.
    The clock accepts a local date/time and can use either a solar day or an 86,164-second sidereal day; negative
    speeds advance the calendar backwards too.

[x] - Run the day-night cycle backwards.
    Sky Clock speed presets include reverse rates, and clouds drift backwards with the cycle.

[x] - It can be hard to tell when something is html, or mana ui.  Is there a way to mark html as such
    Browser loading screens, level selector, pause menu and Escape hint now carry an HTML badge.
    Labels live inside each HTML surface and disappear with it; checked the page layouts in headless Chrome.

[x] - The search location panel is gone, i think it moved somewhere but i can't find it anymore. where is it?
    Location search is a dialog under View > Search Location... (formerly Search Area...), renamed for discoverability.

[x] - There is a blue ball and yellow donut in every scene for some reason - i forgot why, remove them.
    Removed the startup cursor sphere and demo pickup donut from VoxelEditorGameMode; voxel hover feedback remains.

[x] - Finish the Little Pond Level - Add tents and picnic stuff
    Added Kenney Mini Forest tents and woodland, a fire circle, picnic table, blanket and supplies.
    Reusable camping prefabs and refreshed level preview; verified loading and player grounding.

[x] - JSON files seem to be adding lots of new lines again, i thought it was fixed.
    Sprite assets were bypassing ManaJsonFormat. Their shared save path now uses it too, keeping short arrays
    like pivots on one line. Checked value preservation and stable load/save formatting; existing files update when saved.

[x] - Perf: Split up the world perf ManaUI by panel to help know which panel is slow
    World Perf now separates panel CPU timings into Bindings, Measure, Arrange and Record, including floated panels.
    Scope names sit above aligned Peak/Now/Avg milliseconds and peak allocation columns; frame totals stay separate.
    Timings also appear in profile_dump. Verified in-app (DevPhotos/2026-10-01 panel-timings.png); all 2,920 checks pass.

Claude, overnight summary (2026-09-29): every item below is done and pushed; the 2D work went furthest. Open
the 2D Sample and press F5 -- a side-on pixel-art platformer: A/D run, Space (double) jumps, three glowing coins on
the long ledge, parallax mountains and hills, orthographic view. Photos for each step are in DevPhotos/.
One question for you (in the Glow item): how material edits should be saved.
2026-09-30 (night): the web build no longer downloads the 10 MB emoji font or the 5 MB sky photo (play startup 57 -> 41 MB).
Emoji come from the browser's own font, drawn on a canvas (ManaBrowserEmoji); the web loading screen pops each one in
as it is drawn (DevPhotos/2026-09-30 web-loading-emoji.png). On WebGPU the shader sky is drawn into the reflection
cubes, so reflections match the sky on screen (sunsets, night, space); the photo is decoded only for the Photo sky,
fetched from the catalog on the web. Follow-up: Space scenes are darker now (a dark sky lights them less) -- say if
you want an ambient floor there.
2026-09-30 (evening): the whole pack built into assets (5 characters, 18 enemies, items, terrain slices for six grounds)
and the 2D Sample grown into a five-biome run: grass -> desert (cacti, a spike pit crossed on a moving platform, snails)
-> snow (clouds over a gap, a spring up to a coin cache) -> mushroom (bees, flies, ladybugs) -> stone castle (torches,
lava, a saw, the exit door). 28 coins and 4 gems, checkpoints between biomes. DevPhotos/2026-09-30 2d-biome-*.png.
2026-09-30 (later): sprites reworked into three components -- Sprite, Sprite Sheet, Sprite 9-Slice -- on reusable
assets (.manasheet, .manaslice in Content/Sprites) set up once in the Sprite Asset window; every user-made asset kind
now comes from one registry (ManaContentKinds) the Project panel lists, with New. The Import panel lists 2D pictures
(PNG format) to drag in, or turn a selection into a sheet or a 9-slice. DevPhotos/2026-09-30 sprite-sheet-editor.png,
nine-slice-editor.png.
2026-09-30: the 2D Sample now uses Kenney's New Platformer Pack (Assets/kenney_new-platformer-pack-1.1.zip): the green
character, grass terrain, gold coins (Content/Prefabs/Coin 2D), water, a slime, a goal flag and the painted backdrop.
Sprites read straight from the zip; the pack's .ogg sounds need Ogg support in ManaAudio first, so the sample uses
the existing .wav jump/pickup/stomp. DevPhotos/2026-09-30 2d-kenney-platformer-pack.png. Tests: 2,841 checks, all passing.

[x] Press the END key to drop the item onto the floor or other objects. - Writen by copilot, verify its good.
    Claude (overnight): verified and improved. It now casts five rays (centre + footprint corners) and lands on the
    highest hit, as Unreal's End does, so a box half over a ledge rests on the ledge; rays start just above the
    object's bottom so something sunk a hair into the floor rises out. Tested: a cube overhanging a 6 m pillar
    lands on top of it (6.5). `drop_to_floor` command added for scripted checks.

[x] - Moved various data out of Root/ and into /Content/. Anything user authored should go there.  Done by copilot make sure everything still works.
    Claude (overnight): verified. All 8 levels in Content/Levels open with 0 warnings; tests pass (2,799).
    Found and fixed along the way: every level read as "Save *" (changed) the moment it opened. Colliders set to
    "fit the model" were captured by guessing, and the guess flipped once the model finished loading (or the
    moving platform's ghost joined its bounds). Colliders now remember whether they follow the model, refit when
    the model grows, and ignore editor-only children; all 8 levels now open clean. See ColliderIntentChecks.
    Still to do: the planet code had machine-specific GeoData paths -- fixed in the VoxelEditorGameMode split below.

[x] Add Support for 2d gameplay (Sprites, platforms, sprite sheet animations, sprite lighting and normals, polygon colliders, etc)
    Claude (overnight), milestone 1 done:
    - Sprite script: a PNG on a quad facing +Z, sized by pixels-per-unit, pivot, sheet frames (columns x rows),
      flipX, tint, alpha cutout; optional normal map, so lamps/glows shade it with depth (PBR pass).
    - SpriteAnimation script: fps, frame range, loop -- plays in the editor too.
    - PolygonCollider script: any outline (concave OK -- ear-clipped, extruded to a solid), rebuilt when edited.
    - Sample: Content/Levels/2D Sample.manalevel (platforms, walking hero, a torch Glow); art in Content/Sprites
      (drawn procedurally, with normal maps). Photo: DevPhotos/<date> 2d-sprites-torchlit.png. Tests: SpriteChecks.
    - Engine fix on the way: a component can now add another to its node while it ticks (the update loop walks
      components by index), and the level format reads Vector2 ([x, y]).
    Milestone 2 done: a playable 2D character. The Platformer2D script (on the 2D Sample's Player Start) makes
    F5 / Ctrl+F5 play side-on: A/D run, Space jumps (double jump too), W/S do nothing; the character is held to
    its plane, drawn by the hero sheet (idle / run / jump frames, flipped by direction) instead of its 3D model,
    and the camera sits side-on following it. Stop puts everything back. Photo: DevPhotos/2026-09-29
    2d-player-side-on.png. Tests: Platformer2DChecks. (Add Component names with numbers now read "Platformer 2D".)
    Also done: sprites are pixel art by default (Sprite > Pixel Art: nearest sampling, crisp square pixels; off for
    painted art) -- DevPhotos/2026-09-29 2d-pixel-art-crisp.png. And an animating sprite no longer marks its level
    changed (the playing frame is kept apart from the saved one); the 2D Sample opens clean.
    Tiling done: Sprite > Tiled Size (metres) repeats the picture across it instead of stretching -- the 2D Sample
    gained a Long Ledge (8 m of brick from the 4 m tile) with three Glowing Coins to collect on it.
    Photo: DevPhotos/2026-09-29 2d-tiled-ledge-coins.png.
    Orthographic done: Platformer2D > Orthographic (on by default) plays with a flat view, View Height metres tall,
    looking level; stopping puts the editor's perspective back. The sky keeps its gradient and clouds under an
    orthographic camera now (it used to go one flat colour). Photo: DevPhotos/2026-09-29 2d-orthographic-play.png.
    Parallax done: a Parallax script (factor x/y: 0 moves with the level, 1 stays with the camera) on backdrop
    sprites; the 2D Sample has far mountains and nearer hills (Content/Sprites/mountains.png, hills.png) sliding
    at 0.8 and 0.5. Photo: DevPhotos/2026-09-29 2d-parallax-backdrops.png. Tests: ParallaxChecks.
    Polygon editing done: select a node with a PolygonCollider and its points get handles in the viewport -- drag
    one to move it (the editor's move snap applies when on), Ctrl+click an edge to add a point there, Alt+click a
    point to remove it; each is one undo step. Photo: DevPhotos/2026-09-29 2d-polygon-point-handles.png.
    Tests: PolygonEditChecks. NOTE: I could only check the handles draw -- automation has no mouse drag -- so
    please give dragging a try.
    Still open for 2D: the sprite and its collider are separate (moving points does not reshape the picture), and
    OpenGL does not draw sprite textures (it never reads mesh texture paths).

[x] Road generator - There is a city pack with road pieces, it would be nice to be able to drag out a road and have it auto-generate the correct nodes under it dynamically.
    Claude: done earlier -- Grid Map window, Road preset (auto-tile): drag to paint a road and the right straight /
    corner / T / crossing pieces are picked from the neighbours.

[x] Similar to our grid map floating tool, lets move all the voxel arrows and stuff into a tool panel that shows up when selecting a voxel node.
    Claude: verified -- selecting Captain Voxels in Voxel Showcase brings up the Voxel Tools window (and the room walls).

[x] Wave function collapse to populate a dungeon from the kenney dungeon assets, but have it be configurable with some JSON assets and a Window Editor for it.

[x] Add support for UI from JSON or xaml-ish.  And an editor for it. (Like UMG in unreal)
    Claude: done earlier as JSON -- UiMarkup (.manaui files in UI/) and Panels > UI Designer. No XAML flavour yet.

[x] Glow component (next up): levels and prefabs don't save material settings, so emission set in the Inspector is lost on save. Add a "Glow" script (colour, strength, optional light range) that sets emission on the meshes beneath it and adds a small point light, so a "Glowing Coin" is a Coin variant with a Glow added. Strength above 1 blooms.
    Claude (overnight): done. "Glow" script (Add component > Glow): colour, strength (above 1 blooms), light range,
    light intensity, light offset, pulse speed. It makes everything beneath its node glow without touching the shared
    materials (so other coins stay plain) and brings its own small point light. New prefab:
    Content/Prefabs/Glowing Coin.manaprefab -- a variant of Coin whose only change is the added Glow, gently pulsing.
    Photo: DevPhotos/<date> glowing-coin-prefab.png. Tests: GlowChecks.
    Still open: materials themselves (emission set in the Mesh Renderer inspector) still aren't saved with a level.
    QUESTION for you before I build it: materials are shared -- every copy of a model uses the same one, so an edit in
    the inspector changes them all. Two ways to save that:
      a) per-copy overrides (Unity's "material override" / Unreal's material instance on the actor): the level saves
         just the changed values on that node, and only that copy changes. Needs the edit to clone the material first.
      b) the shared material is the asset: the edit is saved beside the model and every copy, in every level, changes.
    I'd suggest (a) -- it matches how the Glow already works and cannot surprise another level -- with (b) later as
    "Apply to all copies". Say which and I'll do it.

[x] Review Changes for prefab variants: the review window works on placed copies; a variant open in the prefab stage could use it too.

[x] ManaEditor\Code\GameModes\VoxelEditorGameMode.cs has A LOT of stuff it shouldn't have, planet stuff mostly, that all needs to be split out into game mode components, or some other class.
    Claude (overnight): VoxelEditorGameMode.cs 3,205 -> 1,273 lines.
    - Planet, true-scale regions, KML airspace, streaming and planet stats -> EditorPlanetScenery (its own class;
      the game mode owns one and only reads its Planet to pick/up-res patches).
    - Dead code deleted: the editor's own water mask and country/state border loaders (unused since planets load
      through ManaLevelLoader), taking the old machine-specific GeoData paths with them. The optional
      KML file is now MANA_KML_AIRSPACE or ../ManaAirSpace/AirSpaceModels/UnitedStates.xml.
    - The rest split into partial files by job: .Room (the grid room), .Placement (placing from the Import panel),
      .ColliderDebug (selection collider wireframes), .Selection (picking + F / End / Escape).
    Checked: Planet Showcase streams, End still lands on a ledge, all tests pass.

[x] - Can we organize the panels menu? Some things are more important than others, some things are for debugging, etc.  Also a search bar that finds a given menu (like unreal) would be cool
    Claude: Panels menu is grouped (Editor, Assets, Debugging, Settings; a new panel lands under Other) and starts with
    Find Panel... -- type part of a name, Enter opens the first match (PanelSearchManaView, tests in PanelSearchChecks).

[x] - Do we support nested prefabs, and is it possible?
    Claude: not supported on purpose -- a Prefab node inside a prefab is rejected on load. Variants (a prefab based on
    another, 8 deep) cover most of what nesting would. Possible, but needs override paths that cross prefab boundaries.

[x] - Dynamic normal maps on sprites?
    Claude: already there -- Sprite > Auto Normal Map makes one from the outline (ManaSpriteNormals; bevel and inner-edge
    settings). Reference scene: Content/Levels/Tests/Sprite Normals.

[x] - Can we also support exporting video in addition to the PNG snapshots we take now.
    Claude: already there for scripts -- the `record` command saves frames at a fixed step and Tools/Automation/sizzle.py
    joins them into an MP4 with ffmpeg. No in-editor button yet.

[x] - Can the mana command runner be in a higher up in the project dependencies? with more commands registered by the project that supports it. - like things only mana voxel can do etc.
    Claude: ManaCommandRunner, ManaCommand and ManaCommandFile now live in Mana (Mana/Code/Automation). The runner keeps a
    table of commands by type: the shared ones (wait, move_camera, sky, wind, emission, material, screenshot, record,
    profile_*, gizmos, teleport/log_player, quit) are built in, and an app adds its own with Register(). Graphics reach it
    as two delegates (capture a frame, dump the environment) so Mana needs no ManaGraphics reference. The editor's
    (open_level, prefabs, panels, selection, grid maps, bow, play) are registered by ManaEditor's VoxelCommands.
    Checked with a script (open, sky, screenshot, unknown command, profile, quit) and all tests pass.

[x] - Some kind of particle system. - Points, trails, ribbons, etc.
    Claude: a "ParticleEmitter" script (Add Component) -- ManaParticleEmitterComponent. Rate, burst, spread cone, radius,
    lifetime, speed, gravity, size and colour from birth to death, brightness (above 1 blooms). Shapes: Points (soft
    discs facing the camera) and Streaks (stretched along travel -- rain, embers). Simulated on the CPU in world space and
    drawn as one unlit, blended mesh sorted back to front (ManaParticleMesh), so an emitter is one draw. Runs in the editor
    for tuning; the level saves only the script's properties. Reference scene: Content/Levels/Tests/Particles
    (sparks, smoke, fountain, rain; Tools/Automation/Checks/particles.json). Tests: ParticleChecks.
    Still open: ribbons (a trail joining each particle's past positions), textured particles / sprite-sheet frames,
    collisions, and OpenGL (untested there -- the mesh updates through the same dynamic path skinned meshes use).

[x] - Can the stars be real constalations?
    Claude: yes -- in Day and Night the sky draws ~130 real bright stars (right ascension, declination, magnitude, colour)
    and 20 constellations (Orion, Ursa Major/Minor, Cassiopeia, Cygnus, Lyra, Scorpius, Leo, Taurus, Gemini, ...) in the
    sky shader, turned by the simulated date, hour and observer (Seattle), so they rise, set and wheel round Polaris with the
    sky clock. Scene Inspector > Night sky > Constellation lines draws the figures. The procedural random stars stay as
    the faint fill. DevPhotos/2026-10-07 real-constellations-*.png; Tools/Automation/Checks/constellations.json.
    Found on the way: the sun, moon and now stars were a mirror image (east on the right when facing south); the sky's
    frame is now -X east, so the sun crosses left to right as you face south.
    Still open: the constellation positions were typed from memory of the catalogue (good to a few arcminutes, worth a
    check against a star atlas), Space mode keeps its random stars, and the observer is fixed at Seattle.

[x] - Release builds failed with ImageSharp 4.x ("No Six Labors license found"); roll back and remove ImageSharp completely.
    Claude (2026-10-08): rolled back to 3.1.12, then removed it. Desktop pictures now go through StbImageSharp (decode)
    and StbImageWriteSharp (PNG, JPEG) -- public domain, no license -- inside ManaImageCodec (ManaCore/Imaging, the only
    place that touched ImageSharp), with a Catmull-Rom resize of our own that works on alpha-premultiplied colour. New
    ManaImageCodec.EncodePng gives the tests and the thumbnail baker in-memory PNGs. The README and About box credit
    Stb now. Checked: all 2,981 checks pass (new ImageCodecChecks: PNG round trip, JPEG, resize, junk refused, the moon
    JPEG), a Release build of ManaCore has no license error, and the 2D Sample opens with its textures.
    Not yet checked: the web publish itself (it never compiled ImageSharp, so it should be unchanged) -- worth one run.
    Baked thumbnails keep their names (they are named by a hash of the PNG, and existing ones are reused).

[x] - can the background stars move with the dynamic stars
    Claude (2026-10-08): yes -- the fill stars now turn with the catalogue's real ones (same frame), so the whole night sky wheels together.

[x] - Can we connect the constalation lines when the camera looks at them? (and: show all at once while holding Alt)
    Claude: with Constellation lines on (Appearance > Sky), each figure's lines fade up as the middle of the view turns toward
    it (full within ~20 degrees, gone past ~45) and hold Alt in the editor to show every figure at once
    (ManaIrradianceSettings.ShowAllConstellations, set from the keyboard in VoxelEditorGameMode.Update).

[x] - Can I get a star preview level
    Claude: Content/Levels/Night Sky.manalevel -- a pine ring under a December night, lines on, the sky turning at 120x
    (a degree every two seconds). Levels can now carry an optional "sky" block (mode, dateTime, timeSpeed,
    constellationLines; ManaLevelSky) that is applied on open and written back on save, so a level can ask for its sky.

[x] - Can i get sample level for particles
    Claude: Content/Levels/Particle Garden.manalevel -- a campfire (flames, sparks, smoke and a light), a fountain, rain
    and fireflies at dusk. The small test scene stays in Levels/Tests.

[x] - Dark mode -> the viewport grid felt too dark and contrasty; the alternating squares weren't visible
    Claude: Dark's grid floor is now a mid grey a step under Light's (#9A9AA2, lines #70727C) and the checker squares are
    12% apart (UiTheme.GridCheckerRatio, carried to grid.wgsl in the quad's metallic slot; Light and LCARS keep 3.5%).
    OpenGL's grid.frag still uses its fixed ratio.

[x] - Theme Support -> I added some ui from kenney, can we make it possible to use that theme onto the ui as well?
    Claude: a new Kenney theme (Appearance > Theme > Kenney): navy with the pack's sky blue, and a UiSkin of its pictures
    -- buttons (blue pill for primary), window frames and title bars, tab content panels, slider and progress tracks --
    drawn as nine-slices from kenney_ui-pack-space-expansion.zip (KenneyUiSkin; ManaUiDrawList.DrawNineSlice). A theme with
    no skin, or a pack that is missing, stays flat. Any theme can now carry a Skin.
    Still open: the pack's Kenney Future font, skinned text boxes, checkboxes, combo boxes and menus, and the web (the pack is
    not in the web asset catalog yet, so the web editor gets the colours but not the pictures).

[x] - Star level: more pines, lights, fireflies, slower time
    Claude (2026-10-08): Night Sky now has 110 pines (spheres and pyramids take their size from the node's `scale` in a level --
    `size` is ignored for them), six glowing lanterns, a flickering camp fire, a moon-blue shadow-casting key light, nine
    firefly drifts, and the sky turns at 15x (about a degree every 12 seconds). Authored by Tools/Levels/make_sky_and_garden.py.

[x] - Features to make the fire look more realistic or more stylized
    Claude: Particle emitters gained additive blending (a new ManaOpacityMode.Additive, honoured by the WebGPU pipeline), a
    three-stop colour gradient (UseMidColor/MidPosition), a size bulge, banded colour (ColorSteps, for cel shading), drag,
    turbulence (a per-particle swirl), and a Flames shape (teardrops standing along their travel); Light scripts gained
    FlickerAmount/FlickerSpeed. Particle Garden has both looks side by side: a realistic fire (white-hot to red additive
    flames, embers, dark smoke, a wavering light) and a stylized one (four flat colour bands, hard edges, chunky puffs).
    Still open: textured/flipbook flames, soft particles against geometry, heat distortion, and OpenGL (untested).

[x] - Dark theme contrast is too high between cells
    Claude: the checker step is 5% (0.95), was 12%; Light and LCARS keep 3.5%.

[x] - Finish integrating the kenney theme to actually use his sprites in that mode
    Claude: the skin now also dresses text boxes and checkboxes (dark inset bar, blue pill when checked), combo boxes,
    expander headers, tabs (view and dock), the menu bar and menu rows (blue pill highlight), menu popups, and scrollbars,
    on top of buttons, windows, tab panels and sliders/progress bars. Border has a SkinName for any other panel.
    Not done: the pack's font, its cursors (the cursor pack is a separate zip) and the web (the pack is not in the asset catalog).

    Claude (2026-10-08, later): matched to Kenney's own sample picture -- the Kenney theme is now a light one: pale lilac
    panels, buttons and fields with dark type, blue title bars and primary buttons, the pack's bright blue behind, raised
    buttons that sit flat when pressed.

    Claude (2026-10-08, third pass): no more 1 px seams in the nine-slices (each quad overlaps its neighbour by half a pixel);
    selected tabs are blue with white type; sliders get a thicker track and a round knob, and the inspector's drag strips are
    sliders too; check boxes, combo and tree arrows and text fields now use the plain UI pack (Assets/kenney_ui-pack.zip) as well.
    Still unused from that pack: its font (Kenney Future), dividers, stars, and the coloured (green/red/yellow) fills.

    Claude (2026-10-08, fourth pass): nine-slice and icon pictures are sampled half a texel in from their edge, so bilinear
    filtering no longer reads the atlas beside them (the faint right-hand line). The ManaUI panel (Panels > ManaUI) is now the
    all-controls page: button variants, tooltip and a menu; set, coloured, segmented, vertical and indeterminate progress bars;
    disabled controls; slider box; colour field; tabs; tree; indicator lights, octagon slots and a glass panel; sparkline and
    a card. New controls to match Kenney's sample: ProgressBar Tone/Vertical/Segments, StatusLight, red close dot on windows,
    a red Danger button.
    Not matched yet: the folder-tab panel shape, the translucent title-less "Energy" tray, crosshair cursors.

    Claude (2026-10-08, fifth pass): the gap was the picture's own outer pixels. The pack's atlas is filtered and mipped, so
    a picture touching empty padding faded at its rim; skin pictures are now packed with a four-pixel border of their own edge
    pixels, and the outer edges of a nine-slice snap outwards (floor/ceiling) so the far edge keeps full coverage. Progress
    bars gained Square and Gloss, in four tones, from every bar the pack has. The Kenney theme now sets in Kenney Future Narrow
    (Mana/Assets/Fonts/Kenney, CC0; also in Appearance > Font, with the wide Kenney Future).

    Claude (2026-10-08, sixth pass): Kenney's fonts have capitals only (lower case is the same outline) and no kerning
    table -- the loose spacing was baked into each glyph's advance. Tools/Fonts/tighten_kenney.py rewrites the two fonts with
    each advance set to the glyph's own width plus a gap (the files in Mana/Assets/Fonts/Kenney are the tightened ones), and
    a capitals-only family (ManaFontManager.CapsOnlyFamilies) draws its lower case as small capitals at 80% size
    (ManaFont.SmallCapsFont). The Kenney theme uses the wide cut at 14 px.
[x] - Scenes panel beside Project / prefabs (2026-10-08).
    Searchable, virtualized level tiles open scenes through the existing level host. Thumbnails use
    Content/Levels/Pictures; scenes without a picture show a placeholder. Refresh rescans saved levels.
    Available under Panels > Assets > Scenes, including saved dock layouts. Checked with the scripted
    editor screenshot in Tools/Automation/Checks/scenes-ui.json and headless search/preview checks.

- [x] Expanded compact material presets to 21 across metals, wood, stone, masonry, ground, fabric and plastic (15.7 MiB total). Added category labels/search, reproducible CC0 source manifest and a 63-shape showcase in `Content/Levels/PBR Gallery.manalevel`; capture in `DevPhotos/2026-10-10 pbr-material-library.png`.

- [x] Converted the public-domain Mat Makin / derkreature ShaderBall OBJ to a unit-height GLB, preserving UVs and normals; applied all 21 ZIP presets in PBR Gallery. Source license and reproducible conversion script included.

- [x] Simplified PBR Gallery to the 21 material shader balls and original display models; removed redundant cube and torus samples.

- [x] Fixed Train Yard Wars return routing after wagon-by-wagon unloading (2026-10-11). Dock approaches now check the final engine heading for a route to connected mines, try alternative continuations, and count only the wagons delivered to a town. Automatic turnarounds also serve occupied unused spurs so connected trains can recover after track changes. Regression coverage in MatchReviewTests includes the reproduced seed-2 bot return trip and a stranded occupied spur.
    Validation: all 274 Train Yard Wars checks passed, including 2/4/8-player bot matches; 140 focused rail, cargo, preview and level checks passed. No flicker tests were run.
