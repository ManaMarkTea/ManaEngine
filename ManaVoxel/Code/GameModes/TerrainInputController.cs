using Mana.Code.Nodes;
using ManaVoxel.Code.Terrain;
using ManaVoxel.Code.UI;
using Silk.NET.Input;

namespace Mana.Code.GameModes
{
    // Sculpting a terrain (the Terrain window's brushes): while on it owns the pointer. Held left button dabs the brush
    // under the pointer every frame; Ctrl swaps raise and lower, Shift smooths. One stroke is one undo step.
    internal sealed class TerrainInputController(ManaContext context, VoxelEditorViewModel viewModel)
    {
        private ManaTerrainNode? _strokeTerrain;
        private (float[] Heights, byte[] Splat)? _strokeBefore;
        private float _strokeFlattenHeight;
        private bool _escapeWasDown;

        /// <summary>True while sculpting, so the click is the brush's and nothing gets selected.</summary>
        public bool UpdateSculpting(bool inputCaptured, IMouse mouse, bool isPlaying, bool uiWantsKeyboard)
        {
            var target = TerrainEditing.Target;
            if (target != null && (isPlaying || !ReferenceEquals(context.World.Selection.SelectedNode, target) || !IsInWorld(target)))
            {
                // Selecting something else, deleting the terrain or starting play all end sculpting.
                EndStroke();
                TerrainEditing.Stop();
                target = null;
            }

            if (target == null)
            {
                return false;
            }

            var keyboard = context.Input.Keyboards.FirstOrDefault();
            if (keyboard != null && !uiWantsKeyboard)
            {
                var escape = keyboard.IsKeyPressed(Key.Escape);
                if (escape && !_escapeWasDown)
                {
                    EndStroke();
                    TerrainEditing.Stop();
                    _escapeWasDown = true;
                    return false;
                }
                _escapeWasDown = escape;
            }

            var leftDown = mouse.IsButtonPressed(MouseButton.Left);
            var hit = inputCaptured ? null : PointerHit(target, mouse.Position);
            TerrainEditing.Hover = hit?.World;
            if (hit is not { } point)
            {
                if (!leftDown)
                {
                    EndStroke();
                }
                return true;
            }

            if (!leftDown)
            {
                EndStroke();
                return true;
            }

            if (_strokeTerrain == null)
            {
                _strokeTerrain = target;
                _strokeBefore = target.CaptureState();
                _strokeFlattenHeight = point.Local.Y;
            }

            var control = keyboard != null && (keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight));
            var shift = keyboard != null && (keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight));
            var brush = TerrainEditing.Brush;
            if (shift)
            {
                brush = ManaTerrainBrush.Smooth;
            }
            else if (control && brush == ManaTerrainBrush.Raise)
            {
                brush = ManaTerrainBrush.Lower;
            }
            else if (control && brush == ManaTerrainBrush.Lower)
            {
                brush = ManaTerrainBrush.Raise;
            }

