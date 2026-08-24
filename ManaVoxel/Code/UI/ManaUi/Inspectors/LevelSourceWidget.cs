using BepuPhysics.Collidables;
using Mana;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaVoxel.Code.Assets;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// The level-file identity for an authored node. This is the part Save captures back to JSON.
    /// </summary>
    public sealed class LevelSourceWidget : InspectorWidget<ManaSceneNode>
    {
        private static readonly string[] ShapeNames =
        {
            "Cube",
            "Sphere",
            "Pyramid",
            "Torus",
            "Icosahedron",
            "Icosphere",
            "Quad"
        };

        protected override bool Accepts(ManaSceneNode node)
        {
            return node.LevelSource != null;
        }

        protected override UiElement Build(ManaSceneNode node, InspectorContext context)
        {
            var theme = context.Theme;
            var rows = new List<UiElement>
            {
                InspectorControls.Row(context, "Saved kind", ReadOnly(theme, () => KindOf(node)))
            };

            switch (node.LevelSource?.Kind)
            {
                case ManaLevelSourceKind.Shape:
                    rows.Add(InspectorControls.Row(context, "Shape", ShapeSelector(node)));
                    rows.Add(InspectorControls.Row(context, "Color", InspectorControls.Colour(
                        () => node.LevelSource?.Color ?? Vector4.One,
                        value => UpdateShapeSource(node, color: value))));
                    rows.Add(InspectorControls.Row(context, SizeLabel(node), InspectorControls.Vector(
                        () => DisplaySize(node.LevelSource?.Shape, node.LevelSource?.Size ?? Vector3.One),
                        value => UpdateShapeSource(node, size: Positive(value)),
                        step: 0.05f)));
                    break;

                case ManaLevelSourceKind.Mesh:
                    rows.Add(InspectorControls.Row(context, "Asset", ReadOnly(theme, () => AssetLabel(node))));
                    break;

                case ManaLevelSourceKind.Group:
                    rows.Add(InspectorControls.Row(context, "Children",
                        ReadOnly(theme, () => node.Children.Count.ToString("N0"))));
                    break;

                case ManaLevelSourceKind.Voxels:
                    rows.Add(InspectorControls.Row(context, "Voxels", ReadOnly(theme, () => VoxelCount(node))));
                    break;

                case ManaLevelSourceKind.PlayerStart:
                    break;

                case ManaLevelSourceKind.Planet:
                    rows.Add(InspectorControls.Row(context, "Baseline", ReadOnly(theme, () => AssetLabel(node))));
                    break;

                case ManaLevelSourceKind.Water:
                    rows.Add(InspectorControls.Row(context, "Surface", ReadOnly(theme, () => WaterSize(node))));
                    rows.Add(InspectorControls.Row(context, "Resolution", ReadOnly(theme, () => WaterResolution(node))));
                    break;
            }

            return new Expander()
                .Header("Level JSON")
                .Section(theme.Spacing, rows.ToArray());
        }

        private static UiElement ShapeSelector(ManaSceneNode node)
        {
            var combo = new ComboBox()
                .Options(ShapeNames)
                .Selected(ShapeIndex(node.LevelSource?.Shape));

            combo.SelectionChanged += (_, index) =>
            {
                if (combo.IsApplyingBinding || index < 0 || index >= ShapeNames.Length)
                {
                    return;
                }

                UpdateShapeSource(node, shape: ShapeNames[index]);
            };

            combo.BindPolled(c => c.SelectedIndex, () => ShapeIndex(node.LevelSource?.Shape));
            return combo;
        }

        private static void UpdateShapeSource(
            ManaSceneNode node,
            string? shape = null,
            Vector4? color = null,
            Vector3? size = null)
        {
            var source = node.LevelSource;
            if (source?.Kind != ManaLevelSourceKind.Shape)
            {
                return;
            }

            var previousShape = source.Shape ?? "Cube";
            var nextShape = shape ?? previousShape;
            var nextColor = color ?? source.Color;
            var displaySize = Positive(size ?? DisplaySize(previousShape, source.Size));
            var nextSize = SourceSizeFromDisplaySize(nextShape, displaySize);
            var shapeChanged = !string.Equals(previousShape, nextShape, StringComparison.Ordinal);

            node.LevelSource = ManaLevelSource.ForShape(nextShape, nextColor, nextSize);
            ApplyShapeVisual(node, nextShape, nextColor, nextSize, shapeChanged);
        }

        private static void ApplyShapeVisual(
            ManaSceneNode node,
            string shape,
            Vector4 color,
            Vector3 size,
            bool shapeChanged)
        {
            var bakesSize = ShapeBakesSize(shape);

            foreach (var renderer in node.Components.OfType<MeshRendererComponent>())
            {
                renderer.Mesh = CreateShapeMesh(node.Context, shape, color, size);
                renderer.Material = renderer.Mesh.Materials.Length > 0
                    ? renderer.Mesh.Materials[0]
                    : ManaMaterial.GetDefaultMaterial(node.Context);
            }

            if (!bakesSize)
            {
                node.Scale = size;
            }
            else if (shapeChanged)
            {
                node.Scale = Vector3.One;
            }

            node.UpdateTransform();
            ResizeBoxCollisionToVisualBounds(node, shape, size);
        }

        private static ManaMesh CreateShapeMesh(ManaContext context, string shape, Vector4 color, Vector3 size)
        {
            return shape switch
            {
                "Cube" => new ManaCube(context, color, size),
                "Sphere" => new ManaSphere(context, color, 0.5f),
                "Pyramid" => new ManaPyramid(context, color, Vector3.One),
                "Torus" => new ManaTorus(context, color),
                "Icosahedron" => new ManaIcosahedron(context, color),
                "Icosphere" => new ManaIsoSphere(context, color, 0.5f),
                "Quad" => new ManaQuad(context, color, size),
                _ => new ManaCube(context, color, size)
            };
        }

        private static bool ShapeBakesSize(string? shape)
        {
            return string.Equals(shape, "Cube", StringComparison.Ordinal)
                || string.Equals(shape, "Quad", StringComparison.Ordinal);
        }

        private static Vector3 DisplaySize(string? shape, Vector3 sourceSize)
        {
            return sourceSize;
        }

        private static Vector3 SourceSizeFromDisplaySize(string? shape, Vector3 displaySize)
        {
            return displaySize;
        }

        private static void ResizeBoxCollisionToVisualBounds(ManaSceneNode node, string shape, Vector3 size)
        {
            if (!TryGetBoxCollision(node, out var collision, out _))
            {
                return;
            }

            var colliderSize = ColliderSizeForShape(shape, size);
            var offset = collision.Position;
            if (ManaCollisionBounds.TryGetVisualBox(node, out var visualSize, out var visualOffset))
            {
                colliderSize = visualSize;
                offset = visualOffset;
            }

            node.Context.Physics.RemoveBody(collision.Body);
            node.Context.Physics.AddBoxBody(node, colliderSize, offset);
        }

        private static Vector3 ColliderSizeForShape(string shape, Vector3 size)
        {
            return size;
        }

        private static string SizeLabel(ManaSceneNode node)
        {
            return node.LevelSource?.Shape switch
            {
                "Cube" => "Size",
                "Quad" => "Quad size",
                _ => "Source scale"
            };
        }

        private static bool TryGetBoxCollision(
            ManaSceneNode node,
            out ManaCollisionComponent collision,
            out Box box)
        {
            foreach (var component in node.GetComponents<ManaCollisionComponent>())
            {
                if (component.Shape is Box found)
                {
                    collision = component;
                    box = found;
                    return true;
                }
            }

            collision = null!;
            box = default;
            return false;
        }

        private static TextBlock ReadOnly(UiTheme theme, Func<string> read)
        {
            return new TextBlock()
                .BindTextPolled(read)
                .Foreground(theme.Text)
                .Ellipsis()
                .CenterV();
        }

        private static string KindOf(ManaSceneNode node)
        {
            return node.LevelSource?.Kind.ToString() ?? "Runtime";
        }

        private static string AssetLabel(ManaSceneNode node)
        {
            var asset = node.LevelSource?.Asset;
            return string.IsNullOrWhiteSpace(asset)
                ? "missing"
                : MeshAssetLibrary.Label(asset);
        }

        private static string VoxelCount(ManaSceneNode node)
        {
            return node is ManaVoxelsNode voxels
                ? voxels.VoxelShape.CommittedVoxels.Count.ToString("N0")
                : "0";
        }

        private static string WaterSize(ManaSceneNode node)
        {
            return node is ManaWaterNode water
                ? $"{water.Mesh.Size.X:0.##} x {water.Mesh.Size.Y:0.##}"
                : "missing";
        }

        private static string WaterResolution(ManaSceneNode node)
        {
            return node is ManaWaterNode water
                ? water.Mesh.Resolution.ToString("N0")
                : "0";
        }

        private static int ShapeIndex(string? shape)
        {
            return Array.FindIndex(ShapeNames, name => string.Equals(name, shape, StringComparison.Ordinal));
        }

        private static Vector3 Positive(Vector3 value)
        {
            return new Vector3(
                MathF.Max(0.001f, value.X),
                MathF.Max(0.001f, value.Y),
                MathF.Max(0.001f, value.Z));
        }

    }
}
