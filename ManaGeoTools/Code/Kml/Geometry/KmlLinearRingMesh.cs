using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Shapes;

namespace ManaGeoTools.Code.Kml.Geometry
{
    /// <summary>
    /// Builds a triangulated sphere-projected surface for a KML LinearRing. The ring's LatLong
    /// points are triangulated as plain (Longitude, Latitude) 2D coordinates — a valid, consistent
    /// 2D basis for a single simple, non-antimeridian-crossing ring — then the resulting triangle
    /// indices are applied against the sphere-projected Vector3 vertex positions for the mesh. This
    /// entirely replaces the source's `Geometry2D.triangulate_polygon(Coordinates.Data)` call, which
    /// passed sphere-projected Vector3 points into a Vector2[]-typed Godot API (a latent
    /// GDScript-coercion bug in the source) — this port triangulates on genuinely-2D points instead.
    /// </summary>
    public sealed class KmlLinearRingMesh : ManaProceduralMesh
    {
        public static readonly Vector4 DefaultRingColor = new(0.45f, 0.65f, 1.0f, 0.28f);

        private readonly KmlLinearRing _ring;

        public KmlLinearRingMesh(ManaContext context, KmlLinearRing ring, Vector4? color = null) : base(context)
        {
            _ring = ring;
            DefaultVertexColor = color ?? DefaultRingColor;
            GenerateData();
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            var dataLatLong = _ring.Coordinates.DataLatLong;
            var points2D = dataLatLong.Select(ll => new Vector2(ll.Longitude, ll.Latitude)).ToList();
            var triangles = PolygonTriangulator.Triangulate(points2D);
            if (triangles.Count == 0)
            {
                Console.WriteLine("[Kml] LinearRing triangulation produced no triangles; skipping mesh.");
                return;
            }

            var verts = new ManaGeometryVertex[dataLatLong.Count];
            for (var i = 0; i < verts.Length; i++)
            {
                var latLong = dataLatLong[i];
                var position = latLong.ToSphere(Kml.EarthRadius, Kml.AltitudeScale);
                var vert = DynamicGeometryContainer.AddPoint(position, Vector3.Normalize(position));
                vert.TexCoord0 = latLong.ToUv();
                vert.Color = DefaultVertexColor;
                verts[i] = vert;
            }

            // The source explicitly flips winding after triangulation before building the surface;
            // replicate that here for visual/backface parity.
            var flatTriangleVerts = new List<ManaGeometryVertex>(triangles.Count);
            for (var t = 0; t < triangles.Count; t += 3)
            {
                flatTriangleVerts.Add(verts[triangles[t]]);
                flatTriangleVerts.Add(verts[triangles[t + 2]]);
                flatTriangleVerts.Add(verts[triangles[t + 1]]);
            }
            DynamicGeometryContainer.CreateFacesFromPoints(flatTriangleVerts);

            // Explicit SetCpuData (now public) so a second GenerateData()/RebuildGeometry() call
            // after the mesh has already rendered once actually takes effect — see GeoTiffTerrainMesh.
            PublishGeneratedGeometry();
        }

        public override ManaMaterial GenerateMaterial()
        {
            // Known limitation: the source's default LinearRing material is unshaded, alpha-blended,
            // and double-sided (Godot BaseMaterial3D flags). An equivalent unlit/translucent/
            // double-sided shader needs new shader + blend/cull-mode plumbing that doesn't exist yet
            // in Mana, and is out of scope for this data/geometry-focused port. Ship a standard
            // ManaMaterial tinted with the ring's color instead.
            var material = new ManaMaterial(Context) { Name = "Kml LinearRing Material" };
            material.Albedo.Value = DefaultVertexColor;
            return material;
        }
    }
}
