using Mana;

namespace ManaEditor.Code.Tools
{
    public enum VoxelToolKind
    {
        Stroke = 0,
        Line = 1,
        Circle = 2,
        Sphere = 3,
        Spline = 4,
        Text = 5,

        /// <summary>
        /// Recolours existing voxels in a brush radius. Handled directly by VoxelEditorGameMode/
        /// VoxelEditorViewModel -- there is no IVoxelTool for it, since it reads and mutates voxels that
        /// already exist rather than building new geometry from drag points.
        /// </summary>
        Paint = 6,

        /// <summary>Recolours a whole connected same-colour region from one click. Handled the same way as <see cref="Paint"/>.</summary>
        FloodFill = 7,

        /// <summary>Removes existing voxels in a brush radius. Handled the same way as <see cref="Paint"/>.</summary>
        Erase = 8
    }

    public enum VoxelWorkPlane
    {
        Auto = 0,
        XZ = 1,
        XY = 2,
        YZ = 3
    }

    public readonly struct VoxelToolPoint
    {
        public Vector3 Position { get; }
        public Vector3 Normal { get; }

        public VoxelToolPoint(Vector3 position, Vector3 normal)
        {
            Position = position;
            Normal = normal;
        }
    }

    public sealed class VoxelToolSettings
    {
        public VoxelToolKind ToolKind { get; init; }
        public VoxelWorkPlane WorkPlane { get; init; }
        public Vector4 Color { get; init; }

        /// <summary>
        /// Circle and Sphere's interior colour. Null means hollow -- paint the rim in <see cref="Color"/>
        /// and leave the interior empty, rather than filling it with anything at all. Every other tool
        /// ignores this; they only ever read <see cref="Color"/>.
        /// </summary>
        public Vector4? FillColor { get; init; }

        public int StrokeRadiusVoxels { get; init; }
        public int TextScale { get; init; }
        public int TextDepth { get; init; }
        public string Text { get; init; } = string.Empty;
    }

    public sealed class VoxelToolResult
    {
        public string Name { get; }
        public Vector3 Anchor { get; }
        public IReadOnlyList<ManaVoxelData> Voxels { get; }

        public VoxelToolResult(string name, Vector3 anchor, IReadOnlyList<ManaVoxelData> voxels)
        {
            Name = name;
            Anchor = anchor;
            Voxels = voxels;
        }
    }

    public interface IVoxelTool
    {
        VoxelToolKind Kind { get; }
        string DisplayName { get; }
        VoxelToolResult Build(IReadOnlyList<VoxelToolPoint> points, VoxelToolSettings settings);
    }

    public static class VoxelToolCatalog
    {
        public static readonly VoxelToolKind[] ToolKinds =
        {
            VoxelToolKind.Stroke,
            VoxelToolKind.Line,
            VoxelToolKind.Circle,
            VoxelToolKind.Sphere,
            VoxelToolKind.Spline,
            VoxelToolKind.Text,
            VoxelToolKind.Paint,
            VoxelToolKind.FloodFill,
            VoxelToolKind.Erase
        };

        public static readonly string[] ToolNames =
        {
            "Stroke",
            "Line",
            "Circle",
            "Sphere",
            "Spline",
            "Text",
            "Paint",
            "Flood Fill",
            "Erase"
        };

        public static readonly string[] PlaneNames =
        {
            "Auto",
            "XZ",
            "XY",
            "YZ"
        };

        public static VoxelToolKind GetToolKind(int index)
        {
            index = Math.Clamp(index, 0, ToolKinds.Length - 1);
            return ToolKinds[index];
        }

        public static int GetToolIndex(VoxelToolKind kind)
        {
            var index = Array.IndexOf(ToolKinds, kind);
            return Math.Max(0, index);
        }

        public static IReadOnlyDictionary<VoxelToolKind, IVoxelTool> CreateTools()
        {
            var tools = new IVoxelTool[]
            {
                new StrokeVoxelTool(),
                new LineVoxelTool(),
                new CircleVoxelTool(),
                new SphereVoxelTool(),
                new SplineVoxelTool(),
                new TextVoxelTool()
            };

            return tools.ToDictionary(tool => tool.Kind);
        }
    }

