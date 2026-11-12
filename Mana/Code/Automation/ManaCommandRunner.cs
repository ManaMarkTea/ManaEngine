using Mana.Code.Nodes;
using ManaCore.Diagnostics;
using ManaCore.Graphics;

namespace Mana.Code.Automation
{
    /// <summary>What a command tells the runner to do once it has run.</summary>
    public enum ManaCommandFlow
    {
        /// <summary>Done; run the next command this frame.</summary>
        Next,

        /// <summary>Done; run the next command on the next frame.</summary>
        Pause,

        /// <summary>Not finished; run this same command again next frame.</summary>
        Retry
    }

    public delegate ManaCommandFlow ManaCommandHandler(ManaCommand command, ManaCommandRunner runner);

    /// <summary>
    /// Drives an app from a <c>--command-file</c> JSON script instead of interactive input: move the camera,
    /// screenshot, quit. The commands every app can do live here; an app adds the ones only it can do with
    /// <see cref="Register(string, ManaCommandHandler)"/> (the voxel editor's open_level, painting grid maps, and so on).
    /// <para>
    /// One command is "in flight" at a time; <see cref="Tick"/> is called once per rendered, past-loading frame.
    /// </para>
    /// </summary>
    public sealed class ManaCommandRunner
    {
        private readonly Dictionary<string, ManaCommandHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);
        private readonly System.Diagnostics.Stopwatch _profileClock = new();
        private readonly List<ManaCommand> _commands;
        private readonly Func<string, bool, Task>? _captureFrame;
        private readonly Action<string>? _dumpEnvironment;

        private int _index;
        private int _waitFramesRemaining;
        private Task? _pendingScreenshot;

        // record .. stop_record: every frame saved as <folder>/00000.jpg.. at a fixed step, for ffmpeg to join.
        private string? _recordFolder;
        private bool _recordClean;
        private bool _recordPaused;
        private int _recordFrame;
        private readonly List<Task> _recordTasks = new();

        // Glides under way (move_camera or sky with frames): each steps once a frame with an eased 0..1, and the
        // script holds until the blocking ones finish; background ones run on alongside what follows.
        private readonly List<(Action<float> Apply, int Frames, bool Blocking)> _glides = new();
        private readonly List<int> _glideSteps = new();

        /// <param name="commandFilePath">Path to the JSON command script to load.</param>
        /// <param name="context">The application context used to execute commands.</param>
        /// <param name="captureFrame">Saves the next frame to a path (path, without the UI); null where the backend cannot.</param>
        /// <param name="dumpEnvironment">Saves the reflection cube's faces; null where the backend cannot.</param>
        public ManaCommandRunner(
            string commandFilePath,
            ManaContext context,
            Func<string, bool, Task>? captureFrame = null,
            Action<string>? dumpEnvironment = null)
        {
            Context = context;
            _captureFrame = captureFrame;
            _dumpEnvironment = dumpEnvironment;
            _commands = ManaCommandFile.Load(commandFilePath);
            RegisterBuiltIns();
        }

        public ManaContext Context { get; }

        /// <summary>Adds (or replaces) a command by its <c>type</c> name.</summary>
        public ManaCommandRunner Register(string type, ManaCommandHandler handler)
        {
            _handlers[type] = handler;
            return this;
        }

        /// <summary>Registers a command that does its work and moves on.</summary>
        public ManaCommandRunner Register(string type, Action<ManaCommand> action)
        {
            return Register(type, (command, _) =>
            {
                action(command);
                return ManaCommandFlow.Next;
            });
        }

