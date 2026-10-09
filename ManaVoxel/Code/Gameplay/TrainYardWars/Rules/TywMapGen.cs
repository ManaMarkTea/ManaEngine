namespace ManaVoxel.Code.Gameplay.TrainYardWars.Rules
{
    /// <summary>
    /// Makes the world: wave function collapse over hexes, so land falls into sensible bands -- sea, beach, then
    /// grass, farmland or desert, forest and hills, mountains in the middle of it all. Smooth noise leans each hex
    /// toward a height and wetness so the bands form coasts and ranges instead of confetti, and the outer rings are
    /// always sea, which is the world's border.
    /// </summary>
    public static class TywMapGen
    {
        // The terrains the collapse chooses between; towns, depots and ports are placed on top afterwards.
        private static readonly TywTerrain[] Kinds =
        {
            TywTerrain.Water, TywTerrain.Sand, TywTerrain.Desert, TywTerrain.Plains,
            TywTerrain.Fields, TywTerrain.Forest, TywTerrain.Hills, TywTerrain.Mountain
        };

        // Which terrains may touch: the bands run sea - beach - lowland - forest/hills - mountain.
        private static readonly (TywTerrain, TywTerrain[])[] Neighbours =
        {
            (TywTerrain.Water, new[] { TywTerrain.Water, TywTerrain.Sand }),
            (TywTerrain.Sand, new[] { TywTerrain.Water, TywTerrain.Sand, TywTerrain.Desert, TywTerrain.Plains, TywTerrain.Fields }),
            (TywTerrain.Desert, new[] { TywTerrain.Sand, TywTerrain.Desert, TywTerrain.Plains, TywTerrain.Hills }),
            (TywTerrain.Plains, new[] { TywTerrain.Sand, TywTerrain.Desert, TywTerrain.Plains, TywTerrain.Fields, TywTerrain.Forest, TywTerrain.Hills }),
            (TywTerrain.Fields, new[] { TywTerrain.Sand, TywTerrain.Plains, TywTerrain.Fields, TywTerrain.Forest }),
            (TywTerrain.Forest, new[] { TywTerrain.Plains, TywTerrain.Fields, TywTerrain.Forest, TywTerrain.Hills, TywTerrain.Mountain }),
            (TywTerrain.Hills, new[] { TywTerrain.Desert, TywTerrain.Plains, TywTerrain.Forest, TywTerrain.Hills, TywTerrain.Mountain }),
            (TywTerrain.Mountain, new[] { TywTerrain.Forest, TywTerrain.Hills, TywTerrain.Mountain })
        };

        // Where each terrain likes to be: (height, wetness, how picky about wetness, how common).
        private static readonly (float Height, float Wet, float WetSpread, float Weight)[] Preference =
        {
            (0.10f, 0.5f, 9.0f, 1.0f),   // Water
            (0.36f, 0.5f, 9.0f, 0.5f),   // Sand
            (0.52f, 0.10f, 0.15f, 0.9f), // Desert
            (0.50f, 0.45f, 0.25f, 1.0f), // Plains
            (0.50f, 0.62f, 0.18f, 1.1f), // Fields
            (0.60f, 0.80f, 0.25f, 1.5f), // Forest
            (0.66f, 0.40f, 0.30f, 0.5f),  // Hills
            (0.80f, 0.50f, 9.0f, 0.7f)   // Mountain
        };

        public const int BorderRings = 2;

        private static readonly int[] Allowed = BuildAllowed();

        private static int[] BuildAllowed()
        {
            var allowed = new int[Kinds.Length];
            foreach (var (kind, others) in Neighbours)
            {
                var mask = 0;
                foreach (var other in others)
                {
                    mask |= 1 << Array.IndexOf(Kinds, other);
                }

                allowed[Array.IndexOf(Kinds, kind)] = mask;
            }

            return allowed;
        }

        /// <summary>Whether two terrains may sit side by side under the rules above.</summary>
        public static bool MayTouch(TywTerrain a, TywTerrain b)
        {
            var i = Array.IndexOf(Kinds, a);
            var j = Array.IndexOf(Kinds, b);
            return i < 0 || j < 0 || (Allowed[i] & (1 << j)) != 0;
        }

        /// <summary>Terrain for every hex of a <paramref name="columns"/> x <paramref name="rows"/> map.</summary>
        public static Dictionary<TywHex, TywTerrain> Collapse(int columns, int rows, int seed)
        {
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var result = TryCollapse(columns, rows, seed + attempt * 7919);
                if (result != null)
                {
                    return result;
                }
            }

            // Never seen in practice; a plain world is better than none.
            return Cells(columns, rows).ToDictionary(c => c.Hex, c => c.Edge < BorderRings ? TywTerrain.Water : TywTerrain.Plains);
        }

        /// <summary>The map's hexes in offset rows (odd rows shifted half a hex), with how far each is from the edge.</summary>
        public static IEnumerable<(TywHex Hex, int Column, int Row, int Edge)> Cells(int columns, int rows)
        {
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var hex = new TywHex(column - (row - (row & 1)) / 2, row);
                    var edge = Math.Min(Math.Min(column, columns - 1 - column), Math.Min(row, rows - 1 - row));
                    yield return (hex, column, row, edge);
                }
            }
        }

        private static Dictionary<TywHex, TywTerrain>? TryCollapse(int columns, int rows, int seed)
        {
            var random = new Random(seed);
            var cells = Cells(columns, rows).ToList();
            var index = new Dictionary<TywHex, int>();
            for (var i = 0; i < cells.Count; i++)
            {
                index[cells[i].Hex] = i;
            }

            var neighbours = cells.Select(c => Enumerable.Range(0, TywHex.DirectionCount)
                .Select(d => index.TryGetValue(c.Hex.Neighbor(d), out var n) ? n : -1)
                .Where(n => n >= 0).ToArray()).ToArray();

            var full = (1 << Kinds.Length) - 1;
            var domain = new int[cells.Count];
            var chosen = new int[cells.Count];
            Array.Fill(chosen, -1);

            // Height and wetness: smooth noise, with the land rising toward the middle of the map.
            var height = new float[cells.Count];
            var wet = new float[cells.Count];
            var noiseSeed = random.Next();
            for (var i = 0; i < cells.Count; i++)
            {
                var (_, column, row, _) = cells[i];
                var u = (column + 0.5f) / columns * 2.0f - 1.0f;
                var v = (row + 0.5f) / rows * 2.0f - 1.0f;
                var falloff = 1.0f - MathF.Min(1.0f, MathF.Pow(MathF.Max(MathF.Abs(u), MathF.Abs(v)), 3.0f) * 0.7f + (u * u + v * v) * 0.25f);
                height[i] = Math.Clamp(Fbm(column * 0.11f, row * 0.11f, noiseSeed) * 0.8f + falloff * 0.3f - 0.06f, 0.0f, 1.0f);
                wet[i] = Math.Clamp(Fbm(column * 0.09f + 40.0f, row * 0.09f - 17.0f, noiseSeed + 1) * 1.3f - 0.15f, 0.0f, 1.0f);
                domain[i] = cells[i].Edge < BorderRings ? 1 : full;
            }

            var pending = new Stack<int>();
            for (var i = 0; i < cells.Count; i++)
            {
                if (domain[i] != full)
                {
                    pending.Push(i);
                }
            }

            if (!Propagate(domain, neighbours, pending))
            {
                return null;
            }

            var weights = new float[Kinds.Length];
            while (true)
            {
                // The undecided hex with the fewest options left; noise breaks ties so the front wanders.
                var pick = -1;
                var fewest = float.MaxValue;
                for (var i = 0; i < cells.Count; i++)
                {
                    if (chosen[i] >= 0)
                    {
                        continue;
                    }

                    var options = System.Numerics.BitOperations.PopCount((uint)domain[i]);
                    if (options == 1)
                    {
                        chosen[i] = System.Numerics.BitOperations.TrailingZeroCount(domain[i]);
                        continue;
                    }

                    var score = options + (float)random.NextDouble() * 0.9f;
                    if (score < fewest)
                    {
                        fewest = score;
                        pick = i;
                    }
                }

                if (pick < 0)
                {
                    break;
                }

                var total = 0.0f;
                for (var k = 0; k < Kinds.Length; k++)
                {
                    weights[k] = 0.0f;
                    if ((domain[pick] & (1 << k)) == 0)
                    {
                        continue;
                    }

                    var (h, w, spread, weight) = Preference[k];
                    var dh = height[pick] - h;
                    var dw = wet[pick] - w;
                    var same = neighbours[pick].Count(n => chosen[n] == k);
                    // Like terrain gathers: each matching neighbour makes the choice likelier.
                    weights[k] = weight * MathF.Exp(-dh * dh / (2 * 0.012f)) * MathF.Exp(-dw * dw / (2 * spread * spread)) * (1.0f + 1.6f * same) + 1e-6f;
                    total += weights[k];
                }

                var roll = (float)random.NextDouble() * total;
                var choice = -1;
                for (var k = 0; k < Kinds.Length; k++)
                {
                    if (weights[k] <= 0.0f)
                    {
                        continue;
                    }

                    choice = k;
                    roll -= weights[k];
                    if (roll <= 0.0f)
                    {
                        break;
                    }
                }

                domain[pick] = 1 << choice;
                chosen[pick] = choice;
                pending.Push(pick);
                if (!Propagate(domain, neighbours, pending))
                {
                    return null;
                }
            }

            var map = new Dictionary<TywHex, TywTerrain>();
            for (var i = 0; i < cells.Count; i++)
            {
                map[cells[i].Hex] = Kinds[chosen[i]];
            }

            return map;
        }

        // Narrows each neighbour to what its neighbours still allow, until nothing changes; false on a dead end.
        private static bool Propagate(int[] domain, int[][] neighbours, Stack<int> pending)
        {
            while (pending.Count > 0)
            {
                var i = pending.Pop();
                var reach = 0;
                for (var k = 0; k < Kinds.Length; k++)
                {
                    if ((domain[i] & (1 << k)) != 0)
                    {
                        reach |= Allowed[k];
                    }
                }

                foreach (var n in neighbours[i])
                {
                    var narrowed = domain[n] & reach;
                    if (narrowed == domain[n])
                    {
                        continue;
                    }

                    if (narrowed == 0)
                    {
                        return false;
                    }

                    domain[n] = narrowed;
                    pending.Push(n);
                }
            }

            return true;
        }

        // Value noise, a few octaves: smooth, seeded, 0..1.
        private static float Fbm(float x, float y, int seed)
        {
            var sum = 0.0f;
            var amplitude = 0.5f;
            var norm = 0.0f;
            for (var octave = 0; octave < 4; octave++)
            {
                sum += Value(x, y, seed + octave * 1013) * amplitude;
                norm += amplitude;
                x *= 2.03f;
                y *= 2.03f;
                amplitude *= 0.5f;
            }

            return sum / norm;
        }

        private static float Value(float x, float y, int seed)
        {
            var x0 = (int)MathF.Floor(x);
            var y0 = (int)MathF.Floor(y);
            var fx = x - x0;
            var fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            var a = Hash(x0, y0, seed);
            var b = Hash(x0 + 1, y0, seed);
            var c = Hash(x0, y0 + 1, seed);
            var d = Hash(x0 + 1, y0 + 1, seed);
            return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                var h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xffffff) / (float)0x1000000;
            }
        }
    }
}