    internal abstract class VoxelToolBase : IVoxelTool
    {
        public abstract VoxelToolKind Kind { get; }
        public abstract string DisplayName { get; }
        public abstract VoxelToolResult Build(IReadOnlyList<VoxelToolPoint> points, VoxelToolSettings settings);

        protected static VoxelToolResult BuildResult(
            string name,
            Vector3 anchorPosition,
            Dictionary<(int X, int Y, int Z), Vector4> cells)
        {
            var anchor = VoxelRasterizer.SnapToCenter(anchorPosition);
            var anchorCell = VoxelRasterizer.ToCell(anchor);
            var voxels = cells
                .OrderBy(cell => cell.Key.Y)
                .ThenBy(cell => cell.Key.Z)
                .ThenBy(cell => cell.Key.X)
                .Select(cell => new ManaVoxelData
                {
                    Position = new Vector3(
                        cell.Key.X - anchorCell.X,
                        cell.Key.Y - anchorCell.Y,
                        cell.Key.Z - anchorCell.Z),
                    Color = cell.Value
                })
                .ToArray();

            return new VoxelToolResult(name, anchor, voxels);
        }

        protected static int ClampStrokeRadius(int radius)
        {
            return Math.Clamp(radius, 0, 12);
        }

        protected static bool HasEnoughPoints(IReadOnlyList<VoxelToolPoint> points)
        {
            return points.Count > 0;
        }
    }

    internal sealed class StrokeVoxelTool : VoxelToolBase
    {
        public override VoxelToolKind Kind => VoxelToolKind.Stroke;
        public override string DisplayName => "Stroke";

        public override VoxelToolResult Build(IReadOnlyList<VoxelToolPoint> points, VoxelToolSettings settings)
        {
            var cells = new Dictionary<(int X, int Y, int Z), Vector4>();
            if (!HasEnoughPoints(points))
            {
                return BuildResult(DisplayName, Vector3.Zero, cells);
            }

            var strokeRadius = ClampStrokeRadius(settings.StrokeRadiusVoxels);
            if (points.Count == 1)
            {
                VoxelRasterizer.AddBrush(cells, points[0].Position, settings.Color, strokeRadius);
            }
            else
            {
                for (var i = 1; i < points.Count; i++)
                {
                    VoxelRasterizer.AddLine(cells, points[i - 1].Position, points[i].Position, settings.Color, strokeRadius);
                }
            }

            return BuildResult(DisplayName, points[0].Position, cells);
        }
    }

    internal sealed class LineVoxelTool : VoxelToolBase
    {
        public override VoxelToolKind Kind => VoxelToolKind.Line;
        public override string DisplayName => "Line";

        public override VoxelToolResult Build(IReadOnlyList<VoxelToolPoint> points, VoxelToolSettings settings)
        {
            var cells = new Dictionary<(int X, int Y, int Z), Vector4>();
            if (!HasEnoughPoints(points))
            {
                return BuildResult(DisplayName, Vector3.Zero, cells);
            }

            var start = points[0].Position;
            var end = points.Count > 1 ? points[^1].Position : start;
            VoxelRasterizer.AddLine(cells, start, end, settings.Color, ClampStrokeRadius(settings.StrokeRadiusVoxels));
            return BuildResult(DisplayName, start, cells);
        }
    }

    internal sealed class CircleVoxelTool : VoxelToolBase
    {
        public override VoxelToolKind Kind => VoxelToolKind.Circle;
        public override string DisplayName => "Circle";

        public override VoxelToolResult Build(IReadOnlyList<VoxelToolPoint> points, VoxelToolSettings settings)
        {
            var cells = new Dictionary<(int X, int Y, int Z), Vector4>();
            if (!HasEnoughPoints(points))
            {
                return BuildResult(DisplayName, Vector3.Zero, cells);
            }

            var center = points[0];
            var edgePosition = points.Count > 1 ? points[^1].Position : center.Position;
            var plane = VoxelRasterizer.ResolvePlane(settings.WorkPlane, center.Normal);
            VoxelRasterizer.AddFilledCircle(
                cells, center.Position, edgePosition, plane, settings.Color, settings.FillColor);
            return BuildResult(DisplayName, center.Position, cells);
        }
    }

