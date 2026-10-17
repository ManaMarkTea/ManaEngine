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
- [ ] Rename the production workspace class to reflect its role.
- [ ] Move mutable workspace state to an instance owned by its UI root; preserve theme rebuild behavior.
- Validate docking, persistence, theme changes and tool windows headlessly and with in-app automation captures.

## 3. Separate asset services from level loading

- [ ] Extract mesh import/cache ownership and asset path resolution from ManaLevelLoader.
- [ ] Replace regex over serialized component properties with explicit asset dependency collection.
- Preserve catalog preloading and browser image decoding order. Validate content checks and desktop/browser loading.

## 4. Extract coherent planet subsystems

- [ ] Separate ocean management from PlanetNode.
- [ ] Separate tile fetch scheduling and split/merge work from scene integration.
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
