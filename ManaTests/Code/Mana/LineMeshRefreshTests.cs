using Mana;
using Mana.Code;
using Mana.Code.Shapes;
using ManaUI;

namespace ManaTests;

/// <summary>
/// <see cref="ManaLineMesh.TryRefreshRibbonGeometry"/>, the in-place path a terrain drape uses instead of
/// regenerating the whole ribbon.
/// <para>
/// The contract worth protecting is equivalence: a refresh must produce byte-for-byte what a full
/// regenerate would, or an overlay would visibly differ depending on which path happened to run. So every
/// check here compares the two against each other rather than against hand-computed numbers.
/// </para>
/// <para>
/// A degenerate line is deliberately placed FIRST in the list. Such a line produces no geometry and so no
/// layout entry, which shifts every later line's layout position away from its index in Lines -- conflating
/// the two silently draped the wrong points onto the wrong vertices (or, when the index happened to run past
/// the end, quietly fell back to the slow path and looked only like a perf bug).
/// </para>
/// </summary>
public static class LineMeshRefreshChecks
{
    private static readonly Vector4 White = new(1.0f, 1.0f, 1.0f, 1.0f);

    private static ManaContext CreateContext()
    {
        var world = new ManaWorld();
        // Uploads deferred, so this never touches a GPU that is not there.
        return new ManaContext(null!, null!, world) { DeferGpuUploads = true };
    }

    // The point arrays are handed to the polylines by reference, exactly as CountryBorderLines does, because
    // mutating them in place is how a drape moves points at all.
    private static (ManaLineMesh Mesh, Vector3[][] Points) BuildMesh(ManaLineProfile profile)
    {
        var degenerate = new[] { new Vector3(0.0f, 5.0f, 0.0f), new Vector3(0.0f, 5.0f, 0.0f) };

        var open = new[]
        {
            new Vector3(5.0f, 0.0f, 0.0f),
            new Vector3(4.6f, 1.9f, 0.2f),
            new Vector3(3.5f, 3.5f, 0.5f),
            new Vector3(1.9f, 4.6f, 0.9f),
        };

        var ring = new[]
        {
            new Vector3(0.0f, 0.0f, 5.0f),
            new Vector3(1.5f, 1.0f, 4.6f),
            new Vector3(2.0f, -1.0f, 4.4f),
            new Vector3(0.5f, -1.8f, 4.6f),
        };

        var lines = new List<ManaLinePolyline>
        {
            new(degenerate, closed: false),
            new(open, closed: false),
            new(ring, closed: true),
        };

        var mesh = new ManaLineMesh(CreateContext(), lines, thickness: 0.2f, White, profile: profile);
        return (mesh, new[] { degenerate, open, ring });
    }

    // A drape only ever changes each point's radius: same count, same order, pushed out along its own
    // direction by however much terrain is under it.
    //
    // The displacement has to be a function of the POSITION, not of the index, or the simulation is not a
    // drape: two coincident points sample the same terrain and therefore stay coincident, which is what keeps
    // deduplication -- and so the topology -- stable across a re-drape.
    private static void SimulateDrape(Vector3[][] points, float amount)
    {
        foreach (var line in points)
        {
            for (var i = 0; i < line.Length; i++)
            {
                var point = line[i];
                var terrain = MathF.Sin((point.X * 1.7f) + (point.Y * 2.3f) + (point.Z * 0.9f));
                line[i] = point * (1.0f + (amount * terrain));
            }
        }
    }

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        // ---------------------------------------------------------------- shader-expanded ribbon
        var (mesh, points) = BuildMesh(ManaLineProfile.ScreenRibbon);
        var generated = mesh.CpuData;
        Check("a generated ribbon has geometry", generated != null && generated.Positions.Length > 0,
            $"{generated?.Positions.Length ?? 0} vertices");

        var positionArray = generated!.Positions;
        var versionBefore = mesh.CpuDataVersion;

        SimulateDrape(points, 0.03f);
        Check("the refresh accepts a drape", mesh.TryRefreshRibbonGeometry());

        var refreshed = mesh.CpuData!;
        Check("it reuses the existing arrays rather than allocating new ones",
            ReferenceEquals(refreshed.Positions, positionArray));

        Check("and bumps the version the backends key off",
            mesh.CpuDataVersion > versionBefore, $"{versionBefore} -> {mesh.CpuDataVersion}");

        // Snapshot before the regenerate below overwrites CpuData with fresh arrays.
        var refreshedPositions = refreshed.Positions.ToArray();
        var refreshedNormals = refreshed.Normals.ToArray();
        var refreshedColors = refreshed.Colors.ToArray();
        var refreshedIndexes = refreshed.Indexes.ToArray();

        // ---------------------------------------------------------------- the contract
        mesh.RebuildGeometry();
        var regenerated = mesh.CpuData!;

