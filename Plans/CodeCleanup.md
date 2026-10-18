# Code cleanup

Started 2026-10-16. Preserve behavior and authored content; land small, independently validated commits.
This is a structural review, not a claim that every large file needs splitting.

## 1. Isolate test paths

- [x] Remove process-wide working-directory changes from tests.
  - [x] Let asset path resolution accept an explicit working directory; retain coverage for launching outside the checkout.
  - [x] Give prefab editing/loading a per-world content root, propagated through variant resolution, refresh, capture and thumbnails.
  - [x] Migrate PrefabTests and PrefabLampTests to isolated content roots without weakening their assertions.
  - Validate with the headless suite and confirm no test assigns Environment.CurrentDirectory.
  - Review other mutable static test hooks separately; do not replace directory changes with a global root override.

## 2. Separate the editor workspace responsibilities

- [x] Replace repeated panel IDs, titles, factories and menu metadata with one panel registry.
  - EditorPanelRegistry owns factories, titles, merge order, default visibility and menu membership/order. Existing saved IDs remain stable; exposed ID/title collections are read-only.
- [x] Extract layout persistence/restoration from ManaUiDockDemo.
  - EditorWorkspaceLayout owns capture, restoration, version migration, presets and floating-window fallback. The UI retains event wiring; saved format/version and settings paths are unchanged.
- [x] Rename the production workspace class to EditorWorkspace and update callers and documentation.
- [x] Move mutable workspace state to an instance owned by its UI root; preserve theme rebuild behavior.
  - [x] Panel request queues, drag controllers, tool-window positions, location search and theme/density subscriptions now belong to each root's EditorWorkspace. Commands and tests target a specific root; the global last-controller hook is removed.
  - [x] Migrate the shared asset-browser model and thumbnail cache, including standalone Inspector and sprite-editor consumers, into explicitly owned services.
  - EditorAssetServices owns the browser library and thumbnail worker per editor world; standalone workspaces have their own service. Native pop-outs share their owner's service. Closing the editor disposes the library/watchers and thumbnail worker.
  - MeshThumbnailImages now keeps UVs, decoded pixels and visibility state per image atlas; roots sharing an atlas share its coordinates. Sprite saves notify the host to invalidate its owned cache.
- Validate docking, persistence, theme changes and tool windows headlessly and with in-app automation captures.

## 3. Separate asset services from level loading

- [x] Extract mesh import/cache ownership and asset path resolution from ManaLevelLoader.
  - ManaMeshAssetLoader owns templates per world; ManaAssetPaths handles catalog/output/checkout resolution. Level construction and grid maps call these services directly.
- [x] Replace regex over serialized component properties with explicit asset dependency collection.
  - ManaLevelAssetPreloader handles catalog preparation. ManaSpriteAssetDependencies reads Sprite, SpriteSheet, Sprite9Slice, Platformer2D and Bow fields, including default sheets, frames and normal maps. New sprite-using components must add their dependency reader here.
  - Content references resolve by ID within the supplied content root; the shared resolver's cache key includes that root. Loose pictures and escaped/Unicode paths are preserved.
- Preserve catalog preloading and browser image decoding order. Validate content checks and desktop/browser loading.

## 4. Extract coherent planet subsystems

- [x] Separate ocean management from PlanetNode.
  - PlanetOceanController owns far/near meshes, visibility, camera-driven rebuilds, altitude hysteresis and shader uniforms. PlanetNode forwards the existing public setting/update API; the controller shares the terrain rebuild lock.
  - Check: Tools/Automation/Checks/planet-ocean.json. After the baseline decodes, expect the far shell to attach, the near patch to attach at altitude 0.2 radii, stay at 0.3, detach at 0.5, then attach at a new ocean location. The screenshots check coverage across these transitions.
- [x] Separate tile fetching, GPU sampling admission and rebuild scheduling from scene integration.
  - PlanetTileStream shares overlapping in-flight requests, keeps the existing global 24-fetch limit and permits retry after failures without retaining another decoded-tile cache.
  - PlanetHeightSampler owns GPU sampling and bounded CPU fallback. A timed-out native readback keeps its slot until it settles; late faults are observed.
  - PlanetRebuildScheduler observes background faults and queues budgeted main-thread handoffs. Split/merge failures clear pending flags while preserving visible geometry. In-place upres now hands off metadata and changed-patch snapshots on the main thread too.
