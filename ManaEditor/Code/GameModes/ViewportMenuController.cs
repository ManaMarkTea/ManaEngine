using Mana.Code.Nodes;
using ManaEditor.Code.UI;
using ManaUI;
using ManaEditor.Code.GridMaps;
using ManaEditor.Code.UI.ManaUi;
using Silk.NET.Input;

namespace Mana.Code.GameModes
{
    // The viewport's right-click menu (add a node where the pointer points) and the X key for snapping.
    internal sealed class ViewportMenuController(ManaContext context, VoxelEditorViewModel viewModel, HashSet<ManaSceneNode> pickingExclusions, Func<UiRoot?> rootProvider)
    {
        private const float RightClickSlopPixels = 5.0f;
        private const long RightClickMilliseconds = 500;

        private bool _contextRightWasDown;
        private bool _contextPressCounts;
        private Vector2 _contextPressedAt;
        private long _contextPressedTime;
        private bool _snapKeyWasDown;

        /// <summary>
        /// A right click in the viewport -- press and release without moving or flying -- opens the Add Node menu at
        /// the point under the pointer. A right drag stays the camera's look.
        /// </summary>
        public void UpdateViewportContextMenu(bool uiWantsMouse, bool isPlaying)
        {
            var mouse = context.Input.Mice.FirstOrDefault();
            if (mouse == null)
            {
                return;
            }

            var down = mouse.IsButtonPressed(MouseButton.Right);
            if (down && !_contextRightWasDown)
            {
                // Painting a grid map has its own right click (clearing a tile), and measuring owns the pointer.
                _contextPressCounts = !uiWantsMouse && !isPlaying && GridMapEditing.Target == null && !viewModel.Measure.Active;
                _contextPressedAt = mouse.Position;
                _contextPressedTime = Environment.TickCount64;
            }
            else if (down && _contextPressCounts &&
                     (Vector2.Distance(mouse.Position, _contextPressedAt) > RightClickSlopPixels || FlyKeyHeld()))
            {
                _contextPressCounts = false;
            }
            else if (!down && _contextRightWasDown && _contextPressCounts &&
                     Environment.TickCount64 - _contextPressedTime <= RightClickMilliseconds)
            {
                OpenViewportContextMenu(mouse.Position);
            }

            _contextRightWasDown = down;
        }

        private bool FlyKeyHeld()
        {
            var keyboard = context.Input.Keyboards.FirstOrDefault();
            return keyboard != null && new[] { Key.W, Key.A, Key.S, Key.D, Key.Q, Key.E }.Any(keyboard.IsKeyPressed);
        }

        private void OpenViewportContextMenu(Vector2 pointer)
        {
            if (rootProvider() is { } root && BuildViewportContextMenu(pointer) is { } items)
            {
                // The UI is laid out at the window size over the display scale; the pointer is in window pixels.
                MenuFlyout.ShowAt(root, pointer / MathF.Max(root.ScreenScale, 0.01f), items);
            }
        }

        private List<MenuItem>? BuildViewportContextMenu(Vector2 pointer)
        {
            if (context.World.MainCamera is not { } camera)
            {
                return null;
            }

            var ray = camera.ProjectScreenToWorld(new Vector3(pointer, 0.0f), new Vector3(pointer, 1.0f));
            var hit = ManaScenePicker.PickWithHit(context.World.Scenes, ray.Origin, ray.Direction, pickingExclusions);
            if (hit is { } backdrop && IsBackdrop(backdrop.Node))
            {
                // A parallax backdrop is scenery far behind: what is added belongs in front, with the level.
                hit = null;
            }
            var scene = TargetScene(hit?.Node);
            Vector3? normal = hit?.Normal;
            var position = hit?.Position ?? MissedPoint(scene, ray.Origin, ray.Direction, out normal);
            var selection = context.World.Selection;

            var items = new List<MenuItem>
            {
                MenuItem.Label("").TextFrom(() => $"Add to {SceneName(scene)}").ShortcutFrom(() => FormatPoint(position)),
                MenuItem.Separator()
            };

            // Which scene, chosen in a tick list that stays open, so the header above follows the choice.
            var scenes = context.World.Scenes.ToList();
            if (scenes.Count > 1)
            {
                var choose = new MenuItem("Change Scene");
                foreach (var candidate in scenes)
                {
                    choose.Items.Add(new MenuItem(SceneName(candidate), () => scene = candidate)
                        .Checkable(() => ReferenceEquals(scene, candidate))
                        .KeepsMenuOpen());
                }

                items.Add(choose);
                items.Add(MenuItem.Separator());
            }

            items.AddRange(SceneAddNodeMenu.Items(selection, () => scene, null, position, normal));
            return items;
        }