        Check("a refresh and a full regenerate agree on vertex count",
            regenerated.Positions.Length == refreshedPositions.Length,
            $"{refreshedPositions.Length} vs {regenerated.Positions.Length}");

        Check("and on topology", regenerated.Indexes.SequenceEqual(refreshedIndexes),
            $"{refreshedIndexes.Length} indexes");

        var worstPosition = 0.0f;
        for (var i = 0; i < Math.Min(refreshedPositions.Length, regenerated.Positions.Length); i++)
        {
            worstPosition = MathF.Max(worstPosition,
                Vector3.Distance(refreshedPositions[i], regenerated.Positions[i]));
        }

        Check("and on every vertex position", worstPosition < 1e-5f, $"worst delta {worstPosition:0.#######}");

        var worstColor = 0.0f;
        for (var i = 0; i < Math.Min(refreshedColors.Length, regenerated.Colors.Length); i++)
        {
            worstColor = MathF.Max(worstColor,
                (refreshedColors[i] - regenerated.Colors[i]).Length());
        }

        // Colour carries the signed offset direction on a shader-expanded ribbon, so this is geometry too --
        // getting it wrong would flip a ribbon's width to the other side of the line.
        Check("and on the offset directions carried in vertex colour",
            worstColor < 1e-5f, $"worst delta {worstColor:0.#######}");

        var worstNormal = 0.0f;
        for (var i = 0; i < Math.Min(refreshedNormals.Length, regenerated.Normals.Length); i++)
        {
            worstNormal = MathF.Max(worstNormal,
                Vector3.Distance(refreshedNormals[i], regenerated.Normals[i]));
        }

        Check("and on normals", worstNormal < 1e-5f, $"worst delta {worstNormal:0.#######}");

        // ---------------------------------------------------------------- repeated refreshes
        // A drape runs for as long as terrain streams, so the layout has to survive being replayed.
        var stillFine = true;
        for (var pass = 0; pass < 5; pass++)
        {
            SimulateDrape(points, 0.01f);
            stillFine &= mesh.TryRefreshRibbonGeometry();
        }

        Check("repeated refreshes keep working", stillFine);

        // ---------------------------------------------------------------- CPU-expanded ribbon
        var (flatMesh, flatPoints) = BuildMesh(ManaLineProfile.Ribbon);
        SimulateDrape(flatPoints, 0.03f);
        Check("a CPU-expanded ribbon refreshes too", flatMesh.TryRefreshRibbonGeometry());

        var flatRefreshed = flatMesh.CpuData!.Positions.ToArray();
        flatMesh.RebuildGeometry();
        var flatRegenerated = flatMesh.CpuData!.Positions;

        var worstFlat = 0.0f;
        for (var i = 0; i < Math.Min(flatRefreshed.Length, flatRegenerated.Length); i++)
        {
            worstFlat = MathF.Max(worstFlat, Vector3.Distance(flatRefreshed[i], flatRegenerated[i]));
        }

        Check("and matches its own regenerate, offsets baked in and all",
            flatRefreshed.Length == flatRegenerated.Length && worstFlat < 1e-5f,
            $"worst delta {worstFlat:0.#######}");

        // ---------------------------------------------------------------- the deliberate limit
        // The layout freezes which source points survived deduplication, so a caller that moves points enough
        // to change that decision keeps the original topology rather than picking up a new one. This is the
        // documented trade for never reallocating -- and it is safe for a drape, which cannot change the
        // decision (see SimulateDrape). Pinned here so the behaviour is a choice rather than a surprise.
        var (frozenMesh, frozenPoints) = BuildMesh(ManaLineProfile.ScreenRibbon);
        var frozenVertexCount = frozenMesh.CpuData!.Positions.Length;

        // Pulls apart the degenerate line's two coincident points, which a real drape cannot do.
        frozenPoints[0][1] += new Vector3(0.0f, 1.0f, 0.0f);

        Check("a topology-changing move still refreshes", frozenMesh.TryRefreshRibbonGeometry());
        Check("but keeps the topology recorded at generate time",
            frozenMesh.CpuData!.Positions.Length == frozenVertexCount,
            $"{frozenVertexCount} vertices");

        frozenMesh.RebuildGeometry();
        Check("and a full regenerate is what picks the new topology up",
            frozenMesh.CpuData!.Positions.Length > frozenVertexCount,
            $"{frozenVertexCount} -> {frozenMesh.CpuData!.Positions.Length} vertices");

        // ---------------------------------------------------------------- profiles it cannot handle
        // A tube shares vertices between four sides at every joint, so there is no per-point rewrite to do.
        // Refusing is the correct answer; the caller falls back to a full regenerate.
        var (tubeMesh, tubePoints) = BuildMesh(ManaLineProfile.Tube);
        SimulateDrape(tubePoints, 0.03f);
        Check("a tube profile declines rather than corrupting itself", !tubeMesh.TryRefreshRibbonGeometry());
    }
}
