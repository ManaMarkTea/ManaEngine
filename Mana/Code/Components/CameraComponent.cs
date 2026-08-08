using ImGuiNET;
using Mana.Code.Nodes;
using ManaCore.Graphics;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace Mana
{
    public enum CameraViewPreset
    {
        Front,
        Back,
        Left,
        Right,
        Top,
        Bottom
    }

    public class CameraComponent : ManaComponent
    {
        public bool OrthoGraphic = false;
        public float NearClipPlane = 0.1f;
        public float FarClipPlane = 1000.0f;
        public float AspectRatio = 1.0f;
        public float FieldOfViewDegrees = 55.0f;
        public float OrthographicHeight = 80.0f;
        public float PanSpeed = 1.0f;
        public bool TouchpadPanInOrthographic = true;
        public Func<bool>? ShouldCaptureMouseInput { get; set; }
        public Vector2D<int> DebugPointCount = new Vector2D<int>(5, 3);


        public Matrix4x4 _ProjectionMatrix;
        public Matrix4x4 _ViewMatrix;
        public Matrix4x4 _WorldInverseMatrix;
        public Vector3 ViewParams { get; internal set; }
        public Vector3[] ViewAxes { get; internal set; } = new Vector3[3];


        float _MovementSpeed = 200;
        public float MovementSpeed
        {
            get => _MovementSpeed;
            set => _MovementSpeed = value;
        }

        int _MouseAverageSize = 4;
        Vector2 _MouseSensitivity = new(8.0f, 8.0f);
        Vector2 _LastMousePos;

        Queue<float> _deltaHistoryX = new Queue<float>();
        Queue<float> _deltaHistoryY = new Queue<float>();

        Queue<float> _deltaAvgHistoryX = new Queue<float>();
        Queue<float> _deltaAvgHistoryY = new Queue<float>();
        private float _viewCenterDistance;

        public CameraComponent(ManaSceneNode owner) : base(owner)
        {
            owner.Context.Window.Resize += _Window_Resize;
            UpdateProjection();
            _viewCenterDistance = EstimateInitialViewCenterDistance(owner);
        }

        private void _Window_Resize(Vector2D<int> obj)
        {
            UpdateProjection();
        }

        public void UpdateProjection()
        {
            var viewportSize = GetProjectionViewportSize();

            AspectRatio = (float)viewportSize.X / (float)viewportSize.Y;
            if (OrthoGraphic)
            {
                OrthographicHeight = MathF.Max(1.0f, OrthographicHeight);
                _ProjectionMatrix = Matrix4x4.CreateOrthographic(OrthographicHeight * AspectRatio, OrthographicHeight, NearClipPlane, FarClipPlane);
                ViewParams = new Vector3(OrthographicHeight * AspectRatio, OrthographicHeight, NearClipPlane);
                return;
            }
            else
            {
                _ProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(FieldOfViewDegrees.ToRadians(), AspectRatio, NearClipPlane, FarClipPlane);
            }

            float planeHeight = NearClipPlane * MathF.Tan(FieldOfViewDegrees.ToRadians() * 0.5f) * 2.0f;
            float planeWidth = planeHeight * AspectRatio;

            ViewParams = new Vector3(planeWidth, planeHeight, NearClipPlane);
        }

        public void SetProjectionMode(bool orthographic)
        {
            if (OrthoGraphic == orthographic)
            {
                return;
            }

            OrthoGraphic = orthographic;
            UpdateProjection();
        }

        public void SetViewPreset(CameraViewPreset preset)
        {
            float distance = GetViewCenterDistance();
            Vector3 target = GetViewCenter(distance);

            Owner.Rotation = preset switch
            {
                CameraViewPreset.Back => new Vector3(0.0f, 180.0f.ToRadians(), 0.0f),
                CameraViewPreset.Left => new Vector3(0.0f, -90.0f.ToRadians(), 0.0f),
                CameraViewPreset.Right => new Vector3(0.0f, 90.0f.ToRadians(), 0.0f),
                CameraViewPreset.Top => new Vector3(-90.0f.ToRadians(), 0.0f, 0.0f),
                CameraViewPreset.Bottom => new Vector3(90.0f.ToRadians(), 0.0f, 0.0f),
                _ => Vector3.Zero,
            };

            Owner.Position = target - Owner.Forward.Normalized() * distance;
            UpdateViewMatrix();
        }

        // "Frame selected" (F key, see VoxelEditorGameMode) -- pulls the camera back/forward along its
        // CURRENT forward direction (orientation is untouched, matching SetViewPreset's own "target -
        // Forward*distance" pattern) until the selection's world bounds fit the vertical FOV. Prefers the
        // specific selected component's own bounds; falls back to the union of every MeshRendererComponent
        // on the selected node (e.g. selecting a bare PlanetNode shared root node with several patches).
        // Returns false (no-op) if nothing selected has any mesh bounds to frame.
        public bool TryFrameSelection(MeshRendererComponent? selectedComponent, ManaSceneNode? selectedNode)
        {
            if (!TryComputeSelectionWorldBounds(selectedComponent, selectedNode, out var worldMin, out var worldMax))
            {
                return false;
            }

            var center = (worldMin + worldMax) * 0.5f;
            var radius = Vector3.Distance(center, worldMax);
            FrameWorldBounds(center, radius);
            return true;
        }

        private static bool TryComputeSelectionWorldBounds(MeshRendererComponent? selectedComponent, ManaSceneNode? selectedNode, out Vector3 worldMin, out Vector3 worldMax)
        {
            // Local function can't capture the method's own out parameters directly (C# forbids it), so
            // accumulate into plain locals and assign the out parameters once at the end.
            var min = Vector3.Zero;
            var max = Vector3.Zero;
            var found = false;

            void Include(MeshRendererComponent renderer)
            {
                if (!renderer.TryGetWorldBounds(out var rendererMin, out var rendererMax))
                {
                    return;
                }

                min = found ? Vector3.Min(min, rendererMin) : rendererMin;
                max = found ? Vector3.Max(max, rendererMax) : rendererMax;
                found = true;
            }

            // Recurses into children -- a node like PlanetNode never carries a MeshRendererComponent
            // itself (it's a pure container), only its descendants do, so framing the whole thing
            // requires walking the subtree rather than just its own direct Components.
            void IncludeSubtree(ManaSceneNode node)
            {
                foreach (var renderer in node.Components.OfType<MeshRendererComponent>())
                {
                    Include(renderer);
                }

                foreach (var child in node.Children)
                {
                    IncludeSubtree(child);
                }
            }

            if (selectedComponent != null)
            {
                Include(selectedComponent);
            }
            else if (selectedNode != null)
            {
                IncludeSubtree(selectedNode);
            }

            worldMin = min;
            worldMax = max;
            return found;
        }

        // Padding keeps the object from touching the screen edge; distance is clamped to at least twice
        // the near clip plane so an extremely small/degenerate bounds (e.g. a single-point mesh) can't
        // put the camera on top of (or behind) its own near plane.
        private const float FramingPaddingMultiplier = 1.25f;

        private void FrameWorldBounds(Vector3 worldCenter, float worldRadius)
        {
            var radius = MathF.Max(worldRadius, 0.05f);
            var distance = (radius / MathF.Sin(FieldOfViewDegrees.ToRadians() * 0.5f)) * FramingPaddingMultiplier;
            distance = MathF.Max(distance, NearClipPlane * 2.0f);

            Owner.Position = worldCenter - Owner.Forward.Normalized() * distance;
            _viewCenterDistance = distance;
            UpdateViewMatrix();
        }

        public void UpdateViewMatrix()
        {
            Vector3 forward = Owner.Forward.Normalized();
            Vector3 up = Owner.Up.Normalized();
            _ViewMatrix = Matrix4x4.CreateLookAt(Owner.Position, Owner.Position + forward, up);
        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();

            var MouseState = Context.Input.Mice.First();
            var KeyboardState = Context.Input.Keyboards.First();
            bool externalCapturesMouse = ShouldCaptureMouseInput?.Invoke() == true;
            bool imguiCapturesMouse = GetImGuiWantsCaptureMouse() || externalCapturesMouse;

            var position = Owner.Position;
            var rotation = Owner.Rotation;
            var rotationEuler = rotation;

            float pitch = rotationEuler.X;
            float yaw = rotationEuler.Y;
            float roll = rotationEuler.Z;

            var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll);
            Vector3 forward = Vector3.TransformNormal(-Vector3.UnitZ, rotationMatrix);
            Vector3 right = Vector3.TransformNormal(Vector3.UnitX, rotationMatrix);

            var worldUp = new Vector3(0, 1, 0);

            float delta = 0.001f;
            float dxAvg = 0, dyAvg = 0;
            bool cameraMouseActive = !imguiCapturesMouse &&
                (MouseState.IsButtonPressed(MouseButton.Middle) || MouseState.IsButtonPressed(MouseButton.Right));
            Vector2 mouseDelta = UpdateAverage(delta, cameraMouseActive, ref dxAvg, ref dyAvg);

            if (KeyboardState.IsKeyPressed(Key.W))
            {
                position += forward * +delta * _MovementSpeed;
            }

            if (KeyboardState.IsKeyPressed(Key.S))
            {
                position += forward * -delta * _MovementSpeed;
            }

            if (KeyboardState.IsKeyPressed(Key.A))
            {
                position += right * -delta * _MovementSpeed;
            }

            if (KeyboardState.IsKeyPressed(Key.D))
            {
                position += right * delta * _MovementSpeed;
            }

            if (KeyboardState.IsKeyPressed(Key.E))
            {
                position += worldUp * +delta * _MovementSpeed;
            }

            if (KeyboardState.IsKeyPressed(Key.Q))
            {
                position += worldUp * -delta * _MovementSpeed;
            }

            if (!imguiCapturesMouse && MouseState.IsButtonPressed(MouseButton.Middle))
            {
                position += GetPanTranslation(mouseDelta);
            }

            if (!imguiCapturesMouse && OrthoGraphic && !MouseState.IsButtonPressed(MouseButton.Right))
            {
                var scroll = MouseState.ScrollWheels[0];
                if (scroll.Y != 0.0f)
                {
                    ZoomOrthographicAtMouse(scroll.Y, MouseState.Position, ref position);
                }

                if (TouchpadPanInOrthographic && scroll.X != 0.0f)
                {
                    const float touchpadPanPixelsPerScrollUnit = 48.0f;
                    position += GetPanTranslation(new Vector2(scroll.X, 0.0f) * touchpadPanPixelsPerScrollUnit);
                }
            }

            var transform = Owner.LocalTransform;
            transform.Translation = Vector3.Zero;
            transform *= Matrix4x4.CreateTranslation(position);
            Owner.LocalTransform = transform;

            if (MouseState.IsButtonPressed(MouseButton.Right))
            {
                rotationEuler.Y += dxAvg * 0.4f;
                rotationEuler.X += dyAvg * 0.4f;
                rotationEuler.X = rotationEuler.X.Clamp(-89.0f.ToRadians(), 89.0f.ToRadians());
            }

            if (MouseState.IsButtonPressed(MouseButton.Right) && MouseState.ScrollWheels[0].Y != 0)
            {
                const float scrollSpeedMultiplierPerNotch = 1.5f;
                _MovementSpeed *= MathF.Pow(scrollSpeedMultiplierPerNotch, MouseState.ScrollWheels[0].Y);
                _MovementSpeed = _MovementSpeed.Clamp(0.25f, 5000.0f);
            }

            Owner.Position = position;
            Owner.Rotation = rotationEuler;
            UpdateViewMatrix();

        }

        private static bool GetImGuiWantsCaptureMouse()
        {
            try
            {
                if (ImGui.GetCurrentContext() == IntPtr.Zero)
                {
                    return false;
                }

                return ImGui.GetIO().WantCaptureMouse;
            }
            catch
            {
                return false;
            }
        }

        public float[] GetMouseDeltaHistoryXSnapshot() => _deltaHistoryX.ToArray();

        public float[] GetMouseDeltaHistoryYSnapshot() => _deltaHistoryY.ToArray();

        private Vector3 GetPanTranslation(Vector2 screenDelta)
        {
            float unitsPerPixel = GetPanUnitsPerPixel();
            return (-Owner.Right * screenDelta.X + Owner.Up * screenDelta.Y) * unitsPerPixel * PanSpeed;
        }

        private void ZoomOrthographicAtMouse(float scrollY, Vector2 mousePosition, ref Vector3 position)
        {
            const float zoomMultiplierPerNotch = 1.15f;
            float oldOrthographicHeight = MathF.Max(1.0f, OrthographicHeight);
            Vector3 worldPointBeforeZoom = GetOrthographicWorldPoint(mousePosition, oldOrthographicHeight, position);

            OrthographicHeight = oldOrthographicHeight / MathF.Pow(zoomMultiplierPerNotch, scrollY);
            OrthographicHeight = OrthographicHeight.Clamp(1.0f, 1000.0f);
            UpdateProjection();

            Vector3 worldPointAfterZoom = GetOrthographicWorldPoint(mousePosition, OrthographicHeight, position);
            position += worldPointBeforeZoom - worldPointAfterZoom;
        }

        private float GetPanUnitsPerPixel()
        {
            float viewportHeight = MathF.Max(1.0f, Context.Window.Size.Y);
            if (OrthoGraphic)
            {
                return MathF.Max(1.0f, OrthographicHeight) / viewportHeight;
            }

            float targetDistance = GetViewCenterDistance();
            float visibleHeight = 2.0f * targetDistance * MathF.Tan(FieldOfViewDegrees.ToRadians() * 0.5f);
            return visibleHeight / viewportHeight;
        }

        private Vector3 GetViewCenter(float distance)
        {
            return Owner.Position + Owner.Forward.Normalized() * distance;
        }

        private float GetViewCenterDistance()
        {
            if (_viewCenterDistance <= NearClipPlane)
            {
                _viewCenterDistance = EstimateInitialViewCenterDistance(Owner);
            }

            return MathF.Max(NearClipPlane, _viewCenterDistance);
        }

        private float EstimateInitialViewCenterDistance(ManaSceneNode owner)
        {
            float originDistance = Vector3.Distance(owner.Position, Vector3.Zero);
            return MathF.Max(originDistance, MathF.Max(OrthographicHeight, 1.0f));
        }

        private Vector3 GetOrthographicWorldPoint(Vector2 screenPosition, float orthographicHeight, Vector3 cameraPosition)
        {
            float viewportWidth = MathF.Max(1.0f, Context.Window.Size.X);
            float viewportHeight = MathF.Max(1.0f, Context.Window.Size.Y);
            float aspectRatio = viewportWidth / viewportHeight;

            float normalizedX = (screenPosition.X / viewportWidth) * 2.0f - 1.0f;
            float normalizedY = 1.0f - (screenPosition.Y / viewportHeight) * 2.0f;

            float orthographicWidth = orthographicHeight * aspectRatio;
            Vector3 cameraRight = Owner.Right.Normalized();
            Vector3 cameraUp = Owner.Up.Normalized();

            return cameraPosition +
                   cameraRight * (normalizedX * orthographicWidth * 0.5f) +
                   cameraUp * (normalizedY * orthographicHeight * 0.5f);
        }

        Vector2 UpdateAverage(float deltaTime, bool acceptMouseDelta, ref float dxAvg, ref float dyAvg)
        {
            var MouseState = Context.Input.Mice.First();

            var mouseDelta = MouseState.Position - _LastMousePos;
            _LastMousePos = MouseState.Position;
            if (!acceptMouseDelta)
            {
                dxAvg = 0.0f;
                dyAvg = 0.0f;
                return Vector2.Zero;
            }

            float dx = -mouseDelta.X * deltaTime * _MouseSensitivity.X;
            float dy = -mouseDelta.Y * deltaTime * _MouseSensitivity.Y;
            _deltaHistoryX.Enqueue(dx);
            _deltaHistoryY.Enqueue(dy);

            int avgSize = Math.Min(_MouseAverageSize, _deltaHistoryX.Count);
            dxAvg = dx;
            dyAvg = dy;
            _deltaAvgHistoryX.Enqueue(dx);
            _deltaAvgHistoryY.Enqueue(dy);

            if (_deltaHistoryX.Count > 90)
            {
                _deltaHistoryX.Dequeue();
                _deltaHistoryY.Dequeue();
                _deltaAvgHistoryX.Dequeue();
                _deltaAvgHistoryY.Dequeue();
            }

            return mouseDelta;
        }

        public Ray ProjectScreenToWorld(in Vector3 MousePositionStart,  in Vector3 MousePositionEnd)
        {            
            Matrix4x4.Invert(_ViewMatrix * _ProjectionMatrix, out Matrix4x4 inverseWorldMatrix);
            Matrix4x4.Invert(_ProjectionMatrix, out Matrix4x4 inverseProjectionMatrix);
            Matrix4x4.Invert(_ViewMatrix, out Matrix4x4 inverseViewMatrix);

            Matrix4x4 world = _ViewMatrix * _ProjectionMatrix;

            var windowSize = GetProjectionViewportSize();
            var mouseStart = ConvertMousePositionForProjection(MousePositionStart);
            var mouseEnd = ConvertMousePositionForProjection(MousePositionEnd);

            Vector4 viewportCoordinate;
            viewportCoordinate.X = 2.0f * mouseStart.X / (float)windowSize.X - 1;
            viewportCoordinate.Y = -(2.0f * mouseStart.Y / (float)windowSize.Y - 1);
            viewportCoordinate.Z = mouseStart.Z;
            viewportCoordinate.W = 1.0f;

            Vector4 viewportCoordinateEnd;
            viewportCoordinateEnd.X = 2.0f * mouseEnd.X / (float)windowSize.X - 1;
            viewportCoordinateEnd.Y = -(2.0f * mouseEnd.Y / (float)windowSize.Y - 1);
            viewportCoordinateEnd.Z = mouseEnd.Z;
            viewportCoordinateEnd.W = 1.0f;
            
            Vector4 eyeSpaceCoordinate = Vector4.Transform(viewportCoordinate, inverseProjectionMatrix);
            Vector4 eyeSpaceCoordinateEnd = Vector4.Transform(viewportCoordinateEnd, inverseProjectionMatrix);

            Vector4 worldCoordinate = Vector4.Transform(eyeSpaceCoordinate, inverseViewMatrix);            
            Vector4 worldCoordinateEnd = Vector4.Transform(eyeSpaceCoordinateEnd, inverseViewMatrix);

            worldCoordinate /= worldCoordinate.W;
            worldCoordinateEnd /= worldCoordinateEnd.W;
            
            return new Ray(worldCoordinate.ToVector3(), (worldCoordinateEnd - worldCoordinate).ToVector3().Normalized());
        }

        private Vector2D<int> GetProjectionViewportSize()
        {
            if (Context.GraphicsBackend == ManaGraphicsBackendKind.WebGpu &&
                Context.Window.FramebufferSize.X > 0 &&
                Context.Window.FramebufferSize.Y > 0)
            {
                return Context.Window.FramebufferSize;
            }

            return Context.Window.Size;
        }

        private Vector3 ConvertMousePositionForProjection(Vector3 mousePosition)
        {
            if (Context.GraphicsBackend != ManaGraphicsBackendKind.WebGpu)
            {
                return mousePosition;
            }

            var windowSize = Context.Window.Size;
            var framebufferSize = Context.Window.FramebufferSize;
            if (windowSize.X <= 0 || windowSize.Y <= 0 ||
                framebufferSize.X <= 0 || framebufferSize.Y <= 0 ||
                (windowSize.X == framebufferSize.X && windowSize.Y == framebufferSize.Y))
            {
                return mousePosition;
            }

            if (mousePosition.X > windowSize.X + 1.0f || mousePosition.Y > windowSize.Y + 1.0f)
            {
                return mousePosition;
            }

            return new Vector3(
                mousePosition.X * framebufferSize.X / windowSize.X,
                mousePosition.Y * framebufferSize.Y / windowSize.Y,
                mousePosition.Z);
        }

        public void AddDebugCameraRayTest(ManaShapeFactory shapes)
        {
            var transform = Owner.WorldTransform;

            float pegSize = 0.25f * 0.5f;
 
            shapes.AddCube(Vector3.UnitX * 0.5f, Colors.Red, new Vector3(1, pegSize, pegSize)).Name = "Camera Debug X Axis";
            shapes.AddCube(Vector3.UnitY * 0.5f, Colors.Green, new Vector3(pegSize, 1, pegSize)).Name = "Camera Debug Y Axis";
            shapes.AddCube(Vector3.UnitZ * 0.5f, Colors.Blue, new Vector3(pegSize, pegSize, 1)).Name = "Camera Debug Z Axis";

            var xArrow = shapes.AddPyramid(Vector3.UnitX * 1f, Colors.Red, 0.25f);
            xArrow.Name = "Camera Debug X Arrow";
            xArrow.Rotation = new Vector3(0, 0, -90.0f.ToRadians());

            var yArrow = shapes.AddPyramid(Vector3.UnitY * 1f, Colors.Green, 0.25f);
            yArrow.Name = "Camera Debug Y Arrow";
            yArrow.Rotation = new Vector3(0, 0, 0);

            var yMidArrow = shapes.AddPyramid(Vector3.UnitY * 0.75f, Colors.Green, 0.25f);
            yMidArrow.Name = "Camera Debug Y Mid Arrow";
            yMidArrow.Rotation = new Vector3(0, 0, 0);

            var zArrow = shapes.AddPyramid(Vector3.UnitZ * 1f, Colors.Blue, 0.25f);
            zArrow.Name = "Camera Debug Z Arrow";
            zArrow.Rotation = new Vector3(90.0f.ToRadians(), 0, 0);

            shapes.AddQuad(Vector3.Zero, Colors.Silver, Vector3.One).Name = "Camera Debug View Plane";

            shapes.AddSphere(transform.Translation, Colors.Red, 0.004f).Name = "Camera Debug Ray Origin";
            {
                float planeHeight = NearClipPlane * MathF.Tan(FieldOfViewDegrees.ToRadians() * 0.5f) * 2.0f;
                float planeWidth = planeHeight * AspectRatio;

                ViewParams = new Vector3(planeWidth, planeHeight, NearClipPlane);
                ViewAxes[0] = Owner.Right;
                ViewAxes[1] = Owner.Up;
                ViewAxes[2] = Owner.Forward;

                Vector3 bottomLeftLocal = new Vector3(-planeWidth / 2, -planeHeight / 2, NearClipPlane);

                for (int x = 0; x < DebugPointCount.X; x++)
                {
                    for (int y = 0; y < DebugPointCount.Y; y++)
                    {
                        float tx = x / (DebugPointCount.X - 1.0f);
                        float ty = y / (DebugPointCount.Y - 1.0f);

                        Vector3 pointLocal = bottomLeftLocal + new Vector3(planeWidth * tx, planeHeight * ty, 0);
                        //Vector3 point = Owner.Position + pointLocal.TransformNormal(Owner.WorldTransform);
                        Vector3 point = Owner.Position + Owner.Right * pointLocal.X + Owner.Up * pointLocal.Y + Owner.Forward * pointLocal.Z;

                        //Game.Instance._World.AddSphere(point, new Vector4(tx, ty, 1.0f, 1.0f), 0.09f);
                    }
                }
            }

        }

    }
}
