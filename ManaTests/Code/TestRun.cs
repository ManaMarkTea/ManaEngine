using System.Diagnostics;
using ManaUI;

namespace ManaTests;

/// <summary>One assertion.</summary>
public sealed record TestCheck(string Name, bool Passed, string Detail);

/// <summary>One suite's worth of assertions.</summary>
public sealed class TestSection
{
    public TestSection(string system, string name)
    {
        System = system;
        Name = name;
    }

    public string System { get; }
    public string Name { get; }
    public List<TestCheck> Checks { get; } = new();
    public TimeSpan Duration { get; internal set; }

    public int Failed => Checks.Count(c => !c.Passed);
    public int Passed => Checks.Count - Failed;
    public bool HasFailures => Failed > 0;
    public string Title => $"{System}: {Name}";
}

/// <summary>The result of a whole run.</summary>
public sealed class TestRun
{
    public List<TestSection> Sections { get; } = new();

    public int Total => Sections.Sum(s => s.Checks.Count);
    public int Failed => Sections.Sum(s => s.Failed);
    public int Passed => Total - Failed;
    public TimeSpan Duration => TimeSpan.FromTicks(Sections.Sum(s => s.Duration.Ticks));
    public bool Success => Failed == 0 && Total > 0;
}

/// <summary>
/// Executes the suites and returns structured results.
/// <para>
/// Separated from the reporting so the same run can be printed to a console or displayed in a window --
/// which is what the GUI runner needs, and what stops the two drifting apart.
/// </para>
/// </summary>
public static class TestRunner
{
    /// <summary>
    /// Every suite in the repo. Add coverage by dropping a file in <c>Code/&lt;System&gt;/</c> and
    /// registering it here.
    /// </summary>
    public static IEnumerable<(string System, string Section)> Catalog =>
        Suites.Select(s => (s.System, s.Section));