        public void Tick()
        {
            CaptureRecordedFrame();
            if (_recordFolder == null && _recordTasks.Count > 0)
            {
                // Stopped: hold until the last frames are written (their readback needs this thread's polling).
                _recordTasks.RemoveAll(task => task.IsCompleted);
                if (_recordTasks.Count > 0)
                {
                    return;
                }
                ManaLog.WriteDiagnostic($"Command file: recorded {_recordFrame} frames");
            }

            if (StepGlides())
            {
                return;
            }

            if (_waitFramesRemaining > 0)
            {
                _waitFramesRemaining--;
                return;
            }

            if (_pendingScreenshot != null)
            {
                if (!_pendingScreenshot.IsCompleted)
                {
                    return;
                }

                if (_pendingScreenshot.Exception != null)
                {
                    var error = _pendingScreenshot.Exception.InnerException?.Message
                        ?? _pendingScreenshot.Exception.Message;
                    ManaLog.WriteDiagnostic($"Command file: screenshot failed: {error}");
                }

                _pendingScreenshot = null;
                Mana.Code.UI.ManaEditorGizmos.FullWindowView = false;
                _index++;
            }

            while (_index < _commands.Count)
            {
                var command = _commands[_index];
                if (!_handlers.TryGetValue(command.Type, out var handler))
                {
                    ManaLog.WriteDiagnostic($"Command file: unknown command type '{command.Type}', skipping.");
                    _index++;
                    continue;
                }

                var flow = handler(command, this);
                if (_pendingScreenshot == null && flow != ManaCommandFlow.Retry)
                {
                    _index++;
                }

                // A screenshot in flight keeps its index; the check above steps past it when the capture ends.
                if (flow != ManaCommandFlow.Next)
                {
                    return;
                }
            }
        }

        // ----- helpers for commands -----------------------------------------------------------------

        public static string RequirePath(ManaCommand command)
        {
            if (string.IsNullOrWhiteSpace(command.Path))
            {
                throw new InvalidDataException($"Command file: '{command.Type}' is missing its required 'path'.");
            }

            return command.Path;
        }

        /// <summary>The named node in any scene (an opened level loads into a scene of its own); "A/B" is B under A.</summary>
        public ManaSceneNode? FindNode(string? name)
        {
            return string.IsNullOrEmpty(name)
                ? null
                : Context.World.Scenes.Select(scene => FindNode(scene.Children, name)).FirstOrDefault(found => found != null);
        }

        public static ManaSceneNode? FindNode(IEnumerable<ManaSceneNode> nodes, string name)
        {
            // "Red Lamp/Bulb": the Bulb under Red Lamp, where every lamp has one.
            var slash = name.IndexOf('/');
            if (slash > 0)
            {
                return FindNode(nodes, name[..slash]) is { } parent ? FindNode(parent.Children, name[(slash + 1)..]) : null;
            }

            foreach (var node in nodes)
            {
                if (node.Name == name)
                {
                    return node;
                }

                if (FindNode(node.Children, name) is { } child)
                {
                    return child;
                }
            }

            return null;
        }

        public static IEnumerable<ManaSceneNode> Walk(ManaSceneNode root)
        {
            yield return root;
            foreach (var child in root.Children)
            {
                foreach (var descendant in Walk(child))
                {
                    yield return descendant;
                }
            }
        }

        /// <summary>Runs <paramref name="apply"/> over <paramref name="frames"/> frames with an eased 0..1.</summary>
        public void AddGlide(Action<float> apply, int frames, bool blocking)
        {
            _glides.Add((apply, frames, blocking));
            _glideSteps.Add(0);
        }

        /// <summary>
        /// Like <see cref="AddGlide"/>, but <paramref name="apply"/> gets the plain 0..1 share of the frames gone (a walk
        /// keeps an even pace) rather than the eased one.
        /// </summary>
        public void AddLinearGlide(Action<float> apply, int frames, bool blocking)
        {
            AddGlide(eased => apply(Uneased(eased)), frames, blocking);
        }

        // Undoes smoothstep (t * t * (3 - 2t)), which StepGlides applies: the inverse is 0.5 - sin(asin(1 - 2y) / 3).
        private static float Uneased(float eased)
        {
            var y = Math.Clamp(eased, 0.0f, 1.0f);
            return 0.5f - MathF.Sin(MathF.Asin(1.0f - 2.0f * y) / 3.0f);
        }

