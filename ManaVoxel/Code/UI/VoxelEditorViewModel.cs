using Mana;
using Mana.Code.Nodes;
using ManaVoxel.Code.Services;
using ManaVoxel.Code.Tools;

namespace ManaVoxel.Code.UI
{
    public enum SceneTransformGizmoMode
    {
        Translate,
        Rotate,
        Scale
    }

    /// <summary>
    /// A queued "load this place as a true-scale cartesian region" request -- see RegionNode.
    /// HexRadiusMeters/StepStrength are only the tiling's STARTING style; both stay adjustable on the
    /// loaded region from the Inspector.
    /// </summary>
    public sealed record RegionLoadRequest(
        string PlaceName,
        float Longitude,
        float Latitude,
        float HalfExtentMeters,
        int Zoom,
        float HexRadiusMeters,
        float StepStrength);

    public class VoxelEditorViewModel : IVoxelEditorCommands
    {
        private const int MaxToolPreviewVoxelCount = 50_000;

        private static readonly string[] LockOverlapPolicies = { "Closest", "Brightest", "First Seen" };

        private readonly List<VoxelLayerViewModel> _voxelLayers = new();
        private readonly CharacterSpawnService _characterSpawnService = new();
        private readonly PlatformCourseService _platformCourseService = new();
        private ManaContext? _context;
        private ManaScene? _scene;
        private ManaVoxelsNode? _previewNode;
        private int? _previewSignature;

        public IReadOnlyList<VoxelLayerViewModel> VoxelLayers => _voxelLayers;
        public VoxelLayerViewModel? ActiveVoxelLayer
        {
            get
            {
                var index = ActiveVoxelLayerIndex - 1;
                if (index < 0 || index >= _voxelLayers.Count)
                {
                    return null;
                }
                return _voxelLayers[index];
            }
        }

        public ManaVoxelShape? Voxel => ActiveVoxelLayer?.Shape;
        public ManaVoxelsNode? VoxelNode => ActiveVoxelLayer?.Node;

        public int ActiveVoxelLayerIndex;
        public int InitialVoxelCount { get; private set; }
        public int BakedPhysicsVoxelCount => _voxelLayers
            .Where(layer => layer.Node.IsBakedToDynamicPhysics)
            .Sum(layer => layer.Node.GetBakedBodyCount());

    public Vector4 PaintVoxelColor = Colors.CornflowerBlue;
        public Vector3 ImportPositionOffset = Vector3.Zero;
        public Vector3 ImportRotationDegrees = Vector3.Zero;
        public Vector3 ImportScale = Vector3.One;
        public bool HasHoverVoxel { get; private set; }
        public Vector3 HoverVoxelPosition { get; private set; }
        public Vector3 HoverVoxelNormal { get; private set; }
        public float HoverCameraDistance { get; private set; }
        public Vector3? LastCommittedVoxelPosition { get; private set; }

        public int SelectedToolIndex;
        public bool IsSelectToolActive = true;
        public int SelectedWorkPlaneIndex;

        /// <summary>
        /// Where a non-Auto work plane sits along its own normal axis -- the height for XZ, the depth for
        /// XY, and so on. This is what makes the plane a real construction plane rather than just an
        /// orientation hint: every tool's placement ray intersects it here instead of raycasting against
        /// whatever scene geometry happens to be under the cursor.
        /// </summary>
        public float WorkPlaneOffset;

        public int StrokeRadiusVoxels;
        public int TextScale = 1;
        public int TextDepth = 1;
        public string ToolText = "MANA";

        /// <summary>Circle and Sphere's rim colour. Every other shape tool paints with <see cref="PaintVoxelColor"/> instead.</summary>
        public Vector4 ShapeStrokeColor = Colors.CornflowerBlue;

        /// <summary>Circle and Sphere's interior colour, used only when <see cref="ShapeHasFill"/> is set.</summary>
        public Vector4 ShapeFillColor = Colors.CornflowerBlue;

        /// <summary>False paints only the rim -- a ring for Circle, a shell for Sphere.</summary>
        public bool ShapeHasFill = true;

