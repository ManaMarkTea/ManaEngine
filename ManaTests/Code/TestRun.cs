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
        ("ManaUI", "bindings", BindingChecks.Run),
        ("ManaUI", "markup", UiMarkupChecks.Run),
        ("ManaUI", "scrolling and text editing", ScrollAndTextChecks.Run),
        ("ManaUI", "popups and combo boxes", PopupChecks.Run),
        ("ManaUI", "menus", MenuChecks.Run),
        ("ManaUI", "tree view", TreeChecks.Run),
        ("ManaUI", "virtual wrap", VirtualWrapChecks.Run),
        ("ManaUI", "numeric, vector and expander editors", EditorChecks.Run),
        ("ManaUI", "colour conversion and picking", ColorChecks.Run),
        ("ManaUI", "debug overlay", DebugOverlayChecks.Run),
        ("ManaUI", "colour atlas and images", ImageChecks.Run),
        ("ManaUI", "floating windows", WindowChecks.Run),
        ("ManaUI", "desktop panel windows", DesktopWindowChecks.Run),
        ("ManaUI", "docking", DockingChecks.Run),
        ("ManaUI", "drag docking", DragDockingChecks.Run),
        ("ManaUI", "dock drop indicators", DockDropIndicatorChecks.Run),
        ("ManaUI", "tab rows", TabRowChecks.Run),
        ("Mana", "pawn movement", PawnMovementChecks.Run),
        ("Mana", "pawn landing", PawnLandingChecks.Run),
        ("Mana", "mouse gesture latch", MouseGestureLatchChecks.Run),
        ("Mana", "rotation conversion", RotationChecks.Run),
        ("Mana", "sprites", SpriteChecks.Run),
        ("Mana", "moving colliders", MovingColliderChecks.Run),
        ("Mana", "platformer 2D", Platformer2DChecks.Run),
        ("Mana", "pawn jump", PawnJumpChecks.Run),
        ("Mana", "parallax", Platformer2DChecks.RunParallax),
        ("ManaCore", "frame profiler", FrameProfilerChecks.Run),
        ("ManaCore", "sky clock", SkyClockChecks.Run),
        ("ManaCore", "image codec", ImageCodecChecks.Run),
        ("ManaCore", "wind", WindChecks.Run),
        ("ManaCore", "json formatting", JsonFormatChecks.Run),
        ("ManaCore", "asset catalog", AssetCatalogChecks.Run),
        ("Mana", "glTF support and camera framing", GltfSupportChecks.Run),
        ("ManaVoxel", "measuring tool", MeasureToolChecks.Run),
        ("ManaLidar", "scan colouring and glTF export", ScanExportChecks.Run),
        ("ManaLidar", "depth fusion", ScanFusionChecks.Run),
        ("ManaLidar", "mesh simplification", ScanSimplifyChecks.Run),
        ("ManaLidar", "depth image meshing", DepthMeshChecks.Run),
        ("Mana", "line mesh in-place refresh", LineMeshRefreshChecks.Run),
        ("Mana", "pawn camera controls", PawnCameraControlChecks.Run),
        ("ManaVoxel", "flying mount", FlyingMountChecks.Run),
        ("ManaVoxel", "course pieces", CoursePieceChecks.Run),
        ("ManaVoxel", "bow", BowChecks.Run),
        ("ManaVoxel", "readable asset references", ReadableAssetReferenceChecks.Run),
        ("ManaVoxel", "mesh asset library", MeshAssetLibraryChecks.Run),
        ("ManaVoxel", "scene click-selection drill-down", SceneSelectionDrillDownChecks.Run),
        ("ManaVoxel", "mesh thumbnail rasterizer", MeshThumbnailChecks.Run),
        ("ManaVoxel", "voxel rasterizer (circle/sphere rim and fill)", VoxelRasterizerChecks.Run),
        ("ManaVoxel", "voxel shape direct edit (paint/erase/flood fill)", VoxelShapeDirectEditChecks.Run),
        ("ManaVoxel", "voxel erase collision cleanup", VoxelEraseCollisionChecks.Run),
        ("ManaVoxel", "level save and load", LevelSerializationChecks.Run),
        ("ManaVoxel", "prefab assets, overrides and grid presets", PrefabChecks.Run),
        ("ManaVoxel", "grid map", GridMapChecks.Run),
        ("ManaVoxel", "grid map generator", GridMapWfcChecks.Run),
        ("ManaVoxel", "ui designer", UiDesignerChecks.Run),
        ("ManaVoxel", "asset multi-select", AssetMultiSelectChecks.Run),
        ("ManaVoxel", "content assets", ContentAssetChecks.Run),
        ("ManaVoxel", "prefab lamps", PrefabLampChecks.Run),
        ("ManaVoxel", "collider intent", ColliderIntentChecks.Run),
        ("ManaVoxel", "polygon editing", PolygonEditChecks.Run),
        ("ManaVoxel", "sprite assets", SpriteAssetChecks.Run),
        ("ManaVoxel", "glow", GlowChecks.Run),
        ("ManaVoxel", "particles", ParticleChecks.Run),
        ("TrainYardWars", "rail rendering batches", TrackBatchChecks.Run),
        ("TrainYardWars", "match review regressions", MatchReviewChecks.Run),
        ("TrainYardWars", "train yard wars rules", TrainYardWarsChecks.Run),
        ("ManaVoxel", "yacht jousting rules", YachtJoustingChecks.Run),
        ("ManaUI", "mesh import", MeshImportChecks.Run),
        ("ManaUI", "wheel", WheelChecks.Run),
        ("ManaUI", "window resize grip", WindowChecks.RunGrip),
        ("ManaUI", "floating persistence", FloatingPersistenceChecks.Run),
        ("ManaVoxel", "place search model", PlaceSearchModelChecks.Run),
        ("ManaVoxel", "grid snapping", EditorSnapChecks.Run),
        ("ManaVoxel", "character size in play", CharacterScaleChecks.Run),
        ("ManaVoxel", "scene placement and moving between scenes", SceneMoveChecks.Run),
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
        ("ManaUI", "ported scene inspector", InspectorChecks.Run),
        ("ManaUI", "authored box colliders", BoxColliderChecks.Run),
        ("ManaUI", "world perf and debug panels", WorldPerfAndDebugChecks.Run),

        ("Graphics", "pbr material maps", MaterialMapChecks.Run),
        ("Mana", "material ZIP packs", MaterialZipChecks.Run),
        ("ManaUI", "material picker", MaterialPickerChecks.Run),
        ("Graphics", "startup diagnostics", StartupDiagnosticsChecks.Run),

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
