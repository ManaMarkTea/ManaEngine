using System.Diagnostics;
using System.Runtime.InteropServices;
using Mana;
using ManaGeoTools.Code.Planet;
using ManaUI;

// ManaVoxel has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// Where one terrain patch's source data actually came from, and whether it can be sharpened further.
    /// <para>
    /// Only relevant for a renderer selected on a planet -- a patch shares its scene node with every other
    /// patch in the same quadtree root, so which one this is comes from the selected component, not the
    /// node.
    /// </para>
    /// </summary>
    public sealed class TerrainPatchWidget : InspectorWidget<MeshRendererComponent>
    {
        protected override bool Accepts(MeshRendererComponent subject)
        {
            return subject.Mesh is PlanetPatchMesh
                && subject.Owner.Parent is PlanetNode planet
                && planet.TryGetPatchKey(subject, out _);
        }

        protected override UiElement Build(MeshRendererComponent renderer, InspectorContext context)
        {
            var theme = context.Theme;
            var patchMesh = (PlanetPatchMesh)renderer.Mesh;
            var planet = (PlanetNode)renderer.Owner.Parent!;
            planet.TryGetPatchKey(renderer, out var key);

            var statusText = new TextBlock().Foreground(theme.Text);
            statusText.BindTextPolled(() => $"Up-res status: {planet.GetPatchUpresStatus(key)}");

            var resolutionText = new TextBlock().Foreground(theme.TextMuted);
            resolutionText.Text($"Mesh resolution: {patchMesh.Resolution} x {patchMesh.Resolution}");

            var zoomText = new TextBlock().Foreground(theme.TextMuted);
            zoomText.BindTextPolled(() => planet.GetPatchDetailInfo(key) is { Zoom: { } zoom } ? $"Source zoom: z{zoom}" : string.Empty);
            zoomText.BindPolled(t => t.Visibility, () => HasZoom(planet, key) ? Visibility.Visible : Visibility.Collapsed);

            var tilesText = new TextBlock().Foreground(theme.TextMuted);
            tilesText.BindTextPolled(() =>
            {
                var (total, cached, _) = DescribeTiles(planet, key);
                return $"Source tiles: {total} ({cached} cached)";
            });
            tilesText.BindPolled(t => t.Visibility, () => HasZoom(planet, key) ? Visibility.Visible : Visibility.Collapsed);

            var openTileButton = new Button().Label("Open Source Tile")
                .OnClick(() =>
                {
                    var (_, _, firstCached) = DescribeTiles(planet, key);
                    if (firstCached != null)
                    {
                        RevealFileInExplorer(firstCached);
                    }
                });
            openTileButton.BindPolled(b => b.Visibility, () => HasZoom(planet, key) ? Visibility.Visible : Visibility.Collapsed);
            openTileButton.BindPolled(b => b.IsEnabled, () => DescribeTiles(planet, key).CachedCount > 0);

            var sourceResolutionText = new TextBlock().Foreground(theme.TextMuted);
            sourceResolutionText.BindTextPolled(() => planet.GetPatchDetailInfo(key) is { SourceResolution: { } sourceResolution }
                ? $"Source resolution: {sourceResolution} x {sourceResolution}"
                : string.Empty);
            sourceResolutionText.BindPolled(t => t.Visibility,
                () => planet.GetPatchDetailInfo(key)?.SourceResolution.HasValue == true ? Visibility.Visible : Visibility.Collapsed);

            var sourcePixelText = new TextBlock().Foreground(theme.TextMuted);
            sourcePixelText.BindTextPolled(() => planet.GetPatchDetailInfo(key) is { SourceMetersPerPixel: { } metersPerPixel }
                ? $"Source pixel: {FormatMetersPerPixel(metersPerPixel)}"
                : string.Empty);
            sourcePixelText.BindPolled(t => t.Visibility,
                () => planet.GetPatchDetailInfo(key)?.SourceMetersPerPixel.HasValue == true ? Visibility.Visible : Visibility.Collapsed);

            var meshSpacingText = new TextBlock().Foreground(theme.TextMuted);
            meshSpacingText.BindTextPolled(() => DescribeMeshSpacing(planet, key, patchMesh.Resolution).Spacing);
            meshSpacingText.BindPolled(t => t.Visibility,
                () => IsMeshCoarserThanSource(planet, key, patchMesh.Resolution) ? Visibility.Visible : Visibility.Collapsed);

            var meshCoarserText = new TextBlock().Foreground(theme.TextMuted);
            meshCoarserText.BindTextPolled(() => DescribeMeshSpacing(planet, key, patchMesh.Resolution).CoarserNote);
            meshCoarserText.BindPolled(t => t.Visibility,
                () => IsMeshCoarserThanSource(planet, key, patchMesh.Resolution) ? Visibility.Visible : Visibility.Collapsed);

            var increaseZoomButton = new Button().Label("Increase source zoom")
                .OnClick(() => planet.TrySplitPatch(key));
            increaseZoomButton.BindPolled(b => b.Visibility, () => ShowIncreaseZoomButton(planet, key) ? Visibility.Visible : Visibility.Collapsed);

            var increasingZoomText = new TextBlock().Text("Increasing source zoom...").Foreground(theme.TextMuted);
            increasingZoomText.BindPolled(t => t.Visibility,
                () => planet.GetPatchUpresStatus(key) == PatchUpresStatus.Done && planet.IsSplitPending(key)
                    ? Visibility.Visible
                    : Visibility.Collapsed);

            var zoomLimitText = new TextBlock().Foreground(theme.TextMuted);
            zoomLimitText.BindTextPolled(() => DescribeZoomLimit(planet, key, patchMesh.Resolution));
            zoomLimitText.BindPolled(t => t.Visibility,
                () => planet.GetPatchUpresStatus(key) == PatchUpresStatus.Done
                    && !planet.IsSplitPending(key)
                    && !planet.CanSplitPatch(key)
                    ? Visibility.Visible
                    : Visibility.Collapsed);

            return new Expander()
                .Header("Terrain Patch")
                .Section(theme.Spacing,
                    statusText,
                    resolutionText,
                    zoomText,
                    tilesText,
                    openTileButton,
                    sourceResolutionText,
                    sourcePixelText,
                    meshSpacingText,
                    meshCoarserText,
                    increaseZoomButton,
                    increasingZoomText,
                    zoomLimitText);
        }

        private static bool HasZoom(PlanetNode planet, PatchKey key)
        {
            return planet.GetPatchDetailInfo(key)?.Zoom.HasValue == true;
        }

        private static (int TotalCount, int CachedCount, string? FirstCached) DescribeTiles(PlanetNode planet, PatchKey key)
        {
            var paths = planet.GetPatchTileCacheFilePaths(key);
            string? first = null;
            var cached = 0;
            foreach (var path in paths)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                cached++;
                first ??= path;
            }

            return (paths.Count, cached, first);
        }

        private static bool IsMeshCoarserThanSource(PlanetNode planet, PatchKey key, int meshResolution)
        {
            var detail = planet.GetPatchDetailInfo(key);
            return detail is { SourceResolution: { } sourceResolution, SourceMetersPerPixel: { } }
                && sourceResolution > meshResolution;
        }

        private static (string Spacing, string CoarserNote) DescribeMeshSpacing(PlanetNode planet, PatchKey key, int meshResolution)
        {
            var detail = planet.GetPatchDetailInfo(key);
            if (detail is not { SourceResolution: { } sourceResolution, SourceMetersPerPixel: { } sourceMetersPerPixel })
            {
                return (string.Empty, string.Empty);
            }

            var meshSpacing = EstimateMeshSpacingMeters(meshResolution, sourceResolution, sourceMetersPerPixel);
            var coarserFactor = Math.Max(1.0, meshSpacing / sourceMetersPerPixel);
            return ($"Mesh spacing: {FormatMetersPerPixel(meshSpacing)}",
                $"Mesh is still coarser than source data ({coarserFactor:0.#}x).");
        }

        private static bool ShowIncreaseZoomButton(PlanetNode planet, PatchKey key)
        {
            return planet.GetPatchUpresStatus(key) == PatchUpresStatus.Done
                && !planet.IsSplitPending(key)
                && planet.CanSplitPatch(key);
        }

        private static string DescribeZoomLimit(PlanetNode planet, PatchKey key, int meshResolution)
        {
            var detail = planet.GetPatchDetailInfo(key);
            var meshIsBelowSource = detail is { SourceResolution: { } sourceResolution } && sourceResolution > meshResolution;
            return meshIsBelowSource
                ? "Next source zoom cannot be represented without undersampling"
                : "Mesh matches streamed source data";
        }

        private static double EstimateMeshSpacingMeters(int meshResolution, int sourceResolution, double sourceMetersPerPixel)
        {
            if (meshResolution <= 1 || sourceResolution <= 1)
            {
                return sourceMetersPerPixel;
            }

            return sourceMetersPerPixel * (sourceResolution - 1) / (meshResolution - 1);
        }

        private static string FormatMetersPerPixel(double metersPerPixel)
        {
            if (metersPerPixel >= 1000.0)
            {
                return $"{metersPerPixel / 1000.0:0.##} km/px";
            }

            if (metersPerPixel >= 100.0)
            {
                return $"{metersPerPixel:0} m/px";
            }

            if (metersPerPixel >= 10.0)
            {
                return $"{metersPerPixel:0.#} m/px";
            }

            return metersPerPixel >= 1.0
                ? $"{metersPerPixel:0.##} m/px"
                : $"{metersPerPixel * 100.0:0.#} cm/px";
        }

        /// <summary>Selects filePath in its containing folder rather than opening it, so sibling tiles are visible too.</summary>
        private static void RevealFileInExplorer(string filePath)
        {
            try
            {
                var startInfo = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? new ProcessStartInfo("open", $"-R \"{filePath}\"") { UseShellExecute = true }
                    : new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true };

                Process.Start(startInfo);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
            {
                ManaGraphics.Code.Diagnostics.ManaConsole.WriteDiagnostic($"Failed to reveal '{filePath}': {ex.Message}");
            }
        }
    }
}
