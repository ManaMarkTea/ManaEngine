using System.ComponentModel;
using Mana.Code.Nodes;
using ManaCore.Graphics;

namespace Mana.Code.Components;

/// <summary>
/// Simple additive particles evaluated entirely by WebGPU's vertex shader. Slots recycle at a fixed
/// lifetime, staggered so Count particles are visible immediately. Each particle uses the emitter's
/// pose at birth and moves under world-space gravity. A small transform history avoids per-particle CPU work.
/// </summary>
[DisplayName("GPU Particle Emitter")]
public sealed class ManaGpuParticleEmitterComponent : ManaComponent
{
    public const int MaximumCount = 10_000_000;
    [Category("Emission")] public bool Enabled { get; set; } = true;
    [Category("Emission")] public int Count { get; set; } = 100_000;
    [Category("Emission")] public int Seed { get; set; } = 1;
    [Category("Emission")] public float SpreadDegrees { get; set; } = 30;
    [Category("Emission")] public float Radius { get; set; } = 2;
    /// <summary>Born on the rim of Radius rather than across the disc: rings, tornado walls, accretion disks.</summary>
    [Category("Emission")] public bool EmitFromEdge { get; set; }
    /// <summary>Births bunched into this many spokes; with Swirl Falloff they wind into spiral arms. 0 is even.</summary>
    [Category("Emission")] public int Arms { get; set; }
    /// <summary>How far births scatter either side of an arm, as a share of the gap between arms.</summary>
    [Category("Emission")] public float ArmWidth { get; set; } = 0.2f;
    /// <summary>Launch speed out from the node's up axis, added to Speed along the spread.</summary>
    [Category("Emission")] public float RadialSpeed { get; set; }
    /// <summary>Bursts a lifetime, each born all at once somewhere within Radius: fireworks. 0 is a steady stream.</summary>
    [Category("Emission")] public int Bursts { get; set; }
    /// <summary>Share of launch speed each particle may lose, so a spray has depth rather than one shell.</summary>
    [Category("Motion")] public float SpeedVariation { get; set; }
    [Category("Motion")] public float Lifetime { get; set; } = 3;
    [Category("Motion")] public float Speed { get; set; } = 5;
    [Category("Motion")] public Vector3 Gravity { get; set; } = new(0, -3, 0);
    [Category("Motion")] public float Drag { get; set; }
    [Category("Motion")] public float WindInfluence { get; set; } = 1;
    /// <summary>Degrees a second round the node's up axis at its birth pose.</summary>
    [Category("Swirl")] public float SwirlSpeed { get; set; }
    /// <summary>Slows the swirl with distance from the axis (per metre), so it shears into spirals.</summary>
    [Category("Swirl")] public float SwirlFalloff { get; set; }
    /// <summary>Draws particles in toward the axis (per second); negative flings them out, a widening funnel.</summary>
    [Category("Swirl")] public float Pull { get; set; }
    /// <summary>Metres a particle wanders in a flowing noise field.</summary>
    [Category("Turbulence")] public float Turbulence { get; set; }
    [Category("Turbulence")] public float TurbulenceScale { get; set; } = 2;
    [Category("Turbulence")] public float TurbulenceSpeed { get; set; } = 0.5f;
    [Category("Bounce")] public bool BouncePlane1Enabled { get; set; }
    [Category("Bounce")] public Vector3 BouncePlane1Position { get; set; }
    [Category("Bounce")] public Vector3 BouncePlane1Normal { get; set; } = Vector3.UnitY;
    [Category("Bounce")] public bool BouncePlane2Enabled { get; set; }
    [Category("Bounce")] public Vector3 BouncePlane2Position { get; set; }
    [Category("Bounce")] public Vector3 BouncePlane2Normal { get; set; } = Vector3.UnitX;
    [Category("Bounce")] public float Bounciness { get; set; } = 0.6f;
    [Category("Look")] public float StartSize { get; set; } = 0.025f;
    [Category("Look")] public float EndSize { get; set; } = 0.005f;
    [Category("Look")] public Vector4 StartColor { get; set; } = new(1, 0.5f, 0.1f, 0.6f);
    [Category("Look")] public Vector4 EndColor { get; set; } = new(1, 0.1f, 0, 0);
    [Category("Look")] public float Brightness { get; set; } = 1;
    [Category("Look")] public bool HardEdge { get; set; }
    /// <summary>Seconds of travel each particle is stretched over: sparks and rain rather than dots.</summary>
    [Category("Look")] public float StreakLength { get; set; }
    /// <summary>Share of the colour wheel each particle's hue is scattered over (1 is a rainbow).</summary>
    [Category("Look")] public float HueVariation { get; set; }
    /// <summary>Turns of hue a particle goes round over its life.</summary>
    [Category("Look")] public float HueShift { get; set; }
    [Category("Look")] public float Twinkle { get; set; }
    [Category("Look")] public float SizeVariation { get; set; }
    private double _seconds;
    public const int SpawnHistoryLength = 256;
    private readonly Matrix4x4[] _spawnTransforms = new Matrix4x4[SpawnHistoryLength];
    private Matrix4x4 _lastTransform;
    private float _historyLifetime;
    private uint _spawnRevision;
    private long _spawnTick;

    private float SafeLifetime => Math.Max(Finite(Lifetime, 3), 0.05f);
    private float SpawnInterval => SafeLifetime / (SpawnHistoryLength - 2);