        // ----- the commands every app has -------------------------------------------------------------

        private void RegisterBuiltIns()
        {
            Register("audio_test", _ =>
            {
                var audio = Context.Audio;
                var voice = audio.Play(ManaAudio.ManaAudioDiagnostics.TestTone);
                ManaLog.WriteDiagnostic($"Command file: audio available {audio.IsAvailable}, unlocked {audio.IsUnlocked}, volume {audio.MasterVolume}, tone playing {voice.IsPlaying}");
            });
            Register("audio_status", _ =>
            {
                var sources = Context.World.Scenes.SelectMany(scene => scene.Children).SelectMany(Walk)
                    .SelectMany(node => node.Components).OfType<Mana.Code.Components.ManaAudioSourceComponent>().ToList();
                ManaLog.WriteDiagnostic($"Command file: audio sources {sources.Count}, playing {sources.Count(source => source.Voice?.IsPlaying == true)}, load errors {sources.Count(source => source.LoadError != null)}, master volume {Context.Audio.MasterVolume}");
                foreach (var source in sources.Where(source => source.LoadError != null))
                    ManaLog.WriteDiagnostic($"Command file: audio error {source.Owner.Name}: {source.LoadError}");
            });

            Register("set_theme", command =>
            {
                if (!Enum.TryParse<ManaUI.UiThemeKind>(command.Name?.Replace("Windows 3.1", "Windows31", StringComparison.OrdinalIgnoreCase).Replace("Windows 11", "Windows11", StringComparison.OrdinalIgnoreCase), true, out var kind) || !Enum.IsDefined(kind))
                {
                    throw new InvalidDataException("Command file: 'set_theme' requires a theme name: Dark, Light, LCARS, Kenney, Windows31, Windows11 or MacOS.");
                }
                var root = Context.GameMode?.ManaUi?.Root
                    ?? throw new InvalidOperationException("Command file: 'set_theme' requires an active ManaUI root.");
                root.RequestTheme(kind);
            });

            Register("wait", (command, _) =>
            {
                _waitFramesRemaining = Math.Max(0, command.Frames);
                return ManaCommandFlow.Pause;
            });

            Register("move_camera", (command, _) =>
            {
                if (command.Fov is { } fov)
                {
                    Context.World.MainCamera.FieldOfViewDegrees = Math.Clamp(fov, 1.0f, 150.0f);
                }
                if (command.Frames > 0 && command.Position is { Length: 3 } && command.Target is { Length: 3 })
                {
                    StartCameraGlide(command);
                    return command.Background ? ManaCommandFlow.Next : ManaCommandFlow.Pause;
                }
                ApplyMoveCamera(command);
                return ManaCommandFlow.Next;
            });

            Register("teleport_player", command =>
            {
                // Straight to a spot while playing, to look at a stretch of a level without walking there.
                if (command.Position is { Length: 3 } spot && Context.World.GameState.Player is { } player)
                {
                    player.TeleportTo(new Vector3(spot[0], spot[1], spot[2]));
                }
            });

            Register("walk_player", command =>
            {
                // Walks the character to a spot as a click-to-move would, turning it to face the way -- to watch it move.
                if (command.Position is { Length: 3 } spot && Context.World.GameState.Player is { } walker)
                {
                    walker.WalkTo(new Vector3(spot[0], spot[1], spot[2]));
                }
            });

            Register("log_player", command =>
            {
                // Where the player is and whether it stands, for a scripted check of a level's footing.
                if (Context.World.GameState.Player is { } logged)
                {
                    ManaLog.WriteDiagnostic($"Command file: player {command.Name} at {logged.Body?.FootPosition ?? logged.Position}, grounded {logged.IsGrounded}");
                }
            });

            Register("debug_view", command =>
            {
                // A PBR debug view by name ("Light Count") or number; "Off" or no name goes back to the shaded picture.
                var names = ManaCore.Graphics.ManaIrradianceSettings.DebugModeNames;
                var mode = 0;
                if (!string.IsNullOrWhiteSpace(command.Name) && !int.TryParse(command.Name, out mode))
                {
                    mode = Array.FindIndex(names, name => string.Equals(name, command.Name, StringComparison.OrdinalIgnoreCase));
                }
                if (mode < 0 || mode >= names.Length)
                {
                    throw new InvalidDataException($"debug_view: '{command.Name}' is not one of {string.Join(", ", names)}.");
                }
                Context.World.IrradianceSettings.WebGpuDebugMode = mode;
            });

            Register("wind", command =>
            {
                // position: [speed m/s, direction in degrees clockwise from -Z, gustiness 0..1]; any can be left off.
                var windSettings = Context.World.IrradianceSettings;
                if (command.Position is { Length: > 0 } windValues)
                {
                    windSettings.WindSpeed = windValues[0];
                    if (windValues.Length > 1)
                    {
                        windSettings.WindDirection = windValues[1];
                    }
                    if (windValues.Length > 2)
                    {
                        windSettings.Gustiness = windValues[2];
                    }
                }
            });

            Register("sky", (command, _) => ApplySky(command));

            Register("emission", command =>
            {
                // Emission on every mesh under the named node: colour from position, strength from frames/100.
                if (FindNode(command.Name) is { } glowing && command.Position is { Length: 3 } glow)
                {
                    foreach (var renderer in Walk(glowing).SelectMany(n => n.Components).OfType<global::Mana.MeshRendererComponent>())
                    {
                        renderer.Material.Emission.Value = new Vector4(glow[0], glow[1], glow[2], 1.0f);
                        renderer.Material.EmissionStrength = command.Frames / 100.0;
                    }
                }
            });

            Register("material", command =>
            {
                // An optional ZIP preset, then metallic/roughness (position x, y), on each mesh under the named node.
                if (FindNode(command.Name) is { } surfaced)
                {
                    foreach (var renderer in Walk(surfaced).SelectMany(n => n.Components).OfType<global::Mana.MeshRendererComponent>())
                    {
                        if (!string.IsNullOrWhiteSpace(command.Path))
                        {
                            Mana.Code.Materials.ManaMaterialLoaderZip.ApplyTo(renderer.DynamicMaterial, command.Path);
                        }
                        if (command.Position is { Length: >= 2 } surface)
                        {
                            renderer.Material.Metallic.Value = surface[0];
                            renderer.Material.Roughness.Value = surface[1];
                        }
                    }
                }
            });

            // The reflection cube's six faces as <path>.face0..5.png, after the next frame.
            Register("dump_environment", command => _dumpEnvironment?.Invoke(RequirePath(command)));

            Register("screenshot", (command, _) =>
            {
                if (_captureFrame == null)
                {
                    ManaLog.WriteDiagnostic(
                        "Command file: screenshot skipped, no WebGPU context (only the WebGPU backend supports it).");
                    return ManaCommandFlow.Next;
                }

                // A clean shot fills the window, so it is centred on the window, not the viewport the panels
                // leave: switched over, then a frame for the camera to re-project before the capture.
                if (command.Clean && !Mana.Code.UI.ManaEditorGizmos.FullWindowView)
                {
                    Mana.Code.UI.ManaEditorGizmos.FullWindowView = true;
                    return ManaCommandFlow.Retry;
                }
                _pendingScreenshot = _captureFrame(RequirePath(command), command.Clean);
                return ManaCommandFlow.Pause;
            });

            Register("record", command =>
            {
                // Path = folder (omit it while recording to just change clean); Frames = frames a second (30).
                if (command.Path != null)
                {
                    Directory.CreateDirectory(command.Path);
                    _recordFolder = command.Path;
                    _recordFrame = 0;
                    ManaTime.FixedFrameSeconds = 1.0 / (command.Frames > 0 ? command.Frames : 30);
                }
                _recordClean = command.Clean;
                _recordPaused = command.Visible == false;
                Mana.Code.UI.ManaEditorGizmos.FullWindowView = _recordClean;
            });

            Register("stop_record", _ =>
            {
                _recordFolder = null;
                ManaTime.FixedFrameSeconds = null;
                Mana.Code.UI.ManaEditorGizmos.FullWindowView = false;
            });

            Register("gizmos", command =>
            {
                Mana.Code.UI.ManaEditorGizmos.Visible = command.Visible ?? !Mana.Code.UI.ManaEditorGizmos.Visible;
            });

            Register("ambient_occlusion", command =>
            {
                var settings = Context.World.IrradianceSettings;
                if (command.Value is { } strength)
                {
                    settings.AmbientOcclusion = strength;
                }
                if (command.Radius is { } radius)
                {
                    settings.AmbientOcclusionRadius = radius;
                }
            });

            // Off, Fxaa, Smaa or Msaa (ManaAntiAliasing).
            // The retro screen filter over whatever is open: name a look, count its lines; visible false takes it off again.
            Register("retro", command =>
            {
                if (command.Visible == false)
                {
                    Context.World.RetroFilterOverride = null;
                    return;
                }
                var look = ManaCore.Graphics.ManaRetroFilterSettings.Preset(command.Name)
                    ?? throw new InvalidOperationException($"retro: '{command.Name}' is not one of {string.Join(", ", ManaCore.Graphics.ManaRetroFilterSettings.PresetNames)}.");
                if (command.Count > 0)
                {
                    look = look with { Lines = command.Count };
                }
                Context.World.RetroFilterOverride = look;
            });

            Register("anti_aliasing", command =>
            {
                if (!Enum.TryParse<ManaCore.Graphics.ManaAntiAliasing>(command.Name, true, out var mode))
                {
                    throw new InvalidOperationException($"anti_aliasing: '{command.Name}' is not one of {string.Join(", ", Enum.GetNames<ManaCore.Graphics.ManaAntiAliasing>())}.");
                }
                Context.World.IrradianceSettings.AntiAliasing = mode;
            });

            // Scene Inspector > Lighting's sliders: name exposure, ibl or light, and its value.
            Register("lighting", command =>
            {
                var settings = Context.World.IrradianceSettings;
                var value = command.Value ?? throw new InvalidOperationException("lighting needs a value.");
                switch (command.Name?.ToLowerInvariant())
                {
                    case "exposure":
                        settings.Exposure = value;
                        break;
                    case "ibl":
                        settings.IblIntensity = value;
                        break;
                    case "light":
                        settings.LightIntensity = value;
                        break;
                    default:
                        throw new InvalidOperationException($"lighting: unknown setting '{command.Name}' (exposure, ibl, light).");
                }
            });

            Register("profile_reset", _ =>
            {
                ManaFrameProfiler.Reset();
                _profileClock.Restart();
            });

            Register("profile_dump", command => WriteProfileReport(RequirePath(command)));

            // Live GPU object counts by kind, and what moved since the last call: a count that keeps climbing while the
            // scene sits still is a leak. Desktop/mobile only; the browser cannot list what it holds.
            Register("gpu_report", command =>
            {
                var counts = ManaGpuResourceReport.Capture();
                if (counts == null)
                {
                    ManaLog.WriteDiagnostic("Command file: gpu report unavailable on this backend");
                    return;
                }

                ManaLog.WriteDiagnostic($"Command file: gpu live {ManaGpuResourceReport.Format(counts)}");
                if (_lastGpuReport != null)
                {
                    var changes = ManaGpuResourceReport.Changes(_lastGpuReport, counts);
                    ManaLog.WriteDiagnostic(changes.Count == 0
                        ? "Command file: gpu unchanged since the last report"
                        : "Command file: gpu changed " + string.Join(", ", changes.Select(change => $"{change.Key} {change.Value:+#;-#;0}")));
                }

                _lastGpuReport = counts;
                if (!string.IsNullOrEmpty(command.Path))
                {
                    File.WriteAllLines(command.Path, counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));
                }
            });

