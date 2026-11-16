using System.Diagnostics;
using Mana;
using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Shapes;
using ManaCore.Diagnostics;
using ManaCore.Graphics;
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
            GeoTiffLibrary? heightLibrary,
            string? imageryPath = null,
            PlanetImageryDetail? detail = null)
            : base(context)
        {
            ImageryPath = imageryPath;
            Detail = detail;
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

        /// <summary>An equirectangular picture of the whole planet the patch wears instead of its biome colours; null for those.</summary>
        public string? ImageryPath { get; private set; }

        /// <summary>Puts on (or takes off) the planet's picture and regenerates, so the vertex colours turn white under it.</summary>
        public void SetImagery(string? imageryPath)
        {
            ImageryPath = imageryPath;
            if (Materials.Length > 0)
            {
                ApplyImagery(Materials[0]);
            }
            GenerateData();
        }

        /// <summary>
        /// Puts on a picture without rebuilding the geometry: the published vertices keep their positions and normals,
        /// only their colours turn white and their coordinates become the picture's. A fraction of a regeneration, so
        /// the planet can be dressed patch by patch off the main thread. False when there is nothing published yet.
        /// </summary>
        public bool TryDressInPlace(string imageryPath)
        {
            if (CpuData == null)
            {
                return false;
            }
            ImageryPath = imageryPath;
            return Dress();
        }

        /// <summary>
        /// A sharper picture of this patch's stretch (null: back to the planet's whole picture), put on as
        /// <see cref="TryDressInPlace"/> does; it stays on through later regenerations. Run under the planet's rebuild lock.
        /// </summary>
        public bool TryDressDetail(PlanetImageryDetail? detail)
        {
            Detail = detail;
            if (ImageryPath == null || CpuData == null)
            {
                return false;
            }
            return Dress();
        }

        private bool Dress()
        {
            if (CpuData is not { } current)
            {
                return false;
            }

            var centerU = CenterU();
            var detail = PictureDetail;
            var positions = current.Positions;
            var coordinates = new Vector2[positions.Length];
            var colors = new Vector4[positions.Length];
            Array.Fill(colors, Vector4.One);
            // The same coordinates GenerateData gives under a picture; skirts hang straight down, so their direction matches the edge's.
            Parallel.For(0, (positions.Length + 4095) / 4096, BackgroundParallelism, block =>
            {
                var end = Math.Min(positions.Length, (block + 1) * 4096);
                for (var i = block * 4096; i < end; i++)
                {
                    coordinates[i] = PictureCoordinate(GlobalUv(positions[i]), centerU, detail);
                }
            });

            SetCpuData(new ManaMeshCpuData
            {
                Indexes = current.Indexes,
                Positions = positions,
                Normals = current.Normals,
                Colors = colors,
                TextureCoordinates0 = coordinates,
                TextureCoordinates1 = coordinates,
                Unshared = true
            });
            if (Materials.Length > 0)
            {
                ApplyImagery(Materials[0]);
            }
            return true;
        }

        /// <summary>The sharper picture this patch wears over the planet's whole one, if any.</summary>
        public PlanetImageryDetail? Detail { get; private set; }

        // Only worn while the planet wears a picture at all.
        private PlanetImageryDetail? PictureDetail
        {
            get
            {
                if (ImageryPath == null)
                {
                    return null;
                }
                return Detail;
            }
        }

        private float CenterU()
        {
            var axisA = new Vector3(FaceNormal.Y, FaceNormal.Z, FaceNormal.X);
            var axisB = Vector3.Cross(FaceNormal, axisA);
            var middle = CubeSphere.WarpCubeToSphere(FaceNormal + (0.5f * (MinU + MaxU) * axisA) + (0.5f * (MinV + MaxV) * axisB));
            return GlobalUv(middle).X;
        }

        /// <summary><see cref="LatLong.ToUv"/> of a point's direction from the centre, without making a LatLong: u east from -180, v south from the north pole.</summary>
        public static Vector2 GlobalUv(Vector3 point)
        {
            var direction = Vector3.Normalize(point);
            var longitude = MathF.Atan2(-direction.X, -direction.Z);
            var latitude = MathF.Asin(Math.Clamp(direction.Y, -1.0f, 1.0f));
            return new Vector2((longitude / MathF.PI + 1.0f) * 0.5f, 0.5f - latitude / MathF.PI);
        }

        // Pictures are uploaded bottom row first, so north is at the top of v.
        private static Vector2 PictureCoordinate(Vector2 globalUv, float centerU, PlanetImageryDetail? detail)
        {
            var u = UnwrapU(globalUv.X, centerU);
            if (detail != null)
            {
                return detail.Map(u, globalUv.Y);
            }
            return new Vector2(u, 1.0f - globalUv.Y);
        }

        private void ApplyImagery(ManaMaterial material)
        {
            if (ImageryPath == null)
            {
                material.Albedo.TextureValue = null;
                return;
            }

            var path = ImageryPath;
            if (PictureDetail is { } detail)
            {
                path = detail.Path;
            }
            material.Albedo.TextureValue = new ManaTexture(Context) { OriginalFilename = path, Name = "Planet Imagery" };
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
            // Smooth: the vertex normals are averaged from the terrain itself. Flat shading derives each pixel's normal from
            // screen derivatives of the world position, which at Earth's radius is good to half a metre -- the dashed noise.
            material.FlatShaded = false;
            material.Albedo.Value = DefaultVertexColor;
            ApplyImagery(material);
            material.EnsureShaderMetadataDefaults();
            return material;
        }

        /// <summary>The longitude coordinate <paramref name="u"/> moved by whole turns to within half a turn of <paramref name="centerU"/>.</summary>
        public static float UnwrapU(float u, float centerU)
        {
            if (u - centerU > 0.5f)
            {
                return u - 1f;
            }

            if (u - centerU < -0.5f)
            {
                return u + 1f;
            }

            return u;
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
            var n = Resolution;
            var gridCount = n * n;
            // The grid (row by row, index y * n + x), then a skirt vertex under each edge vertex, edge by edge.
            // Flat arrays straight into the mesh data: an object per vertex and two per quad (the geometry container)
            // was a quarter of a million allocations a patch, and the collections they forced were the flight's hitches.
            var vertexCount = gridCount + 4 * n;
            var positions = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var texCoords = new Vector2[vertexCount];
            var colors = new Vector4[vertexCount];

            // Heights from the GPU sampler when it ran (indexed [x, y]); otherwise looked up per vertex below.
            var precomputedHeights = _pendingPrecomputedHeights;
            _pendingPrecomputedHeights = null;

            // Only the biome colours read it, and rasterizing the coastlines is the costly part of a small patch.
            GeoWaterLookup? waterLookup = null;
            if (ImageryPath == null)
            {
                waterLookup = BuildWaterLookup();
            }

            // Under a picture the colours are white, and longitudes are kept on the patch centre's side of the
            // date line so a patch across it samples one stretch of the picture, not the whole width backwards.
            var imagery = ImageryPath != null;
            var detail = PictureDetail;
            var centerU = 0.5f;
            if (imagery)
            {
                centerU = CenterU();
            }

            var computeSphereTime = Stopwatch.GetTimestamp();
            // Each row writes only its own vertices, so rows run in parallel; the height library is never written
            // meanwhile (its writers hold PlanetNode's rebuild lock, as the caller of this does).
            ForEachRow(n, y =>
            {
                var v = float.Lerp(MinV, MaxV, (float)y / (n - 1));
                for (var x = 0; x < n; x++)
                {
                    var u = float.Lerp(MinU, MaxU, (float)x / (n - 1));
                    var pointOnUnitSphere = CubeSphere.WarpCubeToSphere(FaceNormal + (u * axisA) + (v * axisB));
                    var uv = GlobalUv(pointOnUnitSphere);

                    LatLong? latLong = null;
                    float? rawHeightMeters;
                    if (precomputedHeights != null)
                    {
                        rawHeightMeters = precomputedHeights[x, y];
                    }
                    else
                    {
                        latLong = LatLong.FromPointOnSphere(pointOnUnitSphere, 1f, 0f);
                        rawHeightMeters = HeightLibrary?.FindCurrentTile(latLong)?.GetHeightAtLatLong(latLong);
                    }

                    var index = y * n + x;
                    positions[index] = pointOnUnitSphere * (Radius + (rawHeightMeters ?? 0f) * HeightScale);
                    if (imagery)
                    {
                        texCoords[index] = PictureCoordinate(uv, centerU, detail);
                        colors[index] = Vector4.One;
                        continue;
                    }

                    latLong ??= LatLong.FromPointOnSphere(pointOnUnitSphere, 1f, 0f);
                    texCoords[index] = uv;
                    colors[index] = PlanetColors.GetBiomeColor(
                        rawHeightMeters,
                        latLong.Latitude,
                        WaterDepthAt(waterLookup, latLong.Longitude, latLong.Latitude));
                }
            });
            ManaLog.WriteVerboseElapsedSignificant($"[PlanetPatchMesh] Compute sphere time", computeSphereTime);

            // Each normal is the average of the quads touching the vertex, from the displaced positions, so the
            // shading follows the real slope (the undisplaced radial direction reads as faceted).
            ForEachRow(n, y =>
            {
                for (var x = 0; x < n; x++)
                {
                    var sum = Vector3.Zero;
                    if (x > 0 && y > 0)
                    {
                        sum += QuadNormal(positions, n, x - 1, y - 1);
                    }
                    if (x < n - 1 && y > 0)
                    {
                        sum += QuadNormal(positions, n, x, y - 1);
                    }
                    if (x > 0 && y < n - 1)
                    {
                        sum += QuadNormal(positions, n, x - 1, y);
                    }
                    if (x < n - 1 && y < n - 1)
                    {
                        sum += QuadNormal(positions, n, x, y);
                    }
                    normals[y * n + x] = Vector3.Normalize(sum);
                }
            });

            // (x,y),(x+1,y),(x+1,y+1),(x,y+1) is CCW seen from outside on all six faces (axisA x axisB is the face normal).
            var indexes = new uint[(n - 1) * (n - 1) * 6 + 4 * (n - 1) * 6];
            var next = 0;
            for (var y = 0; y < n - 1; y++)
            {
                for (var x = 0; x < n - 1; x++)
                {
                    var a = (uint)(y * n + x);
                    next = AddQuad(indexes, next, a, a + 1, a + 1 + (uint)n, a + (uint)n);
                }
            }

            // Skirts: a wall hanging straight down toward the centre from each edge, filling the crack where two
            // neighbours of different resolution meet (their in-between edge vertices have no partner). Culling is
            // off for procedural meshes, so the walls' winding does not matter.
            var skirtDepth = HeightScale * SkirtDepthMeters;
            for (var edge = 0; edge < 4; edge++)
            {
                var skirtStart = gridCount + edge * n;
                for (var i = 0; i < n; i++)
                {
                    var source = EdgeVertex(edge, i, n);
                    var position = positions[source];
                    positions[skirtStart + i] = position - Vector3.Normalize(position) * skirtDepth;
                    normals[skirtStart + i] = normals[source];
                    texCoords[skirtStart + i] = texCoords[source];
                    colors[skirtStart + i] = colors[source];
                }
                for (var i = 0; i < n - 1; i++)
                {
                    next = AddQuad(indexes, next, (uint)EdgeVertex(edge, i, n), (uint)EdgeVertex(edge, i + 1, n),
                        (uint)(skirtStart + i + 1), (uint)(skirtStart + i));
                }
            }

            MinSize = new Vector3(-Radius, -Radius, -Radius);
            MaxSize = new Vector3(Radius, Radius, Radius);

            // Every patch is its own piece of ground: fingerprinting it for instancing that can never match cost tens
            // of milliseconds on the render thread per streamed patch.
            SetCpuData(new ManaMeshCpuData
            {
                Indexes = indexes,
                Positions = positions,
                Normals = normals,
                Colors = colors,
                TextureCoordinates0 = texCoords,
                TextureCoordinates1 = texCoords,
                Unshared = true
            });
            ReleaseGeometryBuildData();
        }

        // Edge vertices: the y = 0 and y = n-1 rows, then the x = 0 and x = n-1 columns.
        private static int EdgeVertex(int edge, int i, int n)
        {
            switch (edge)
            {
                case 0:
                    return i;
                case 1:
                    return (n - 1) * n + i;
                case 2:
                    return i * n;
                default:
                    return i * n + n - 1;
            }
        }

        // Two triangles (a, b, c) and (a, c, d), as the geometry container made them.
        private static int AddQuad(uint[] indexes, int next, uint a, uint b, uint c, uint d)
        {
            indexes[next] = a;
            indexes[next + 1] = b;
            indexes[next + 2] = c;
            indexes[next + 3] = a;
            indexes[next + 4] = c;
            indexes[next + 5] = d;
            return next + 6;
        }

        // Outward normal of the quad whose low corner is (x, y): cross(b - a, d - a) for the CCW order above.
        private static Vector3 QuadNormal(Vector3[] positions, int n, int x, int y)
        {
            var a = positions[y * n + x];
            return Vector3.Cross(positions[y * n + x + 1] - a, positions[(y + 1) * n + x] - a);
        }

        // Baseline/never-up-res'd patches (the common case -- see DefaultBaselineMaxPatchMeshResolution =
        // 97) sit well under this; only genuinely up-res'd/split patches reach it. Parallel.For's
        // thread-pool dispatch overhead isn't worth it for a small grid, and calling it dozens/hundreds of
        // times in a tight loop (e.g. the initial whole-globe baseline load, one call per patch) measurably
        // thrashes the thread pool rather than helping -- confirmed by testing: baseline load with an
        // unconditional Parallel.For pegged every core and took far longer than the original sequential
        // loop, even though a SINGLE large patch is much faster in isolation.
        private const int ParallelGenerationResolutionThreshold = 128;

        /// <summary>
        /// Half the cores at most for one patch's build: streaming builds several at once, and with every core taken the
        /// main thread waited to be scheduled -- frames of 30-40 ms with nothing on the main thread to show for them.
        /// </summary>
        public static readonly ParallelOptions BackgroundParallelism = new() { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) };

        private static void ForEachRow(int resolution, Action<int> body)
        {
            if (resolution >= ParallelGenerationResolutionThreshold)
            {
                Parallel.For(0, resolution, BackgroundParallelism, body);
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

    /// <summary>
    /// A sharper picture of one stretch of a planet, and where it lies in the whole picture's coordinates (u east from
    /// -180, v south from the north pole, both 0..1; u unwrapped as the patches unwrap it).
    /// </summary>
    public sealed record PlanetImageryDetail(string Key, string Path, float MinU, float MaxU, float MinV, float MaxV, float MetresPerPixel = 0.0f)
    {
        public Vector2 Map(float u, float v) => new((u - MinU) / (MaxU - MinU), 1.0f - (v - MinV) / (MaxV - MinV));
    }
}