        public string[] ToolOptions => VoxelToolCatalog.ToolNames;
        public string[] WorkPlaneOptions => VoxelToolCatalog.PlaneNames;
        public string[] VoxelLayerNames
        {
            get
            {
                var list = new List<string> { "<New Node>" };
                list.AddRange(_voxelLayers.Select((layer, index) => $"{index + 1}. {layer.Name} ({layer.Shape.VoxelCount})"));
                return list.ToArray();
            }
        }

        public float BakeExplosionStrength = 70.5f;
        public float BakeExplosionRadiusPadding = 1.0f;
        public float ReassembleSettleDistance = 0.06f;
        public float ReassembleSettleVelocity = 0.18f;
        public float ReassemblePullGain = 9.5f;
        public float ReassembleVelocityDamping = 0.86f;
        public float ReassembleRotationLerp = 0.18f;
        public float ClickExplosionStrengthScale = 0.2f;

        public bool ShowLockPreview;

        /// <summary>
        /// The mouse-picking ray as of the last frame, for the Debug panel's readout. Set once, right where
        /// the ray is actually computed each frame, rather than recomputed a second time for the panel.
        /// </summary>
        public Vector3 DebugRayOrigin;
        public Vector3 DebugRayDirection;

        public bool ShowAboutWindow;
        public bool ShowSearchWindow;
        public bool UseLightTheme = true;
        public bool ShowLightningBall = true;
        public bool ShowLiquidBall = true;
        public bool ShowWater = true;
        public bool ShowWireframe;
        public bool ShowNormals;
        public bool ShowVertexNormals;
        public float VertexNormalLength = 1.0f;
        public bool ShowSelectedColliderBounds;
        public bool ShowTrackBounds;
        public bool ShowTrackPivots;
        public bool ShowTrackSnapPoints;
        public bool ShowTransformGizmo = true;
        public SceneTransformGizmoMode TransformGizmoMode = SceneTransformGizmoMode.Translate;
        public EditorUndoHistory History { get; } = new();

        // Off by default -- the demo KML is a ~10.5MB external file whose parse + mesh build cost is not
        // worth paying on every startup. Toggling this on is what triggers the (lazy, background) load;
        // see VoxelEditorGameMode.SyncKmlAirspaceVisibility.
        // Set by VoxelSearchPanelView when a result's "Region" button is pressed, consumed and cleared by
        // VoxelEditorGameMode.Update on the main thread. A plain hand-off field rather than a direct call
        // because building a region adds to the scene graph, which the panel's draw pass has no
        // business doing mid-frame.
        public RegionLoadRequest? PendingRegionRequest;

        public bool ShowKmlAirspace;
        // Both written by VoxelEditorGameMode, read by the View menu. KmlAirspaceAvailable is false on any
        // machine that doesn't have the (non-repo) KML file, which greys the menu item out rather than
        // offering a toggle that could only ever no-op.
        public bool KmlAirspaceAvailable;
        public bool KmlAirspaceLoading;

        public int LockOverlapPolicyIndex;
        public string[] LockOverlapPolicyOptions => LockOverlapPolicies;

        public bool HasBakedVoxels => _voxelLayers.Any(layer => layer.Node.IsBakedToDynamicPhysics);
        public bool HasEditableVoxels => _voxelLayers.Any(layer => !layer.Node.IsBakedToDynamicPhysics);
        public bool IsReassembling => _voxelLayers.Any(layer => layer.Node.IsReassembling);
        public string ModeText => HasBakedVoxels && HasEditableVoxels ? "Mixed" : HasBakedVoxels ? "Baked" : "Edit";
        public string ReassemblyText => IsReassembling ? "Reassembling" : "Idle";

        public int VoxelCount => _voxelLayers.Sum(layer => layer.Shape.VoxelCount);
        public int ActiveVoxelCount => Voxel?.VoxelCount ?? 0;
        public int PendingVoxelCount => _voxelLayers.Sum(layer => layer.Shape.PendingVoxelCount);
        public int DuplicateVoxelRejectCount => _voxelLayers.Sum(layer => layer.Shape.DuplicateVoxelRejectCount);
        public int CreatedThisSessionVoxelCount => Math.Max(0, VoxelCount - InitialVoxelCount);
        public bool HasHoverDelta => HasHoverVoxel && LastCommittedVoxelPosition.HasValue;
        public Vector3 HoverDeltaFromLast => HasHoverDelta
            ? HoverVoxelPosition - LastCommittedVoxelPosition!.Value
            : Vector3.Zero;
        public IVoxelEditorCommands Commands => this;

