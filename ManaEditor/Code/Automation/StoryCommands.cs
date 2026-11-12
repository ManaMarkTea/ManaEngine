using Mana;
using Mana.Code.Automation;
using Mana.Code.Components;
using Mana.Code.Models;
using Mana.Code.Nodes;
using ManaEditor.Code.Assets;

namespace ManaEditor.Code.Automation
{
    /// <summary>
    /// The command-file commands that stage a story (Tools/Stories): <c>place</c> props, characters and prefabs,
    /// <c>animate</c> them, <c>walk</c> them somewhere, <c>face</c> them toward a point, <c>remove</c> them, and hold a
    /// <c>title</c> card in front of the camera. Everything they make lives in a "Story" scene of its own, marked
    /// editor-transient, so no level ever saves it.
    /// </summary>
    public static class StoryCommands
    {
        public const string SceneName = "Story";

        /// <summary>Walking pace when a walk names no frames: an unhurried 1.4 m/s at 30 frames a second.</summary>
        public const float WalkSpeed = 1.4f;

        public static void Register(ManaCommandRunner runner)
        {
            var context = runner.Context;

            runner.Register("place", command =>
            {
                var name = RequireName(command, "place");
                var asset = ManaCommandRunner.RequirePath(command);
                if (runner.FindNode(name) is { } existing)
                {
                    Remove(existing);
                }
                var placed = Place(context, StoryScene(context), asset, name, command)
                    ?? throw new InvalidDataException($"place: '{asset}' could not be loaded.");
                ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Command file: placed {name} ({asset}) at {placed.Position}.");
            });

            runner.Register("animate", command =>
            {
                var node = RequireStoryNode(runner, command, "animate");
                var clip = command.Clip ?? throw new InvalidDataException("animate: needs a clip.");
                if (!Play(node, clip, command.Loop ?? true, command.Speed ?? 1.0f))
                {
                    throw new InvalidDataException($"animate: {node.Name} has no clip '{clip}' (it has {string.Join(", ", ClipNames(node))}).");
                }
            });

            runner.Register("walk", (command, _) =>
            {
                var node = RequireStoryNode(runner, command, "walk");
                if (command.Position is not { Length: 3 } spot)
                {
                    throw new InvalidDataException("walk: needs a position to walk to.");
                }
                var from = node.Position;
                var to = new Vector3(spot[0], spot[1], spot[2]);
                var frames = command.Frames;
                if (frames <= 0)
                {
                    frames = Math.Max(1, (int)MathF.Round(Vector3.Distance(from, to) / WalkSpeed * 30.0f));
                }
                TurnToward(node, to);
                Play(node, command.Clip ?? "walk", true, command.Speed ?? 1.0f);
                runner.AddLinearGlide(share =>
                {
                    node.Position = Vector3.Lerp(from, to, share);
                    if (share >= 1.0f)
                    {
                        Play(node, "idle", true, 1.0f);
                    }
                }, frames, !command.Background);
                if (command.Background)
                {
                    return ManaCommandFlow.Next;
                }
                return ManaCommandFlow.Pause;
            });

            runner.Register("face", (command, _) =>
            {
                var node = RequireStoryNode(runner, command, "face");
                if (command.Target is not { Length: 3 } point)
                {
                    throw new InvalidDataException("face: needs a target point.");
                }
                var fromYaw = node.Rotation.Y;
                var toYaw = YawToward(node.Position, new Vector3(point[0], point[1], point[2]), fromYaw);
                // The short way round.
                var turn = MathF.IEEERemainder(toYaw - fromYaw, MathF.Tau);
                if (command.Frames <= 0)
                {
                    node.Rotation = node.Rotation with { Y = fromYaw + turn };
                    return ManaCommandFlow.Next;
                }
                runner.AddGlide(eased => node.Rotation = node.Rotation with { Y = fromYaw + turn * eased }, command.Frames, !command.Background);
                if (command.Background)
                {
                    return ManaCommandFlow.Next;
                }
                return ManaCommandFlow.Pause;
            });

            runner.Register("remove", command =>
            {
                // "*" clears the whole stage -- a story does before each new set.
                if (command.Name == "*")
                {
                    var scene = FindStoryScene(context);
                    if (scene != null)
                    {
                        foreach (var child in scene.Children.ToList())
                        {
                            Remove(child);
                        }
                    }
                    return;
                }
                Remove(RequireStoryNode(runner, command, "remove"));
            });

            runner.Register("title", command =>
            {
                var name = command.Name ?? "Title";
                if (runner.FindNode(name) is { } existing)
                {
                    Remove(existing);
                }
                if (command.Visible == false)
                {
                    return;
                }
                var picture = ManaCommandRunner.RequirePath(command);
                var size = ManaCore.Imaging.ManaImageCodec.Size(global::Mana.ManaSpritePictures.Resolve(picture) ?? picture)
                    ?? throw new InvalidDataException($"title: '{picture}' is not a picture.");
                var node = new ManaSceneNode(context, Matrix4x4.Identity) { Name = name, IsEditorTransient = true };
                var sprite = new ManaSpriteComponent(node) { Texture = picture, Pivot = new Vector2(0.5f, 0.5f), Unlit = true };
                node.Components.Add(sprite);
                var screen = Vector2.Zero;
                if (command.Position is { Length: >= 2 } at)
                {
                    screen = new Vector2(at[0], at[1]);
                }
                node.Components.Add(new StoryTitleComponent(node, sprite, size.Height, command.Scale ?? 0.12f, screen));
                var stage = StoryScene(context);
                lock (context.World.SceneLock)
                {
                    stage.Children.Add(node);
                }
            });
        }

