namespace Mana.Code.GameModes;

internal static class LiveScanCommands
{
#if !MANA_BROWSER
        /// <summary>
        /// The live_scan script command: "listen" (as opening the panel does), "photos"/"coverage" colouring, "clear",
        /// and "bake" into the level (to <paramref name="path"/> when given). Logs what has streamed in.
        /// </summary>
        public static void Run(ManaContext context, ManaVoxel.Code.UI.VoxelEditorViewModel viewModel, string? name, string? path)
        {
            var session = ManaVoxel.Code.LiveScan.LiveScanSession.Current
                ?? throw new InvalidDataException("live_scan: no live-scan session yet.");
            switch (name?.ToLowerInvariant())
            {
                case "listen": session.Start(); break;
                case "photos": session.Colour = ManaVoxel.Code.LiveScan.LiveScanSession.Colouring.Photos; break;
                case "coverage": session.Colour = ManaVoxel.Code.LiveScan.LiveScanSession.Colouring.Coverage; break;
                case "clear": session.Clear(); break;
                case "bake":
                    var file = Path.GetFullPath(path ?? Path.Combine("Assets/Lidar", $"Live {DateTime.Now:yyyy-MM-dd HH.mm.ss}.glb"));
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    session.Bake(file, ManaLidar.ScanDetail.High, null, (baked, at) =>
                    {
                        var model = context.World.LoadFromGltf(baked, into: viewModel.ActiveScene ?? context.World.DefaultScene);
                        model.LevelSource = Mana.Code.Levels.ManaLevelSource.ForMesh(baked);
                        model.Position = at;
                        session.Clear();
                        ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Command file: live scan baked to {baked}, placed at {at}.");
                    }, error => ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Command file: live scan bake failed: {error}"));
                    break;
                case "log": break;
                default: throw new InvalidDataException($"live_scan: unknown '{name}'.");
            }
            ManaCore.Diagnostics.ManaLog.WriteDiagnostic(
                $"Command file: live scan {name}: phone {session.Server?.Phone ?? "none"}, {session.PieceCount} pieces, "
                + $"{session.TriangleCount} triangles, {session.PhotoCount} photos, port {session.Server?.Port}");
        }
#endif
}
