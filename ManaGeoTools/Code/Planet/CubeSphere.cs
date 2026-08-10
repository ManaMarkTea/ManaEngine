namespace ManaGeoTools.Code.Planet
{
    public static class CubeSphere
    {
        public static readonly Vector3[] FaceNormals =
        {
            Vector3.UnitY, -Vector3.UnitY,
            Vector3.UnitX, -Vector3.UnitX,
            Vector3.UnitZ, -Vector3.UnitZ,
        };

        // From http://mathproofs.blogspot.com/2005/07/mapping-cube-to-sphere.html
        public static Vector3 WarpCubeToSphere(Vector3 pointOnUnitCube)
        {
            var p = pointOnUnitCube;
            var x2 = p.X * p.X / 2;
            var y2 = p.Y * p.Y / 2;
            var z2 = p.Z * p.Z / 2;
            var x = p.X * MathF.Sqrt(1 - y2 - z2 + (p.Y * p.Y * p.Z * p.Z) / 3);
            var y = p.Y * MathF.Sqrt(1 - z2 - x2 + (p.X * p.X * p.Z * p.Z) / 3);
            var z = p.Z * MathF.Sqrt(1 - x2 - y2 + (p.X * p.X * p.Y * p.Y) / 3);
            return new Vector3(x, y, z);
        }

        /// <summary>The (axisA, axisB) tangent basis for a face, matching PlanetPatchMesh's own convention.</summary>
        public static (Vector3 AxisA, Vector3 AxisB) GetFaceAxes(Vector3 faceNormal)
        {
            var axisA = new Vector3(faceNormal.Y, faceNormal.Z, faceNormal.X);
            return (axisA, Vector3.Cross(faceNormal, axisA));
        }

        /// <summary>
        /// Inverse of WarpCubeToSphere: which face a unit-sphere direction lands on, and its (u, v) in
        /// that face's [-1,1] square. The forward map couples all three axes so there's no closed form;
        /// this Newton-solves it in the face's tangent basis from a gnomonic first guess, which converges
        /// in a couple of iterations because that guess is already close.
        /// </summary>
        public static bool TryUnwarpSphereToFaceUv(Vector3 direction, out int face, out float u, out float v)
        {
            face = 0;
            u = 0f;
            v = 0f;

            var d = direction.LengthSquared() > 0f ? Vector3.Normalize(direction) : Vector3.Zero;
            if (d == Vector3.Zero)
            {
                return false;
            }

            var absX = MathF.Abs(d.X);
            var absY = MathF.Abs(d.Y);
            var absZ = MathF.Abs(d.Z);
            if (absY >= absX && absY >= absZ)
            {
                face = d.Y > 0f ? 0 : 1;
            }
            else if (absX >= absZ)
            {
                face = d.X > 0f ? 2 : 3;
            }
            else
            {
                face = d.Z > 0f ? 4 : 5;
            }

            var normal = FaceNormals[face];
            var (axisA, axisB) = GetFaceAxes(normal);

            var denominator = Vector3.Dot(d, normal);
            if (MathF.Abs(denominator) < 1e-6f)
            {
                return false;
            }

            // Gnomonic guess: where the ray through d pierces the face plane.
            var planePoint = d / denominator;
            u = Vector3.Dot(planePoint, axisA);
            v = Vector3.Dot(planePoint, axisB);

            const float step = 1e-3f;
            for (var iteration = 0; iteration < 4; iteration++)
            {
                var current = Project(normal, axisA, axisB, u, v);
                var errorA = Vector3.Dot(d - current, axisA);
                var errorB = Vector3.Dot(d - current, axisB);
                if (MathF.Abs(errorA) < 1e-6f && MathF.Abs(errorB) < 1e-6f)
                {
                    break;
                }

                // Numeric 2x2 Jacobian of (u, v) -> tangent-space position.
                var du = Project(normal, axisA, axisB, u + step, v) - current;
                var dv = Project(normal, axisA, axisB, u, v + step) - current;
                var j00 = Vector3.Dot(du, axisA) / step;
                var j10 = Vector3.Dot(du, axisB) / step;
                var j01 = Vector3.Dot(dv, axisA) / step;
                var j11 = Vector3.Dot(dv, axisB) / step;

                var determinant = (j00 * j11) - (j01 * j10);
                if (MathF.Abs(determinant) < 1e-9f)
                {
                    break;
                }

                u += ((errorA * j11) - (errorB * j01)) / determinant;
                v += ((errorB * j00) - (errorA * j10)) / determinant;
            }

            u = Math.Clamp(u, -1f, 1f);
            v = Math.Clamp(v, -1f, 1f);
            return true;
        }

        private static Vector3 Project(Vector3 normal, Vector3 axisA, Vector3 axisB, float u, float v)
        {
            return WarpCubeToSphere(normal + (u * axisA) + (v * axisB));
        }
    }
}
