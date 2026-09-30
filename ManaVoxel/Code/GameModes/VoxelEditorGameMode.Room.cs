using BepuPhysics.Collidables;
using ManaUI;
using System.Linq;
using Mana.Code.Components;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using Mana.Code.UI;
using ManaVoxel.Code.Services;
using ManaVoxel.Code.Tools;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.Levels;
using ManaVoxel.Code.Search;
using ManaVoxel.Code.UI;
using ManaVoxel.Code.UI.ManaUi;
using ManaCore.Diagnostics;
using ManaCore.Graphics;
using ManaCore.Loading;
using ManaGraphics.Code.Diagnostics;
using ManaGraphics.Code.Graphics;
using ManaGeoTools.Code;
using ManaGeoTools.Code.Planet;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace Mana.Code.GameModes
{
    // The editor room -- floor, walls, ceiling and LCARS coves -- and the colliders that follow their models.
    public partial class VoxelEditorGameMode
    {
        // The demo grid room's own nodes -- tracked so a live theme switch can tear the room down and
        // rebuild it rounded (LCARS) or square (Dark/Light) instead of only ever picking a shape once at
        // startup. See BuildGridRoom/SyncGridRoomShape.
        private ManaSceneNode? _gridXzPlane;
        private ManaSceneNode? _gridCeilingPlane;
        private ManaSceneNode? _gridYxPlane;
        private ManaSceneNode? _gridZyPlane;
        private ManaSceneNode? _gridWallZFar;
        private ManaSceneNode? _gridWallXFar;
        private readonly ManaSceneNode?[] _gridWallCoves = new ManaSceneNode?[4];
        private bool? _gridRoomRounded;
        private bool? _gridWallsShown;

        /// <summary>
        /// Paints the ground-plane grid quads with the active UI theme's floor/line colours. Also called
        /// from <see cref="ManaVoxel.Code.UI.ManaUi.ManaUiDockDemo"/> on every later theme switch; this
        /// call is the one that matters for the very first frame, since it runs after the planes exist.
        /// </summary>
        private void ApplyGridTheme(params ManaSceneNode?[] planes)
        {
            var theme = ManaUi?.Root?.Theme;
            if (theme == null)
            {
                return;
            }

            foreach (var plane in planes)
            {
                if (plane == null)
                {
                    continue;
                }

                foreach (var renderer in plane.GetComponents<MeshRendererComponent>())
                {
                    renderer.Material.Albedo.Value = theme.GridFloor;
                    renderer.Material.Emission.Value = theme.GridLine;
                    renderer.Material.EmissionStrength = theme.GridLineThickness;
                }
            }
        }

        /// <summary>
        /// (Re)builds the demo grid room -- floor, ceiling, four walls, and (LCARS only) the four
        /// vertical coves rounding where the walls meet. Safe to call again later: any existing room is
        /// torn down first (see <see cref="RemoveGridRoom"/>), so a live theme switch can actually change
        /// the room's topology instead of only ever picking a shape once at startup.
        /// </summary>
        private void BuildGridRoom(bool rounded)
        {
            const float gridSize = GridSize;
            float halfGridSize = gridSize * 0.5f;

            // How far each wall gets trimmed back from its two neighbours so a rounded cove (see
            // ManaGridCoveFillet) can fill each gap instead of a sharp seam. Floor and ceiling are never
            // trimmed -- every wall still meets them at its original, sharp edge. What rounds is the four
            // vertical seams where the walls meet each other, which is what actually reads as a rounded
            // room -- the flat walls blending into a cylindrical curve at every corner -- rather than a
            // bevel where wall meets floor or ceiling.
            float coveRadius = rounded ? 4.0f : 0.0f;
            float uvTrim = coveRadius / gridSize;
            float uvSpan = 1.0f - 2.0f * uvTrim;

            RemoveGridRoom();

            // The room is centred on the origin, so the world axes cross in the middle of the floor. Every
            // quad is still built from its corner, which is why each position below starts from here.
            var corner = new Vector3(-halfGridSize, 0.0f, -halfGridSize);

            // Full enclosed box: floor, ceiling, and all four walls. xzPlane/yxPlane/zyPlane keep their
            // original names and positions (the "near" faces at Y=0/Z=0/X=0); the other three are their
            // mirrors at Y=gridSize/Z=gridSize/X=gridSize.
            ManaSceneNode xzPlane, yxPlane, zyPlane, ceilingPlane, wallZFar, wallXFar;
            var wallCoves = new ManaSceneNode?[4];

            var shapes = new ManaShapeFactory(Context, Context.World.DefaultScene);

            lock (Context.World.SceneLock)
            {
                // Floor and ceiling: full extent, never trimmed -- see the comment above.
                xzPlane = shapes.AddQuad(corner, Colors.White, ManaQuadPlanes.XZ, 0, gridSize, 0, gridSize, "Grid Plane XZ");
                ceilingPlane = shapes.AddQuad(corner + new Vector3(0, gridSize, 0), Colors.White, ManaQuadPlanes.XZ, 0, gridSize, 0, gridSize, "Grid Plane Ceiling");

                // The four walls. Each is trimmed at BOTH ends of its horizontal extent once rounded --
                // every wall touches two of the room's four vertical corners, not one.
                yxPlane = shapes.AddQuad(corner, Colors.White, ManaQuadPlanes.YX, 0, gridSize, coveRadius, gridSize - coveRadius, "Grid Plane YX");
                zyPlane = shapes.AddQuad(corner, Colors.White, ManaQuadPlanes.ZY, coveRadius, gridSize - coveRadius, 0, gridSize, "Grid Plane ZY");
                wallZFar = shapes.AddQuad(corner + new Vector3(0, 0, gridSize), Colors.White, ManaQuadPlanes.YX, 0, gridSize, coveRadius, gridSize - coveRadius, "Grid Plane YX Far");
                wallXFar = shapes.AddQuad(corner + new Vector3(gridSize, 0, 0), Colors.White, ManaQuadPlanes.ZY, coveRadius, gridSize - coveRadius, 0, gridSize, "Grid Plane ZY Far");

                if (rounded)
                {
                    // UV remap so every trimmed wall's tiles keep their true 8-unit size and stay aligned
                    // with their coves -- see grid.wgsl/grid.frag's use of uv_transform. Floor and ceiling
                    // need none: their UV was never touched.
                    //
                    // ManaQuadPlanes.YX: axis=Y (untouched, full height), extrusion=X (trimmed) -- the
                    // quad's V (extrusion/UV.y) axis needs remapping for both Z walls.
                    foreach (var yxWall in new[] { yxPlane, wallZFar })
                    {
                        if (yxWall.Components[0] is MeshRendererComponent yxRenderer)
                        {
                            yxRenderer.Material.UvScale = new Vector2(1.0f, uvSpan);
                            yxRenderer.Material.UvOffset = new Vector2(0.0f, uvTrim);
                        }
                    }

                    // ManaQuadPlanes.ZY: axis=Z (trimmed), extrusion=Y (untouched, full height) -- the U
                    // axis (UV.x) needs remapping for both X walls instead.
                    foreach (var zyWall in new[] { zyPlane, wallXFar })
                    {
                        if (zyWall.Components[0] is MeshRendererComponent zyRenderer)
                        {
                            zyRenderer.Material.UvScale = new Vector2(uvSpan, 1.0f);
                            zyRenderer.Material.UvOffset = new Vector2(uvTrim, 0.0f);
                        }
                    }

                    // The four vertical wall-to-wall coves, each swept the full height and placed at its
                    // own corner. crossA/crossB are the two adjoining walls' own inward normals -- see
                    // ManaGridCoveFillet's doc comment for the general two-plane shape, and crossA x
                    // crossB = +Y is kept the same at every corner so all four sweep the same rotational
                    // direction and none end up wound backwards.
                    var corners = new (Vector3 Position, Vector3 CrossA, Vector3 CrossB, string Name)[]
                    {
                        (new Vector3(0, 0, 0), Vector3.UnitZ, Vector3.UnitX, "Grid Cove X0-Z0"),
                        (new Vector3(0, 0, gridSize), Vector3.UnitX, -Vector3.UnitZ, "Grid Cove X0-ZFar"),
                        (new Vector3(gridSize, 0, gridSize), -Vector3.UnitZ, -Vector3.UnitX, "Grid Cove XFar-ZFar"),
                        (new Vector3(gridSize, 0, 0), -Vector3.UnitX, Vector3.UnitZ, "Grid Cove XFar-Z0"),
                    };

                    for (var i = 0; i < corners.Length; i++)
                    {
                        var (position, crossA, crossB, name) = corners[i];
                        var cove = shapes.AddGridCoveFillet(
                            Vector3.UnitY, crossA, crossB, gridSize, coveRadius, position: corner + position, name: name);
                        cove.LevelSource = null;
                        wallCoves[i] = cove;
                    }
                }

                // Demo scenery, not level content: the shape factory tags everything it builds so placed
                // props round-trip, and these are the ones the game mode puts there itself. Left tagged
                // they would be saved into every level and then loaded a second time on top of the copies
                // this code always makes. See ManaLevelSource.
                xzPlane.LevelSource = null;
                ceilingPlane.LevelSource = null;
                yxPlane.LevelSource = null;
                zyPlane.LevelSource = null;
                wallZFar.LevelSource = null;
                wallXFar.LevelSource = null;
            }

            _gridXzPlane = xzPlane;
            _gridCeilingPlane = ceilingPlane;
            _gridYxPlane = yxPlane;
            _gridZyPlane = zyPlane;
            _gridWallZFar = wallZFar;
            _gridWallXFar = wallXFar;
            for (var i = 0; i < wallCoves.Length; i++)
            {
                _gridWallCoves[i] = wallCoves[i];
            }

            // Painted with whatever theme is active right now -- on the very first build that is whatever
            // BuildManaUi set up before Load ran; on a later live rebuild (see SyncGridRoomShape) it is
            // the theme just switched to.
            ApplyGridTheme(new[] { xzPlane, ceilingPlane, yxPlane, zyPlane, wallZFar, wallXFar }
                .Concat(wallCoves).ToArray());

            // Colliders stay full-size flat boxes regardless of the visual trim -- a voxel placement
            // raycast does not need the coves' few units of curvature to be accurate.
            //
            // Extended away from the room (not centred on the visual plane) and much thicker than a flush
            // 0.1 units -- a character falling from the standard 0.5-unit spawn clearance lands around 7-8
            // u/s, which alone covers ~0.13 units in a single 60 FPS physics step (more under any hitch,
            // since ManaPhysics.Update clamps as low as 30 FPS), comfortably tunnelling through a thin slab
            // before Bepu's narrow phase ever sees an overlap. Each box keeps the surface a
            // character/voxel-raycast actually touches exactly where it visually is, and only grows into
            // the space nobody stands on.
            const float ColliderThickness = 2.0f;
            Context.Physics.AddBoxBody(xzPlane, new Vector3(gridSize, ColliderThickness, gridSize), new Vector3(halfGridSize, -ColliderThickness * 0.5f, halfGridSize));
            Context.Physics.AddBoxBody(xzPlane, new Vector3(gridSize, ColliderThickness, gridSize), new Vector3(halfGridSize, gridSize + ColliderThickness * 0.5f, halfGridSize));
            Context.Physics.AddBoxBody(xzPlane, new Vector3(gridSize, gridSize, ColliderThickness), new Vector3(halfGridSize, halfGridSize, -ColliderThickness * 0.5f));
            Context.Physics.AddBoxBody(xzPlane, new Vector3(gridSize, gridSize, ColliderThickness), new Vector3(halfGridSize, halfGridSize, gridSize + ColliderThickness * 0.5f));
            Context.Physics.AddBoxBody(xzPlane, new Vector3(ColliderThickness, gridSize, gridSize), new Vector3(-ColliderThickness * 0.5f, halfGridSize, halfGridSize));
            Context.Physics.AddBoxBody(xzPlane, new Vector3(ColliderThickness, gridSize, gridSize), new Vector3(gridSize + ColliderThickness * 0.5f, halfGridSize, halfGridSize));

            // Ground/wall planes are raycast helpers for voxel placement, not meaningful selection targets.
            _pickingExclusions.Add(xzPlane);
            _pickingExclusions.Add(ceilingPlane);
            _pickingExclusions.Add(yxPlane);
            _pickingExclusions.Add(zyPlane);
            _pickingExclusions.Add(wallZFar);
            _pickingExclusions.Add(wallXFar);
            foreach (var cove in wallCoves)
            {
                if (cove != null)
                {
                    _pickingExclusions.Add(cove);
                }
            }

            _gridRoomRounded = rounded;
            _gridWallsShown = null;
        }

        /// <summary>
        /// The room's walls, ceiling and coves show while drawing voxels -- a voxel node selected or a voxel tool
        /// on -- or when Show Room is on. Otherwise only the floor is drawn; the walls' colliders stay regardless.
        /// </summary>
        private int _visualBoundsFrame;

        /// <summary>Every half second or so, refits colliders that follow a model's bounds the model has outgrown.</summary>
        private void SyncVisualBoundsColliders()
        {
            if (++_visualBoundsFrame % 30 == 0)
            {
                ManaCollisionBounds.RefitAll(Context.World);
            }
        }

        private void SyncGridRoomWalls()
        {
            var drawing = !IsPlaying && (Context.World.Selection.SelectedNode is ManaVoxelsNode || !_viewModel.IsSelectToolActive);
            var show = ManaEditorGizmos.ShowRoom || drawing;
            if (_gridWallsShown == show)
            {
                return;
            }

            _gridWallsShown = show;
            foreach (var node in new[] { _gridCeilingPlane, _gridYxPlane, _gridZyPlane, _gridWallZFar, _gridWallXFar }.Concat(_gridWallCoves))
            {
                foreach (var renderer in node?.GetComponents<MeshRendererComponent>() ?? [])
                {
                    renderer.IsVisible = show;
                }
            }
        }

        /// <summary>Tears down whatever <see cref="BuildGridRoom"/> last built, if anything. A no-op the first time.</summary>
        private void RemoveGridRoom()
        {
            var nodes = new[] { _gridXzPlane, _gridCeilingPlane, _gridYxPlane, _gridZyPlane, _gridWallZFar, _gridWallXFar }
                .Concat(_gridWallCoves)
                .Where(node => node != null)
                .Select(node => node!)
                .ToArray();

            if (nodes.Length == 0)
            {
                return;
            }

            lock (Context.World.SceneLock)
            {
                foreach (var node in nodes)
                {
                    foreach (var collision in node.GetComponents<ManaCollisionComponent>().ToArray())
                    {
                        Context.Physics.RemoveBody(collision.Body);
                    }

                    _pickingExclusions.Remove(node);
                    node.Parent?.RemoveChild(node);
                    Context.World.DefaultScene.Children.Remove(node);
                }
            }

            _gridXzPlane = null;
            _gridCeilingPlane = null;
            _gridYxPlane = null;
            _gridZyPlane = null;
            _gridWallZFar = null;
            _gridWallXFar = null;
            Array.Clear(_gridWallCoves);
        }

        /// <summary>
        /// Rebuilds the grid room the instant the active theme's rounded-ness (LCARS vs everything else)
        /// changes, so switching into or out of LCARS updates the room live instead of only ever deciding
        /// its shape once at startup -- which is what previously left a square room behind until the next
        /// full editor restart.
        /// </summary>
        private void SyncGridRoomShape()
        {
            var rounded = ManaUi?.Root?.Theme?.Kind == UiThemeKind.Lcars;
            if (_gridRoomRounded == rounded)
            {
                return;
            }

            BuildGridRoom(rounded);
        }
    }
}
