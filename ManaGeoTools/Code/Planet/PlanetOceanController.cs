using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using ManaGeoTools.Code.GeoTiff;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>Owns the far ocean shell and camera-following wave patch of a planet.</summary>
    internal sealed class PlanetOceanController
    {
        private readonly PlanetNode _planet;
        // Shared with terrain sampling so height data and ocean rebuilds use the same synchronization.
        private readonly object _rebuildLock;

        private readonly PlanetOceanMesh _oceanMesh;
        private readonly ManaMeshSceneNode _oceanNode;
        private readonly MeshRendererComponent _oceanRenderer;
        private bool _oceanAttached;

        // Keep the child node stable; detach its renderer when the wave patch is not needed.
        private readonly PlanetNearOceanMesh _nearOceanMesh;
        private readonly ManaSceneNode _nearOceanNode;
        private readonly MeshRendererComponent _nearOceanRenderer;
        private bool _nearOceanAttached;

        // Written by the rebuild task, read every frame by the main thread -- volatile rather than
        // locked because UpdateNearOcean must never block on a rebuild it is only polling for.
        private volatile bool _nearOceanRebuildInFlight;
        private volatile bool _nearOceanReady;
        private volatile bool _nearOceanHeightDataDirty;

        // Wave dimensions use metres, independent of the planet's scene scale.
        private readonly float _metersPerUnit;

        // Separate altitude thresholds prevent attachment flicker near the edge of the wave-detail band.
        private const float NearOceanActivationAltitudeFraction = 0.25f;
        private const float NearOceanDeactivationAltitudeFraction = 0.4f;

        // Size detail from altitude, bounded by the gnomonic projection's useful range.
        private const float NearOceanSpanAltitudeMultiplier = 3f;
        private const float NearOceanMinHalfSpanRadians = 0.0005f;

        // How far the camera has to drift, as a fraction of the current half-span, before the patch
        // re-centers. Rebuilding is ~66k vertices of bathymetry sampling, so this deliberately trades a
        // little wasted footprint for far fewer rebuilds.
        private const float NearOceanRecenterFraction = 0.125f;
        private const float NearOceanRespanFraction = 0.25f;

        // Lift by depth-buffer precision, capped in metres so water does not stand above coastlines.
        private const float OceanDepthLiftQuanta = 3f;
        private const float OceanDepthLiftMaxMeters = 2500f;

        // Depth24Plus (see ManaWebGpuContext._depthFormat).
        private const float DepthBufferLevels = 16777216f;


        public bool Enabled { get; set; } = true;

        public PlanetOceanController(PlanetNode planet, object rebuildLock)
        {
            _planet = planet;
            _rebuildLock = rebuildLock;
            var context = planet.Context;
            var radius = planet.Radius;
            var oceanResolution = planet.OceanResolution;
            _metersPerUnit = PlanetLoader.EarthRadiusMeters / MathF.Max(radius, 1e-6f);

            // Far tier: a single un-tiled, wave-free shell covering the whole globe (not one per
            // terrain patch), masked to real ocean coverage once height data arrives.
            _oceanMesh = new PlanetOceanMesh(context, radius, oceanResolution, heightLibrary: null);
            _oceanNode = new ManaMeshSceneNode(context, _oceanMesh) { Name = "Ocean" };
            _oceanRenderer = _oceanNode.MeshRenderer;
            _planet.AddChild(_oceanNode);

            // Hide the initially unmasked sphere until baseline data can distinguish sea from land.
            _oceanAttached = true;
            DetachFarOcean();

            // Near tier: starts detached, and stays that way until UpdateNearOcean sees the camera drop
            // into the altitude band where waves are actually resolvable.
            _nearOceanMesh = new PlanetNearOceanMesh(
                context, radius, _metersPerUnit, Vector3.UnitY, NearOceanMinHalfSpanRadians, heightLibrary: null);
            _nearOceanNode = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Near Ocean" };
            _nearOceanRenderer = new MeshRendererComponent(_nearOceanNode, _nearOceanMesh) { Name = "Near Ocean" };
            _planet.AddChild(_nearOceanNode);

            SetFarOceanNearFade(
                active: false,
                worldCenter: Vector3.Zero,
                innerRadius: 0f,
                outerRadius: 0f,
                depthLiftCoefficient: 0f,
                depthLiftMaxUnits: 0f);
        }

        public void ApplyHeightData(GeoTiffLibrary library)
        {
            lock (_rebuildLock)
            {
                _oceanMesh.ApplyHeightData(library);
                // The near patch refreshes lazily when the camera next needs it.
                _nearOceanHeightDataDirty = true;
            }
        }

        /// <summary>Updates wave detail and cross-fade uniforms on the main thread.</summary>
        public void Update(CameraComponent camera)
        {
            // Applies to the far shell whether or not the near patch is active, so it is computed and
            // pushed before any of the early-outs below.
            var (depthLiftCoefficient, depthLiftMaxUnits) = ComputeDepthLift(camera);

            // Neither tier is worth showing before the height data that masks it: the far shell would be an
            // opaque sea-level sphere over every continent, and the near patch would sample bathymetry that
            // has not decoded yet. This method already owns which tier is visible, so the gate lives here.
            if (!_planet.IsOceanMaskReady)
            {
                DetachFarOcean();
                DetachNearOcean();
                _nearOceanReady = false;
                SetFarOceanNearFade(active: false, Vector3.Zero, 0f, 0f, depthLiftCoefficient, depthLiftMaxUnits);
                return;
            }

            AttachFarOcean();

            if (!Enabled)
            {
                DetachNearOcean();
                SetFarOceanNearFade(active: false, Vector3.Zero, 0f, 0f, depthLiftCoefficient, depthLiftMaxUnits);
                return;
            }

            if (!Matrix4x4.Invert(_planet.WorldTransform, out var worldToLocal))
            {
                return;
            }

            // Same local-space framing UpdateAutoLod uses: the planet's own world transform is undone
            // once here, so nothing below has to reapply it per vertex or per patch.
            var cameraLocalPosition = Vector3.Transform(camera.Owner.Position, worldToLocal);
            var cameraDistanceFromCenter = cameraLocalPosition.Length();
            if (cameraDistanceFromCenter < 0.001f)
            {
                return;
            }

            var cameraDirection = cameraLocalPosition / cameraDistanceFromCenter;
            var altitudeFraction = MathF.Max(0f, (cameraDistanceFromCenter - _planet.Radius) / _planet.Radius);

            // Hysteresis: once attached, the patch only lets go at the higher ceiling.
            var ceilingFraction = _nearOceanAttached
                ? NearOceanDeactivationAltitudeFraction
                : NearOceanActivationAltitudeFraction;
            if (altitudeFraction > ceilingFraction)
            {
                DetachNearOcean();
                SetFarOceanNearFade(active: false, Vector3.Zero, 0f, 0f, depthLiftCoefficient, depthLiftMaxUnits);
                // Forces a fresh rebuild rather than reattaching stale geometry on the way back down.
                _nearOceanReady = false;
                return;
            }

            // Altitude fraction IS an angle to good approximation: a camera one tenth of a radius up
            // sees roughly a tenth of a radian of ground around the point below it.
            var targetHalfSpanRadians = Math.Clamp(
                altitudeFraction * NearOceanSpanAltitudeMultiplier,
                NearOceanMinHalfSpanRadians,
                PlanetNearOceanMesh.MaxHalfSpanRadians);

            if (!_nearOceanRebuildInFlight)
            {
                var currentHalfSpanRadians = _nearOceanMesh.HalfSpanRadians;
                // Great-circle distance / radius is the angle between the patch center and the camera's
                // sub-point, which is directly comparable to the span angle.
                var driftRadians = GreatCircleDistanceFromCameraDirection(_nearOceanMesh.CenterDirection, cameraDirection) / _planet.Radius;
                var needsRebuild = !_nearOceanReady
                    || _nearOceanHeightDataDirty
                    || driftRadians > currentHalfSpanRadians * NearOceanRecenterFraction
                    || MathF.Abs(targetHalfSpanRadians - currentHalfSpanRadians) > currentHalfSpanRadians * NearOceanRespanFraction;

                if (needsRebuild)
                {
                    BeginNearOceanRebuild(cameraDirection, targetHalfSpanRadians);
                }
            }

            // Only show the patch once a rebuild has actually placed it under this camera -- attaching
            // sooner would flash the previous location's geometry for however long the rebuild takes.
            if (!_nearOceanReady)
            {
                DetachNearOcean();
                SetFarOceanNearFade(active: false, Vector3.Zero, 0f, 0f, depthLiftCoefficient, depthLiftMaxUnits);
                return;
            }

            AttachNearOcean();
            PushNearOceanUniforms(depthLiftCoefficient, depthLiftMaxUnits);
        }

        // Standard depth precision degrades as distance squared; match that curve in both shaders.
        private (float Coefficient, float MaxUnits) ComputeDepthLift(CameraComponent camera)
        {
            var nearPlane = MathF.Max(camera.NearClipPlane, 1e-4f);
            var coefficient = OceanDepthLiftQuanta / (nearPlane * DepthBufferLevels);
            var maxUnits = OceanDepthLiftMaxMeters / _metersPerUnit;
            return (coefficient, maxUnits);
        }

        private void BeginNearOceanRebuild(Vector3 centerDirection, float halfSpanRadians)
        {
            _nearOceanRebuildInFlight = true;
            _nearOceanHeightDataDirty = false;
            var heightLibrary = _planet.HeightLibrary;

            Task.Run(() =>
            {
                if (string.IsNullOrEmpty(System.Threading.Thread.CurrentThread.Name))
                {
                    System.Threading.Thread.CurrentThread.Name = "Planet near ocean rebuild";
                }
                try
                {
                    // Held for the same reason PlanetPatchMesh's rebuilds hold it: the per-vertex
                    // bathymetry sampling reads the height library, and ApplyTileToOverlappingPatches adds
                    // tiles to it under this same lock.
                    lock (_rebuildLock)
                    {
                        _nearOceanMesh.RecenterTo(centerDirection, halfSpanRadians, heightLibrary);
                    }

                    _nearOceanReady = true;
                }
                finally
                {
                    _nearOceanRebuildInFlight = false;
                }
            });
        }

        private void PushNearOceanUniforms(float depthLiftCoefficient, float depthLiftMaxUnits)
        {
            // The fade radii are patch-local lengths, so a scaled PlanetNode needs them scaled too --
            // ocean.wgsl compares them against a world-space distance. Uniform scale is assumed, which
            // is what every planet setup here uses.
            var worldScale = new Vector3(_planet.WorldTransform.M11, _planet.WorldTransform.M12, _planet.WorldTransform.M13).Length();
            var worldCenter = Vector3.Transform(_nearOceanMesh.CenterDirection * _planet.Radius, _planet.WorldTransform);
            SetFarOceanNearFade(
                active: true,
                worldCenter,
                _nearOceanMesh.EdgeFadeInnerRadiusUnits * worldScale,
                _nearOceanMesh.EdgeFadeOuterRadiusUnits * worldScale,
                depthLiftCoefficient,
                depthLiftMaxUnits);

            var material = _nearOceanRenderer.Material;
            material.SetShaderUniform("depthLiftCoefficient", depthLiftCoefficient);
            material.SetShaderUniform("depthLiftMaxUnits", depthLiftMaxUnits);
            material.SetShaderUniform("waveEast", _nearOceanMesh.East);
            material.SetShaderUniform("waveNorth", _nearOceanMesh.North);
            material.SetShaderUniform("unitsPerMeter", 1f / _metersPerUnit);
            material.SetShaderUniform("waveTileMeters", PlanetNearOceanMesh.WaveTileMeters);
            material.SetShaderUniform("vertexSpacingMeters", _nearOceanMesh.VertexSpacingMeters);
        }

        private void SetFarOceanNearFade(
            bool active,
            Vector3 worldCenter,
            float innerRadius,
            float outerRadius,
            float depthLiftCoefficient,
            float depthLiftMaxUnits)
        {
            var material = _oceanRenderer.Material;
            material.SetShaderUniform("nearFadeCenter", worldCenter);
            material.SetShaderUniform("nearFadeInnerRadius", active ? innerRadius : 0f);
            // ocean.wgsl reads a non-positive outer radius as "no near patch", so this one value is
            // what actually switches the shell back to full coverage.
            material.SetShaderUniform("nearFadeOuterRadius", active ? outerRadius : 0f);
            material.SetShaderUniform("depthLiftCoefficient", depthLiftCoefficient);
            material.SetShaderUniform("depthLiftMaxUnits", depthLiftMaxUnits);
        }

        private void AttachFarOcean()
        {
            if (_oceanAttached)
            {
                return;
            }

            _oceanNode.Components.Add(_oceanRenderer);
            _oceanAttached = true;
            Console.WriteLine("[Ocean] Far shell attached (baseline height data decoded, mask is valid).");
        }

        private void DetachFarOcean()
        {
            if (!_oceanAttached)
            {
                return;
            }

            // Same selection hand-off DetachNearOcean does, for the same reason: the renderer is about to
            // leave the scene graph.
            var selection = _planet.Context.World.Selection;
            if (ReferenceEquals(selection.SelectedComponent, _oceanRenderer))
            {
                selection.SelectedComponent = null;
            }

            _oceanNode.Components.Remove(_oceanRenderer);
            _oceanAttached = false;
        }

        private void AttachNearOcean()
        {
            if (_nearOceanAttached)
            {
                return;
            }

            _nearOceanNode.Components.Add(_nearOceanRenderer);
            _nearOceanAttached = true;

            // Logged because "the high-detail water isn't showing up" is otherwise indistinguishable
            // from "it is showing up but looks like the flat shell" -- the span and vertex spacing say
            // which, since they're what decide whether waves are big enough to see at this altitude.
            Console.WriteLine(
                $"[NearOcean] Attached: halfSpan={_nearOceanMesh.HalfSpanRadians:F4} rad "
                + $"({_nearOceanMesh.HalfSpanMeters / 1000f:F1} km), vertexSpacing={_nearOceanMesh.VertexSpacingMeters:F1} m.");
        }

        private void DetachNearOcean()
        {
            if (!_nearOceanAttached)
            {
                return;
            }

            Console.WriteLine("[NearOcean] Detached (camera left the near-ocean altitude band).");

            // The renderer is about to leave the scene graph; drop it as the selection first, the same
            // way BeginSplitLocked/FinalizeMerge do before removing a patch's component.
            var selection = _planet.Context.World.Selection;
            if (ReferenceEquals(selection.SelectedComponent, _nearOceanRenderer))
            {
                selection.SelectedComponent = null;
            }

            _nearOceanNode.Components.Remove(_nearOceanRenderer);
            _nearOceanAttached = false;
        }

        private float GreatCircleDistanceFromCameraDirection(Vector3 patchLocalCenter, Vector3 cameraDirection)
        {
            var patchDirection = Vector3.Normalize(patchLocalCenter);
            var cosAngle = Math.Clamp(Vector3.Dot(patchDirection, cameraDirection), -1f, 1f);
            return MathF.Acos(cosAngle) * _planet.Radius;
        }

    }
}