        /// <summary>
        /// For scripts: the viewport menu at a world point (projected to where the pointer would be), shown, or with
        /// <paramref name="item"/> that entry chosen at once and the new node logged.
        /// </summary>
        public void RunViewportMenuCommand(string? item, float[]? position)
        {
            if (position is not { Length: 3 } || context.World.MainCamera is not { } camera)
            {
                throw new InvalidDataException("viewport_menu: needs a world 'position' to open at.");
            }

            var window = new Vector2(context.Window.Size.X, context.Window.Size.Y);
            if (!ManaEditor.Code.UI.ManaUi.VoxelPlacementDepthOverlay.TryProject(camera._ViewMatrix * camera._ProjectionMatrix, window,
                    new Vector3(position[0], position[1], position[2]), out var pointer, out _))
            {
                throw new InvalidDataException("viewport_menu: that point is behind the camera.");
            }

            if (string.IsNullOrWhiteSpace(item))
            {
                OpenViewportContextMenu(pointer);
                return;
            }

            var entry = BuildViewportContextMenu(pointer)?.FirstOrDefault(candidate =>
                    string.Equals(candidate.Text, item, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"viewport_menu: no '{item}' in the menu.");
            entry.Invoke();
            var created = context.World.Selection.SelectedNode;
            ManaCore.Diagnostics.ManaLog.WriteDiagnostic(
                $"Command file: viewport_menu {item} -> {created?.Name} at {created?.Position} in {(created != null ? SceneOf(created)?.Name : null)}");
        }

        /// <summary>
        /// Where a new node goes: the scene of the level object clicked, else the selection's scene, else the open
        /// level -- never the editor's own Default Scene while a level is open, as nothing there is saved.
        /// </summary>
        private ManaScene TargetScene(ManaSceneNode? clicked)
        {
            var world = context.World;
            if (clicked != null && SceneOf(clicked) is { LevelPath: not null } clickedScene)
            {
                return clickedScene;
            }

            if (world.Selection.SelectedScene is { } selectedScene)
            {
                return selectedScene;
            }

            if (world.Selection.SelectedNode != null && SceneOf(world.Selection.SelectedNode) is { } selectedNodeScene)
            {
                return selectedNodeScene;
            }

            return world.Scenes.FirstOrDefault(scene => scene.LevelPath != null) ?? world.DefaultScene;
        }

        private static bool IsBackdrop(ManaSceneNode node)
        {
            for (var current = node; current != null; current = current.Parent)
            {
                if (current.Components.OfType<Mana.Code.Components.ManaParallaxComponent>().Any())
                {
                    return true;
                }
            }

            return false;
        }

        private ManaScene? SceneOf(ManaSceneNode node)
        {
            var root = node;
            while (root.Parent != null)
            {
                root = root.Parent;
            }

            return context.World.Scenes.FirstOrDefault(scene => scene.Children.Contains(root));
        }

        /// <summary>
        /// Nothing under the pointer (the editor floor is not pickable): on a 2D level its play plane, the Player
        /// Start's depth; otherwise the ground; else a little way in front of the camera.
        /// </summary>
        private static Vector3 MissedPoint(ManaScene scene, Vector3 origin, Vector3 direction, out Vector3? groundNormal)
        {
            groundNormal = null;
            if (PlayPlaneZ(scene) is { } planeZ && MathF.Abs(direction.Z) > 1e-4f)
            {
                var along = (planeZ - origin.Z) / direction.Z;
                if (along > 0.0f)
                {
                    return origin + direction * along;
                }
            }

            if (direction.Y < -1e-4f)
            {
                var distance = -origin.Y / direction.Y;
                if (distance is > 0.0f and < 1000.0f)
                {
                    // On the floor, so a shape sits on it rather than half sunk.
                    groundNormal = Vector3.UnitY;
                    return origin + direction * distance;
                }
            }

            return origin + direction * 10.0f;
        }

        private static float? PlayPlaneZ(ManaScene scene)
        {
            var pending = new Stack<ManaSceneNode>(scene.Children);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (node.Components.OfType<Mana.Code.Components.ManaPlatformer2DComponent>().Any())
                {
                    return node.WorldTransform.Translation.Z;
                }

                foreach (var child in node.Children)
                {
                    pending.Push(child);
                }
            }

            return null;
        }

        private static string SceneName(ManaScene scene) => string.IsNullOrWhiteSpace(scene.Name) ? "Scene" : scene.Name;

        private static string FormatPoint(Vector3 point) =>
            FormattableString.Invariant($"{point.X:0.#}, {point.Y:0.#}, {point.Z:0.#}");

        /// <summary>X turns grid snapping on or off, as Edit > Snap to Grid does.</summary>
        public void UpdateSnapShortcut(bool isPlaying, bool uiWantsKeyboard)
        {
            var keyboard = context.Input.Keyboards.FirstOrDefault();
            if (keyboard == null)
            {
                return;
            }

            var control = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight)
                || keyboard.IsKeyPressed(Key.SuperLeft) || keyboard.IsKeyPressed(Key.SuperRight);
            var keyDown = !uiWantsKeyboard && !isPlaying && !control && keyboard.IsKeyPressed(Key.X);
            if (keyDown && !_snapKeyWasDown)
            {
                viewModel.Snap.Enabled = !viewModel.Snap.Enabled;
                viewModel.Levels.SetStatus(viewModel.Snap.Enabled ? "Snap to grid on." : "Snap to grid off.");
            }

            _snapKeyWasDown = keyDown;
        }
    }
}