        public void AttachScene(ManaContext context, ManaScene scene)
        {
            _context = context;
            _scene = scene;
        }

        public void AttachPawnController(ManaPawnController pawnController)
        {
            PawnController = pawnController;
        }

        public ManaPawnController? PawnController { get; private set; }

        /// <summary>Level and play state. Its host is wired by the game mode, which owns the world.</summary>
        public LevelViewModel Levels { get; } = new();

        public string? CharacterSpawnError { get; set; }
        public bool CharacterPhysicsEnabled { get; set; } = true;
        public bool HasPlatformCourse => _platformCourseService.Platforms.Count > 0;

        public void AttachVoxelData(ManaVoxelShape voxel, ManaVoxelsNode voxelNode)
        {
            RegisterVoxelLayer("Imported Voxels", "Import", voxel, voxelNode);
            InitialVoxelCount = VoxelCount;
        }

        public VoxelToolSettings CreateToolSettings()
        {
            var kind = VoxelToolCatalog.GetToolKind(SelectedToolIndex);

            // Circle and Sphere paint a rim and an optional interior; every other tool paints one colour
            // throughout, so it keeps using the plain paint colour it always has.
            var isShapeTool = kind is VoxelToolKind.Circle or VoxelToolKind.Sphere;

            return new VoxelToolSettings
            {
                ToolKind = kind,
                WorkPlane = (VoxelWorkPlane)Math.Clamp(SelectedWorkPlaneIndex, 0, WorkPlaneOptions.Length - 1),
                Color = isShapeTool ? ShapeStrokeColor : PaintVoxelColor,
                FillColor = isShapeTool && ShapeHasFill ? ShapeFillColor : null,
                StrokeRadiusVoxels = Math.Clamp(StrokeRadiusVoxels, 0, 12),
                TextScale = Math.Clamp(TextScale, 1, 8),
                TextDepth = Math.Clamp(TextDepth, 1, 16),
                Text = ToolText
            };
        }

        public bool CommitToolResult(VoxelToolResult result)
        {
            var activeLayer = ActiveVoxelLayer;
            if (activeLayer != null)
            {
                var activeNode = activeLayer.Node;
                foreach (var voxel in result.Voxels)
                {
                    var worldPos = result.Anchor + voxel.Position;
                    var localPos = worldPos - activeNode.Position;
                    activeNode.VoxelShape.AddVoxel(localPos, voxel.Color);
                }
                activeNode.VoxelShape.CommitData();
                activeNode.UpdateTransform();
                LastCommittedVoxelPosition = result.Anchor;
                return true;
            }
            else
            {
                var created = CreateVoxelLayer(result.Name, result.Name, result.Anchor, result.Voxels);
                if (created)
                {
                    LastCommittedVoxelPosition = result.Anchor;
                }
                return created;
            }
        }

        public void SetToolPreview(VoxelToolResult? result)
        {
            if (result == null)
            {
                ClearToolPreview();
                return;
            }

            var previewNode = EnsurePreviewNode();
            if (previewNode == null)
            {
                return;
            }

            if (result.Voxels.Count > MaxToolPreviewVoxelCount)
            {
                ClearToolPreview();
                return;
            }

            var previewSignature = CalculatePreviewSignature(result);
            if (_previewSignature == previewSignature)
            {
                return;
            }

            _previewSignature = previewSignature;
            previewNode.Position = result.Anchor;
            var previewVoxels = result.Voxels
                .Select(voxel => new ManaVoxelData
                {
                    Position = voxel.Position,
                    Color = new Vector4(voxel.Color.X, voxel.Color.Y, voxel.Color.Z, 0.55f)
                })
                .ToArray();

            previewNode.VoxelShape.ReplaceCommittedVoxels(previewVoxels, logStats: false);
            previewNode.UpdateTransform();
        }

