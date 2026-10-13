using System.Globalization;
using System.Numerics;

namespace ManaVoxel.Code.Measure;

public enum MeasureMode
{
    /// <summary>Two points: how far apart, and how much of that is across and how much up.</summary>
    Distance,
    /// <summary>A centre and a point on the rim: a circle in the centre's surface.</summary>
    Radius,
    /// <summary>Point after point: the length along all of them, e.g. round a room.</summary>
    Path,
}

public enum MeasureUnits
{
    Metric,
    Imperial,
}

/// <summary>One measurement: its points (already snapped and projected) and the plane it was taken in.</summary>
public sealed class Measurement
{
    public Measurement(MeasureMode mode, Vector3 normal)
    {
        Mode = mode;
        Normal = normal;
    }

    public MeasureMode Mode { get; }
    public List<Vector3> Points { get; } = new();
    /// <summary>The first point's surface normal: a radius's circle lies across it.</summary>
    public Vector3 Normal { get; }

    /// <summary>Along every segment in turn; for a radius, the radius.</summary>
    public float Length
    {
        get
        {
            var total = 0f;
            for (var i = 1; i < Points.Count; i++)
            {
                total += Vector3.Distance(Points[i - 1], Points[i]);
            }
            return total;
        }
    }

    public Vector3 Delta => Points.Count < 2 ? Vector3.Zero : Points[^1] - Points[0];
    /// <summary>Straight from first to last, across the ground (the X/Z plane).</summary>
    public float Horizontal => new Vector2(Delta.X, Delta.Z).Length();
    public float Vertical => MathF.Abs(Delta.Y);
}

/// <summary>
/// The editor's measuring tool: what is on, what it snaps to, the measurements taken and the one being taken.
/// No rendering or input here -- the game mode feeds it snapped points and the overlay draws what it holds -- so
/// its rules can be checked headless.
/// </summary>
public sealed class MeasureTool
{
    private bool _active;

    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            if (!value)
            {
                Cancel();
                Hover = null;
            }
        }
    }

    private MeasureMode _mode = MeasureMode.Distance;

    /// <summary>Switching drops a measurement half taken in the old mode.</summary>
    public MeasureMode Mode
    {
        get => _mode;
        set
        {
            if (Current != null && Current.Mode != value)
            {
                Cancel();
            }
            _mode = value;
        }
    }
    public MeasureSnapTargets Targets { get; set; } = MeasureSnapTargets.All;
    /// <summary>Every later point is dropped onto the first point's surface (also while Shift is held).</summary>
    public bool StayOnFirstSurface { get; set; }
    public MeasureUnits Units { get; set; } = MeasureUnits.Metric;

    public List<Measurement> Measurements { get; } = new();
    /// <summary>The measurement being taken, its points so far; the hover point is its live end.</summary>
    public Measurement? Current { get; private set; }

    /// <summary>Where a click would put the next point, already snapped and projected.</summary>
    public MeasureSnap? Hover { get; set; }
    /// <summary>The point under the pointer before it was dropped onto the first surface, to draw the drop.</summary>
    public Vector3? HoverUnprojected { get; set; }

    /// <summary>Bumped on every change, so a list showing the measurements knows to rebuild.</summary>
    public int Version { get; private set; }

    /// <summary>The plane later points are dropped onto, while there is one to drop onto.</summary>
    public (Vector3 Point, Vector3 Normal)? LockPlane(bool shiftHeld) =>
        Current is { Points.Count: > 0 } current && (StayOnFirstSurface || shiftHeld || current.Mode == MeasureMode.Radius)
            ? (current.Points[0], current.Normal)
            : null;

    /// <summary>A click: starts a measurement or adds the next point, finishing one that has all it needs.</summary>
    public void Click(MeasureSnap snap)
    {
        if (Current == null)
        {
            Current = new Measurement(Mode, snap.Normal);
            Current.Points.Add(snap.Position);
            Version++;
            return;
        }

        // A second click on the same spot (a double-click) finishes a path rather than adding a zero-length leg.
        if (Vector3.Distance(Current.Points[^1], snap.Position) < 1e-4f)
        {
            if (Current.Mode == MeasureMode.Path)
            {
                Finish();
            }
            return;
        }

        Current.Points.Add(snap.Position);
        Version++;
        if (Current.Mode != MeasureMode.Path)
        {
            Finish();
        }
    }

    /// <summary>Keeps the measurement in progress if it has a length, and starts afresh.</summary>
    public void Finish()
    {
        if (Current is { Points.Count: >= 2 } done)
        {
            Measurements.Add(done);
        }
        Current = null;
        Version++;
    }

    public void Cancel()
    {
        if (Current != null)
        {
            Current = null;
            Version++;
        }
    }

    public void Clear()
    {
        Measurements.Clear();
        Current = null;
        Version++;
    }

    public void Remove(Measurement measurement)
    {
        if (Measurements.Remove(measurement))
        {
            Version++;
        }
    }

    /// <summary>The main figure for a measurement: its length, or its radius.</summary>
    public string Headline(Measurement measurement) => measurement.Mode switch
    {
        MeasureMode.Radius => $"r {Format(measurement.Length)}",
        _ => Format(measurement.Length),
    };

    /// <summary>The figures under the headline: across and up for a distance, diameter and area for a radius.</summary>
    public string Details(Measurement measurement)
    {
        switch (measurement.Mode)
        {
            case MeasureMode.Radius:
                var r = measurement.Length;
                return $"Ø {Format(2 * r)}  ·  {FormatArea(MathF.PI * r * r)}";
            case MeasureMode.Path:
                return measurement.Points.Count > 2 ? $"{measurement.Points.Count - 1} legs  ·  start to end {Format(Vector3.Distance(measurement.Points[0], measurement.Points[^1]))}" : string.Empty;
            default:
                var d = measurement.Delta;
                return $"across {Format(measurement.Horizontal)}  ·  up {Format(measurement.Vertical)}\n"
                    + $"X {FormatSigned(d.X)}  Y {FormatSigned(d.Y)}  Z {FormatSigned(d.Z)}";
        }
    }

    public string Format(float metres) => Format(metres, Units);

    /// <summary>Metres to three places from a metre up, centimetres below one; feet and inches in imperial.</summary>
    public static string Format(float metres, MeasureUnits units)
    {
        var c = CultureInfo.InvariantCulture;
        // Float noise under a tenth of a millimetre would otherwise print as "-0.0 cm".
        if (MathF.Abs(metres) < 5e-5f)
        {
            metres = 0f;
        }
        if (units == MeasureUnits.Imperial)
        {
            var inches = metres / 0.0254f;
            var feet = (int)MathF.Floor(MathF.Abs(inches) / 12f);
            var rest = MathF.Abs(inches) - feet * 12f;
            var sign = metres < 0 ? "-" : string.Empty;
            return feet > 0 ? $"{sign}{feet}' {rest.ToString("0.0", c)}\"" : $"{sign}{rest.ToString("0.00", c)}\"";
        }
        return MathF.Abs(metres) >= 1f
            ? $"{metres.ToString("0.000", c)} m"
            : $"{(metres * 100f).ToString("0.0", c)} cm";
    }

    private string FormatSigned(float metres) => (metres > -5e-5f ? "+" : string.Empty) + Format(metres);

    private string FormatArea(float squareMetres) => Units == MeasureUnits.Imperial
        ? $"{(squareMetres * 10.7639f).ToString("0.00", CultureInfo.InvariantCulture)} ft²"
        : $"{squareMetres.ToString("0.000", CultureInfo.InvariantCulture)} m²";
}
