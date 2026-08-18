using System.Diagnostics;
using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Shapes;
using ManaCore.Diagnostics;
using ManaGeoTools.Code.Geo;
using ManaGeoTools.Code.GeoTiff;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// One cube-face patch of the planet's surface: a resolution x resolution grid spanning
    /// [MinU, MaxU] x [MinV, MaxV] of one cube face's local [-1,1] x [-1,1] space, warped onto the
    /// sphere via CubeSphere.WarpCubeToSphere (same per-vertex math the earlier whole-sphere
    /// PlanetMesh used), displaced by real elevation wherever a GeoTiffLibrary tile covers a vertex.
    /// Cube-face subdivision has no polar coordinate singularity, unlike the lat/long-degree patch
    /// scheme this replaced — patches stay uniformly sized everywhere instead of degenerating into
    /// thin wedges near the poles. Adjacent patches (including across a cube-face boundary) meet
    /// without seams because boundary vertices on both sides are computed from the same cube-space
    /// input through the same pure formula, independently landing on identical world positions.
    /// </summary>
    public sealed class PlanetPatchMesh : ManaProceduralMesh
    {
        public Vector3 FaceNormal { get; }
        public float MinU { get; }
        public float MaxU { get; }
        public float MinV { get; }
        public float MaxV { get; }
        public float Radius { get; }
        public int Resolution { get; private set; }
        public float HeightScale { get; private set; }
        public GeoTiffLibrary? HeightLibrary { get; private set; }

        // Set right before GenerateData() by ApplyHeightDataWithResolution's precomputed-heights
        // overload (GPU height-sampling path, see PlanetNode.UpresPatchAsync), consumed and cleared
        // inside GenerateData() itself. When set, Resolution x Resolution, indexed [x,y] matching
        // GenerateData()'s own vertex loop -- null means "sample HeightLibrary per-vertex on the CPU as
        // usual" (every other caller/overload).
        private float[,]? _pendingPrecomputedHeights;

        // How far (in real-world meters, scaled through HeightScale the same as actual elevation) the
        // skirt geometry around this patch's 4 edges hangs inward toward the planet's center -- see the
        // skirt-building block in GenerateData for why this exists. 500m comfortably covers the height
        // differences that show up at a boundary between two patches sampling at different resolutions
        // or from different data sources, without being large enough to visibly show through anywhere
        // reasonable terrain would actually be viewed from.
        private const float SkirtDepthMeters = 500f;

        public PlanetPatchMesh(
            ManaContext context,
            Vector3 faceNormal,
            float minU,
            float maxU,
            float minV,
            float maxV,
            float radius,
            int resolution,
            float heightScale,
            GeoTiffLibrary? heightLibrary)
            : base(context)
        {
            FaceNormal = faceNormal;
            MinU = minU;
            MaxU = maxU;
            MinV = minV;
            MaxV = maxV;
            Radius = radius;
            Resolution = Math.Max(2, resolution);
            HeightScale = heightScale;
            HeightLibrary = heightLibrary;
            DefaultVertexColor = Colors.White;

            GenerateData();
        }

        /// <summary>
        /// Swaps in real elevation/color data (e.g. once a background GeoTIFF decode finishes) and
        /// regenerates. Safe to call from any thread — see SetCpuData's doc comment on ManaMesh.
        /// </summary>
        public void ApplyHeightData(GeoTiffLibrary heightLibrary)
        {
            HeightLibrary = heightLibrary;
            GenerateData();
        }

        /// <summary>
        /// Same as ApplyHeightData, but also sets the patch's own vertex grid resolution to match the
        /// detail level the caller picked for the source data. Plain ApplyHeightData re-samples the
        /// same fixed-size grid with new data, which only improves per-vertex accuracy; it can't add
        /// visible density since the vertex count never changes. On-demand up-res uses this path so a
        /// patch can match the streamed source-data sample span directly.
        /// </summary>
        public void ApplyHeightDataWithResolution(GeoTiffLibrary heightLibrary, int resolution)
        {
            HeightLibrary = heightLibrary;
            Resolution = Math.Max(2, resolution);
            GenerateData();
        }

        /// <summary>
        /// Same as ApplyHeightDataWithResolution, but with per-vertex heights already sampled elsewhere
        /// (the GPU compute path, see IHeightSamplingComputeDevice/PlanetNode.UpresPatchAsync) instead of
        /// looked up per-vertex against heightLibrary here. heightLibrary is still stored (kept
        /// consistent with the non-precomputed overload, and used by anything that reads it later, e.g.
        /// HeightExaggeration's Rebuild path) but is NOT consulted for sampling this time.
        /// precomputedHeights must be resolution x resolution, indexed [x,y] the same way
        /// GenerateData()'s own vertex loop is.
        /// </summary>
        public void ApplyHeightDataWithResolution(GeoTiffLibrary heightLibrary, int resolution, float[,] precomputedHeights)
        {
            HeightLibrary = heightLibrary;
            Resolution = Math.Max(2, resolution);
            _pendingPrecomputedHeights = precomputedHeights;
            GenerateData();
        }

        /// <summary>Updates the height displacement scale (e.g. from an Inspector edit) and regenerates.</summary>
        public void Rebuild(float heightScale)
        {
            HeightScale = heightScale;
            GenerateData();
        }

        /// <summary>
        /// Computes the (longitude, latitude) degrees grid GenerateData() would place vertices at for the
        /// given bounds/resolution, without sampling height or building any geometry -- used by the GPU
        /// height-sampling path (PlanetNode.UpresPatchAsync) to gather sample points BEFORE calling
        /// ApplyHeightDataWithResolution, since the grid must be computed at the TARGET resolution, which
        /// is generally different from whatever this mesh's current Resolution is. Static, and takes the
        /// same bounds parameters the constructor does, rather than reading them off an instance, so a
        /// caller can compute this before the mesh's own Resolution/HeightLibrary are updated. Uses the
        /// exact same per-vertex formula as GenerateData()'s own loop so GPU-sampled heights line up with
        /// this mesh's actual vertex positions point-for-point.
        /// </summary>
        public static Vector2[,] ComputeLonLatGrid(Vector3 faceNormal, float minU, float maxU, float minV, float maxV, int resolution)
        {
            resolution = Math.Max(2, resolution);
            var axisA = new Vector3(faceNormal.Y, faceNormal.Z, faceNormal.X);
            var axisB = Vector3.Cross(faceNormal, axisA);
            var grid = new Vector2[resolution, resolution];

            ForEachRow(resolution, y =>
            {
                var ty = (float)y / (resolution - 1);
                var v = float.Lerp(minV, maxV, ty);

                for (var x = 0; x < resolution; x++)
                {
                    var tx = (float)x / (resolution - 1);
                    var u = float.Lerp(minU, maxU, tx);

                    var pointOnUnitCube = faceNormal + (u * axisA) + (v * axisB);
                    var pointOnUnitSphere = CubeSphere.WarpCubeToSphere(pointOnUnitCube);
                    var latLong = LatLong.FromPointOnSphere(pointOnUnitSphere, 1f, 0f);
                    grid[x, y] = new Vector2(latLong.Longitude, latLong.Latitude);
                }
            });

            return grid;
        }

        // The patch's lat/long bounding box from its perimeter -- a cube-face patch isn't axis-aligned
        // in lat/long, and its edges bow outward past the corners, so sampling the border is the cheap
        // way to bound it without a second full grid pass.
        //
        // Patches spanning a pole or the antimeridian get a longitude range covering most of the globe,
        // which makes the raster coarse but never wrong -- the lookup is still per-vertex lat/long.
        private GeoWaterLookup? BuildWaterLookup()
        {
            var mask = GeoWaterMask.Shared;
            if (mask == null || !mask.HasAnyData)
            {
                return null;
            }

            const int samplesPerEdge = 32;
            var axisA = new Vector3(FaceNormal.Y, FaceNormal.Z, FaceNormal.X);
            var axisB = Vector3.Cross(FaceNormal, axisA);
            var minLon = float.MaxValue;
            var maxLon = float.MinValue;
            var minLat = float.MaxValue;
            var maxLat = float.MinValue;

            Span<(float U, float V)> perimeter = stackalloc (float, float)[4];
            for (var i = 0; i <= samplesPerEdge; i++)
            {
                var t = (float)i / samplesPerEdge;
                perimeter[0] = (float.Lerp(MinU, MaxU, t), MinV);
                perimeter[1] = (float.Lerp(MinU, MaxU, t), MaxV);
                perimeter[2] = (MinU, float.Lerp(MinV, MaxV, t));
                perimeter[3] = (MaxU, float.Lerp(MinV, MaxV, t));

                foreach (var (u, v) in perimeter)
                {
                    var pointOnUnitSphere = CubeSphere.WarpCubeToSphere(FaceNormal + (u * axisA) + (v * axisB));
                    var latLong = LatLong.FromPointOnSphere(pointOnUnitSphere, 1f, 0f);
                    minLon = MathF.Min(minLon, latLong.Longitude);
                    maxLon = MathF.Max(maxLon, latLong.Longitude);
                    minLat = MathF.Min(minLat, latLong.Latitude);
                    maxLat = MathF.Max(maxLat, latLong.Latitude);
                }
            }

            if (maxLon <= minLon || maxLat <= minLat)
            {
                return null;
            }

            return mask.Rasterize(minLon, maxLon, minLat, maxLat, Resolution, Resolution);
        }

        public override ManaMaterial GenerateMaterial()
        {
            // The base ManaProceduralMesh material leaves Shader null, which only gets backfilled by
            // the OpenGL-only finalize step — under WebGPU that leaves the Inspector showing no PBR
            // channels. Assign it explicitly instead, mirroring ManaWaterPlane.GenerateMaterial().
            var material = new ManaMaterial(Context) { Name = "Planet Patch Material" };
            material.Shader = new ManaPbrShader(Context);
            material.FlatShaded = true;
            material.Albedo.Value = DefaultVertexColor;
            material.EnsureShaderMetadataDefaults();
            return material;
        }

        // -1 means "not water", which is what PlanetColors expects for the land ramp.
        private static float WaterDepthAt(GeoWaterLookup? lookup, float longitude, float latitude)
        {
            if (lookup == null || !lookup.IsWater(longitude, latitude))
            {
                return -1f;
            }

            return lookup.GetWaterDepth01(longitude, latitude);
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            var axisA = new Vector3(FaceNormal.Y, FaceNormal.Z, FaceNormal.X);
            var axisB = Vector3.Cross(FaceNormal, axisA);

            // Phase 1 (parallel): the actual expensive part -- height-tile lookup, trig, LatLong/UV/color
            // computation -- writes only to each vertex's own (x,y) slot in plain arrays, so it's safe to
            // spread across cores. HeightLibrary is never mutated during this call (every call site that
            // adds a tile holds PlanetNode's _rebuildLock for the full duration of whatever triggered this
            // GenerateData(), same as this call itself), so concurrent reads here have no concurrent
            // writer to race with -- exactly .NET's documented safe case for unsynchronized Dictionary/
            // List reads.
            var positions = new Vector3[Resolution, Resolution];
            var placeholderNormals = new Vector3[Resolution, Resolution];
            var texCoords = new Vector2[Resolution, Resolution];
            var colors = new Vector4[Resolution, Resolution];

            // Consumed here (not a per-vertex lookup below) -- GPU path already sampled every vertex's
            // height in one batched compute dispatch (see IHeightSamplingComputeDevice/
            // PlanetNode.UpresPatchAsync); this whole loop still needs to run either way, since UV/color
            // computation and the position/pointOnUnitSphere math don't depend on WHERE the height came
            // from, only the per-vertex HeightLibrary.FindCurrentTile/GetHeightAtLatLong call is skipped.
            var precomputedHeights = _pendingPrecomputedHeights;
            _pendingPrecomputedHeights = null;

            // Built before the parallel loop below so every row can read it lock-free. Null when the
            // Natural Earth data isn't present, which falls back to elevation-only colouring.
            var waterLookup = BuildWaterLookup();

            var computeSphereTime = Stopwatch.GetTimestamp();

            ForEachRow(Resolution, y =>
            {
                var ty = (float)y / (Resolution - 1);
                var v = float.Lerp(MinV, MaxV, ty);

                for (var x = 0; x < Resolution; x++)
                {
                    var tx = (float)x / (Resolution - 1);
                    var u = float.Lerp(MinU, MaxU, tx);

                    var pointOnUnitCube = FaceNormal + (u * axisA) + (v * axisB);
                    var pointOnUnitSphere = CubeSphere.WarpCubeToSphere(pointOnUnitCube);
                    var latLong = LatLong.FromPointOnSphere(pointOnUnitSphere, 1f, 0f);

                    float? rawHeightMeters;
                    if (precomputedHeights != null)
                    {
                        rawHeightMeters = precomputedHeights[x, y];
                    }
                    else
                    {
                        var tile = HeightLibrary?.FindCurrentTile(latLong);
                        rawHeightMeters = tile?.GetHeightAtLatLong(latLong);
                    }

                    var displacement = (rawHeightMeters ?? 0f) * HeightScale;
                    positions[x, y] = pointOnUnitSphere * (Radius + displacement);
                    // Placeholder normal -- overwritten below once every vertex's displaced position
                    // is known, from the actual built geometry rather than the undisplaced sphere.
                    placeholderNormals[x, y] = pointOnUnitSphere;
                    texCoords[x, y] = latLong.ToUv();
                    colors[x, y] = PlanetColors.GetBiomeColor(
                        rawHeightMeters,
                        latLong.Latitude,
                        WaterDepthAt(waterLookup, latLong.Longitude, latLong.Latitude));
                }
            });

            ManaLog.WriteVerboseElapsedSignificant($"[PlanetPatchMesh] Compute sphere time", computeSphereTime);

            // Phase 2 (sequential): AddPoint appends to DynamicGeometryContainer's shared Verts list and
            // assigns sequential Index values, so this part can't be parallelized -- but it's now pure
            // array-to-container copying with no lookups/trig/allocation left in it.
            var vertexGenerationStart = Stopwatch.GetTimestamp();
            
            var verts = new ManaGeometryVertex[Resolution, Resolution];
            for (var y = 0; y < Resolution; y++)
            {
                for (var x = 0; x < Resolution; x++)
                {
                    var vert = DynamicGeometryContainer.AddPoint(positions[x, y], placeholderNormals[x, y]);
                    vert.TexCoord0 = texCoords[x, y];
                    vert.Color = colors[x, y];
                    verts[x, y] = vert;
                }
            }

            ManaLog.WriteVerboseElapsedSignificant($"[PlanetPatchMesh] Vertex generation time", vertexGenerationStart);

            // Real elevation displacement can be substantial (HeightExaggeration commonly pushes it
            // well past what's visually negligible), so lighting the surface with the raw undisplaced
            // sphere-radial direction (the old behavior) noticeably mismatches the actual geometry --
            // it reads as faceted/triangulated because the shading doesn't track the true local slope.
            // Fix: replace each vertex's normal with the average of the face normals of every quad
            // that touches it, computed directly from the already-displaced positions above -- this
            // guarantees the lighting normal can never disagree with what's actually rendered (unlike
            // a separate finite-difference estimate against the height function, which would introduce
            // its own subtly different notion of the surface).
            //
            // Safe to parallelize like phase 1 above: every iteration only READS neighboring verts[,]
            // positions (fixed since phase 2 finished) and writes its OWN vertex's .Normal -- two threads
            // never touch the same ManaGeometryVertex instance.
            
            var normalCalculationStart = Stopwatch.GetTimestamp();
            
            ForEachRow(Resolution, y =>
            {
                for (var x = 0; x < Resolution; x++)
                {
                    var normalSum = Vector3.Zero;
                    if (x > 0 && y > 0)
                    {
                        normalSum += QuadNormal(verts[x - 1, y - 1], verts[x, y - 1], verts[x - 1, y]);
                    }
                    if (x < Resolution - 1 && y > 0)
                    {
                        normalSum += QuadNormal(verts[x, y - 1], verts[x + 1, y - 1], verts[x, y]);
                    }
                    if (x > 0 && y < Resolution - 1)
                    {
                        normalSum += QuadNormal(verts[x - 1, y], verts[x, y], verts[x - 1, y + 1]);
                    }
                    if (x < Resolution - 1 && y < Resolution - 1)
                    {
                        normalSum += QuadNormal(verts[x, y], verts[x + 1, y], verts[x, y + 1]);
                    }
                    verts[x, y].Normal = Vector3.Normalize(normalSum);
                }
            });
            ManaLog.WriteVerboseElapsedSignificant($"[PlanetPatchMesh] Normal calculation time", normalCalculationStart);

            // (x,y),(x+1,y),(x+1,y+1),(x,y+1) is CCW-as-seen-from-outside for every one of the 6
            // axis-aligned face normals (axisA x axisB == FaceNormal algebraically for all of them),
            // axis-aligned face normals (axisA x axisB == FaceNormal algebraically for all of them),
            // so one consistent quad order works regardless of which face this patch is on.
            var faceGenStart = Stopwatch.GetTimestamp();

            for (var y = 0; y < Resolution - 1; y++)
            {
                for (var x = 0; x < Resolution - 1; x++)
                {
                    var a = verts[x, y];
                    var b = verts[x + 1, y];
                    var c = verts[x + 1, y + 1];
                    var d = verts[x, y + 1];
                    DynamicGeometryContainer.CreateFacesFromQuad(a, b, c, d);
                }
            }
            ManaLog.WriteVerboseElapsedSignificant($"[PlanetPatchMesh] Face generation time", faceGenStart);


            // Skirts: a thin wall of extra geometry hanging inward (toward the planet center) from
            // each of the patch's 4 edges. Adjacent patches' shared boundary vertices land on
            // identical world positions when both sides compute them from the same cube-space input at
            // the same resolution (see this class's doc comment) -- but that guarantee breaks the
            // moment two neighbors have DIFFERENT resolution along their shared edge (e.g. one has been
            // on-demand up-res'd via ApplyHeightDataWithResolution and the other hasn't), since the
            // finer side's in-between edge vertices then have no partner on the coarser side, leaving a
            // visible crack. Skirts don't fix the underlying mismatch, they just fill the gap with
            // geometry so it isn't visible -- always added, on every patch, since any patch could end
            // up neighboring a differently-resolved one at any time. CullMode.None is used for every
            // procedural mesh pipeline in this engine (confirmed in ManaWebGpuContext.Pipelines.cs), so
            // the skirt quads' winding direction doesn't need to be carefully oriented.
            var skirtGenStart = Stopwatch.GetTimestamp();
            {
                var skirtDepth = HeightScale * SkirtDepthMeters;
                AddSkirtStrip(GetColumn(verts, row: 0), skirtDepth);
                AddSkirtStrip(GetColumn(verts, row: Resolution - 1), skirtDepth);
                AddSkirtStrip(GetRow(verts, col: 0), skirtDepth);
                AddSkirtStrip(GetRow(verts, col: Resolution - 1), skirtDepth);
            }
            ManaLog.WriteVerboseElapsedSignificant($"[PlanetPatchMesh] Skirt generation time", skirtGenStart);

            MinSize = new Vector3(-Radius, -Radius, -Radius);
            MaxSize = new Vector3(Radius, Radius, Radius);

            // Explicit SetCpuData (now public) so ApplyHeightData()/Rebuild() calling GenerateData()
            // again after the mesh has already rendered once actually takes effect — see
            // GeoTiffTerrainMesh for the fuller explanation of why this matters.
            var cpuDataGenStart = Stopwatch.GetTimestamp();
            PublishGeneratedGeometry();
            ManaLog.WriteVerboseElapsedSignificant($"[PlanetPatchMesh] CPU data generation time", cpuDataGenStart);
        }

        private static ManaGeometryVertex[] GetColumn(ManaGeometryVertex[,] verts, int row)
        {
            var result = new ManaGeometryVertex[verts.GetLength(0)];
            for (var x = 0; x < result.Length; x++)
            {
                result[x] = verts[x, row];
            }
            return result;
        }

        private static ManaGeometryVertex[] GetRow(ManaGeometryVertex[,] verts, int col)
        {
            var result = new ManaGeometryVertex[verts.GetLength(1)];
            for (var y = 0; y < result.Length; y++)
            {
                result[y] = verts[col, y];
            }
            return result;
        }

        private void AddSkirtStrip(ManaGeometryVertex[] edgeVerts, float skirtDepth)
        {
            for (var i = 0; i < edgeVerts.Length - 1; i++)
            {
                var a = edgeVerts[i];
                var b = edgeVerts[i + 1];
                var skirtA = AddSkirtVertex(a, skirtDepth);
                var skirtB = AddSkirtVertex(b, skirtDepth);
                DynamicGeometryContainer.CreateFacesFromQuad(a, b, skirtB, skirtA);
            }
        }

        // Position is pointOnUnitSphere scaled by a positive factor (Radius + displacement), so
        // normalizing it recovers that original outward radial direction exactly -- pushing inward
        // along it (rather than along the vertex's own shaded/averaged Normal) keeps the skirt going
        // straight toward the planet's center regardless of local terrain slope.
        private ManaGeometryVertex AddSkirtVertex(ManaGeometryVertex source, float skirtDepth)
        {
            var radialDirection = Vector3.Normalize(source.Position);
            var skirtPosition = source.Position - (radialDirection * skirtDepth);
            var skirtVert = DynamicGeometryContainer.AddPoint(skirtPosition, source.Normal);
            skirtVert.TexCoord0 = source.TexCoord0;
            skirtVert.Color = source.Color;
            return skirtVert;
        }

        // Outward-facing flat normal of quad (a,b,c,d) in the same CCW-as-seen-from-outside vertex
        // order used everywhere else in this file -- cross(b-a, d-a) is the right-hand-rule normal for
        // that winding (verified against the axis-aligned case: a=(0,0,0), b=(1,0,0), d=(0,1,0) gives
        // cross((1,0,0),(0,1,0)) = (0,0,1), the correct outward +Z for a CCW square viewed from +Z).
        private static Vector3 QuadNormal(ManaGeometryVertex a, ManaGeometryVertex b, ManaGeometryVertex d)
        {
            return Vector3.Cross(b.Position - a.Position, d.Position - a.Position);
        }

        // Baseline/never-up-res'd patches (the common case -- see DefaultBaselineMaxPatchMeshResolution =
        // 97) sit well under this; only genuinely up-res'd/split patches reach it. Parallel.For's
        // thread-pool dispatch overhead isn't worth it for a small grid, and calling it dozens/hundreds of
        // times in a tight loop (e.g. the initial whole-globe baseline load, one call per patch) measurably
        // thrashes the thread pool rather than helping -- confirmed by testing: baseline load with an
        // unconditional Parallel.For pegged every core and took far longer than the original sequential
        // loop, even though a SINGLE large patch is much faster in isolation.
        private const int ParallelGenerationResolutionThreshold = 128;

        private static void ForEachRow(int resolution, Action<int> body)
        {
            if (resolution >= ParallelGenerationResolutionThreshold)
            {
                Parallel.For(0, resolution, body);
            }
            else
            {
                for (var y = 0; y < resolution; y++)
                {
                    body(y);
                }
            }
        }
    }
}
