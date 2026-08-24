using Mana;
using Mana.Code.Levels;
using ManaCore.Graphics;
using ManaUI;
using ManaVoxel.Code.Assets;
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
            && platform.Collision!.Size[0] == 3.0f);

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
        Check("a shape source records the factory argument, not the node scale",
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
}