        public void ClearToolPreview()
        {
            if (_previewSignature == null)
            {
                return;
            }

            _previewSignature = null;
            _previewNode?.VoxelShape.ReplaceCommittedVoxels(Array.Empty<ManaVoxelData>(), logStats: false);
        }

        public bool CreateVoxelLayer(
            string name,
            string source,
            Vector3 anchor,
            IReadOnlyList<ManaVoxelData> voxels)
        {
            if (_context == null || _scene == null || voxels.Count == 0)
            {
                return false;
            }

            var node = new ManaVoxelsNode(_context, Matrix4x4.CreateTranslation(anchor));
            node.Position = anchor;
            node.Name = BuildUniqueLayerName(name);
            _scene.Children.Add(node);

            foreach (var voxel in voxels)
            {
                node.VoxelShape.AddVoxel(voxel.Position, voxel.Color);
            }

            node.VoxelShape.CommitData();
            node.UpdateTransform();
            RegisterVoxelLayer(node.Name, source, node.VoxelShape, node);
            return true;
        }

        private static int CalculatePreviewSignature(VoxelToolResult result)
        {
            var hash = new HashCode();
            hash.Add(result.Name);
            hash.Add(result.Anchor);
            hash.Add(result.Voxels.Count);

            foreach (var voxel in result.Voxels)
            {
                hash.Add(voxel.Position);
                hash.Add(voxel.Color);
            }

            return hash.ToHashCode();
        }

        // Spawn placement is deliberately in front of the camera rather than at a fixed world point --
        // the editor camera roams a 256-unit grid, and a character that lands off-screen reads as
        // "nothing happened".
        public void SpawnCharacter()
        {
            CharacterSpawnError = null;

            if (_context == null || PawnController == null)
            {
                CharacterSpawnError = "The editor is still loading.";
                return;
            }

            var camera = _context.World.MainCamera;
            var forward = camera.Owner.Forward;
            var flatForward = new Vector3(forward.X, 0.0f, forward.Z);
            flatForward = flatForward.LengthSquared() < 1e-6f
                ? -Vector3.UnitZ
                : Vector3.Normalize(flatForward);

            var spawnPosition = camera.Owner.Position + flatForward * 8.0f;
            spawnPosition.Y = 0.0f;

            // Spawned a little above the floor so the capsule settles onto it rather than starting
            // interpenetrated, which the solver would resolve as a shove.
            spawnPosition.Y = CharacterPhysicsEnabled ? 0.5f : 0.0f;

            ManaPawnNode? pawn;
            lock (_context.World.SceneLock)
            {
                pawn = _characterSpawnService.SpawnNextCharacter(
                    _context.World,
                    spawnPosition,
                    PawnController,
                    CharacterPhysicsEnabled);
            }

            if (pawn == null)
            {
                CharacterSpawnError = _characterSpawnService.LastError ?? "Failed to spawn character.";
                return;
            }

            // Possessing on spawn means the very next ground click already walks it somewhere.
            PawnController.Possess(pawn);
        }

        public void SpawnPlatformCourse()
        {
            if (_context == null || _scene == null)
            {
                return;
            }

            _platformCourseService.ClearCourse(_context);

            // Laid out from a fixed, easy-to-find spot rather than in front of the camera: a course is
            // a place you go back to, unlike a character, which you want where you are looking.
            lock (_context.World.SceneLock)
            {
                _platformCourseService.BuildCourse(_context, _scene, new Vector3(20.0f, 0.0f, 20.0f));
            }
        }

        public void RemovePlatformCourse()
        {
            if (_context == null)
            {
                return;
            }

            _platformCourseService.ClearCourse(_context);
        }

        public IReadOnlyList<CharacterAsset> CharacterCatalog => _characterSpawnService.Catalog;

