using Mana;
using Mana.Code.Nodes;
using ManaGeoTools.Code;
using ManaGeoTools.Code.Geocoding;
using ManaGeoTools.Code.Planet;

namespace ManaEditor.Code.Search
{
    /// <summary>
    /// Flies the camera to a search result and drops a marker there.
    /// <para>
    /// Shared by both search panels rather than duplicated, so the marker is one node that moves rather than
    /// one per panel: two markers left behind in the scene is the sort of thing nobody notices until the
    /// second one is somewhere you have already been.
    /// </para>
    /// </summary>
    public sealed class PlaceSearchNavigator
    {
        private static readonly Vector4 MarkerColor = Colors.Orange;

        private ManaSceneNode? _marker;

        public void FlyTo(GeocodeResult result, ManaContext context, PlanetNode planet, CameraComponent camera)
        {
            var latLong = new LatLong(result.Longitude, result.Latitude);
            var worldPoint = Vector3.Transform(
                latLong.ToSphere(planet.Radius, altitudeScale: 1.0f),
                planet.WorldTransform);

            // The planet's scene-unit radius is arbitrary, so both the altitude and the marker are sized in
            // real metres and converted through the same ratio.
            var metersPerSceneUnit = PlanetLoader.EarthRadiusMeters / planet.Radius;

            camera.FlyToWorldPosition(worldPoint, PlaceSearchModel.ArrivalAltitudeMeters / metersPerSceneUnit);
            PlaceMarker(context, worldPoint, PlaceSearchModel.MarkerRadiusMeters / metersPerSceneUnit);
            planet.RequestDetailAt(result.Longitude, result.Latitude);
        }

        private void PlaceMarker(ManaContext context, Vector3 worldPosition, float radius)
        {
            if (_marker == null)
            {
                _marker = new ManaSceneNode(context, Matrix4x4.CreateTranslation(worldPosition))
                {
                    Name = "Search Marker"
                };

                _marker.Components.Add(new MeshRendererComponent(_marker, new ManaSphere(context, MarkerColor, radius)));
                context.World.DefaultScene.Children.Add(_marker);
                return;
            }

            _marker.Position = worldPosition;
            _marker.UpdateTransform();
        }
    }
}
