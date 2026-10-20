[] - Foundry factory builder: plan and roadmap in Plans/Foundry.md; game lives in Games/Foundry.
    [x] First playable (2026-10-20): finite mining, belts/backpressure, crafting, power, science goal,
        responsive ManaUI board, manual recovery/feeding, example layout, desktop save/load and automation check.
        Play Content/Levels/Foundry.manalevel; controls and recipes in Games/Foundry/README.md.
    [x] 3D and depth (2026-10-20): rebuilt in 3D on the Kenney factory kit with a class per building, smooth belts
        with curves and side-loading, splitters/sorting, day/night with solar, coal and batteries, assembler recipes,
        a tech tree with red/green science, guided goals and a Space Beacon finale. Games/Foundry/README.md.
    Next: inserters/two-lane belts, production graphs, sound, autosave, browser and macOS checks.

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

[] - Do we have cloth simulation in bepu? If so can we set it up to work with ManaWind on some of the cloths (tents, flags,

[] - Terrain support?  Would be nice to have rolling hills, or dirt paths, sculpting, etc. Can also be marching cube style voxel terrain to support caves and cliffs?
    Plan: Plans/Terrain.md. Claude (2026-10-07, not started -- a design choice for you): I'd build it in two layers. (1) A heightfield terrain node:
    a grid of heights drawn as one mesh, with sculpt brushes (raise, lower, smooth, flatten), a splat map for grass / dirt /
    path painted the same way, and a heightfield collider -- covers hills and paths cheaply, and the existing Grid Map and
    Voxel tools show how a floating tool window plus a brush works. (2) Caves and cliffs: a marching-cubes mesh over a density
    grid, reusing the voxel node's chunking, as a second node kind. Say if you want (1) first.

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
    up to 10 million per emitter, without per-particle CPU work. Test scene: Content/Levels/GPU Particles.manalevel.
    Run Tools/Automation/benchmark-gpu-particles.ps1 for a count sweep with a zero-particle baseline. Details: Plans/GpuParticles.md.
    Moving/rotating emitters now preserve each particle's birth pose, with world-space gravity; no per-particle CPU state.
    Optional bounce against two world-space infinite planes, with restitution and drag. Demo: GPU Particle Bounce.


[] - UI Editor is not working and not used, needs updating - low priority
    Claude (2026-10-07): it opens and works headless here (Panels > UI Designer shows Main Menu's outline, live preview and
    properties; the tests pass), so I don't know what is broken for you. What did you hit?

[x] Shadows seem to be slightly off, they don't touch the ground at the same spot that the actor does.
    Claude (2026-10-07): could not reproduce. In play on the Sample Course at a low sun the character's shadow meets its feet,
    and the Shadow Contact scene's cubes, pillar and disk all touch their shadows (the bias is already in metres, from an
    earlier fix). If you still see it, tell me the level and the time of day -- or does it show only at a distance, where
    the shadow cascades get coarser?

[] - Big step!!  Rename ManaVoxel, its no longer just a voxel editor


[] - Lights:  I dont like the limits, games like minecraft seem to have unlimited light sources. Unreal has MegaLights, etc.  How can we increase the light amounts?  Forward+ rendering?  Fake lights? etc

[] - Do we have cloth simulation in bepu? If so can we set it up to work with ManaWind on some of the cloths (tents, flags, etc)
