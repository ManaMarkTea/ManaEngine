using ManaGeoTools.Code.Region;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// The hex grid a region's terrain is tiled into. Only relevant once the async build in
    /// <see cref="RegionNode.TerrainMesh"/> has actually produced a mesh to tile.
    /// </summary>
    public sealed class RegionHexTilingWidget : InspectorWidget<RegionNode>
    {
        protected override bool Accepts(RegionNode subject) => subject.TerrainMesh != null;

        protected override UiElement Build(RegionNode region, InspectorContext context)
        {
            var theme = context.Theme;
            var terrain = region.TerrainMesh!;

            var summary = new TextBlock().Foreground(theme.TextMuted);
            summary.BindTextPolled(() => $"{terrain.HexCellCount:N0} hexes at {terrain.EffectiveHexRadiusMeters:0.#} m");

            // Written on every drag tick, same as the ImGui version, but the mesh rebuild -- expensive
            // enough to matter at this cell count -- only fires once the edit settles rather than on
            // every intermediate value.
            var hexRadius = InspectorControls.Scalar(
                () => terrain.HexRadiusMeters,
                value => terrain.HexRadiusMeters = value,
                0.5f,
                2000.0f);
            hexRadius.ValueCommitted += (_, _) => terrain.RebuildGeometry();

            var stepStrength = InspectorControls.Scalar(
                () => terrain.StepStrength,
                value => terrain.StepStrength = value,
                0.0f,
                1.0f);
            stepStrength.ValueCommitted += (_, _) => terrain.RebuildGeometry();

            return new Expander()
                .Header("Region Hex Tiling")
                .Section(theme.Spacing,
                    summary,
                    InspectorControls.Row(context, "Hex radius (m)", hexRadius),
                    InspectorControls.Row(context, "Step strength", stepStrength,
                        "1 = flat plateaus with vertical walls between them, 0 = a continuous surface that "
                        + "is merely hex-faceted."));
        }
    }
}
