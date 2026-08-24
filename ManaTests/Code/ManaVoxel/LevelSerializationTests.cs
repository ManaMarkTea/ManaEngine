using System.Text.Json;
using BepuPhysics.Collidables;
using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using ManaCore.Graphics;
using ManaGeoTools.Code.Planet;
using ManaUI;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.Gameplay.Trains;
using ManaVoxel.Code.Levels;

namespace ManaTests;

/// <summary>
/// The level file: what it keeps, what it deliberately drops, and whether what it keeps survives a trip
/// through disk unchanged.
/// <para>
/// Checkable headlessly because the format is deliberately data, not scene graph -- capture produces plain
/// records and loading consumes them, so everything except the two ends (a live world in, a live world out)
/// can be exercised with no graphics device. The parts that need a world are covered where they are
/// cheapest to be wrong: the voxel codec, which is the only place a level can lose information silently.
/// </para>
/// </summary>
public static class LevelSerializationChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        static IEnumerable<ManaLevelNode> WalkNodes(IEnumerable<ManaLevelNode> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;
                if (node.Children == null)
                {
                    continue;
                }

                foreach (var child in WalkNodes(node.Children))
                {
                    yield return child;
                }
            }
        }

        // ---------------------------------------------------------------- json round trip
        var level = new ManaLevel
        {
            Name = "Test Level",
            Camera = new ManaLevelCamera
            {
                Position = new[] { 1.0f, 2.0f, 3.0f },
                Rotation = new[] { 0.1f, 0.2f, 0.3f }
            },
            Nodes =
            {
                new ManaLevelNode
                {
                    Name = "Platform",
                    Kind = ManaLevelSourceKind.Shape,
                    Shape = "Cube",
                    Position = new[] { 4.0f, 5.0f, 6.0f },
                    Rotation = new[] { 0.0f, 1.5f, 0.0f },
                    Scale = new[] { 1.0f, 1.0f, 1.0f },
                    Color = new[] { 0.25f, 0.5f, 0.75f, 1.0f },
                    Size = new[] { 1.5f, 0.25f, 1.5f },
                    Collision = new ManaLevelCollision
                    {
                        Size = new[] { 3.0f, 0.5f, 3.0f },
                        Offset = new[] { 0.0f, 0.0f, 0.0f }
                    },
                    Scripts = new List<ManaLevelScript>
                    {
                        new ManaLevelScript
                        {
                            Type = TrainCarComponent.ScriptType,
                            Properties =
                            {
                                ["routeKey"] = JsonValue("A"),
                                ["carIndex"] = JsonValue(2),
                                ["speed"] = JsonValue(6.25f)
                            }
                        }
                    }
                },
                new ManaLevelNode
                {
                    Name = "Player Start",
                    Kind = ManaLevelSourceKind.PlayerStart,
                    Position = new[] { 10.0f, 0.0f, -2.0f },
                    Rotation = new[] { 0.0f, 3.0f, 0.0f }
                }
            }
        };

        var reloaded = ManaLevel.FromJson(level.ToJson());

        Check("a level survives a json round trip",
            reloaded.Nodes.Count == 2
            && reloaded.Name == "Test Level"
            && reloaded.Camera?.Position[1] == 2.0f,
            $"{reloaded.Nodes.Count} nodes");

        var platform = reloaded.Nodes[0];
        Check("a shape keeps its colour, size and collider",
            platform.Shape == "Cube"
            && platform.Color![2] == 0.75f
            && platform.Size![1] == 0.25f
            && platform.Collision!.Size![0] == 3.0f);

        var platformScript = platform.Scripts?.FirstOrDefault();
        Check("a node keeps script component data",
            platformScript?.Type == TrainCarComponent.ScriptType
            && platformScript.Properties["routeKey"].GetString() == "A"
            && platformScript.Properties["carIndex"].GetInt32() == 2
            && Math.Abs(platformScript.Properties["speed"].GetSingle() - 6.25f) < 0.001f);

        Check("the player start round trips with its facing",
            reloaded.PlayerStart != null
            && reloaded.PlayerStart.Position[0] == 10.0f
            && reloaded.PlayerStart.Rotation[1] == 3.0f);

        var groupedLevel = new ManaLevel
        {
            Nodes =
            {
                new ManaLevelNode
                {
                    Name = "Gameplay",
                    Kind = ManaLevelSourceKind.Group,
                    Position = new[] { 0.0f, 0.0f, 0.0f },
                    Rotation = new[] { 0.0f, 0.0f, 0.0f },
                    Scale = new[] { 1.0f, 1.0f, 1.0f },
                    Children = new List<ManaLevelNode>
                    {
                        new()
                        {
                            Name = "Nested Player Start",
                            Kind = ManaLevelSourceKind.PlayerStart,
                            Position = new[] { 7.0f, 0.0f, 8.0f },
                            Rotation = new[] { 0.0f, 1.0f, 0.0f }
                        }
                    }
                }
            }
        };

        var groupedReloaded = ManaLevel.FromJson(groupedLevel.ToJson());
        Check("a group keeps its named children",
            groupedReloaded.Nodes[0].Kind == ManaLevelSourceKind.Group
            && groupedReloaded.Nodes[0].Children?[0].Name == "Nested Player Start");

        Check("player start lookup walks grouped children",
            groupedReloaded.PlayerStart?.Position[0] == 7.0f);

        var waterReloaded = ManaLevel.FromJson(new ManaLevel
        {
            Name = "Water Round Trip",
            Nodes =
            {
                new ManaLevelNode
                {
                    Name = "Pond Water",
                    Kind = ManaLevelSourceKind.Water,
                    Position = new[] { 32.0f, 0.2f, 32.0f },
                    Water = new ManaLevelWater
                    {
                        Size = new[] { 18.0f, 12.0f },
                        Resolution = 72
                    }
                }
            }
        }.ToJson());
        Check("water construction settings survive JSON",
            waterReloaded.Nodes.SingleOrDefault() is
            {
                Kind: ManaLevelSourceKind.Water,
                Water: { Resolution: 72 }
            } waterEntry
            && waterEntry.Water.Size.SequenceEqual(new[] { 18.0f, 12.0f }));

        var gizmoType = typeof(Mana.Code.GameModes.VoxelEditorGameMode).Assembly.GetType(
            "Mana.Code.GameModes.SceneTransformGizmo");
        var projectAxis = gizmoType?.GetMethod(
            "TryAxisParameterOnPlane",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var distanceToHandle = gizmoType?.GetMethod(
            "DistanceFromRayToSegment",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Check("gizmo drag projection helpers are available", projectAxis != null && distanceToHandle != null);

        if (projectAxis != null)
        {
            object?[] centerRay =
            {
                new Vector3(0, 0, 10),
                Vector3.Normalize(new Vector3(0, 0, -10)),
                Vector3.Zero,
                Vector3.UnitX,
                Vector3.UnitZ,
                0.0f
            };
            object?[] rightRay =
            {
                new Vector3(0, 0, 10),
                Vector3.Normalize(new Vector3(1, 0, -10)),
                Vector3.Zero,
                Vector3.UnitX,
                Vector3.UnitZ,
                0.0f
            };
            object?[] upRay =
            {
                new Vector3(0, 0, 10),
                Vector3.Normalize(new Vector3(0, 1, -10)),
                Vector3.Zero,
                Vector3.UnitY,
                Vector3.UnitZ,
                0.0f
            };

            var centerProjected = projectAxis.Invoke(null, centerRay) is true;
            var rightProjected = projectAxis.Invoke(null, rightRay) is true;
            var upProjected = projectAxis.Invoke(null, upRay) is true;
            var centerParameter = centerRay[5] is float center ? center : float.NaN;
            var rightParameter = rightRay[5] is float right ? right : float.NaN;
            var upParameter = upRay[5] is float up ? up : float.NaN;
            Check("gizmo X follows the mouse right at scene scale",
                centerProjected && rightProjected
                && MathF.Abs(centerParameter) < 0.001f
                && MathF.Abs(rightParameter - 1.0f) < 0.001f,
                $"{centerParameter:0.###} -> {rightParameter:0.###}");
            Check("gizmo Y follows the mouse up at scene scale",
                upProjected && MathF.Abs(upParameter - 1.0f) < 0.001f,
                upParameter.ToString("0.###"));
        }

        if (distanceToHandle != null)
        {
            var directHit = (float)distanceToHandle.Invoke(null, new object[]
            {
                new Vector3(0, 0, 10),
                Vector3.Normalize(new Vector3(1, 0, -10)),
                Vector3.Zero,
                new Vector3(2, 0, 0)
            })!;
            var clearMiss = (float)distanceToHandle.Invoke(null, new object[]
            {
                new Vector3(0, 0, 10),
                Vector3.Normalize(new Vector3(0, 1, -10)),
                Vector3.Zero,
                new Vector3(2, 0, 0)
            })!;
            Check("gizmo shafts have stable analytic hit targets",
                directHit < 0.001f && clearMiss > 0.5f,
                $"hit {directHit:0.###}, miss {clearMiss:0.###}");
        }

        var planetWorld = new ManaWorld();
        var planetContext = new ManaContext(null!, null!, planetWorld) { DeferGpuUploads = true };
        var planetScene = new ManaScene(planetContext) { Name = "Planet Scene" };
        planetWorld.Scenes.Add(planetScene);
        var planetResult = ManaLevelLoader.Load(
            new ManaLevel
            {
                Nodes =
                {
                    new ManaLevelNode
                    {
                        Name = "Tiny Planet",
                        Kind = ManaLevelSourceKind.Planet,
                        Position = new[] { 1.0f, 2.0f, 3.0f },
                        Scale = new[] { 1.0f, 1.0f, 1.0f },
                        Planet = new ManaLevelPlanet
                        {
                            PatchesPerFaceEdge = 1,
                            Radius = 3.0f,
                            Resolution = 2,
                            OceanResolution = 2,
                            HeightExaggeration = 4.0f,
                            AutoLodEnabled = true,
                            NearOceanEnabled = false
                        }
                    }
                }
            },
            planetContext,
            planetScene,
            replaceAuthoredNodes: false);
        Check("planet level data constructs a planet node",
            planetResult.Planet is
            {
                Name: "Tiny Planet",
                PatchesPerFaceEdge: 1,
                Radius: 3.0f,
                Resolution: 2,
                OceanResolution: 2,
                AutoLodEnabled: true,
                NearOceanEnabled: false
            }
            && planetResult.Planet.Position == new Vector3(1.0f, 2.0f, 3.0f));

        var capturedPlanet = ManaLevelCapture.CaptureScene(planetWorld, planetScene, "Planet Scene");
        Check("saving a planet preserves its construction settings",
            capturedPlanet.Nodes.SingleOrDefault()?.Planet is
            {
                PatchesPerFaceEdge: 1,
                Radius: 3.0f,
                OceanResolution: 2,
                HeightExaggeration: 4.0f
            });

        var builtInLevelNames = new[]
        {
            "Voxel Showcase",
            "Flight Helmet",
            "Planet Showcase",
            "PBR Gallery",
            "Little Pond"
        };
        var builtInLevels = builtInLevelNames
            .Select(name => ManaLevelLibrary.ResolvePath(name))
            .Where(File.Exists)
            .Select(ManaLevel.Load)
            .ToList();
        Check("showcase content lives in separate level files",
            builtInLevels.Count == builtInLevelNames.Length,
            string.Join(", ", builtInLevels.Select(item => item.Name)));

        var voxelShowcase = builtInLevels.FirstOrDefault(item => item.Name == "Voxel Showcase");
        Check("the voxel showcase references the Captain asset",
            voxelShowcase?.Nodes.Any(node =>
                node.Kind == ManaLevelSourceKind.Voxels
                && node.Asset?.EndsWith("Captain.vox", StringComparison.OrdinalIgnoreCase) == true) == true);

        var planetShowcase = builtInLevels.FirstOrDefault(item => item.Name == "Planet Showcase");
        Check("the planet showcase carries authored streaming settings",
            planetShowcase?.Nodes.SingleOrDefault(node => node.Kind == ManaLevelSourceKind.Planet)?.Planet is
            {
                PatchesPerFaceEdge: 6,
                Radius: 40.0f
            });

        var gallery = builtInLevels.FirstOrDefault(item => item.Name == "PBR Gallery");
        var galleryMeshes = WalkNodes(gallery?.Nodes ?? Enumerable.Empty<ManaLevelNode>())
            .Where(node => node.Kind == ManaLevelSourceKind.Mesh)
            .ToList();
        Check("the PBR gallery presents four material-rich models",
            galleryMeshes.Count == 4,
            string.Join(", ", galleryMeshes.Select(node => node.Name)));
        Check("every gallery model resolves from its portable asset path",
            galleryMeshes.All(node =>
                node.Asset != null && File.Exists(ManaLevelLoader.ResolveAssetPath(node.Asset))),
            string.Join(", ", galleryMeshes.Select(node => node.Asset)));

        var pond = builtInLevels.FirstOrDefault(item => item.Name == "Little Pond");
        var pondWater = WalkNodes(pond?.Nodes ?? Enumerable.Empty<ManaLevelNode>())
            .SingleOrDefault(node => node.Kind == ManaLevelSourceKind.Water);
        Check("the pond owns a ripple-capable water surface",
            pondWater?.Water is { Resolution: 96 } pondSettings
            && pondSettings.Size.SequenceEqual(new[] { 16.0f, 16.0f }));

        var allBundledLevels = Directory
            .EnumerateFiles(Path.GetDirectoryName(ManaLevelLibrary.ResolvePath("Little Pond"))!, "*.manalevel")
            .Select(ManaLevel.Load)
            .ToList();
        Check("bundled level cameras stay clear of the zero-axis grid walls",
            allBundledLevels.All(item => item.Camera is { Position.Length: >= 3 }
                && item.Camera.Position[0] >= 32.0f
                && item.Camera.Position[2] >= 32.0f),
            string.Join(", ", allBundledLevels.Select(item => item.Name)));
        Check("bundled level roots use the 32,0,32 scene offset",
            allBundledLevels.All(item => item.Nodes.Count > 0
                && item.Nodes.All(node => node.Position.Length >= 3
                    && node.Position[0] >= 32.0f
                    && node.Position[2] >= 32.0f)),
            string.Join(", ", allBundledLevels.Select(item => item.Name)));

        // ---------------------------------------------------------------- scene capture
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world);
        var sceneA = new ManaScene(context) { Name = "Scene A", LevelPath = "C:/levels/a.manalevel" };
        var sceneB = new ManaScene(context) { Name = "Scene B", LevelPath = "C:/levels/b.manalevel" };
        var sceneANode = new ManaSceneNode(context, Matrix4x4.Identity)
        {
            Name = "Scene A Box",
            LevelSource = ManaLevelSource.ForShape("Cube", Vector4.One, Vector3.One)
        };
        var sceneBNode = new ManaSceneNode(context, Matrix4x4.Identity)
        {
            Name = "Scene B Box",
            LevelSource = ManaLevelSource.ForShape("Cube", Vector4.One, Vector3.One)
        };
        var trainScript = new TrainCarComponent(sceneANode);
        trainScript.ConfigureDefaultRoute("B", 1, 0.25f);
        trainScript.Speed = 7.5f;
        sceneANode.Components.Add(trainScript);

        sceneA.Children.Add(sceneANode);
        sceneB.Children.Add(sceneBNode);
        world.Scenes.Add(sceneA);
        world.Scenes.Add(sceneB);

        var sceneOnly = ManaLevelCapture.CaptureScene(world, sceneA, "Scene A");
        Check("capturing one scene keeps only that scene's authored nodes",
            sceneOnly.Nodes.Count == 1 && sceneOnly.Nodes[0].Name == "Scene A Box",
            string.Join(", ", sceneOnly.Nodes.Select(node => node.Name)));

        var capturedScript = sceneOnly.Nodes[0].Scripts?.FirstOrDefault();
        Check("capturing a scene writes train scripts",
            capturedScript?.Type == TrainCarComponent.ScriptType
            && capturedScript.Properties["routeKey"].GetString() == "B"
            && capturedScript.Properties["carIndex"].GetInt32() == 1
            && Math.Abs(capturedScript.Properties["speed"].GetSingle() - 7.5f) < 0.001f);

        Check("runtime train state is transient",
            capturedScript != null && !capturedScript.Properties.ContainsKey("paused"));

        var scriptedWorld = new ManaWorld();
        var scriptedContext = new ManaContext(null!, null!, scriptedWorld);
        var scriptedScene = new ManaScene(scriptedContext) { Name = "Scripted Scene" };
        scriptedWorld.Scenes.Add(scriptedScene);

        var scriptedLevel = new ManaLevel
        {
            Nodes =
            {
                new ManaLevelNode
                {
                    Name = "Scripted Train",
                    Kind = ManaLevelSourceKind.Group,
                    Scripts = new List<ManaLevelScript>
                    {
                        new()
                        {
                            Type = TrainCarComponent.ScriptType,
                            Properties =
                            {
                                ["routeKey"] = JsonValue("C"),
                                ["carIndex"] = JsonValue(4),
                                ["speed"] = JsonValue(8.75f),
                                ["paused"] = JsonValue(false)
                            }
                        }
                    }
                }
            }
        };

        var scriptedResult = ManaLevelLoader.Load(
            scriptedLevel,
            scriptedContext,
            scriptedScene,
            replaceAuthoredNodes: false);
        var loadedTrain = scriptedResult.Nodes[0].GetComponents<TrainCarComponent>().FirstOrDefault();
        Check("script json applies directly to the train component",
            loadedTrain != null
            && loadedTrain.RouteKey == "C"
            && loadedTrain.CarIndex == 4
            && Math.Abs(loadedTrain.Speed - 8.75f) < 0.001f);

        Check("transient train state is not applied from json",
            loadedTrain?.Paused == true);

        var scriptedTrainStart = loadedTrain?.Owner.Position ?? Vector3.Zero;
        scriptedWorld.BeginPlay();
        Check("world play starts scripted components",
            scriptedWorld.IsPlaying && loadedTrain?.Paused == false);

        if (loadedTrain != null)
        {
            loadedTrain.Owner.Position = new Vector3(99.0f, 88.0f, 77.0f);
        }

        scriptedWorld.EndPlay();
        Check("world stop restores scripted component state",
            !scriptedWorld.IsPlaying
            && loadedTrain?.Paused == true
            && loadedTrain.Owner.Position == scriptedTrainStart);

        var visualCollisionWorld = new ManaWorld();
        var visualCollisionContext = new ManaContext(null!, null!, visualCollisionWorld);
        visualCollisionContext.Physics = new ManaPhysics(visualCollisionContext);
        var visualCollisionScene = new ManaScene(visualCollisionContext) { Name = "Visual Collision Scene" };
        visualCollisionWorld.Scenes.Add(visualCollisionScene);

        var visualCollisionLevel = new ManaLevel
        {
            Nodes =
            {
                new ManaLevelNode
                {
                    Name = "Visual Platform",
                    Kind = ManaLevelSourceKind.Shape,
                    Shape = "Cube",
                    Position = new[] { 0.0f, 0.0f, 0.0f },
                    Rotation = new[] { 0.0f, 0.0f, 0.0f },
                    Scale = new[] { 1.0f, 1.0f, 1.0f },
                    Color = new[] { 1.0f, 1.0f, 1.0f, 1.0f },
                    Size = new[] { 2.0f, 0.25f, 3.0f },
                    Collision = new ManaLevelCollision
                    {
                        UseVisualBounds = true
                    }
                }
            }
        };

        var visualCollisionResult = ManaLevelLoader.Load(
            visualCollisionLevel,
            visualCollisionContext,
            visualCollisionScene,
            replaceAuthoredNodes: false);
        var visualCollision = visualCollisionResult.Nodes[0]
            .GetComponents<ManaCollisionComponent>()
            .FirstOrDefault();
        var visualBox = visualCollision?.Shape is Box box ? box : (Box?)null;
        var visualSize = visualBox.HasValue
            ? new Vector3(visualBox.Value.Width, visualBox.Value.Height, visualBox.Value.Length)
            : Vector3.Zero;

        Check("visual-bounds collision loads from the rendered cube bounds",
            NearlyEqual(visualSize, new Vector3(2.0f, 0.25f, 3.0f))
            && NearlyEqual(visualCollision?.Position ?? new Vector3(float.NaN), Vector3.Zero),
            visualBox == null
                ? "missing"
                : $"{visualBox.Value.Width}, {visualBox.Value.Height}, {visualBox.Value.Length}; offset {visualCollision?.Position}");

        var capturedVisualCollision = ManaLevelCapture.CaptureScene(
            visualCollisionWorld,
            visualCollisionScene,
            "Visual Collision Scene").Nodes[0].Collision;
        Check("visual-bounds collision saves as mesh-derived collision",
            capturedVisualCollision is { UseVisualBounds: true, Size: null },
            capturedVisualCollision == null
                ? "missing"
                : $"useVisualBounds={capturedVisualCollision.UseVisualBounds}, size={capturedVisualCollision.Size?.Length}");

        var wholeWorld = ManaLevelCapture.Capture(world, "World");
        Check("capturing the world still walks every scene",
            wholeWorld.Nodes.Count == 2,
            string.Join(", ", wholeWorld.Nodes.Select(node => node.Name)));

        // ---------------------------------------------------------------- format guards
        Check("a foreign json is rejected by name, not by null reference",
            Throws(() => ManaLevel.FromJson("{\"format\":\"something-else\",\"version\":1}")));

        Check("a level from a newer build is rejected rather than half-read",
            Throws(() => ManaLevel.FromJson(
                $"{{\"format\":\"{ManaLevel.FormatName}\",\"version\":{ManaLevel.CurrentVersion + 1}}}")));

        // ---------------------------------------------------------------- voxel codec
        // Half-unit positions on purpose: that is the grid resolution ManaVoxelShape keys voxels at, so
        // this is the finest distinction the codec is required to preserve.
        var voxels = new List<ManaVoxelData>
        {
            new() { Position = new Vector3(0.5f, 0.5f, 0.5f), Color = new Vector4(1.0f, 0.0f, 0.0f, 1.0f) },
            new() { Position = new Vector3(-1.5f, 2.5f, 3.5f), Color = new Vector4(0.0f, 1.0f, 0.0f, 1.0f) },
            new() { Position = new Vector3(10.0f, -4.0f, 0.0f), Color = new Vector4(1.0f, 0.0f, 0.0f, 1.0f) }
        };

        var encoded = ManaLevelVoxelCodec.Encode(voxels);
        var decoded = ManaLevelVoxelCodec.Decode(encoded);

        Check("every voxel comes back",
            decoded.Count == voxels.Count, $"{decoded.Count} of {voxels.Count}");

        Check("voxel positions come back exactly, half-units included",
            decoded[0].Position == voxels[0].Position
            && decoded[1].Position == voxels[1].Position
            && decoded[2].Position == voxels[2].Position);

        Check("voxel colours come back exactly",
            decoded[0].Color == voxels[0].Color && decoded[1].Color == voxels[1].Color);

        // Two of the three voxels are red, so a palette that did its job holds two entries, not three.
        Check("repeated colours share one palette entry",
            encoded.Palette.Count == 2, $"{encoded.Palette.Count} entries");

        var voxelLevel = new ManaLevel
        {
            Nodes = { new ManaLevelNode { Kind = ManaLevelSourceKind.Voxels, Voxels = encoded } }
        };

        var voxelsAfterJson = ManaLevelVoxelCodec.Decode(
            ManaLevel.FromJson(voxelLevel.ToJson()).Nodes[0].Voxels!);

        Check("voxels survive the json trip as well as the blob",
            voxelsAfterJson.Count == voxels.Count
            && voxelsAfterJson[1].Position == voxels[1].Position
            && voxelsAfterJson[1].Color == voxels[1].Color);

        // A blob whose length disagrees with the declared count means one of the two was written by
        // something that is not this codec, and reading it would produce silently wrong geometry.
        Check("a truncated voxel blob is caught rather than decoded short",
            Throws(() => ManaLevelVoxelCodec.Decode(new ManaLevelVoxels
            {
                Palette = encoded.Palette,
                Count = 99,
                Data = encoded.Data
            })));

        Check("a voxel pointing past the palette is caught",
            Throws(() => ManaLevelVoxelCodec.Decode(new ManaLevelVoxels
            {
                Palette = new List<float[]>(),
                Count = voxels.Count,
                Data = encoded.Data
            })));

        Check("an empty grid encodes and decodes as empty",
            ManaLevelVoxelCodec.Decode(ManaLevelVoxelCodec.Encode(Array.Empty<ManaVoxelData>())).Count == 0);

        // ---------------------------------------------------------------- file i/o
        var directory = Path.Combine(Path.GetTempPath(), "ManaTests", Guid.NewGuid().ToString("n"));
        try
        {
            var path = Path.Combine(directory, "sample" + ManaLevel.FileExtension);
            level.Save(path);

            Check("saving creates the folder it was pointed at", File.Exists(path));

            var fromDisk = ManaLevel.Load(path);
            Check("a level reads back off disk", fromDisk.Nodes.Count == level.Nodes.Count);

            // Saving over an existing level must replace it, and must not leave the temp file behind.
            level.Nodes.RemoveAt(1);
            level.Save(path);

            Check("saving again replaces the file rather than failing",
                ManaLevel.Load(path).Nodes.Count == 1);

            Check("the write-then-move leaves no temp file behind", !File.Exists(path + ".tmp"));

            Check("a name typed into the panel resolves to a level file",
                ManaLevelLibrary.ResolvePath("My Level")
                    .EndsWith("My Level" + ManaLevel.FileExtension, StringComparison.Ordinal));

            Check("a full path is left as a full path",
                ManaLevelLibrary.ResolvePath(path) == path);

            Check("invalid filename characters are stripped rather than throwing",
                !Throws(() => ManaLevelLibrary.ResolvePath("bad:name?")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        // ---------------------------------------------------------------- command line
        // The launch flags are the contract between the editor's Play In New Window and the second process
        // it starts. A typo here is invisible: the level argument is simply never seen and the game opens
        // an empty world, which looks exactly like a level that failed to save.
        var playArgs = ManaLaunchOptions.Parse(new[] { "--play", "C:/levels/My Level.manalevel" });
        Check("--play carries the level path",
            playArgs.PlayLevelPath == "C:/levels/My Level.manalevel", playArgs.PlayLevelPath ?? "null");

        var editArgs = ManaLaunchOptions.Parse(new[] { "--level", "sample.manalevel" });
        Check("--level opens the editor on a level, not the game",
            editArgs.EditLevelPath == "sample.manalevel" && editArgs.PlayLevelPath == null);

        var combined = ManaLaunchOptions.Parse(
            new[] { "--play", "a.manalevel", "--graphics", "opengl", "--wgpu-backend", "vulkan" });
        Check("play survives alongside the graphics flags it is launched with",
            combined.PlayLevelPath == "a.manalevel"
            && combined.GraphicsBackend == ManaGraphicsBackendKind.OpenGL
            && combined.WebGpuBackend == "vulkan",
            $"{combined.PlayLevelPath}, {combined.GraphicsBackend}, {combined.WebGpuBackend}");

        Check("=-form works too, as it does for every other flag",
            ManaLaunchOptions.Parse(new[] { "--play=b.manalevel" }).PlayLevelPath == "b.manalevel");

        Check("no level flag leaves both null",
            ManaLaunchOptions.Parse(Array.Empty<string>()) is { PlayLevelPath: null, EditLevelPath: null });

        // ---------------------------------------------------------------- level source tagging
        Check("a shape source records the visual factory size, not the node scale",
            ManaLevelSource.ForShape("Cube", Vector4.One, new Vector3(2.0f, 1.0f, 2.0f)).Size.X == 2.0f);

        Check("a mesh source carries its asset reference",
            ManaLevelSource.ForMesh("pack.zip|models/tree.glb").Asset == "pack.zip|models/tree.glb");

        var relativeZipAsset = MeshAssetLibrary.EncodeRelativeZip("Assets/pack.zip", "Models/train.glb");
        Check("a relative zip asset keeps its zip filename and entry",
            MeshAssetLibrary.TryDecodeZip(relativeZipAsset, out var decodedZip, out var decodedEntry)
            && Path.GetFileName(decodedZip).Equals("pack.zip", StringComparison.OrdinalIgnoreCase)
            && decodedEntry == "Models/train.glb",
            $"{decodedZip} | {decodedEntry}");
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static bool NearlyEqual(Vector3 left, Vector3 right, float epsilon = 0.001f)
    {
        return MathF.Abs(left.X - right.X) <= epsilon
            && MathF.Abs(left.Y - right.Y) <= epsilon
            && MathF.Abs(left.Z - right.Z) <= epsilon;
    }

    private static JsonElement JsonValue<T>(T value)
    {
        using var document = JsonSerializer.SerializeToDocument(value);
        return document.RootElement.Clone();
    }
}
