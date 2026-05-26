namespace ManaVoxel.Code.Tools
{
    public static class VoxelRasterizer
    {
        public static Vector3 SnapToCenter(Vector3 position)
        {
            return new Vector3(
                MathF.Floor(position.X) + 0.5f,
                MathF.Floor(position.Y) + 0.5f,
                MathF.Floor(position.Z) + 0.5f);
        }

        public static (int X, int Y, int Z) ToCell(Vector3 position)
        {
            return (
                (int)MathF.Floor(position.X),
                (int)MathF.Floor(position.Y),
                (int)MathF.Floor(position.Z));
        }

        public static VoxelWorkPlane ResolvePlane(VoxelWorkPlane requestedPlane, Vector3 normal)
        {
            if (requestedPlane != VoxelWorkPlane.Auto)
            {
                return requestedPlane;
            }

            var absNormal = Vector3.Abs(normal);
            if (absNormal.Y >= absNormal.X && absNormal.Y >= absNormal.Z)
            {
                return VoxelWorkPlane.XZ;
            }

            if (absNormal.Z >= absNormal.X)
            {
                return VoxelWorkPlane.XY;
            }

            return VoxelWorkPlane.YZ;
        }

        public static void AddBrush(
            Dictionary<(int X, int Y, int Z), Vector4> cells,
            Vector3 position,
            Vector4 color,
            int radius)
        {
            AddBrush(cells, ToCell(SnapToCenter(position)), color, radius);
        }

        public static void AddLine(
            Dictionary<(int X, int Y, int Z), Vector4> cells,
            Vector3 start,
            Vector3 end,
            Vector4 color,
            int brushRadius)
        {
            var startCell = ToCell(SnapToCenter(start));
            var endCell = ToCell(SnapToCenter(end));
            var dx = endCell.X - startCell.X;
            var dy = endCell.Y - startCell.Y;
            var dz = endCell.Z - startCell.Z;
            var steps = Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz)));

            if (steps == 0)
            {
                AddBrush(cells, startCell, color, brushRadius);
                return;
            }

            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                var cell = (
                    X: (int)MathF.Round(startCell.X + dx * t),
                    Y: (int)MathF.Round(startCell.Y + dy * t),
                    Z: (int)MathF.Round(startCell.Z + dz * t));

                AddBrush(cells, cell, color, brushRadius);
            }
        }

        public static void AddFilledCircle(
            Dictionary<(int X, int Y, int Z), Vector4> cells,
            Vector3 center,
            Vector3 edge,
            VoxelWorkPlane plane,
            Vector4 color)
        {
            var centerCell = ToCell(SnapToCenter(center));
            var radius = MathF.Max(0.0f, PlaneDistance(center, edge, plane));
            var radiusCells = (int)MathF.Ceiling(radius);
            var radiusSquared = radius * radius + 0.001f;
            var axes = GetPlaneAxes(plane);

            for (var u = -radiusCells; u <= radiusCells; u++)
            {
                for (var v = -radiusCells; v <= radiusCells; v++)
                {
                    var distanceSquared = u * u + v * v;
                    if (distanceSquared > radiusSquared)
                    {
                        continue;
                    }

                    var cell = Add(Add(centerCell, Multiply(axes.U, u)), Multiply(axes.V, v));
                    cells[cell] = color;
                }
            }
        }

        public static void AddFilledSphere(
            Dictionary<(int X, int Y, int Z), Vector4> cells,
            Vector3 center,
            Vector3 edge,
            Vector4 color)
        {
            var centerCell = ToCell(SnapToCenter(center));
            var radius = MathF.Max(0.0f, Vector3.Distance(SnapToCenter(center), SnapToCenter(edge)));
            var radiusCells = (int)MathF.Ceiling(radius);
            var radiusSquared = radius * radius + 0.001f;

            for (var x = -radiusCells; x <= radiusCells; x++)
            {
                for (var y = -radiusCells; y <= radiusCells; y++)
                {
                    for (var z = -radiusCells; z <= radiusCells; z++)
                    {
                        var distanceSquared = x * x + y * y + z * z;
                        if (distanceSquared > radiusSquared)
                        {
                            continue;
                        }

                        var cell = (centerCell.X + x, centerCell.Y + y, centerCell.Z + z);
                        cells[cell] = color;
                    }
                }
            }
        }

        public static void AddText(
            Dictionary<(int X, int Y, int Z), Vector4> cells,
            Vector3 origin,
            string text,
            VoxelWorkPlane plane,
            int scale,
            int depth,
            Vector4 color)
        {
            var originCell = ToCell(SnapToCenter(origin));
            var axes = GetPlaneAxes(plane);
            var cursor = 0;
            var normalizedText = text.Replace('\r', ' ').Replace('\n', ' ');

            foreach (var character in normalizedText)
            {
                if (character == ' ')
                {
                    cursor += VoxelTextFont.SpaceWidth * scale;
                    continue;
                }

                foreach (var pixel in VoxelTextFont.GetPixels(character))
                {
                    for (var sx = 0; sx < scale; sx++)
                    {
                        for (var sy = 0; sy < scale; sy++)
                        {
                            for (var sd = 0; sd < depth; sd++)
                            {
                                var u = cursor + pixel.X * scale + sx;
                                var v = (VoxelTextFont.Height - 1 - pixel.Y) * scale + sy;
                                var cell = Add(
                                    Add(Add(originCell, Multiply(axes.U, u)), Multiply(axes.V, v)),
                                    Multiply(axes.Normal, sd));

                                cells[cell] = color;
                            }
                        }
                    }
                }

                cursor += (VoxelTextFont.Width + 1) * scale;
            }
        }

        private static void AddBrush(
            Dictionary<(int X, int Y, int Z), Vector4> cells,
            (int X, int Y, int Z) center,
            Vector4 color,
            int radius)
        {
            if (radius <= 0)
            {
                cells[center] = color;
                return;
            }

            var radiusSquared = radius * radius;
            for (var x = -radius; x <= radius; x++)
            {
                for (var y = -radius; y <= radius; y++)
                {
                    for (var z = -radius; z <= radius; z++)
                    {
                        if (x * x + y * y + z * z > radiusSquared)
                        {
                            continue;
                        }

                        var cell = (center.X + x, center.Y + y, center.Z + z);
                        cells[cell] = color;
                    }
                }
            }
        }

        private static float PlaneDistance(Vector3 a, Vector3 b, VoxelWorkPlane plane)
        {
            var delta = SnapToCenter(b) - SnapToCenter(a);
            return plane switch
            {
                VoxelWorkPlane.XY => MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y),
                VoxelWorkPlane.YZ => MathF.Sqrt(delta.Y * delta.Y + delta.Z * delta.Z),
                _ => MathF.Sqrt(delta.X * delta.X + delta.Z * delta.Z),
            };
        }

        private static (
            (int X, int Y, int Z) U,
            (int X, int Y, int Z) V,
            (int X, int Y, int Z) Normal) GetPlaneAxes(VoxelWorkPlane plane)
        {
            return plane switch
            {
                VoxelWorkPlane.XY => ((1, 0, 0), (0, 1, 0), (0, 0, 1)),
                VoxelWorkPlane.YZ => ((0, 0, 1), (0, 1, 0), (1, 0, 0)),
                _ => ((1, 0, 0), (0, 0, 1), (0, 1, 0)),
            };
        }

        private static (int X, int Y, int Z) Add(
            (int X, int Y, int Z) a,
            (int X, int Y, int Z) b)
        {
            return (a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        private static (int X, int Y, int Z) Multiply((int X, int Y, int Z) axis, int scalar)
        {
            return (axis.X * scalar, axis.Y * scalar, axis.Z * scalar);
        }
    }
}
