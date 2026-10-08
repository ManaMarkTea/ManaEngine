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
    [Category("Motion")] public float Lifetime { get; set; } = 3;
    [Category("Motion")] public float Speed { get; set; } = 5;
    [Category("Motion")] public Vector3 Gravity { get; set; } = new(0, -3, 0);
    [Category("Motion")] public float Drag { get; set; }
    [Category("Motion")] public float WindInfluence { get; set; } = 1;
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
        new(BouncePlane1Enabled ? 1 : 0, BouncePlane2Enabled ? 1 : 0, Math.Clamp(Finite(Bounciness, 0.6f), 0, 1), 0), Math.Max(Finite(WindInfluence, 1), 0));
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