        // Swaps the controlled character for the next model in the catalogue, landing it where the old
        // one stood, facing the same way, still possessed and still on the same camera. Implemented as
        // spawn-then-replace rather than by re-skinning in place: each model brings its own rig, clip
        // set and proportions, so there is nothing meaningful to keep.
        public void CycleControlledCharacter(int direction = 1)
        {
            CharacterSpawnError = null;

            if (_context == null || PawnController == null)
            {
                return;
            }

            var catalog = _characterSpawnService.Catalog;
            if (catalog.Count == 0)
            {
                CharacterSpawnError = "No character packs found in any Assets folder.";
                return;
            }

            var current = PawnController.PossessedPawn;
            if (current == null)
            {
                // Nothing to swap; spawning is the sensible reading of "next character" here.
                SpawnCharacter();
                return;
            }

            var currentIndex = _characterSpawnService.IndexOf(current.SourcePackName, current.Name);
            var nextIndex = ((currentIndex + direction) % catalog.Count + catalog.Count) % catalog.Count;

            var position = current.Position;
            var rotation = current.Rotation;
            var respawnPoint = current.RespawnPoint;
            var usePhysics = current.Body != null;

            ManaPawnNode? replacement;
            lock (_context.World.SceneLock)
            {
                replacement = _characterSpawnService.Spawn(
                    catalog[nextIndex],
                    _context.World,
                    position,
                    PawnController,
                    usePhysics);
            }

            if (replacement == null)
            {
                CharacterSpawnError = _characterSpawnService.LastError ?? "Failed to change character.";
                return;
            }

            replacement.Rotation = rotation;
            replacement.RespawnPoint = respawnPoint;
            replacement.TeleportTo(position);

            // Possess first, then remove: unregistering the possessed pawn would release possession and
            // tear the camera off its boom for a frame before the replacement picked it back up.
            PawnController.Possess(replacement);
            RemoveCharacter(current);
        }

        /// <summary>
        /// Spawns a character at an exact spot and facing, without possessing it -- what Play needs, where
        /// the Player Start decides both and possession is the caller's business.
        /// </summary>
        public ManaPawnNode? SpawnCharacterAt(Vector3 position, float yawRadians)
        {
            CharacterSpawnError = null;

            if (_context == null)
            {
                CharacterSpawnError = "The editor is still loading.";
                return null;
            }

            ManaPawnNode? pawn;
            lock (_context.World.SceneLock)
            {
                pawn = _characterSpawnService.SpawnNextCharacter(
                    _context.World,
                    position,
                    PawnController,
                    CharacterPhysicsEnabled);
            }

            if (pawn == null)
            {
                CharacterSpawnError = _characterSpawnService.LastError ?? "Failed to spawn character.";
                return null;
            }

            pawn.Rotation = new Vector3(0.0f, yawRadians, 0.0f);
            pawn.UpdateTransform();
            return pawn;
        }

        /// <summary>Removes one character, physics capsule included. Public because Play despawns its own.</summary>
        public void RemoveCharacter(ManaPawnNode pawn)
        {
            if (_context == null)
            {
                return;
            }

            lock (_context.World.SceneLock)
            {
                pawn.DisablePhysics();
                pawn.Parent?.RemoveChild(pawn);
                foreach (var scene in _context.World.Scenes)
                {
                    scene.Children.Remove(pawn);
                }
            }

            PawnController?.Unregister(pawn);
        }

        public void RemoveAllCharacters()
        {
            if (_context == null || PawnController == null)
            {
                return;
            }

            // Via RemoveCharacter so the physics capsules are disposed too -- removing only the scene
            // nodes left orphaned capsules in the simulation, still colliding with everything.
            foreach (var pawn in PawnController.Pawns.ToList())
            {
                RemoveCharacter(pawn);
            }
        }

        public void BakeToPhysics()
        {
            ClearToolPreview();

            foreach (var layer in _voxelLayers.Where(layer => !layer.Node.IsBakedToDynamicPhysics))
            {
                layer.Node.BakeVoxelsToDynamicPhysics();
            }
        }

        public void LockBakedVoxelsForEditing()
        {
            foreach (var layer in _voxelLayers.Where(layer => layer.Node.IsBakedToDynamicPhysics))
            {
                layer.Node.LockBakedVoxelsForEditing();
            }
        }