- [x] Extract patch coverage and source-resolution planning into PlanetPatchPlanner.
  - Tile bounds, exact-zoom selection, source sample density and recursive child planning now live outside PlanetNode; tile/mesh/depth limits and quadrant order are unchanged.
- [x] Implement explicit planet retirement on permanent level removal/reload and app shutdown.
  - ManaLevelLoader retires nested planets before authored trees or opened scenes are removed; both app modes retire remaining planets on closing. Ordinary reparenting is unchanged.
  - GameSilkWebGpu now invokes the app closing callback before graphics/input disposal, after any active frame callback unwinds. The graphics host alone owns input disposal; previously the game-mode Closing overrides were never called.
  - Retired nodes reject detail requests, skip remaining baseline tiles and discard results before terrain mutation. The scheduler rejects new work/handoffs and clears queued completions; pending flags and snapshots are cleared under the rebuild lock.
  - Shared fetches and already-dispatched native readbacks finish normally. Sampling checks retirement after admission, so waiting work skips native dispatch; admission slots remain held until active readbacks settle.
  - Check: Tools/Automation/Checks/planet-retirement.json closes during baseline decode, closes during refinement, then reopens and refines a fresh planet.
- [x] Extract remaining geometry work from PlanetNode.
  - PlanetPatchGeometry owns patch-local bounds, baseline tile overlap/source measurements and CPU/precomputed-height application. PlanetNode retains scene integration, synchronization and retirement gates; mesh vertex generation remains in PlanetPatchMesh.
  - Check: Tools/Automation/Checks/planet-streaming.json requests zoom 7, expects 219 leaves and idle counters after refinement, merges root F4/R0/C2, then expects 216 leaves and idle counters.
- Preserve main-thread scene mutation, cancellation and resource ownership. Validate LOD transitions visually.

## 5. Separate Train Yard Wars visuals

- [x] Extract ground/chunk rendering, train visuals and interaction overlays from TywBoardView in separate steps.
  - [x] Ground/chunk rendering: TywGroundRenderer owns dirty chunks, tile render state, territory ground tint, sea and coastal scenery. Board callbacks retain scene ownership and locking; stable batch nodes preserve deferred GPU uploads. TywViewGeometry shares existing placement/rail mesh calculations.
  - [x] Train visuals: TywTrainRenderer owns locomotive/wagon instances, smoke, pose blending, route-history reuse and transfer indicators; the board supplies cached model instances and scene ownership callbacks.
  - [x] Interaction overlays: TywInteractionOverlay owns hover, per-seat route/ready/cursor caches, territory and siege geometry. Board methods forward existing calls; locking, throttling and stable territory mesh replacement are unchanged.
- Preserve batching, retirement of old meshes and existing appearance; run game suites and automation checks.

## 6. Clarify project and test ownership

- [x] Move Yacht Jousting to Games/ with app services supplied at registration, following Train Yard Wars.
  - Games/YachtJousting references only Mana. ManaGames supplies model loading through immutable bounds/instance adapters, preserving shared mesh placement and existing level type/settings. The game declares catalog model/audio dependencies; rules and registration tests now live under their own system.
- [x] Move domain assertions out of ManaUI suites into their owning systems; keep UI interaction tests in place.
  - Forty assertions now run under Mana (material render instances, texture library, collider geometry, pickup motion and rotation) or ManaVoxel (collider persistence and level discovery). Each extracted fixture owns its world/files; panel interactions and UI-specific metadata remain in ManaUI.
- Validate game registration, content loading, desktop tests and affected platform builds.

## 7. Review renderer resource ownership

- [x] Audit ManaWebGpuContext's partial files for ownership and teardown boundaries before extracting services.
  - [RendererResourceOwnership.md](RendererResourceOwnership.md) maps all 16 partial files, GPU owners/borrowers, callback roots, asynchronous handoffs and the current shutdown sequence.