            Register("quit", (_, _) =>
            {
                Context.Window.Close();
                return ManaCommandFlow.Pause;
            });
        }

        private ManaCommandFlow ApplySky(ManaCommand command)
        {
            // The sky mode by name; a time of day holds the cycle still there, for a screenshot.
            var sky = Context.World.IrradianceSettings;
            if (Enum.TryParse<ManaSkyMode>(command.Name, true, out var skyMode))
            {
                sky.Sky = skyMode;
            }
            if (command.Visible is { } lines)
            {
                sky.ConstellationLines = lines;
            }
            if (command.SkyDateTime is { } skyDateTime)
            {
                sky.SkyDateTime = skyDateTime;
                sky.TimeSpeed = 0.0f;
                sky.RealTime = false;
                sky.TimeOfDay = sky.DaySystem == ManaSkyDaySystem.Sidereal
                    ? ManaSky.SiderealPhaseAt(skyDateTime)
                    : ManaSky.PhaseAt(skyDateTime.TimeOfDay.TotalHours);
            }
            if (command.TimeOfDay is { } timeOfDay)
            {
                sky.TimeSpeed = 0.0f;
                sky.RealTime = false;
                void SetTime(float phase)
                {
                    sky.TimeOfDay = phase;
                    var hours = ManaSky.HoursAt(phase);
                    sky.SkyDateTime = new DateTimeOffset(sky.SkyDateTime.Date.AddHours(hours), sky.SkyDateTime.Offset);
                }

                if (command.Frames > 0)
                {
                    // Always forward, as the day goes -- sunset to night, or on through dawn.
                    var from = sky.TimeOfDay;
                    var ahead = ((timeOfDay - from) % 1.0f + 1.0f) % 1.0f;
                    AddGlide(eased => SetTime((from + ahead * eased) % 1.0f), command.Frames, !command.Background);
                    return command.Background ? ManaCommandFlow.Next : ManaCommandFlow.Pause;
                }
                SetTime(timeOfDay);
            }
            return ManaCommandFlow.Next;
        }

        private void CaptureRecordedFrame()
        {
            if (_recordFolder == null || _recordPaused || _captureFrame == null)
            {
                return;
            }

            _recordTasks.RemoveAll(task => task.IsCompleted);
            if (_recordTasks.Count > 6)
            {
                // Saving has fallen behind; the oldest frame has long since been read back, so this wait is short.
                try
                {
                    _recordTasks[0].Wait(5000);
                }
                catch (AggregateException)
                {
                }
            }

            var capture = _captureFrame(Path.Combine(_recordFolder, $"{_recordFrame:D5}.jpg"), _recordClean);
            if (capture.IsFaulted)
            {
                // A screenshot command holds the slot this frame; the sequence must stay gapless, so skip numbering it.
                return;
            }
            _recordTasks.Add(capture);
            _recordFrame++;
        }

        // Frames per second since the last profile_reset, then every timed scope by average cost -- what makes
        // "the editor is slow" a list of names instead of a feeling.
        private IReadOnlyDictionary<string, long>? _lastGpuReport;

        private void WriteProfileReport(string path)
        {
            var seconds = _profileClock.Elapsed.TotalSeconds;
            var frames = ManaFrameProfiler.FrameCount;
            var particles = new List<ManaCore.Graphics.ManaGpuParticleEmitter>();
            foreach (var scene in Context.World.Scenes)
                foreach (var node in scene.Children) node.CollectGpuParticles(particles);
            var lines = new List<string>
            {
                $"seconds={seconds:0.00} frames={frames} fps={(seconds > 0.0 ? frames / seconds : 0.0):0.0} worstFrameMs={ManaFrameProfiler.WorstFrameMs:0.0} hitches={ManaFrameProfiler.HitchCount}",
                $"nodes={CountNodes(Context.World.DefaultScene.Children)} meshes={Context.World.LastMeshCount} playing={Context.World.IsPlaying}",
                $"gpuParticles={particles.Sum(p => (long)p.Count)} gpuEmitters={particles.Count}",
                "scope | avgMs | peakMs | calls"
            };

            var entries = new List<(string Name, ManaFrameProfiler.Sample Sample)>();
            foreach (var name in ManaFrameProfiler.Names)
            {
                if (ManaFrameProfiler.TryGetSample(name, out var sample))
                {
                    entries.Add((name, sample));
                }
            }

            foreach (var (name, sample) in entries.OrderByDescending(entry => entry.Sample.AverageMs))
            {
                lines.Add($"{name} | {sample.AverageMs:0.00} | {sample.PeakMs:0.00} | {sample.LastCalls}");
            }

            File.WriteAllLines(path, lines);
        }

        private static int CountNodes(IEnumerable<ManaSceneNode> nodes)
        {
            var count = 0;
            foreach (var node in nodes)
            {
                count += 1 + CountNodes(node.Children);
            }

            return count;
        }

        private void StartCameraGlide(ManaCommand command)
        {
            var owner = Context.World.MainCamera.Owner;
            var toPosition = ToVector3(command.Position!);
            var toTarget = ToVector3(command.Target!);
            // The start's look point sits as far ahead as the end's, so the aim swings evenly rather than lurching.
            var reach = MathF.Max(Vector3.Distance(toPosition, toTarget), 0.01f);
            var fromPosition = owner.Position;
            var fromTarget = owner.Position + owner.Forward * reach;
            AddGlide(eased =>
            {
                owner.Position = Vector3.Lerp(fromPosition, toPosition, eased);
                LookAt(owner, Vector3.Lerp(fromTarget, toTarget, eased));
            }, command.Frames, !command.Background);
        }

        /// <summary>Moves every glide on a frame; true while a blocking one is still going.</summary>
        private bool StepGlides()
        {
            var blocking = false;
            for (var i = _glides.Count - 1; i >= 0; i--)
            {
                var (apply, frames, blocks) = _glides[i];
                var step = ++_glideSteps[i];
                var t = Math.Clamp((float)step / frames, 0.0f, 1.0f);
                apply(t * t * (3.0f - 2.0f * t));
                if (step >= frames)
                {
                    _glides.RemoveAt(i);
                    _glideSteps.RemoveAt(i);
                }
                else
                {
                    blocking |= blocks;
                }
            }
            return blocking;
        }

        private static Vector3 ToVector3(float[] values) => new(values[0], values[1], values[2]);

        private void ApplyMoveCamera(ManaCommand command)
        {
            var owner = Context.World.MainCamera.Owner;

            if (command.Position is { Length: 3 } position)
            {
                owner.Position = new Vector3(position[0], position[1], position[2]);
            }

            if (command.Target is { Length: 3 } target)
            {
                LookAt(owner, ToVector3(target));
            }
        }

        private static void LookAt(ManaSceneNode owner, Vector3 target)
        {
            var direction = target - owner.Position;
            if (direction.LengthSquared() < 1e-8f)
            {
                return;
            }

            var forward = Vector3.Normalize(direction);

            // Inverse of ManaSceneNode.Forward's own mapping (CreateFromYawPitchRoll(yaw=Rotation.Y,
            // pitch=Rotation.X, roll=0) applied to local -Z): forward = (-cos(pitch)*sin(yaw),
            // sin(pitch), -cos(pitch)*cos(yaw)).
            var pitch = MathF.Asin(Math.Clamp(forward.Y, -1f, 1f));
            var yaw = MathF.Atan2(-forward.X, -forward.Z);
            owner.Rotation = new Vector3(pitch, yaw, 0.0f);
        }
    }
}