        /// <summary>The scene the story's things go in, made the first time (and again after a level change took it).</summary>
        public static ManaScene StoryScene(ManaContext context)
        {
            if (FindStoryScene(context) is { } found)
            {
                return found;
            }
            var scene = new ManaScene(context) { Name = SceneName };
            lock (context.World.SceneLock)
            {
                context.World.Scenes.Add(scene);
            }
            return scene;
        }

        private static ManaScene? FindStoryScene(ManaContext context)
        {
            lock (context.World.SceneLock)
            {
                return context.World.Scenes.FirstOrDefault(scene => scene.Name == SceneName && scene.LevelPath == null);
            }
        }

        /// <summary>A model (a repo or <c>zip:</c> path) or a prefab (<c>.manaprefab</c>, a content path), placed by the command.</summary>
        public static ManaSceneNode? Place(ManaContext context, ManaScene scene, string asset, string name, ManaCommand command)
        {
            var position = Vector3.Zero;
            if (command.Position is { Length: 3 } p)
            {
                position = new Vector3(p[0], p[1], p[2]);
            }
            var yaw = (command.Yaw ?? 0.0f) * MathF.PI / 180.0f;
            var scale = command.Scale ?? 1.0f;

            ManaSceneNode? node;
            if (asset.EndsWith(".manaprefab", StringComparison.OrdinalIgnoreCase))
            {
                var placement = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(position);
                node = ManaPrefabs.Spawn(context, scene, asset, placement, name);
            }
            else
            {
                node = ManaMeshAssetLoader.For(context.World).Load(asset, scene);
                node.Name = name;
                node.Position = position;
                node.Rotation = new Vector3(0.0f, yaw, 0.0f);
                node.Scale = new Vector3(scale);
                node.UpdateTransform();
            }
            if (node == null)
            {
                return null;
            }
            foreach (var part in Walk(node))
            {
                part.IsEditorTransient = true;
            }
            // A character stands at ease until told otherwise, rather than in its T-pose.
            Play(node, "idle", true, 1.0f);
            return node;
        }

