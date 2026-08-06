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
    }
}