            // Strength is a rate: the same stroke does the same at any frame rate.
            var seconds = (float)Math.Clamp(ManaTime.GlobalTime.DeltaTime.TotalSeconds, 0.0, 0.1);
            target.Apply(new ManaTerrainDab(brush, point.Local.X, point.Local.Z, TerrainEditing.Size,
                TerrainEditing.Strength * seconds * 6.0f, TerrainEditing.Falloff, _strokeFlattenHeight, TerrainEditing.Layer));
            return true;
        }

        /// <summary>
        /// The terrain script command: "sculpt"/"off" turn sculpting on the first terrain on and off; a brush name
        /// dabs it at a world <paramref name="position"/>, <paramref name="count"/> times, with <paramref name="settings"/>
        /// as [radius, strength, layer] (the window's values where missing) -- one undo step, as a stroke is.
        /// </summary>
        public void RunTerrainCommand(string? name, float[]? position, float[]? settings, int count)
        {
            var terrain = FindTerrain();
            if (terrain == null)
            {
                throw new InvalidDataException("terrain: the open levels have no terrain.");
            }

            switch (name?.ToLowerInvariant())
            {
                case "sculpt":
                    context.World.Selection.SelectedNode = terrain;
                    TerrainEditing.Begin(terrain);
                    return;
                case "off":
                    TerrainEditing.Stop();
                    return;
            }

            if (!Enum.TryParse<ManaTerrainBrush>(name, ignoreCase: true, out var brush))
            {
                throw new InvalidDataException($"terrain: '{name}' is not sculpt, off or a brush (raise, lower, smooth, flatten, noise, paint).");
            }

            if (position is not { Length: 3 } p || !Matrix4x4.Invert(terrain.WorldTransform, out var toLocal))
            {
                throw new InvalidDataException("terrain: a brush needs a world 'position' [x, y, z].");
            }

            var local = Vector3.Transform(new Vector3(p[0], p[1], p[2]), toLocal);
            var radius = TerrainEditing.Size;
            var strength = TerrainEditing.Strength;
            var layer = TerrainEditing.Layer;
            if (settings is { Length: > 0 })
            {
                radius = settings[0];
            }
            if (settings is { Length: > 1 })
            {
                strength = settings[1];
            }
            if (settings is { Length: > 2 })
            {
                layer = (int)settings[2];
            }

            var before = terrain.CaptureState();
            for (var i = 0; i < Math.Max(1, count); i++)
            {
                terrain.Apply(new ManaTerrainDab(brush, local.X, local.Z, radius, strength, TerrainEditing.Falloff, local.Y, layer));
            }
            var after = terrain.CaptureState();
            viewModel.History.Record("Sculpt terrain", () => terrain.RestoreState(before), () => terrain.RestoreState(after));
        }

        private void EndStroke()
        {
            var terrain = _strokeTerrain;
            var before = _strokeBefore;
            _strokeTerrain = null;
            _strokeBefore = null;
            if (terrain == null || before is not { } was)
            {
                return;
            }

            var after = terrain.CaptureState();
            if (was.Heights.AsSpan().SequenceEqual(after.Heights) && was.Splat.AsSpan().SequenceEqual(after.Splat))
            {
                return;
            }

            viewModel.History.Record("Sculpt terrain", () => terrain.RestoreState(was), () => terrain.RestoreState(after));
        }

        private (Vector3 Local, Vector3 World)? PointerHit(ManaTerrainNode terrain, Vector2 pointer)
        {
            var camera = context.World.MainCamera;
            if (camera == null || !Matrix4x4.Invert(terrain.WorldTransform, out var toLocal))
            {
                return null;
            }

            var ray = camera.ProjectScreenToWorld(new Vector3(pointer, 0.0f), new Vector3(pointer, 1.0f));
            var origin = Vector3.Transform(ray.Origin, toLocal);
            var direction = Vector3.TransformNormal(ray.Direction, toLocal);
            if (terrain.Raycast(origin, direction) is not { } local)
            {
                return null;
            }

            return (local, Vector3.Transform(local, terrain.WorldTransform));
        }

        private ManaTerrainNode? FindTerrain()
        {
            if (context.World.Selection.SelectedNode is ManaTerrainNode selected)
            {
                return selected;
            }

            lock (context.World.SceneLock)
            {
                return context.World.Scenes.SelectMany(scene => scene.Children).SelectMany(Walk).OfType<ManaTerrainNode>().FirstOrDefault();
            }
        }

        private static IEnumerable<ManaSceneNode> Walk(ManaSceneNode node)
        {
            yield return node;
            foreach (var child in node.Children)
            {
                foreach (var below in Walk(child))
                {
                    yield return below;
                }
            }
        }

        private bool IsInWorld(ManaSceneNode node)
        {
            var root = node;
            while (root.Parent != null)
            {
                root = root.Parent;
            }

            lock (context.World.SceneLock)
            {
                return context.World.Scenes.Any(scene => scene.Children.Contains(root));
            }
        }
    }
}