        public void BeginReassembleBakedVoxels()
        {
            foreach (var layer in _voxelLayers.Where(layer => layer.Node.IsBakedToDynamicPhysics))
            {
                layer.Node.BeginReassembleBakedVoxels();
            }
        }

        public void ExplodeBakedVoxels()
        {
            foreach (var layer in _voxelLayers.Where(layer => layer.Node.IsBakedToDynamicPhysics))
            {
                layer.Node.ExplodeBakedVoxels();
            }
        }

        public void ExplodeBakedVoxelsAt(Vector3 position)
        {
            var clickStrength = MathF.Max(1.0f, BakeExplosionStrength * ClickExplosionStrengthScale);
            foreach (var layer in _voxelLayers.Where(layer => layer.Node.IsBakedToDynamicPhysics))
            {
                layer.Node.ExplodeBakedVoxelsAt(
                    position,
                    radius: 6.0f,
                    strength: clickStrength,
                    preserveReassembly: layer.Node.IsReassembling);
            }
        }

        public void AddVoxelAt(Vector3 position)
        {
            var activeLayer = ActiveVoxelLayer;
            if (activeLayer != null)
            {
                var activeNode = activeLayer.Node;
                var localPos = position - activeNode.Position;
                activeNode.VoxelShape.AddVoxel(localPos, PaintVoxelColor);
                activeNode.VoxelShape.CommitData();
                activeNode.UpdateTransform();
                LastCommittedVoxelPosition = position;
            }
            else
            {
                var voxel = new ManaVoxelData
                {
                    Position = Vector3.Zero,
                    Color = PaintVoxelColor
                };

                if (CreateVoxelLayer("Voxel", "Stroke", position, new[] { voxel }))
                {
                    LastCommittedVoxelPosition = position;
                }
            }
        }

        /// <summary>Brush radius in world units for Paint/Erase, one voxel wider than the requested cell count so the voxel exactly at that distance is not missed to floating-point rounding.</summary>
        private float DirectEditBrushRadius => Math.Clamp(StrokeRadiusVoxels, 0, 12) + 0.5f;

        /// <summary>
        /// Paints along the segment from <paramref name="from"/> to <paramref name="to"/>, not just at
        /// <paramref name="to"/> -- pass the same point for both on the first dab of a stroke. Sweeping the
        /// segment (rather than a single dab at the current position) is what keeps a fast mouse move, or a
        /// frame hitch, from leaving an unpainted gap between where the cursor was last frame and where it
        /// landed this frame.
        /// </summary>
        public void PaintAt(Vector3 from, Vector3 to)
        {
            var activeLayer = ActiveVoxelLayer;
            if (activeLayer == null)
            {
                return;
            }

            var activeNode = activeLayer.Node;
            var localFrom = from - activeNode.Position;
            var localTo = to - activeNode.Position;
            if (activeNode.VoxelShape.PaintRadius(localFrom, localTo, DirectEditBrushRadius, PaintVoxelColor) > 0)
            {
                activeNode.UpdateTransform();
                LastCommittedVoxelPosition = to;
            }
        }

        /// <summary>Erases along the segment from <paramref name="from"/> to <paramref name="to"/> -- see <see cref="PaintAt"/> for why a segment rather than just a point.</summary>
        public void EraseAt(Vector3 from, Vector3 to)
        {
            var activeLayer = ActiveVoxelLayer;
            if (activeLayer == null)
            {
                return;
            }

            var activeNode = activeLayer.Node;
            var localFrom = from - activeNode.Position;
            var localTo = to - activeNode.Position;
            if (activeNode.VoxelShape.EraseRadius(localFrom, localTo, DirectEditBrushRadius) > 0)
            {
                activeNode.UpdateTransform();
                LastCommittedVoxelPosition = to;
            }
        }

        public void FloodFillAt(Vector3 position)
        {
            var activeLayer = ActiveVoxelLayer;
            if (activeLayer == null)
            {
                return;
            }

            var activeNode = activeLayer.Node;
            var localPos = position - activeNode.Position;
            if (activeNode.VoxelShape.FloodFill(localPos, PaintVoxelColor) > 0)
            {
                activeNode.UpdateTransform();
                LastCommittedVoxelPosition = position;
            }
        }

