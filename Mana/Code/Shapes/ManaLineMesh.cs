namespace Mana.Code.Shapes
{
    /// <summary>One polyline: a sequence of points in the owning mesh's local space, plus whether the
    /// last point should connect back to the first (a closed loop, e.g. a country border ring) or not
    /// (an open line, e.g. a flight path).</summary>
    public readonly struct ManaLinePolyline
    {
        public IReadOnlyList<Vector3> Points { get; }
        public bool Closed { get; }

        public ManaLinePolyline(IReadOnlyList<Vector3> points, bool closed)
        {
            Points = points;
            Closed = closed;
        }
    }

    public enum ManaLineProfile
    {
        Tube,
        Ribbon,

        /// <summary>
        /// Like Ribbon, but vertices sit on the CENTERLINE and carry their signed unit offset in vertex
        /// colour, leaving the widening to the vertex shader (see draped_line.wgsl). Lets width track
        /// camera distance per-vertex with no rebuild -- baked width would need a whole-mesh regenerate
        /// every time the camera moved far enough.
        /// </summary>
        ScreenRibbon
    }

    /// <summary>
    /// Generic thin-line-in-3D-space mesh: any number of polylines, each swept into either a flat
    /// surface ribbon or a continuous square-cross-section tube (shared vertices between segments, a
    /// mitered cross-section at every joint that automatically bisects whatever angle the path turns
    /// through) rather than a chain of independent boxes. An earlier version built each segment as its
    /// own separate oriented box, which left gaps at any turn sharper than dead-straight (each box's
    /// flat end-cap only lines up with its neighbor's when both point the same direction) and tried to
    /// patch that with an extra filler cube at every joint -- a real hack, replaced entirely by the
    /// sweep below.
    ///
    /// The hard part of a ribbon sweep is orienting the cross-section consistently along the path
    /// without twisting: naively recomputing "some perpendicular basis" independently at each point
    /// (the way the old per-segment version picked its rotation) works fine when every segment is an
    /// isolated rigid box, but produces a visibly kinked/pinched ribbon once vertices are shared
    /// between segments, because there's no guarantee consecutive bases agree with each other. This
    /// uses parallel transport instead: each frame is derived from the previous one by the minimal
    /// rotation that takes the previous tangent to the new one, so the cross-section only ever turns
    /// as much as the path itself does. Closed loops additionally get their accumulated rotation
    /// (there's no guarantee a transported frame returns to exactly where it started after a full
    /// loop -- a real geometric effect, not a bug) measured and evenly cancelled back out across every
    /// frame, so the seam where the loop closes doesn't kink either.
    ///
    /// Originally written specifically for country border lines (see ManaGeoTools' CountryBorderLines,
    /// which decimates + projects lat/long boundary rings onto a sphere and hands the resulting points
    /// here) -- pulled out to engine level since nothing about the line-building itself is
    /// planet-specific. Any node that wants to draw lines (flight paths, debug gizmos, KML geometry,
    /// etc.) can build a list of ManaLinePolylines and use this directly.
    /// </summary>
    public class ManaLineMesh : ManaProceduralMesh
    {
        public IReadOnlyList<ManaLinePolyline> Lines { get; }
        public float Thickness { get; private set; }
        public float EmissionStrength { get; }
        public ManaLineProfile Profile { get; }

        public ManaLineMesh(
            ManaContext context,
            IReadOnlyList<ManaLinePolyline> lines,
            float thickness,
            Vector4 color,
            float emissionStrength = 0f,
            ManaLineProfile profile = ManaLineProfile.Tube)
            : base(context)
        {
            Lines = lines;
            Thickness = thickness;
            EmissionStrength = emissionStrength;
            Profile = profile;
            DefaultVertexColor = color;

            GenerateData();
        }

        // Distance at which a ScreenRibbon reaches its full Thickness. Nearer than this it thins down
        // toward the floor below, further it is capped. Chosen so the whole-planet view (the distance
        // these overlays were tuned at) lands on the authored thickness.
        private const float ScreenRibbonReferenceDistance = 120.0f;

        // Keeps the Thickness argument meaningful for ScreenRibbon, whose geometry carries no width at
        // all -- without this the constructor parameter would silently do nothing. These are seeded as
        // material uniforms, so they stay live-editable in the Inspector afterwards.
        private void SeedScreenRibbonWidth(ManaMaterial material)
        {
            var halfThickness = Thickness * 0.5f;
            material.SetShaderUniform("widthPerDistance", halfThickness / ScreenRibbonReferenceDistance);
            material.SetShaderUniform("minHalfWidth", halfThickness * 0.05f);
            material.SetShaderUniform("maxHalfWidth", halfThickness);
        }

        /// <summary>Live-editable thickness (e.g. from an Inspector slider) -- a real geometry change,
        /// so this regenerates immediately. Color and emission are NOT baked into the geometry (see
        /// AddSegment's use of Vector4.One below) specifically so they can be edited live by mutating
        /// the node's MeshRendererComponent.Material directly, with no rebuild needed at all -- see
        /// VoxelEditorGameMode's Inspector hook for line meshes.</summary>
        public void SetThickness(float thickness)
        {
            Thickness = thickness;
            GenerateData();
        }

        public override ManaMaterial GenerateMaterial()
        {
            var material = new ManaMaterial(Context) { Name = $"{GetShapeDisplayName()} Material" };
            material.Shader = Profile == ManaLineProfile.ScreenRibbon
                ? new ManaDrapedLineShader(Context)
                : new ManaPbrShader(Context);
            material.Albedo.Value = DefaultVertexColor;
            if (EmissionStrength > 0f)
            {
                // Optional: lets lines read clearly regardless of scene lighting (e.g. country
                // borders on a planet's unlit/night side) without forcing every use of this mesh to
                // glow -- EmissionStrength defaults to 0, which leaves the material non-emissive.
                material.Emission.Value = DefaultVertexColor;
                material.EmissionStrength = EmissionStrength;
            }
            if (Profile == ManaLineProfile.ScreenRibbon)
            {
                SeedScreenRibbonWidth(material);
            }

            material.EnsureShaderMetadataDefaults();
            return material;
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            var hasAnyPoint = false;

            foreach (var line in Lines)
            {
                var points = DeduplicatePoints(line.Points, line.Closed);
                if (points.Count < 2)
                {
                    continue;
                }

                if (Profile == ManaLineProfile.ScreenRibbon)
                {
                    AddFlatRibbon(points, line.Closed, shaderExpanded: true);
                }
                else if (Profile == ManaLineProfile.Ribbon)
                {
                    AddFlatRibbon(points, line.Closed, shaderExpanded: false);
                }
                else
                {
                    AddRibbon(points, line.Closed);
                }

                foreach (var point in points)
                {
                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                    hasAnyPoint = true;
                }
            }

            var margin = new Vector3(Thickness);
            MinSize = hasAnyPoint ? min - margin : Vector3.Zero;
            MaxSize = hasAnyPoint ? max + margin : Vector3.Zero;

            SetCpuData(DynamicGeometryContainer.CreateCpuData());
        }

        // Zero-length segments (consecutive duplicate points -- real-world boundary data routinely
        // has these, e.g. a ring's explicit closing point repeating its first) leave the tangent at
        // that point undefined; simplest correct fix is to just not have them.
        private static List<Vector3> DeduplicatePoints(IReadOnlyList<Vector3> points, bool closed)
        {
            const float epsilonSq = 0.0001f * 0.0001f;
            var result = new List<Vector3>(points.Count) { points[0] };
            for (var i = 1; i < points.Count; i++)
            {
                if (Vector3.DistanceSquared(result[^1], points[i]) > epsilonSq)
                {
                    result.Add(points[i]);
                }
            }
            // A closed ring's last point commonly duplicates its first (see above) -- drop it here too
            // so AddRibbon's own wrap-around (index % count) doesn't produce a trailing zero-length
            // closing segment. Only for closed lines: an open line's last point is a real endpoint and
            // must never be silently dropped just because it happens to coincide with the first.
            if (closed && result.Count > 1 && Vector3.DistanceSquared(result[0], result[^1]) <= epsilonSq)
            {
                result.RemoveAt(result.Count - 1);
            }
            return result;
        }

        private void AddRibbon(List<Vector3> points, bool closed)
        {
            var count = points.Count;
            if (closed && count < 3)
            {
                return; // a 2-point "closed loop" is degenerate -- nothing meaningful to sweep
            }

            var tangents = ComputeTangents(points, closed);
            var (normals, binormals) = ComputeParallelTransportFrames(tangents, closed);

            var halfThickness = Thickness * 0.5f;
            var ring = new ManaGeometryVertex[count, 4];
            for (var i = 0; i < count; i++)
            {
                var center = points[i];
                var n = normals[i] * halfThickness;
                var b = binormals[i] * halfThickness;

                AddRingVertex(ring, i, 0, center + n + b, n + b);
                AddRingVertex(ring, i, 1, center - n + b, -n + b);
                AddRingVertex(ring, i, 2, center - n - b, -n - b);
                AddRingVertex(ring, i, 3, center + n - b, n - b);
            }

            var connectionCount = closed ? count : count - 1;
            for (var i = 0; i < connectionCount; i++)
            {
                var next = (i + 1) % count;
                for (var side = 0; side < 4; side++)
                {
                    var nextSide = (side + 1) % 4;
                    DynamicGeometryContainer.CreateFacesFromQuad(
                        ring[i, side], ring[i, nextSide], ring[next, nextSide], ring[next, side]);
                }
            }

            if (!closed)
            {
                // Open lines need caps at both ends or the tube reads as hollow from that angle.
                DynamicGeometryContainer.CreateFacesFromQuad(ring[0, 0], ring[0, 1], ring[0, 2], ring[0, 3]);
                DynamicGeometryContainer.CreateFacesFromQuad(ring[count - 1, 3], ring[count - 1, 2], ring[count - 1, 1], ring[count - 1, 0]);
            }
        }

        private void AddFlatRibbon(List<Vector3> points, bool closed, bool shaderExpanded)
        {
            var count = points.Count;
            if (closed && count < 3)
            {
                return;
            }

            var tangents = ComputeTangents(points, closed);
            var halfThickness = Thickness * 0.5f;
            var left = new ManaGeometryVertex[count];
            var right = new ManaGeometryVertex[count];

            for (var i = 0; i < count; i++)
            {
                var center = points[i];
                var outward = center.LengthSquared() > 0.000001f
                    ? Vector3.Normalize(center)
                    : ArbitraryPerpendicular(tangents[i]);
                var side = Vector3.Cross(outward, tangents[i]);
                if (side.LengthSquared() <= 0.000001f)
                {
                    side = ArbitraryPerpendicular(tangents[i]);
                }
                var sideDirection = Vector3.Normalize(side);
                if (shaderExpanded)
                {
                    // Both vertices sit on the centerline; the shader offsets them along the signed
                    // direction carried in vertex colour. Colour is free here -- ribbons have always
                    // written white, with the material as the sole source of colour.
                    left[i] = AddRibbonVertex(center, outward, new Vector4(sideDirection, 1f));
                    right[i] = AddRibbonVertex(center, outward, new Vector4(-sideDirection, 1f));
                }
                else
                {
                    var offset = sideDirection * halfThickness;
                    left[i] = AddRibbonVertex(center + offset, outward, Vector4.One);
                    right[i] = AddRibbonVertex(center - offset, outward, Vector4.One);
                }
            }

            var connectionCount = closed ? count : count - 1;
            for (var i = 0; i < connectionCount; i++)
            {
                var next = (i + 1) % count;
                DynamicGeometryContainer.CreateFacesFromQuad(left[i], right[i], right[next], left[next]);
            }
        }

        private ManaGeometryVertex AddRibbonVertex(Vector3 position, Vector3 normal, Vector4 color)
        {
            var vert = DynamicGeometryContainer.AddPoint(position, Vector3.Normalize(normal));
            vert.Color = color;
            return vert;
        }

        private void AddRingVertex(ManaGeometryVertex[,] ring, int i, int side, Vector3 position, Vector3 outward)
        {
            var vert = DynamicGeometryContainer.AddPoint(position, Vector3.Normalize(outward));
            // White, not DefaultVertexColor -- see SetThickness's doc comment: the shader multiplies
            // vertex color by material albedo, so baking white here means the material's
            // Albedo/Emission (mutable live, no rebuild) are the sole source of color.
            vert.Color = Vector4.One;
            ring[i, side] = vert;
        }

        // One tangent per point: the direction from the previous point to the next one (for a closed
        // loop, wrapping at both ends; for an open line, the single adjacent segment at each
        // endpoint). Averaging the incoming/outgoing directions this way is what makes the cross-
        // section naturally bisect a joint's turn angle instead of needing separate miter math.
        private static Vector3[] ComputeTangents(List<Vector3> points, bool closed)
        {
            var count = points.Count;
            var tangents = new Vector3[count];
            for (var i = 0; i < count; i++)
            {
                Vector3 prev, next;
                if (closed)
                {
                    prev = points[(i - 1 + count) % count];
                    next = points[(i + 1) % count];
                }
                else
                {
                    prev = i > 0 ? points[i - 1] : points[i];
                    next = i < count - 1 ? points[i + 1] : points[i];
                }
                tangents[i] = Vector3.Normalize(next - prev);
            }
            return tangents;
        }

        // Parallel transport: start with an arbitrary normal perpendicular to the first tangent, then
        // at every subsequent point, rotate the previous point's normal by the minimal rotation that
        // takes the previous tangent to this point's tangent (re-orthogonalizing against the new
        // tangent afterward to correct for floating-point drift). This is what keeps the ribbon from
        // twisting between segments -- each frame only differs from its neighbor by however much the
        // path itself turned, nothing extra.
        private static (Vector3[] Normals, Vector3[] Binormals) ComputeParallelTransportFrames(Vector3[] tangents, bool closed)
        {
            var count = tangents.Length;
            var normals = new Vector3[count];
            var binormals = new Vector3[count];

            normals[0] = ArbitraryPerpendicular(tangents[0]);
            binormals[0] = Vector3.Cross(tangents[0], normals[0]);

            for (var i = 1; i < count; i++)
            {
                var rotation = RotationBetween(tangents[i - 1], tangents[i]);
                var transported = Vector3.Transform(normals[i - 1], rotation);
                normals[i] = Reorthogonalize(transported, tangents[i]);
                binormals[i] = Vector3.Cross(tangents[i], normals[i]);
            }

            // A closed loop's transported frame generally does NOT land back on normals[0] after a
            // full circuit -- the mismatch (holonomy) is a real geometric property of the path, not a
            // bug. Measure the signed twist and cancel it by distributing an equal, opposite rotation
            // across every frame, so the seam where the loop closes doesn't kink.
            if (closed && count > 1)
            {
                var closingRotation = RotationBetween(tangents[count - 1], tangents[0]);
                var wrapped = Reorthogonalize(Vector3.Transform(normals[count - 1], closingRotation), tangents[0]);
                var twist = SignedAngle(normals[0], wrapped, tangents[0]);

                for (var i = 0; i < count; i++)
                {
                    var correction = Quaternion.CreateFromAxisAngle(tangents[i], -twist * i / count);
                    normals[i] = Vector3.Normalize(Vector3.Transform(normals[i], correction));
                    binormals[i] = Vector3.Cross(tangents[i], normals[i]);
                }
            }

            return (normals, binormals);
        }

        private static Vector3 ArbitraryPerpendicular(Vector3 direction)
        {
            var helper = MathF.Abs(direction.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
            return Vector3.Normalize(Vector3.Cross(direction, helper));
        }

        private static Vector3 Reorthogonalize(Vector3 vector, Vector3 against)
        {
            return Vector3.Normalize(vector - (Vector3.Dot(vector, against) * against));
        }

        // Signed angle from `a` to `b`, both assumed perpendicular to `axis` -- used to measure how
        // far a closed loop's transported frame drifted from where it started.
        private static float SignedAngle(Vector3 a, Vector3 b, Vector3 axis)
        {
            var unsigned = MathF.Acos(Math.Clamp(Vector3.Dot(a, b), -1f, 1f));
            var sign = Vector3.Dot(Vector3.Cross(a, b), axis) < 0f ? -1f : 1f;
            return unsigned * sign;
        }

        // The rotation that maps `from` onto `to` -- used both to seed each ring's cross-section and
        // to parallel-transport it from one point's tangent to the next.
        private static Quaternion RotationBetween(Vector3 from, Vector3 to)
        {
            const float parallelThreshold = 0.9999f;
            var dot = Vector3.Dot(from, to);

            if (dot > parallelThreshold)
            {
                return Quaternion.Identity;
            }

            if (dot < -parallelThreshold)
            {
                var axis = ArbitraryPerpendicular(from);
                return Quaternion.CreateFromAxisAngle(axis, MathF.PI);
            }

            var rotationAxis = Vector3.Normalize(Vector3.Cross(from, to));
            var angle = MathF.Acos(Math.Clamp(dot, -1.0f, 1.0f));
            return Quaternion.CreateFromAxisAngle(rotationAxis, angle);
        }
    }
}
