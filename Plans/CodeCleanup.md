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
- [ ] Extract remaining geometry work and implement explicit planet retirement on removal/reload.
  - Lifecycle audit: ManaLevelLoader.RemoveAuthoredNodes removes authored trees without a retirement callback; EditorPlanetScenery.Reset drops its reference, and ManaSceneNode.RemoveChild also runs during ordinary reparenting. Do not treat every detachment as permanent retirement.
  - Add an explicit retirement operation at permanent level removal (including planets nested under authored roots), reload and app shutdown. Stop admitting work, invalidate queued handoffs and check retirement after awaited work and under the rebuild lock before mutation.
  - Shared tile fetches must remain available to other planets. Already-dispatched native readbacks must retain their admission slots/resources until they settle; cancel interest in results rather than disposing live native work.
  - Check: Tools/Automation/Checks/planet-streaming.json requests zoom 7, expects 219 leaves and idle counters after refinement, merges root F4/R0/C2, then expects 216 leaves and idle counters.
- Preserve main-thread scene mutation, cancellation and resource ownership. Validate LOD transitions visually.

## 5. Separate Train Yard Wars visuals

- [ ] Extract ground/chunk rendering, train visuals and interaction overlays from TywBoardView in separate steps.
- Preserve batching, retirement of old meshes and existing appearance; run game suites and automation checks.

## 6. Clarify project and test ownership

- [ ] Move Yacht Jousting to Games/ with app services supplied at registration, following Train Yard Wars.
- [ ] Move domain assertions out of ManaUI suites into their owning systems; keep UI interaction tests in place.
- Validate game registration, content loading, desktop tests and affected platform builds.

## 7. Review renderer resource ownership

- [ ] Audit ManaWebGpuContext's partial files for ownership and teardown boundaries before extracting services.
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
