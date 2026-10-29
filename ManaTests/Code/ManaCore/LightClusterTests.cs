using ManaCore.Graphics;
using ManaUI;

namespace ManaTests;

/// <summary>
/// Clustered lighting (ManaLightClusters): a pixel shades only the lights listed in its cell, so every light whose range
/// reaches a visible point must be in that point's cell -- checked against a brute-force answer, as the shader finds the
/// cell, for a perspective and an orthographic camera.
/// </summary>
public static class LightClusterChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var viewport = new Vector2(1600, 900);
        var eye = new Vector3(10, 8, 30);
        var view = Matrix4x4.CreateLookAt(eye, new Vector3(0, 0, 0), Vector3.UnitY);
        var perspective = new ManaCameraData(view, Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, viewport.X / viewport.Y, 0.1f, 500.0f), eye, 0.1f, 500.0f);
        var orthographic = new ManaCameraData(view, Matrix4x4.CreateOrthographic(60, 60 * viewport.Y / viewport.X, 0.1f, 500.0f), eye, 0.1f, 500.0f);

        var random = new Random(7);
        var lights = new List<(Vector4 Position, Vector4 Color)>();
        for (var i = 0; i < 2000; i++)
        {
            var at = new Vector3(random.NextSingle() * 80 - 40, random.NextSingle() * 10 - 2, random.NextSingle() * 80 - 40);
            lights.Add((new Vector4(at, 0.5f + random.NextSingle() * 6), Vector4.One));
        }

        foreach (var (name, camera) in new[] { ("perspective", perspective), ("orthographic", orthographic) })
        {
            var clusters = new ManaLightClusters();
            clusters.Build(lights, camera, viewport);
            var viewProjection = camera.View * camera.Projection;
            var forward = clusters.Forward;
            var missing = 0;
            var checkedPoints = 0;
            var shaded = 0L;
            for (var sample = 0; sample < 4000; sample++)
            {
                var point = new Vector3(random.NextSingle() * 80 - 40, random.NextSingle() * 10 - 2, random.NextSingle() * 80 - 40);
                var clip = Vector4.Transform(new Vector4(point, 1), viewProjection);
                if (clip.W <= 0)
                {
                    continue;
                }
                var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
                if (MathF.Abs(ndc.X) >= 1 || MathF.Abs(ndc.Y) >= 1 || ndc.Z < 0 || ndc.Z > 1)
                {
                    continue;
                }
                checkedPoints++;
                var pixel = new Vector2((ndc.X * 0.5f + 0.5f) * viewport.X, (0.5f - ndc.Y * 0.5f) * viewport.Y);
                var listed = clusters.LightsOf(clusters.CellOf(pixel, Vector3.Dot(point - eye, forward))).ToArray().ToHashSet();
                shaded += listed.Count;
                for (var i = 0; i < lights.Count; i++)
                {
                    var light = lights[i].Position;
                    if (Vector3.Distance(point, new Vector3(light.X, light.Y, light.Z)) < light.W && !listed.Contains((uint)i))
                    {
                        missing++;
                    }
                }
            }
            check($"{name}: every light reaching a visible point is in that point's cell", checkedPoints > 500 && missing == 0,
                $"{checkedPoints} points, {missing} missed");
            check($"{name}: a pixel shades a few lights, not all {lights.Count}", checkedPoints > 0 && shaded / checkedPoints < lights.Count / 10,
                $"{shaded / Math.Max(1, checkedPoints)} on average");
        }

        // Behind the camera, or with no range, a light is in no cell.
        var behind = new ManaLightClusters();
        behind.Build([(new Vector4(eye + (eye - Vector3.Zero), 1), Vector4.One), (new Vector4(0, 0, 0, 0), Vector4.One)], perspective, viewport);
        check("a light behind the camera or without a range lights no cell", behind.IndexCount == 0, $"{behind.IndexCount} entries");

        // Point-light shadows: the casters nearest the camera get the slots, named in their colour's w as slot + 1.
        var lamps = new List<(Vector4 Position, Vector4 Color)>();
        var casters = new List<int>();
        for (var i = 0; i < 12; i++)
        {
            lamps.Add((new Vector4(i * 3.0f, 2, 0, 5), new Vector4(1, 1, 1, 0)));
            if (i != 1)
            {
                casters.Add(i);
            }
        }
        var slots = ManaRenderScene.AssignPointShadows(lamps, casters, new Vector3(30, 2, 0));
        check("at most MaxPointShadows lights cast shadows", slots.Count == ManaRenderScene.MaxPointShadows, $"{slots.Count}");
        check("the nearest caster to the camera has the first slot", slots[0].X == 30 && lamps[10].Color.W == 1, $"{slots[0]}");
        check("the farthest casters and the non-caster keep no slot", lamps[0].Color.W == 0 && lamps[1].Color.W == 0 && lamps[2].Color.W == 0,
            string.Join(", ", lamps.Select(l => l.Color.W)));
        check("each slot names its own light", Enumerable.Range(0, slots.Count).All(s => lamps.Count(l => l.Color.W == s + 1 && l.Position == slots[s]) == 1), "");
    }
}
