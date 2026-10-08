using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Shapes;

namespace Mana
{
    /// <summary>
    /// A quarter-cylinder cove connecting two perpendicular grid planes along their shared edge, so a
    /// corner of the editor's viewport grid reads as a smooth rounded room (holodeck-style) rather than
    /// two flat planes meeting in a sharp seam.
    /// <para>
    /// Only the CrossAxisA plane (the wall, in VoxelEditorGameMode's use) is trimmed back before this is
    /// added; the CrossAxisB plane (the floor) is left at its full, untrimmed extent -- like a photography
    /// backdrop's paper, where it is the wall that curves down and drapes onto an undisturbed floor rather
    /// than both surfaces being cut back equally. <see cref="FloorLift"/> raises the fillet a hair off that
    /// untrimmed floor so the two don't z-fight where the curve's foot lies flush against it.
    /// </para>
    /// <para>
    /// The cross-section is a quarter circle whose centre sits <see cref="Radius"/> out along <i>both</i>
    /// plane normals from the wall's original (untrimmed) corner edge, not on the edge itself -- that is
    /// what curves the surface <i>away</i> from the room (a true concave cove) instead of bulging it toward
    /// the room (a rounded-over convex edge, which centring on the edge would produce instead).
    /// </para>
    /// </summary>
    public class ManaGridCoveFillet : ManaProceduralMesh
    {
        /// <summary>The direction the fillet runs in, full length along the shared edge.</summary>
        public Vector3 SweepAxis { get; }

        /// <summary>
        /// The two directions spanning the cross-section, each equal to the surface normal of one of the
        /// two planes being joined: the plane whose own normal is <see cref="CrossAxisA"/> meets this
        /// fillet at cross-parameter 0, and the plane normal to <see cref="CrossAxisB"/> meets it at 1.
        /// </summary>
        public Vector3 CrossAxisA { get; }

        public Vector3 CrossAxisB { get; }

        public float SweepLength { get; }

        public float Radius { get; }

        public int Segments { get; }

        /// <summary>How far along CrossAxisB (the untrimmed plane's own normal) the whole fillet is offset, to clear it of a z-fight against that plane's coincident surface.</summary>
        public float FloorLift { get; }

        public ManaGridCoveFillet(
            ManaContext context,
            Vector3 sweepAxis,
            Vector3 crossAxisA,
            Vector3 crossAxisB,
            float sweepLength,
            float radius,
            float floorLift = 0.0f,
            int segments = 10)
            : base(context)
        {
            SweepAxis = sweepAxis;
            CrossAxisA = crossAxisA;
            CrossAxisB = crossAxisB;
            SweepLength = sweepLength;
            Radius = radius;
            FloorLift = floorLift;
            Segments = Math.Max(1, segments);

            GenerateData();
        }

        public override ManaMaterial GenerateMaterial()
        {
            var material = new ManaMaterial(Context);
            material.Name = "Grid Cove Fillet Material";
            material.Shader = new ManaGridShader(Context);

            // Same fallback colours as ManaQuad's grid planes -- overwritten once a UiTheme is available.
            // Left at the identity UV transform: the coordinates written below are already expressed in
            // the same 256-unit (RoomSize) space grid.wgsl/grid.frag expect, so nothing needs remapping.
            material.Albedo.Value = new Vector4(0.604f, 0.604f, 0.635f, 1.0f);
            material.Emission.Value = new Vector4(0.439f, 0.447f, 0.486f, 1.0f);
            material.EmissionStrength = 1.0;
            material.Metallic.Value = 0.95f;

            return material;
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            MinSize = Vector3.Zero;
            MaxSize = SweepAxis * SweepLength + (CrossAxisA + CrossAxisB) * Radius;

            // Arc length, not the straight chord: a quarter circle of radius R is noticeably longer than
            // the R-by-R corner it replaces, and using the chord would squeeze the grid's tiles as they
            // wrap the curve.
            //
            // Divide by 256, matching grid.wgsl/grid.frag's RoomSize/chunkSize constant (see
            // VoxelEditorGameMode's gridSize) -- not some other span. This used to be a room-size-256
            // layout; when the room doubled to a full 512-unit enclosure this divisor was missed, so the
            // cove's tile grid ran at exactly double the density/phase of the flat walls it joins.
            var arcLength = Radius * MathF.PI * 0.5f;
            var crossUvSpan = arcLength / 256.0f;
            var sweepUvSpan = SweepLength / 256.0f;

            var ring = new ManaGeometryVertex[2, Segments + 1];

            for (var s = 0; s <= 1; s++)
            {
                var sweepOffset = SweepAxis * (SweepLength * s);

                for (var i = 0; i <= Segments; i++)
                {
                    var u = (float)i / Segments;

                    // Quarter circle whose centre sits Radius out along BOTH plane normals from the shared
                    // edge -- not centred on the edge itself, which would bulge the surface into the room
                    // (a rounded-over convex edge) instead of curving it away (a true concave cove). At
                    // phi=0 this sits exactly on the CrossAxisA plane with that plane's own normal; at
                    // phi=90 degrees it sits on the CrossAxisB plane with that plane's normal, so the
                    // fillet meets both flat planes tangentially with no crease.
                    var phi = u * (MathF.PI * 0.5f);
                    var cos = MathF.Cos(phi);
                    var sin = MathF.Sin(phi);

                    var pos = sweepOffset
                        + CrossAxisA * (Radius * (1.0f - cos))
                        + CrossAxisB * (Radius * (1.0f - sin) + FloorLift);

                    var normal = Vector3.Normalize(CrossAxisA * cos + CrossAxisB * sin);

                    // Centred on 0, not starting there: the arc is exactly mirror-symmetric about u=0.5
                    // (swapping CrossAxisA/B above), but a grid line only ever falls on a multiple of the
                    // tile spacing, and 0 is always one of those. Anchoring the coordinate to an edge left
                    // whichever line happened to land near the middle looking arbitrarily off-centre;
                    // centring it here instead guarantees one falls exactly on the curve's true midpoint.
                    var vertex = DynamicGeometryContainer.AddPoint(pos, normal);
                    vertex.TexCoord0 = new Vector2(s * sweepUvSpan, (u - 0.5f) * crossUvSpan);
                    vertex.Color = DefaultVertexColor;
                    ring[s, i] = vertex;
                }
            }

            for (var i = 0; i < Segments; i++)
            {
                // Same perimeter order ManaQuad uses for its own four corners: across the cross-section,
                // along the sweep, back across, back along -- so winding stays consistent with every other
                // grid quad this sits beside.
                DynamicGeometryContainer.CreateFacesFromQuad(
                    ring[0, i], ring[0, i + 1], ring[1, i + 1], ring[1, i]);
            }

            SetCpuData(DynamicGeometryContainer.CreateCpuData());
            if (Context.OpenGl != null)
            {
#if !MANA_BROWSER
                VertexBufferObject = DynamicGeometryContainer.CreateVertexBufferObject(Context);
#endif
            }
        }
    }
}