    private void EnsureHistory()
    {
        if (_historyLifetime == SafeLifetime) return;
        _historyLifetime = SafeLifetime;
        _lastTransform = Owner.WorldTransform;
        Array.Fill(_spawnTransforms, _lastTransform);
        _spawnTick = (long)Math.Floor(_seconds / SafeLifetime * (SpawnHistoryLength - 2));
        _spawnRevision++;
    }

    public ManaGpuParticleEmitterComponent(ManaSceneNode owner) : base(owner) => Name = "GPU Particle Emitter";

    public override void UpdateComponent() => Step((float)(ManaTime.FixedFrameSeconds
        ?? Math.Min(ManaTime.GlobalTime.DeltaTime.TotalSeconds, 0.1)));

    public void Step(float seconds)
    {
        if (!Enabled || !float.IsFinite(seconds) || seconds <= 0) return;
        EnsureHistory();
        var before = _seconds;
        _seconds += seconds;
        var tick = (long)Math.Floor(_seconds / SafeLifetime * (SpawnHistoryLength - 2));
        var transform = Owner.WorldTransform;
        // At most one ring of work, even after a long pause. Samples between frame poses are fixed
        // when written, so a particle's interpolated birth pose cannot change as the emitter moves.
        for (var sample = Math.Max(_spawnTick + 1, tick - SpawnHistoryLength + 1); sample <= tick; sample++)
        {
            var fraction = (float)Math.Clamp((sample * (double)SafeLifetime / (SpawnHistoryLength - 2) - before) / seconds, 0, 1);
            var pose = _lastTransform == transform ? transform : Matrix4x4.Lerp(_lastTransform, transform, fraction);
            var index = (int)(sample % SpawnHistoryLength);
            if (_spawnTransforms[index] != pose)
            {
                _spawnTransforms[index] = pose;
                _spawnRevision++;
            }
        }
        _spawnTick = tick;
        _lastTransform = transform;
    }

    public ManaGpuParticleEmitter Describe()
    {
        EnsureHistory();
        return new(
        Owner.WorldTransform, Math.Clamp(Count, 0, MaximumCount),
        // Phase repeats exactly every lifetime; keeping time small avoids long-session float jitter.
        (float)(_seconds % Math.Max(Finite(Lifetime, 3), 0.05f)),
        Math.Max(Finite(Lifetime, 3), 0.05f), Finite(Speed, 0),
        Math.Clamp(Finite(SpreadDegrees, 30), 0, 180), Math.Max(Finite(Radius, 0), 0),
        new(Finite(Gravity.X, 0), Finite(Gravity.Y, 0), Finite(Gravity.Z, 0)),
        Math.Max(Finite(Drag, 0), 0), Math.Max(Finite(StartSize, 0), 0), Math.Max(Finite(EndSize, 0), 0),
        StartColor, EndColor, Math.Max(Finite(Brightness, 1), 0), Seed, HardEdge,
        _spawnTransforms, _spawnRevision, (uint)(_spawnTick % SpawnHistoryLength),
        (uint)(_spawnTick % (SpawnHistoryLength - 2)), SpawnInterval,
        Plane(BouncePlane1Position, BouncePlane1Normal), Plane(BouncePlane2Position, BouncePlane2Normal),
        new(BouncePlane1Enabled ? 1 : 0, BouncePlane2Enabled ? 1 : 0, Math.Clamp(Finite(Bounciness, 0.6f), 0, 1), 0), Math.Max(Finite(WindInfluence, 1), 0),
        new(Finite(SwirlSpeed, 0) * MathF.PI / 180, Math.Max(Finite(SwirlFalloff, 0), 0), Math.Clamp(Finite(Pull, 0), -5, 20), Finite(RadialSpeed, 0)),
        // The noise clock wraps far apart (10 minutes) so the field never visibly jumps the way the lifetime phase would.
        new(Math.Max(Finite(Turbulence, 0), 0), 1 / Math.Max(Finite(TurbulenceScale, 2), 0.01f), Finite(TurbulenceSpeed, 0.5f), (float)(_seconds % 600)),
        new(Math.Clamp(Finite(HueVariation, 0), 0, 1), Finite(HueShift, 0), Math.Clamp(Finite(Twinkle, 0), 0, 1), Math.Clamp(Finite(SizeVariation, 0), 0, 1)),
        new(EmitFromEdge ? 1 : 0, Math.Clamp(Arms, 0, 64), Math.Clamp(Finite(StreakLength, 0), 0, 2), Math.Clamp(Finite(ArmWidth, 0.2f), 0, 1)),
        new(Math.Clamp(Bursts, 0, 1024), (float)(Math.Floor(_seconds / SafeLifetime) % 4096), Math.Clamp(Finite(SpeedVariation, 0), 0, 1), 0));
    }

    // Planes are world-space. Their normals point into the permitted half-space.
    private static Vector4 Plane(Vector3 position, Vector3 normal)
    {
        if (!float.IsFinite(normal.LengthSquared()) || normal.LengthSquared() < 0.000001f) normal = Vector3.UnitY;
        normal = Vector3.Normalize(normal);
        position = new(Finite(position.X, 0), Finite(position.Y, 0), Finite(position.Z, 0));
        return new(normal, -Vector3.Dot(normal, position));
    }

    private static float Finite(float value, float fallback) => float.IsFinite(value) ? value : fallback;
}
