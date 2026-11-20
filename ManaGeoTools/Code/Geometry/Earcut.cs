namespace ManaGeoTools.Code.Geometry
{
    /// <summary>
    /// Polygon triangulation by ear clipping with holes: a port of Mapbox's earcut (ISC licence, (c) Mapbox), without its
    /// z-order hashing. Holes are joined to the outer ring by bridges first, so a lake's islands stay land.
    /// </summary>
    public static class Earcut
    {
        /// <param name="xy">Every ring's points, x then y, the outer ring first.</param>
        /// <param name="holeStarts">The point index each hole starts at.</param>
        /// <returns>Triangles as point indices, three at a time.</returns>
        public static List<int> Triangulate(double[] xy, IReadOnlyList<int> holeStarts)
        {
            var triangles = new List<int>();
            var outerEnd = holeStarts.Count > 0 ? holeStarts[0] * 2 : xy.Length;
            var outer = LinkedList(xy, 0, outerEnd, true);
            if (outer == null || outer.Next == outer.Prev)
            {
                return triangles;
            }
            if (holeStarts.Count > 0)
            {
                outer = EliminateHoles(xy, holeStarts, outer);
            }
            EarcutLinked(outer, triangles, 0);
            return triangles;
        }

        private sealed class Node(int index, double x, double y)
        {
            public int I = index;
            public double X = x;
            public double Y = y;
            public Node Prev = null!;
            public Node Next = null!;
            public bool Steiner;
        }

        private static Node? LinkedList(double[] data, int start, int end, bool clockwise)
        {
            Node? last = null;
            if (clockwise == (SignedArea(data, start, end) > 0))
            {
                for (var i = start; i < end; i += 2)
                {
                    last = InsertNode(i / 2, data[i], data[i + 1], last);
                }
            }
            else
            {
                for (var i = end - 2; i >= start; i -= 2)
                {
                    last = InsertNode(i / 2, data[i], data[i + 1], last);
                }
            }
            if (last != null && EqualPoints(last, last.Next))
            {
                RemoveNode(last);
                last = last.Next;
            }
            return last;
        }

        private static Node? FilterPoints(Node? start, Node? end = null)
        {
            if (start == null)
            {
                return start;
            }
            end ??= start;
            var p = start;
            bool again;
            do
            {
                again = false;
                if (!p.Steiner && (EqualPoints(p, p.Next) || Area(p.Prev, p, p.Next) == 0))
                {
                    RemoveNode(p);
                    p = end = p.Prev;
                    if (p == p.Next)
                    {
                        break;
                    }
                    again = true;
                }
                else
                {
                    p = p.Next;
                }
            }
            while (again || p != end);
            return end;
        }

        private static void EarcutLinked(Node? ear, List<int> triangles, int pass)
        {
            if (ear == null)
            {
                return;
            }
            var stop = ear;
            while (ear.Prev != ear.Next)
            {
                var prev = ear.Prev;
                var next = ear.Next;
                if (IsEar(ear))
                {
                    triangles.Add(prev.I);
                    triangles.Add(ear.I);
                    triangles.Add(next.I);
                    RemoveNode(ear);
                    ear = next.Next;
                    stop = next.Next;
                    continue;
                }
                ear = next;
                if (ear == stop)
                {
                    // No ear found in a whole turn: clean up, then untangle, then split the polygon in two.
                    if (pass == 0)
                    {
                        EarcutLinked(FilterPoints(ear), triangles, 1);
                    }
                    else if (pass == 1)
                    {
                        ear = CureLocalIntersections(FilterPoints(ear)!, triangles);
                        EarcutLinked(ear, triangles, 2);
                    }
                    else if (pass == 2)
                    {
                        SplitEarcut(ear, triangles);
                    }
                    break;
                }
            }
        }

        private static bool IsEar(Node ear)
        {
            var a = ear.Prev;
            var b = ear;
            var c = ear.Next;
            if (Area(a, b, c) >= 0)
            {
                return false;
            }
            var x0 = Math.Min(a.X, Math.Min(b.X, c.X));
            var y0 = Math.Min(a.Y, Math.Min(b.Y, c.Y));
            var x1 = Math.Max(a.X, Math.Max(b.X, c.X));
            var y1 = Math.Max(a.Y, Math.Max(b.Y, c.Y));
            var p = c.Next;
            while (p != a)
            {
                if (p.X >= x0 && p.X <= x1 && p.Y >= y0 && p.Y <= y1
                    && PointInTriangle(a.X, a.Y, b.X, b.Y, c.X, c.Y, p.X, p.Y) && Area(p.Prev, p, p.Next) >= 0)
                {
                    return false;
                }
                p = p.Next;
            }
            return true;
        }

        private static Node CureLocalIntersections(Node start, List<int> triangles)
        {
            var p = start;
            do
            {
                var a = p.Prev;
                var b = p.Next.Next;
                if (!EqualPoints(a, b) && Intersects(a, p, p.Next, b) && LocallyInside(a, b) && LocallyInside(b, a))
                {
                    triangles.Add(a.I);
                    triangles.Add(p.I);
                    triangles.Add(b.I);
                    RemoveNode(p);
                    RemoveNode(p.Next);
                    p = start = b;
                }
                p = p.Next;
            }
            while (p != start);
            return FilterPoints(p)!;
        }

        private static void SplitEarcut(Node start, List<int> triangles)
        {
            var a = start;
            do
            {
                var b = a.Next.Next;
                while (b != a.Prev)
                {
                    if (a.I != b.I && IsValidDiagonal(a, b))
                    {
                        var c = SplitPolygon(a, b);
                        a = FilterPoints(a, a.Next)!;
                        c = FilterPoints(c, c.Next)!;
                        EarcutLinked(a, triangles, 0);
                        EarcutLinked(c, triangles, 0);
                        return;
                    }
                    b = b.Next;
                }
                a = a.Next;
            }
            while (a != start);
        }

        private static Node EliminateHoles(double[] data, IReadOnlyList<int> holeStarts, Node outer)
        {
            var queue = new List<Node>();
            for (var i = 0; i < holeStarts.Count; i++)
            {
                var start = holeStarts[i] * 2;
                var end = i < holeStarts.Count - 1 ? holeStarts[i + 1] * 2 : data.Length;
                var list = LinkedList(data, start, end, false);
                if (list == null)
                {
                    continue;
                }
                if (list == list.Next)
                {
                    list.Steiner = true;
                }
                queue.Add(LeftMost(list));
            }
            queue.Sort(static (a, b) =>
            {
                var result = a.X.CompareTo(b.X);
                if (result == 0)
                {
                    result = a.Y.CompareTo(b.Y);
                }
                return result;
            });
            foreach (var hole in queue)
            {
                outer = EliminateHole(hole, outer);
            }
            return outer;
        }

        private static Node EliminateHole(Node hole, Node outer)
        {
            var bridge = FindHoleBridge(hole, outer);
            if (bridge == null)
            {
                return outer;
            }
            var bridgeReverse = SplitPolygon(bridge, hole);
            FilterPoints(bridgeReverse, bridgeReverse.Next);
            return FilterPoints(bridge, bridge.Next)!;
        }

        // A point of the outer ring the hole's leftmost point can see, to join them by.
        private static Node? FindHoleBridge(Node hole, Node outer)
        {
            var p = outer;
            var hx = hole.X;
            var hy = hole.Y;
            var qx = double.NegativeInfinity;
            Node? m = null;
            do
            {
                if (hy <= p.Y && hy >= p.Next.Y && p.Next.Y != p.Y)
                {
                    var x = p.X + (hy - p.Y) * (p.Next.X - p.X) / (p.Next.Y - p.Y);
                    if (x <= hx && x > qx)
                    {
                        qx = x;
                        m = p.X < p.Next.X ? p : p.Next;
                        if (x == hx)
                        {
                            return m;
                        }
                    }
                }
                p = p.Next;
            }
            while (p != outer);
            if (m == null)
            {
                return null;
            }

            var stop = m;
            var mx = m.X;
            var my = m.Y;
            var tanMin = double.PositiveInfinity;
            p = m;
            do
            {
                if (hx >= p.X && p.X >= mx && hx != p.X
                    && PointInTriangle(hy < my ? hx : qx, hy, mx, my, hy < my ? qx : hx, hy, p.X, p.Y))
                {
                    var tan = Math.Abs(hy - p.Y) / (hx - p.X);
                    if (LocallyInside(p, hole)
                        && (tan < tanMin || (tan == tanMin && (p.X > m.X || (p.X == m.X && SectorContainsSector(m, p))))))
                    {
                        m = p;
                        tanMin = tan;
                    }
                }
                p = p.Next;
            }
            while (p != stop);
            return m;
        }

        private static bool SectorContainsSector(Node m, Node p)
        {
            return Area(m.Prev, m, p.Prev) < 0 && Area(p.Next, m, m.Next) < 0;
        }

        private static Node LeftMost(Node start)
        {
            var p = start;
            var leftmost = start;
            do
            {
                if (p.X < leftmost.X || (p.X == leftmost.X && p.Y < leftmost.Y))
                {
                    leftmost = p;
                }
                p = p.Next;
            }
            while (p != start);
            return leftmost;
        }

        private static bool PointInTriangle(double ax, double ay, double bx, double by, double cx, double cy, double px, double py)
        {
            return (cx - px) * (ay - py) >= (ax - px) * (cy - py)
                && (ax - px) * (by - py) >= (bx - px) * (ay - py)
                && (bx - px) * (cy - py) >= (cx - px) * (by - py);
        }

        private static bool IsValidDiagonal(Node a, Node b)
        {
            return a.Next.I != b.I && a.Prev.I != b.I && !IntersectsPolygon(a, b)
                && ((LocallyInside(a, b) && LocallyInside(b, a) && MiddleInside(a, b)
                    && (Area(a.Prev, a, b.Prev) != 0 || Area(a, b.Prev, b) != 0))
                    || (EqualPoints(a, b) && Area(a.Prev, a, a.Next) > 0 && Area(b.Prev, b, b.Next) > 0));
        }

        private static double Area(Node p, Node q, Node r)
        {
            return (q.Y - p.Y) * (r.X - q.X) - (q.X - p.X) * (r.Y - q.Y);
        }

        private static bool EqualPoints(Node a, Node b)
        {
            return a.X == b.X && a.Y == b.Y;
        }

        private static bool Intersects(Node p1, Node q1, Node p2, Node q2)
        {
            var o1 = Math.Sign(Area(p1, q1, p2));
            var o2 = Math.Sign(Area(p1, q1, q2));
            var o3 = Math.Sign(Area(p2, q2, p1));
            var o4 = Math.Sign(Area(p2, q2, q1));
            if (o1 != o2 && o3 != o4)
            {
                return true;
            }
            if (o1 == 0 && OnSegment(p1, p2, q1))
            {
                return true;
            }
            if (o2 == 0 && OnSegment(p1, q2, q1))
            {
                return true;
            }
            if (o3 == 0 && OnSegment(p2, p1, q2))
            {
                return true;
            }
            return o4 == 0 && OnSegment(p2, q1, q2);
        }

        private static bool OnSegment(Node p, Node q, Node r)
        {
            return q.X <= Math.Max(p.X, r.X) && q.X >= Math.Min(p.X, r.X) && q.Y <= Math.Max(p.Y, r.Y) && q.Y >= Math.Min(p.Y, r.Y);
        }

        private static bool IntersectsPolygon(Node a, Node b)
        {
            var p = a;
            do
            {
                if (p.I != a.I && p.Next.I != a.I && p.I != b.I && p.Next.I != b.I && Intersects(p, p.Next, a, b))
                {
                    return true;
                }
                p = p.Next;
            }
            while (p != a);
            return false;
        }

        private static bool LocallyInside(Node a, Node b)
        {
            if (Area(a.Prev, a, a.Next) < 0)
            {
                return Area(a, b, a.Next) >= 0 && Area(a, a.Prev, b) >= 0;
            }
            return Area(a, b, a.Prev) < 0 || Area(a, a.Next, b) < 0;
        }

        private static bool MiddleInside(Node a, Node b)
        {
            var p = a;
            var inside = false;
            var px = (a.X + b.X) / 2;
            var py = (a.Y + b.Y) / 2;
            do
            {
                if (((p.Y > py) != (p.Next.Y > py)) && p.Next.Y != p.Y && (px < (p.Next.X - p.X) * (py - p.Y) / (p.Next.Y - p.Y) + p.X))
                {
                    inside = !inside;
                }
                p = p.Next;
            }
            while (p != a);
            return inside;
        }

        // Joins a and b by a diagonal, splitting the ring in two; returns the second ring's copy of b.
        private static Node SplitPolygon(Node a, Node b)
        {
            var a2 = new Node(a.I, a.X, a.Y);
            var b2 = new Node(b.I, b.X, b.Y);
            var an = a.Next;
            var bp = b.Prev;
            a.Next = b;
            b.Prev = a;
            a2.Next = an;
            an.Prev = a2;
            b2.Next = a2;
            a2.Prev = b2;
            bp.Next = b2;
            b2.Prev = bp;
            return b2;
        }

        private static Node InsertNode(int index, double x, double y, Node? last)
        {
            var p = new Node(index, x, y);
            if (last == null)
            {
                p.Prev = p;
                p.Next = p;
            }
            else
            {
                p.Next = last.Next;
                p.Prev = last;
                last.Next.Prev = p;
                last.Next = p;
            }
            return p;
        }

        private static void RemoveNode(Node p)
        {
            p.Next.Prev = p.Prev;
            p.Prev.Next = p.Next;
        }

        private static double SignedArea(double[] data, int start, int end)
        {
            var sum = 0.0;
            for (int i = start, j = end - 2; i < end; j = i, i += 2)
            {
                sum += (data[j] - data[i]) * (data[i + 1] + data[j + 1]);
            }
            return sum;
        }
    }
}