    private static readonly (string System, string Section, Action<ManaFontManager, Action<string, bool, string>> Run)[] Suites =
    {
        ("Audio", "WAV decoding and voice lifetime", AudioChecks.Run),
        ("Audio", "course cues, persistence and cleanup", CourseAudioChecks.Run),
        ("ManaUI", "glyph coverage", GlyphChecks.Run),
        ("ManaUI", "layout, text and draw list", LayoutChecks.Run),
        ("ManaUI", "paint order", PaintOrderChecks.Run),
        ("ManaUI", "input", InputChecks.Run),
        ("ManaUI", "text area", TextAreaChecks.Run),
        ("ManaUI", "surfaces", SurfaceChecks.Run),
        ("ManaUI", "bindings", BindingChecks.Run),
        ("ManaUI", "markup", UiMarkupChecks.Run),
        ("ManaUI", "scrolling and text editing", ScrollAndTextChecks.Run),
        ("ManaUI", "popups and combo boxes", PopupChecks.Run),
        ("ManaUI", "menus", MenuChecks.Run),
        ("ManaUI", "tree view", TreeChecks.Run),
        ("ManaUI", "virtual wrap", VirtualWrapChecks.Run),
        ("ManaUI", "numeric, vector and expander editors", EditorChecks.Run),
        ("ManaUI", "colour conversion and picking", ColorChecks.Run),
        ("ManaUI", "inventory slots, grids and hotbars", SlotChecks.Run),
        ("ManaUI", "debug overlay", DebugOverlayChecks.Run),
        ("ManaUI", "colour atlas and images", ImageChecks.Run),
        ("ManaUI", "floating windows", WindowChecks.Run),
        ("ManaUI", "desktop panel windows", DesktopWindowChecks.Run),
        ("ManaUI", "docking", DockingChecks.Run),
        ("ManaUI", "workspace isolation", WorkspaceIsolationChecks.Run),
        ("ManaUI", "drag docking", DragDockingChecks.Run),
        ("ManaUI", "dock drop indicators", DockDropIndicatorChecks.Run),
        ("ManaUI", "tab rows", TabRowChecks.Run),
        ("Mana", "orthographic navigation", OrthographicNavigationChecks.Run),
        ("Mana", "pawn movement", PawnMovementChecks.Run),
        ("Mana", "pawn landing", PawnLandingChecks.Run),
        ("Mana", "mouse gesture latch", MouseGestureLatchChecks.Run),
        ("Mana", "rotation conversion", RotationChecks.Run),
        ("Mana", "mesh batch", MeshBatchChecks.Run),
        ("ManaGraphics", "render-only static mesh batches", StaticRenderBatchChecks.Run),
        ("ManaEditor", "imported mesh editing", ImportedMeshEditingChecks.Run),
        ("Mana", "fixed-step clock", FixedStepClockChecks.Run),
        ("Mana", "sound bank and music", SoundBankChecks.Run),
        ("Mana", "panel window placement", WindowPlacementChecks.Run),
        ("Mana", "pickup motion", PickupMotionChecks.Run),
        ("Mana", "stage animation", StageAnimationChecks.Run),
        ("Mana", "retro filter", RetroFilterChecks.Run),
        ("Mana", "material render instances", MaterialRenderInstanceChecks.Run),
        ("Mana", "cloth", ClothChecks.Run),
        ("Mana", "scatter", ScatterChecks.Run),
        ("Mana", "texture library", TextureLibraryChecks.Run),
        ("Mana", "sprites", SpriteChecks.Run),
        ("Mana", "pixel fx", PixelFxChecks.Run),
        ("ManaPixelFx", "parity with pixelfx", PixelFxParityChecks.Run),
        ("Mana", "moving colliders", MovingColliderChecks.Run),
        ("Mana", "box collider geometry", BoxColliderGeometryChecks.Run),
        ("Mana", "platformer 2D", Platformer2DChecks.Run),
        ("Mana", "pawn jump", PawnJumpChecks.Run),
        ("Mana", "parallax", Platformer2DChecks.RunParallax),
        ("ManaCore", "frame profiler", FrameProfilerChecks.Run),
        ("ManaCore", "clustered lights", LightClusterChecks.Run),
        ("ManaCore", "mesh data", MeshDataChecks.Run),
        ("ManaCore", "sky clock", SkyClockChecks.Run),
        ("ManaCore", "image codec", ImageCodecChecks.Run),
        ("ManaCore", "dds", DdsChecks.Run),
        ("ManaCore", "probe voxelizer", ProbeVoxelizerChecks.Run),
        ("ManaCore", "mip chain", MipChainChecks.Run),
        ("ManaEditor", "scene import", SceneImportChecks.Run),
        ("ManaEditor", "story commands", StoryCommandChecks.Run),
        ("ManaEditor", "scene node editing", SceneNodeEditingChecks.Run),
        ("ManaEditor", "level downloads", LevelDownloadChecks.Run),
        ("ManaCore", "wind", WindChecks.Run),
        ("ManaCore", "json formatting", JsonFormatChecks.Run),
        ("ManaCore", "asset catalog", AssetCatalogChecks.Run),
        ("Mana", "glTF support and camera framing", GltfSupportChecks.Run),
        ("ManaEditor", "measuring tool", MeasureToolChecks.Run),
        ("TargetPractice", "rules, mode and sample level", TargetPracticeChecks.Run),
        ("ManaEditor", "playable levels", PlayableLevelsChecks.Run),
        ("ManaLidar", "scan colouring and glTF export", ScanExportChecks.Run),
        ("ManaLidar", "depth fusion", ScanFusionChecks.Run),
        ("ManaLidar", "live-scan stream", ScanStreamChecks.Run),
        ("ManaLidar", "mesh simplification", ScanSimplifyChecks.Run),
        ("ManaLidar", "depth image meshing", DepthMeshChecks.Run),
        ("Mana", "line mesh in-place refresh", LineMeshRefreshChecks.Run),
        ("Mana", "pawn camera controls", PawnCameraControlChecks.Run),
        ("ManaEditor", "flying mount", FlyingMountChecks.Run),
        ("ManaEditor", "course pieces", CoursePieceChecks.Run),
        ("ManaEditor", "bow", BowChecks.Run),
        ("ManaEditor", "readable asset references", ReadableAssetReferenceChecks.Run),
        ("ManaEditor", "mesh asset library", MeshAssetLibraryChecks.Run),
        ("ManaEditor", "scene click-selection drill-down", SceneSelectionDrillDownChecks.Run),
        ("ManaEditor", "mesh thumbnail rasterizer", MeshThumbnailChecks.Run),
        ("ManaEditor", "voxel rasterizer (circle/sphere rim and fill)", VoxelRasterizerChecks.Run),
        ("ManaEditor", "voxel shape direct edit (paint/erase/flood fill)", VoxelShapeDirectEditChecks.Run),
        ("ManaEditor", "voxel erase collision cleanup", VoxelEraseCollisionChecks.Run),
        ("ManaEditor", "level save and load", LevelSerializationChecks.Run),
        ("ManaEditor", "prefab assets, overrides and grid presets", PrefabChecks.Run),
        ("ManaEditor", "grid map", GridMapChecks.Run),
        ("ManaEditor", "terrain", TerrainChecks.Run),
        ("ManaEditor", "render targets panel", RenderTargetsChecks.Run),
        ("ManaEditor", "grid map generator", GridMapWfcChecks.Run),
        ("ManaEditor", "ui designer", UiDesignerChecks.Run),
        ("ManaEditor", "asset multi-select", AssetMultiSelectChecks.Run),
        ("ManaEditor", "content assets", ContentAssetChecks.Run),
        ("ManaEditor", "prefab lamps", PrefabLampChecks.Run),
        ("ManaEditor", "content mounts: each game's own Content folder", ContentMountChecks.Run),
        ("ManaEditor", "collider intent", ColliderIntentChecks.Run),
        ("ManaEditor", "box collider persistence", BoxColliderPersistenceChecks.Run),
        ("ManaEditor", "level library", LevelLibraryChecks.Run),
        ("ManaEditor", "polygon editing", PolygonEditChecks.Run),
        ("ManaEditor", "sprite assets", SpriteAssetChecks.Run),
        ("ManaEditor", "sprite asset dependencies", SpriteAssetDependencyChecks.Run),
        ("ManaGeoTools", "planet streaming and rebuild handoff", PlanetStreamingChecks.Run),
        ("ManaGeoTools", "planet baseline", PlanetBaselineChecks.Run),
        ("ManaGeoTools", "flight traffic", FlightTrafficChecks.Run),
        ("ManaEditor", "glow", GlowChecks.Run),
        ("ManaEditor", "particles", ParticleChecks.Run),
        ("ManaUI", "mesh import", MeshImportChecks.Run),
        ("ManaUI", "wheel", WheelChecks.Run),
        ("ManaUI", "window resize grip", WindowChecks.RunGrip),
        ("ManaUI", "floating persistence", FloatingPersistenceChecks.Run),
        ("ManaEditor", "place search model", PlaceSearchModelChecks.Run),
        ("ManaEditor", "grid snapping", EditorSnapChecks.Run),
        ("ManaEditor", "scale box handles", SceneScaleBoxChecks.Run),
        ("ManaEditor", "character size in play", CharacterScaleChecks.Run),
        ("ManaEditor", "scene placement and moving between scenes", SceneMoveChecks.Run),
        ("ManaUI", "place search panel", PlaceSearchChecks.Run),
        ("ManaUI", "viewport overlays", ViewportOverlayChecks.Run),
        ("ManaUI", "loading screen", LoadingScreenChecks.Run),
        ("ManaUI", "layout persistence", PersistenceChecks.Run),
        ("ManaUI", "real editor views", ShowcaseChecks.Run),
        ("ManaUI", "dock regions (menu, bottom strip, Layers removal)", DockRegionsChecks.Run),
        ("ManaUI", "ported voxel tool palette", PortedPaletteChecks.Run),
        ("ManaUI", "editor toolbar and status bar", EditorToolbarChecks.Run),
        ("ManaUI", "ported scene outliner", OutlinerChecks.Run),
        ("ManaUI", "theme contrast", ThemeContrastChecks.Run),
        ("ManaUI", "gradients", GradientChecks.Run),
        ("ManaUI", "backdrop blur", BackdropChecks.Run),
        ("ManaUI", "tabs and graphs", TabsAndGraphChecks.Run),
        ("ManaUI", "ported main menu", PortedMainMenuChecks.Run),
        ("ManaUI", "panel search", PanelSearchChecks.Run),
        ("ManaUI", "skins", SkinChecks.Run),
        ("ManaUI", "small caps", SmallCapsChecks.Run),
        ("ManaUI", "scenes", ScenesChecks.Run),
        ("ManaUI", "save level as", SaveLevelAsChecks.Run),
        ("ManaUI", "ported scene inspector", InspectorChecks.Run),
        ("ManaUI", "authored box colliders", BoxColliderChecks.Run),
        ("ManaUI", "world perf and debug panels", WorldPerfAndDebugChecks.Run),

        ("Graphics", "pbr material maps", MaterialMapChecks.Run),
        ("Mana", "material ZIP packs", MaterialZipChecks.Run),
        ("ManaUI", "material picker", MaterialPickerChecks.Run),
        ("Graphics", "startup diagnostics", StartupDiagnosticsChecks.Run),
        ("Graphics", "readback callback lifetime", ReadbackLifetimeChecks.Run),
        ("Graphics", "environment preparation lifetime", EnvironmentLifetimeChecks.Run),
        ("Graphics", "local resource ownership", LocalOwnershipChecks.Run),
        ("Graphics", "gpu resource report", GpuResourceReportChecks.Run),
        ("Graphics", "frame pass ownership", FramePassOwnershipChecks.Run),

        // Last on purpose: it runs a filtered suite of its own, and being last keeps that nesting to one
        // level rather than having a later suite run inside it too.
        ("ManaUI", "gui test runner", RunnerChecks.Run)
    };

