using Mana;
using Mana.Code.Nodes;
using ManaVoxel.Code.Tools;

namespace ManaVoxel.Code.UI
{
    public class VoxelEditorViewModel : IVoxelEditorCommands
    {
        private const int MaxToolPreviewVoxelCount = 50_000;

        private static readonly string[] LockOverlapPolicies = { "Closest", "Brightest", "First Seen" };

        private readonly List<VoxelLayerViewModel> _voxelLayers = new();
        private ManaContext? _context;
        private ManaScene? _scene;
        private ManaVoxelsNode? _previewNode;

        public IReadOnlyList<VoxelLayerViewModel> VoxelLayers => _voxelLayers;
        public VoxelLayerViewModel? ActiveVoxelLayer => _voxelLayers.Count == 0
            ? null
            : _voxelLayers[Math.Clamp(ActiveVoxelLayerIndex, 0, _voxelLayers.Count - 1)];

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
        public int SelectedWorkPlaneIndex;
        public int StrokeRadiusVoxels;
        public int TextScale = 1;
        public int TextDepth = 1;
        public string ToolText = "MANA";

        public string[] ToolOptions => VoxelToolCatalog.ToolNames;
        public string[] WorkPlaneOptions => VoxelToolCatalog.PlaneNames;
        public string[] VoxelLayerNames => _voxelLayers
            .Select((layer, index) => $"{index + 1}. {layer.Name} ({layer.Shape.VoxelCount})")
            .ToArray();

        public float BakeExplosionStrength = 70.5f;
        public float BakeExplosionRadiusPadding = 1.0f;
        public float ReassembleSettleDistance = 0.06f;
        public float ReassembleSettleVelocity = 0.18f;
        public float ReassemblePullGain = 9.5f;
        public float ReassembleVelocityDamping = 0.86f;
        public float ReassembleRotationLerp = 0.18f;
        public float ClickExplosionStrengthScale = 0.2f;

        public bool ShowLockPreview;
        public bool ShowImGuiDemoWindow;
        public bool UseLightTheme = true;
        public bool ShowLightningBall = true;
        public bool ShowLiquidBall = true;

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

        public void AttachVoxelData(ManaVoxelShape voxel, ManaVoxelsNode voxelNode)
        {
            RegisterVoxelLayer("Imported Voxels", "Import", voxel, voxelNode);
            InitialVoxelCount = VoxelCount;
        }

        public VoxelToolSettings CreateToolSettings()
        {
            return new VoxelToolSettings
            {
                ToolKind = VoxelToolCatalog.GetToolKind(SelectedToolIndex),
                WorkPlane = (VoxelWorkPlane)Math.Clamp(SelectedWorkPlaneIndex, 0, WorkPlaneOptions.Length - 1),
                Color = PaintVoxelColor,
                StrokeRadiusVoxels = Math.Clamp(StrokeRadiusVoxels, 0, 12),
                TextScale = Math.Clamp(TextScale, 1, 8),
                TextDepth = Math.Clamp(TextDepth, 1, 16),
                Text = ToolText
            };
        }

        public bool CommitToolResult(VoxelToolResult result)
        {
            var created = CreateVoxelLayer(result.Name, result.Name, result.Anchor, result.Voxels);
            if (created)
            {
                LastCommittedVoxelPosition = result.Anchor;
            }

            return created;
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

            previewNode.Position = result.Anchor;
            var previewVoxels = result.Voxels
                .Select(voxel => new ManaVoxelData
                {
                    Position = voxel.Position,
                    Color = new Vector4(voxel.Color.X, voxel.Color.Y, voxel.Color.Z, 0.55f)
                })
                .ToArray();

            previewNode.VoxelShape.ReplaceCommittedVoxels(previewVoxels, logStats: false);
        }

        public void ClearToolPreview()
        {
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
            node.Name = BuildUniqueLayerName(name);
            _scene.Children.Add(node);

            foreach (var voxel in voxels)
            {
                node.VoxelShape.AddVoxel(voxel.Position, voxel.Color);
            }

            node.VoxelShape.CommitData();
            RegisterVoxelLayer(node.Name, source, node.VoxelShape, node);
            return true;
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

        private void RegisterVoxelLayer(string name, string source, ManaVoxelShape shape, ManaVoxelsNode node)
        {
            if (string.IsNullOrWhiteSpace(node.Name) || node.Name == nameof(ManaVoxelsNode))
            {
                node.Name = BuildUniqueLayerName(name);
            }

            _voxelLayers.Add(new VoxelLayerViewModel(node.Name, source, shape, node));
            ActiveVoxelLayerIndex = _voxelLayers.Count - 1;
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