- [x] Repair callback/readback retirement and thumbnail shutdown cleanup before extracting services.
  - Readbacks use never-reused userdata IDs and serialized admission/retirement. Staging and thumbnail draw resources release before task settlement/device teardown; thumbnail continuations use only copied pixels. Partial thumbnail allocations roll back on errors.
- [x] Retire environment background work and marshal GPU work through render-thread admission.
  - ManaWebGpuEnvironmentLoad owns generation/cancellation and CPU-only photo/LUT preparation; the frame-boundary drain creates/uploads/prefilters GPU maps. Source replacement retains the visible map until activation. Shutdown drops late CPU results and releases pending/active maps; failed candidate creation rolls back textures.
- [ ] Balance height-compute disposal, pending captures, construction rollback and texture acquisition failures.
- [ ] Extract reviewed owners in small steps after their lifetime contracts are validated.
- Start with a written resource/lifetime map; preserve native callback lifetimes and shutdown ordering.
- Require desktop and browser rendering checks for changes; compilation alone is insufficient.

## Progress

- Initial scan: 841 C# files, approximately 182,000 lines. Existing baseline: 3,462 headless checks passed.
- First implementation: explicit asset-resolution working directory, removing the directory mutation in ReadableAssetReferenceTests.
- Validation: all 3,463 headless checks passed; includes missing-asset fallback coverage for the explicit launch directory.
- Runner inspection: TestRunner currently executes suites sequentially despite AGENTS.md describing parallel suites; process-wide mutation still violates the isolation convention.
- Prefab test isolation implemented: ManaContentLibrary binds an immutable root to each world; editing, Inspector views, runtime loading and thumbnail worlds use it. Standalone prefab loading accepts an explicit root and carries it through variant chains.
- Added checks for identical prefab IDs in separate worlds, variant thumbnails and protection of bundled sources. No test changes the process working directory.
- Prefab isolation validation: all 3,466 headless checks passed. Browser/iOS runtime validation was not run for this step.
- Panel registry validation: all 3,466 headless checks passed and the desktop app builds without warnings. In-app automation opens panels and switches Dark/Light themes; captures live under Artifacts/Automation/cleanup-panels-*.png. User workspace settings are restored after the check.
- Layout extraction validation: all 3,466 headless checks passed, including persistence, upgrades and floating-window coverage. Desktop build has no warnings; automation switched themes and reopened the app with the saved panel arrangement. Captures: Artifacts/Automation/cleanup-layout-*.png. User settings were restored afterward; browser/iOS were not run.
- Workspace instance validation: all 3,472 checks passed. New coverage checks request isolation, controller ownership, separate location search state and theme rebuilds with two live roots. Desktop build and panel/theme/reopen automation passed; artifacts use the cleanup-workspace prefix. Shared asset services remain a follow-up; browser/iOS were not run.
- Asset ownership validation: all 3,478 checks passed. Coverage includes independent browser filters, browser state across theme rebuilds, separate atlas coordinates, shared-atlas roots, late cache availability and native-popout service ownership. Desktop build has no warnings. Automation displayed Import previews, Project tiles and a sprite sheet, switched theme, detached Import and reopened its saved layout; artifacts use the cleanup-assets prefix. User workspace settings were restored. Browser/iOS were not run.
- Asset loading separation validation: all 3,485 checks passed. Sample Course opened with 245 nodes and zero warnings and entered desktop play; captures use cleanup-mesh-loading. Browser Release/AOT publish and Chrome 2D Sample passed, including character/background/slice pictures and bow input, with no premature image-load or WebGPU validation errors; captures/logs are in Artifacts/WebDrive/cleanup-asset-preloader-settled.
- SDK follow-up: 10.0.402 lacks its wasm-tools registration; workload restore requires administrator privileges. Browser validation used the retained 10.0.300 SDK directly, without adding a global.json. Register the new workload with `sudo dotnet workload restore ManaVoxel.Web/ManaVoxel.Web.csproj`. iOS was not run.
- Ocean extraction validation: all 3,485 headless checks passed; desktop build has no warnings. Planet Showcase opened with zero warnings, decoded its baseline, and logged far attach / near attach / near detach / near attach in the expected order. In-app orbit, near, middle-band, far and recentered captures are under Artifacts/Automation/cleanup-ocean-*.png. Original user workspace settings were restored. No shader or authored level changes; browser/iOS were not run.
- Streaming/scheduler validation: all 3,498 headless checks passed, including 13 small checks under ManaTests/Code/ManaGeoTools for fetch sharing/retry/concurrency, completion budgeting and timed-out GPU admission/late-fault recovery. Desktop build has no warnings. Planet Showcase refined 4/4 children from 42/42 unique tiles (219 leaves), then merged back to 216 leaves; both status reports showed zero in-progress upres/splits/merges/fetches. In-app captures/logs use Artifacts/Automation/cleanup-streaming. User workspace settings were restored; no shaders, authored levels or LOD/source-quality thresholds changed. Browser/iOS were not run.