        /// <summary>Plays a clip on whatever animates beneath the node; false when nothing there has that clip.</summary>
        public static bool Play(ManaSceneNode node, string clip, bool loop, float speed)
        {
            var animation = Animation(node);
            if (animation == null || !animation.HasClip(clip))
            {
                return false;
            }
            animation.Looping = loop;
            animation.PlaybackSpeed = speed;
            animation.IsPlaying = true;
            return animation.Play(clip, restart: false);
        }

        public static IEnumerable<string> ClipNames(ManaSceneNode node) =>
            Animation(node)?.Clips.Select(clip => clip.Name) ?? [];

        private static ManaSkeletalAnimationComponent? Animation(ManaSceneNode node) =>
            Walk(node).SelectMany(part => part.Components).OfType<ManaSkeletalAnimationComponent>().FirstOrDefault();

        /// <summary>The yaw that turns a model facing +Z (as Kenney's are) from <paramref name="from"/> toward a point.</summary>
        public static float YawToward(Vector3 from, Vector3 to, float otherwise)
        {
            var direction = to - from;
            if (direction.X * direction.X + direction.Z * direction.Z < 1e-6f)
            {
                return otherwise;
            }
            return MathF.Atan2(direction.X, direction.Z);
        }

        private static void TurnToward(ManaSceneNode node, Vector3 point)
        {
            node.Rotation = node.Rotation with { Y = YawToward(node.Position, point, node.Rotation.Y) };
        }

        // Despawn ends scripts, frees colliders and sounds, and detaches -- right for a model as for a prefab.
        private static void Remove(ManaSceneNode node) => ManaPrefabs.Despawn(node);

        private static string RequireName(ManaCommand command, string type)
        {
            if (string.IsNullOrWhiteSpace(command.Name))
            {
                throw new InvalidDataException($"{type}: needs a name.");
            }
            return command.Name;
        }

        private static ManaSceneNode RequireStoryNode(ManaCommandRunner runner, ManaCommand command, string type)
        {
            return runner.FindNode(RequireName(command, type))
                ?? throw new InvalidDataException($"{type}: nothing named '{command.Name}' (place it first).");
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
    }

    /// <summary>
    /// Keeps a title card just in front of the camera, facing it, at a fixed place and height on screen -- drawn in the
    /// scene (so the retro filter pixelates it with everything else), not over it like the HUD.
    /// </summary>
    public sealed class StoryTitleComponent : ManaComponent
    {
        private readonly ManaSpriteComponent _sprite;
        private readonly int _pictureHeight;
        private readonly float _screenHeight;
        private readonly Vector2 _screen;

        public StoryTitleComponent(ManaSceneNode owner, ManaSpriteComponent sprite, int pictureHeight, float screenHeight, Vector2 screen)
            : base(owner)
        {
            Name = "Story Title";
            _sprite = sprite;
            _pictureHeight = Math.Max(1, pictureHeight);
            _screenHeight = Math.Clamp(screenHeight, 0.01f, 2.0f);
            _screen = screen;
        }

        public override bool UpdatesBeforeNodeTransform => true;

        public override void UpdateComponent()
        {
            var camera = Owner.Context.World.MainCamera;
            if (camera == null)
            {
                return;
            }
            var eye = camera.Owner;
            // Just past the near plane, so nothing in the scene comes between the card and the lens.
            var distance = camera.NearClipPlane * 4.0f;
            var halfHeight = distance * MathF.Tan(camera.EffectiveFieldOfViewDegrees * MathF.PI / 360.0f);
            var halfWidth = halfHeight * camera.AspectRatio;
            Owner.Position = eye.Position + eye.Forward * distance + eye.Right * (_screen.X * halfWidth) + eye.Up * (_screen.Y * halfHeight);
            Owner.Rotation = eye.Rotation;
            var pixelsPerUnit = _pictureHeight / (_screenHeight * 2.0f * halfHeight);
            if (MathF.Abs(_sprite.PixelsPerUnit - pixelsPerUnit) > pixelsPerUnit * 0.001f)
            {
                _sprite.PixelsPerUnit = pixelsPerUnit;
            }
        }
    }
}
