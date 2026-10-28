using System.Numerics;
using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Nodes;
using ManaUI;

namespace ManaTests;

/// <summary><see cref="ManaScatterComponent"/>'s placements on a terrain: by paint layer, off steep ground, from the seed.</summary>
public static class ScatterChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        world.Context = context;

        // 32 m square, all grass, with a rock patch of radius 4 m painted at its centre.
        var terrain = new ManaTerrainNode(context, 65, 65, 0.5f);
        terrain.Apply(new ManaTerrainDab(ManaTerrainBrush.Paint, 0.0f, 0.0f, 4.0f, 1.0f, 0.0f, Layer: 2));
        var scatter = new ManaScatterComponent(terrain) { Models = ["a", "b"], Layer = 2, Density = 1.0f, MinimumPaint = 0.5f };

        var rocks = scatter.Plan().ToList();
        var area = MathF.PI * 4.0f * 4.0f;
        Check("copies grow where their layer is painted, about as dense as asked",
            rocks.Count > area * 0.6f && rocks.Count < area * 1.4f, $"{rocks.Count} in {area:0} m2");
        Check("and nowhere else",
            rocks.All(rock => new Vector2(rock.Transform.Translation.X, rock.Transform.Translation.Z).Length() < 4.6f));
        Check("each copy stands on the ground",
            rocks.All(rock => MathF.Abs(rock.Transform.Translation.Y - terrain.HeightAt(rock.Transform.Translation.X, rock.Transform.Translation.Z)) < 1e-4f));
        Check("both models are used", rocks.Any(rock => rock.Model == 0) && rocks.Any(rock => rock.Model == 1));
        Check("the same seed grows the same copies",
            scatter.Plan().Select(rock => rock.Transform).SequenceEqual(rocks.Select(rock => rock.Transform)));
        scatter.Seed = 2;
        Check("another seed grows others", !scatter.Plan().Select(rock => rock.Transform).SequenceEqual(rocks.Select(rock => rock.Transform)));

        scatter.MaximumCount = 5;
        Check("the cap holds", scatter.Plan().Count() == 5);
        scatter.MaximumCount = 5000;

        // A slope steeper than the limit (about 63 degrees) is left bare.
        terrain.SetHeights((x, z) => x * 2.0f);
        scatter.MaximumSlope = 45.0f;
        Check("steep ground stays bare", !scatter.Plan().Any());
        scatter.MaximumSlope = 70.0f;
        Check("and is grown on when the limit allows it", scatter.Plan().Any());

        scatter.FollowSlope = 0.0f;
        Check("upright copies stay upright on a slope",
            scatter.Plan().All(rock => Vector3.TransformNormal(Vector3.UnitY, rock.Transform) is var up && MathF.Abs(up.X) < 1e-3f && MathF.Abs(up.Z) < 1e-3f));
        scatter.FollowSlope = 1.0f;
        Check("leaning copies lean with it",
            scatter.Plan().All(rock => MathF.Abs(Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, rock.Transform)).X) > 0.5f));
    }
}