- Patch planner validation: all 3,498 headless checks passed in 26.0s; desktop build has no warnings. Planet Showcase refined 4/4 children from 42/42 tiles to 219 leaves and merged back to 216, with all busy counters zero at both checkpoints. In-app orbit capture inspected; captures/logs use Artifacts/Automation/cleanup-planner. Saved workspace settings were restored after a successful reopen. Removal/reload lifetime requirements are recorded above; cancellation is not implemented in this extraction. Browser/iOS were not run.

- Planet retirement validation: all 3,502 headless checks passed in 25.9s; the focused planet suite passed 17 checks in 0.1s. Desktop build has no warnings. Live automation unloaded during baseline decode and an active 42-tile refinement, then reopened and refined a fresh planet to 219 leaves with all busy counters zero. Editor and standalone-play quit during loading and reopened successfully; shutdown logs confirm retirement. Captures/logs use Artifacts/Automation/cleanup-retire*, and saved workspace settings were restored. Browser/iOS were not run.

- Planet geometry extraction validation: all 3,502 headless checks passed in 25.6s; desktop build has no warnings. Planet Showcase refined 4/4 children from 42/42 tiles to 219 leaves, then merged to 216; both checkpoints had all busy counters zero. Detail and orbit captures inspected under Artifacts/Automation/cleanup-geometry*.png. User workspace settings were restored. Geometry formulas, source-quality thresholds, shaders and authored content are unchanged; browser/iOS were not run.

- Train Yard Wars ground extraction validation (2026-10-18): all 208 game checks passed in 3.2s, including board-reset ownership checks; desktop build has zero warnings. Existing train-yard-wars automation completed with two stand-in pads, bot play and score logging. In-app capture inspected at Artifacts/Automation/cleanup-tyw-ground.png; user settings restored. Ground batching keeps stable scene nodes during replacement uploads; rail retirement stays board-owned. Browser/iOS were not run.

- Train visual extraction validation (2026-10-18): all 208 game checks passed in 3.2s, including wagon reuse, cargo indicator visibility, train removal and board reset. Desktop build has zero warnings; automation completed and capture inspected at Artifacts/Automation/cleanup-tyw-trains.png. User reported excessively dark terrain; investigation found SetLighting sets only the legacy phase while ManaSky evaluates the date-time. The mismatch predates these extractions and is being fixed separately. Browser/iOS were not run.

- Lighting follow-up (2026-10-18): TrainYardWarsMode.SetLighting now updates SkyDateTime as well as TimeOfDay, preserving the date and UTC offset. The sun follows the configured hour and L-key choices again. Both new regression checks failed before the fix; all 210 game checks now pass in 3.4s. Desktop automation completed with unchanged intensity settings, and the daylight capture was inspected at Artifacts/Automation/cleanup-tyw-lighting.png. Visual record: DevPhotos/2026-10-18 train-yard-wars-lighting.png. User settings restored; browser/iOS were not run.

