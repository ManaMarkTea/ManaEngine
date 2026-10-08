using System.Numerics;

namespace ManaCore.Graphics;

/// <summary>A procedural, continuously recycling GPU stream. No per-particle CPU data.</summary>
public readonly record struct ManaGpuParticleEmitter(
    Matrix4x4 Transform, int Count, float Seconds, float Lifetime, float Speed,
    float SpreadDegrees, float Radius, Vector3 Gravity, float Drag,
    float StartSize, float EndSize, Vector4 StartColor, Vector4 EndColor,
    float Brightness, int Seed, bool HardEdge,
    ReadOnlyMemory<Matrix4x4> SpawnTransforms, uint SpawnRevision,
    uint SpawnHead, uint SpawnTickWithinLifetime, float SpawnInterval, Vector4 BouncePlane1, Vector4 BouncePlane2, Vector4 BounceSettings, float WindInfluence);
