namespace ManaVoxel.Code.Tools
{
    public sealed class VoxelToolController
    {
        private const float MinimumPointDistanceSquared = 0.0625f;

        private readonly IReadOnlyDictionary<VoxelToolKind, IVoxelTool> _tools;
        private readonly List<VoxelToolPoint> _points = new();
        private bool _pointerWasDown;

        public VoxelToolController()
        {
            _tools = VoxelToolCatalog.CreateTools();
        }

        public bool IsDrawing => _pointerWasDown && _points.Count > 0;

        public VoxelToolResult? UpdatePointer(
            bool pointerDown,
            VoxelToolPoint? hitPoint,
            VoxelToolSettings settings)
        {
            if (pointerDown)
            {
                if (hitPoint.HasValue)
                {
                    if (!_pointerWasDown)
                    {
                        Begin(hitPoint.Value);
                    }
                    else
                    {
                        Append(hitPoint.Value, settings.ToolKind);
                    }
                }

                _pointerWasDown = true;
                return null;
            }

            if (!_pointerWasDown)
            {
                return null;
            }

            _pointerWasDown = false;
            return Commit(settings);
        }

        public VoxelToolResult? BuildPreview(VoxelToolSettings settings)
        {
            if (_points.Count == 0)
            {
                return null;
            }

            if (!_tools.TryGetValue(settings.ToolKind, out var tool))
            {
                return null;
            }

            var result = tool.Build(_points, settings);
            return result.Voxels.Count > 0 ? result : null;
        }

        public void Cancel()
        {
            _points.Clear();
            _pointerWasDown = false;
        }

        private void Begin(VoxelToolPoint point)
        {
            _points.Clear();
            _points.Add(point);
        }

        private void Append(VoxelToolPoint point, VoxelToolKind toolKind)
        {
            if (_points.Count == 0)
            {
                _points.Add(point);
                return;
            }

            if (toolKind is VoxelToolKind.Line or VoxelToolKind.Circle or VoxelToolKind.Sphere or VoxelToolKind.Text)
            {
                if (_points.Count == 1)
                {
                    _points.Add(point);
                }
                else
                {
                    _points[^1] = point;
                }

                return;
            }

            var previous = _points[^1].Position;
            if (Vector3.DistanceSquared(previous, point.Position) < MinimumPointDistanceSquared)
            {
                return;
            }

            _points.Add(point);
        }

        private VoxelToolResult? Commit(VoxelToolSettings settings)
        {
            if (_points.Count == 0)
            {
                return null;
            }

            if (!_tools.TryGetValue(settings.ToolKind, out var tool))
            {
                return null;
            }

            var result = tool.Build(_points, settings);
            _points.Clear();
            return result.Voxels.Count > 0 ? result : null;
        }
    }
}