    internal sealed class SphereVoxelTool : VoxelToolBase
    {
        public override VoxelToolKind Kind => VoxelToolKind.Sphere;
        public override string DisplayName => "Sphere";

        public override VoxelToolResult Build(IReadOnlyList<VoxelToolPoint> points, VoxelToolSettings settings)
        {
            var cells = new Dictionary<(int X, int Y, int Z), Vector4>();
            if (!HasEnoughPoints(points))
            {
                return BuildResult(DisplayName, Vector3.Zero, cells);
            }

            var center = points[0].Position;
            var edgePosition = points.Count > 1 ? points[^1].Position : center;
            VoxelRasterizer.AddFilledSphere(cells, center, edgePosition, settings.Color, settings.FillColor);
            return BuildResult(DisplayName, center, cells);
        }
    }

    internal sealed class SplineVoxelTool : VoxelToolBase
    {
        public override VoxelToolKind Kind => VoxelToolKind.Spline;
        public override string DisplayName => "Spline";

        public override VoxelToolResult Build(IReadOnlyList<VoxelToolPoint> points, VoxelToolSettings settings)
        {
            var cells = new Dictionary<(int X, int Y, int Z), Vector4>();
            if (!HasEnoughPoints(points))
            {
                return BuildResult(DisplayName, Vector3.Zero, cells);
            }

            var strokeRadius = ClampStrokeRadius(settings.StrokeRadiusVoxels);
            if (points.Count < 3)
            {
                var end = points.Count > 1 ? points[^1].Position : points[0].Position;
                VoxelRasterizer.AddLine(cells, points[0].Position, end, settings.Color, strokeRadius);
                return BuildResult(DisplayName, points[0].Position, cells);
            }

            var previousSample = points[0].Position;
            for (var pointIndex = 0; pointIndex < points.Count - 1; pointIndex++)
            {
                var p0 = pointIndex == 0 ? points[pointIndex].Position : points[pointIndex - 1].Position;
                var p1 = points[pointIndex].Position;
                var p2 = points[pointIndex + 1].Position;
                var p3 = pointIndex + 2 < points.Count ? points[pointIndex + 2].Position : p2;
                var segmentLength = Vector3.Distance(p1, p2);
                var sampleCount = Math.Max(4, (int)MathF.Ceiling(segmentLength * 4.0f));

                for (var sampleIndex = 1; sampleIndex <= sampleCount; sampleIndex++)
                {
                    var t = sampleIndex / (float)sampleCount;
                    var sample = CatmullRom(p0, p1, p2, p3, t);
                    VoxelRasterizer.AddLine(cells, previousSample, sample, settings.Color, strokeRadius);
                    previousSample = sample;
                }
            }

            return BuildResult(DisplayName, points[0].Position, cells);
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            var t2 = t * t;
            var t3 = t2 * t;
            return (
                p1 * 2.0f +
                (-p0 + p2) * t +
                (p0 * 2.0f - p1 * 5.0f + p2 * 4.0f - p3) * t2 +
                (-p0 + p1 * 3.0f - p2 * 3.0f + p3) * t3) * 0.5f;
        }
    }

    internal sealed class TextVoxelTool : VoxelToolBase
    {
        public override VoxelToolKind Kind => VoxelToolKind.Text;
        public override string DisplayName => "Text";

        public override VoxelToolResult Build(IReadOnlyList<VoxelToolPoint> points, VoxelToolSettings settings)
        {
            var cells = new Dictionary<(int X, int Y, int Z), Vector4>();
            if (!HasEnoughPoints(points))
            {
                return BuildResult(DisplayName, Vector3.Zero, cells);
            }

            var text = string.IsNullOrWhiteSpace(settings.Text) ? "TEXT" : settings.Text;
            var plane = VoxelRasterizer.ResolvePlane(settings.WorkPlane, points[0].Normal);
            VoxelRasterizer.AddText(
                cells,
                points[0].Position,
                text,
                plane,
                Math.Clamp(settings.TextScale, 1, 8),
                Math.Clamp(settings.TextDepth, 1, 16),
                settings.Color);

            return BuildResult($"{DisplayName}: {text}", points[0].Position, cells);
        }
    }
}