- Interaction overlay extraction validation (2026-10-18): all 3,510 headless checks passed in 26.3s. Added checks for unchanged highlight reuse, per-seat clearing and preview/cursor recreation after board reset. Desktop build has zero warnings. Existing game automation completed with two stand-in pads, bot play and score logging; daylight capture inspected at Artifacts/Automation/cleanup-tyw-overlays.png with no logged exceptions or WebGPU validation errors. User settings restored. TywBoardView is now 768 lines (previously 2,086); scene ownership and rail retirement remain there. Browser/iOS were not run.

- Yacht Jousting project extraction validation (2026-10-18): all 3,512 headless checks passed in 25.1s, including saved type/id/settings round-trip and the editor-free assembly boundary. Desktop build has zero warnings; lifecycle automation reached a 5:1 win with 30 impacts and left identical saved/current level snapshots after stop. In-app captures/logs use Artifacts/Automation/cleanup-yacht*. Browser Release/AOT publish and headless Chrome passed with the game mode, models, HUD and lance input; inspected captures/logs are under Artifacts/WebDrive/cleanup-yacht-verified, with no logged exceptions or WebGPU validation errors. iOS Compile passed with zero warnings using the retained 10.0.300 SDK (10.0.402 lacks ios workload registration, as it lacks wasm-tools); no phone deployment was performed. User workspace settings were restored. Authored levels and game rules are unchanged.

- Test ownership cleanup validation (2026-10-18): all 3,512 headless checks passed in 23.4s, including game registration and strict content loading. Forty domain assertions were relocated without reducing the total; material checks now use an isolated generated texture instead of conditionally depending on the editor texture library. Test builds report seven existing nullable warnings in ImagesTests, ReadableAssetReferenceTests and CoursePieceTests. Production code, authored content and platform projects are unchanged; browser/iOS were not run.

- Renderer ownership audit (2026-10-18): written map added in Plans/RendererResourceOwnership.md. Traced root/device lifetimes, scene/texture retirement, native and browser callbacks, thumbnail and environment continuations, initialization rollback and shutdown. Recorded confirmed cleanup omissions and unverified native lifetime risks with an ordered fix/validation plan. Documentation only; no production changes or rendering/build checks in this step.

- Readback/thumbnail lifetime repair (2026-10-18): all 3,523 headless checks passed in 25.9s, including 11 new lifetime checks; focused Graphics passed 57 checks in 0.2s. Desktop build has zero warnings. Native Metal smoke verified actual thumbnail pixels with padded rows, rollback after an injected draw failure, pending-map shutdown, queued-request cancellation and zero residual registry/texture references. In-app reflections/screenshots/six-face dumps and Import checks completed; captures/logs use Artifacts/Automation/readback-*. Browser Release/AOT publish (SDK 10.0.300) and Chrome editor/resize and 2D Sample play/input passed with no logged exceptions or WebGPU validation errors; artifacts use Artifacts/WebDrive/readback-*. Browser checks cover rendering compatibility; deterministic readback cancellation was exercised on native Metal. User workspace settings were restored and byte-compared. iOS Compile passed with zero warnings using SDK 10.0.300; no phone deployment was performed. Environment background retirement and other audit findings remain open.

- Environment lifetime repair (2026-10-18): all 3,538 headless checks passed in 28.2s, including 15 new generation/cancellation and CPU photo/LUT checks; focused Graphics passed 72 checks in 0.2s. Native Metal smoke verified CPU-only preparation, render-thread activation, visible-map retention, source replacement, repeat photo updates, shutdown with pending CPU/GPU work and fresh-context reopening. Desktop build and iOS Compile have zero warnings; no phone deployment. In-app Photo/Day/Photo and six-face reflection checks passed with inspected captures under Artifacts/Automation/environment-*; repeatable fixture: Tools/Automation/Checks/environment-lifetime.json. Browser Release/AOT publish (SDK 10.0.300) and Chrome editor/resize and 2D Sample play/input passed without logged exceptions or WebGPU validation errors (Artifacts/WebDrive/environment-*). User workspace settings were restored and byte-compared. No shaders or authored levels changed. Remaining local disposal/request/initialization gaps stay open before service extraction.