        public void SetHoveredVoxel(Vector3 position, Vector3 normal, Vector3 cameraPosition)
        {
            HasHoverVoxel = true;
            HoverVoxelPosition = position;
            HoverVoxelNormal = normal;
            HoverCameraDistance = Vector3.Distance(position, cameraPosition);
        }

        public void ClearHoveredVoxel()
        {
            HasHoverVoxel = false;
            HoverCameraDistance = 0.0f;
        }

        public int GetLockPreviewVoxelCount()
        {
            return _voxelLayers
                .Where(layer => layer.Node.IsBakedToDynamicPhysics)
                .Sum(layer => layer.Node.GetLockPreviewVoxelCount());
        }

        public int GetLockPreviewOverlapCount()
        {
            return _voxelLayers
                .Where(layer => layer.Node.IsBakedToDynamicPhysics)
                .Sum(layer => layer.Node.LockPreviewOverlapCount);
        }

        public void ApplyRuntimeSettings()
        {
            foreach (var layer in _voxelLayers)
            {
                layer.Node.ExplosionStrength = BakeExplosionStrength;
                layer.Node.ExplosionRadiusPadding = BakeExplosionRadiusPadding;
                layer.Node.ReassemblePullGain = ReassemblePullGain;
                layer.Node.ReassembleVelocityDamping = ReassembleVelocityDamping;
                layer.Node.ReassembleRotationLerp = ReassembleRotationLerp;
                layer.Node.ReassembleSettleDistance = ReassembleSettleDistance;
                layer.Node.ReassembleSettleVelocity = ReassembleSettleVelocity;

                LockOverlapPolicyIndex = Math.Clamp(LockOverlapPolicyIndex, 0, LockOverlapPolicies.Length - 1);
                layer.Node.OverlapPolicy = (ManaVoxelsNode.LockOverlapPolicy)LockOverlapPolicyIndex;
                layer.Node.SetLockPreviewEnabled(ShowLockPreview && layer.Node.IsBakedToDynamicPhysics);
            }
        }

        private ManaVoxelsNode? EnsurePreviewNode()
        {
            if (_previewNode != null)
            {
                return _previewNode;
            }

            if (_context == null || _scene == null)
            {
                return null;
            }

            _previewNode = new ManaVoxelsNode(_context, Matrix4x4.Identity)
            {
                Name = "Tool Preview"
            };
            _scene.Children.Add(_previewNode);
            return _previewNode;
        }

        /// <summary>
        /// Drops every layer this view model knew about, ready for a level to register its own.
        /// <para>
        /// Called before the level loader has finished, not after, because the layer list is the one thing
        /// here that outlives a level: every entry points at a node the loader is about to remove, and a
        /// palette editing a deleted shape is how a tool ends up drawing into nothing.
        /// </para>
        /// </summary>
        public void ResetVoxelLayersAfterLevelChange()
        {
            ClearToolPreview();
            _voxelLayers.Clear();
            ActiveVoxelLayerIndex = 0;
            InitialVoxelCount = 0;
        }

        private void RegisterVoxelLayer(string name, string source, ManaVoxelShape shape, ManaVoxelsNode node)
        {
            if (string.IsNullOrWhiteSpace(node.Name) || node.Name == nameof(ManaVoxelsNode))
            {
                node.Name = BuildUniqueLayerName(name);
            }

            node.Source = source;
            _voxelLayers.Add(new VoxelLayerViewModel(node.Name, source, shape, node));
            ActiveVoxelLayerIndex = _voxelLayers.Count;
        }

        private string BuildUniqueLayerName(string baseName)
        {
            var normalizedBaseName = string.IsNullOrWhiteSpace(baseName) ? "Voxel Shape" : baseName.Trim();
            var candidate = normalizedBaseName;
            var suffix = 2;

            while (_voxelLayers.Any(layer => layer.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            {
                candidate = $"{normalizedBaseName} {suffix}";
                suffix++;
            }

            return candidate;
        }
    }
}
