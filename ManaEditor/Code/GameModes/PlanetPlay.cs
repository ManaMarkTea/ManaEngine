using Mana;
using ManaGeoTools.Code.Planet;

namespace Mana.Code.GameModes
{
    /// <summary>
    /// What playing a planet level needs each frame, shared by the desktop/phone play mode and the browser's: the camera
    /// fit (clip planes, fly speed, up) and the planet's streaming -- detail, horizon culling, near ocean, splits and merges.
    /// </summary>
    public sealed class PlanetPlay
    {
        private readonly PlanetNode _planet;
        private PlanetCameraFit? _cameraFit;

        public PlanetPlay(PlanetNode planet)
        {
            _planet = planet;
        }

        public void Tick(ManaWorld world)
        {
            // Play may swap cameras after loading (the free debug camera); the fit follows whichever is current.
            var camera = world.MainCamera;
            if (_cameraFit?.Camera != camera)
            {
                _cameraFit?.Release();
                _cameraFit = new PlanetCameraFit(camera, _planet, surfaceUp: true);
            }
            _cameraFit?.Update();
            _planet.UpdateAutoLod(camera);
            _planet.UpdateHorizonCulling(camera);
            _planet.UpdateNearOcean(camera);
            _planet.UpdateDetailChase();
            _planet.ProcessPendingSplits();
            _planet.ProcessPendingMerges();
        }

        public void Release()
        {
            _cameraFit?.Release();
            _cameraFit = null;
        }
    }
}