    /// <summary>
    /// Runs the suites matching <paramref name="systemFilter"/> (null runs everything).
    /// <paramref name="onCheck"/> is called as each assertion completes, for live console output.
    /// </summary>
    public static TestRun Run(
        ManaFontManager fonts,
        string? systemFilter = null,
        Action<TestSection, TestCheck>? onCheck = null,
        Action<TestSection>? onSectionStarted = null)
    {
        var run = new TestRun();

        // The text box checks exercise copy and paste, which write to a static the host may have pointed
        // at the real clipboard. A test run must not replace what the user had copied.
        using var clipboard = ClipboardIsolation.Begin();

        foreach (var suite in Suites)
        {
            if (systemFilter != null
                && !suite.System.Contains(systemFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var section = new TestSection(suite.System, suite.Section);
            run.Sections.Add(section);
            onSectionStarted?.Invoke(section);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                suite.Run(fonts, (name, ok, detail) =>
                {
                    var check = new TestCheck(name, ok, detail);
                    section.Checks.Add(check);
                    onCheck?.Invoke(section, check);
                });
            }
            catch (Exception ex)
            {
                // A suite that throws is recorded as a failure rather than aborting the run, so one
                // broken area does not hide the state of every area after it.
                var check = new TestCheck($"{section.Name} completed without throwing", false, ex.ToString());
                section.Checks.Add(check);
                onCheck?.Invoke(section, check);
            }

            stopwatch.Stop();
            section.Duration = stopwatch.Elapsed;
        }

        return run;
    }
}
