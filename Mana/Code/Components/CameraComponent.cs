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

        public void DrawImGui()
        {
            if (BaseGameMode.UtilityDockId != 0)
            {
                ImGui.SetNextWindowDockID(BaseGameMode.UtilityDockId, ImGuiCond.Always);
            }

            ImGui.Begin("Camera");

            var numericPos = Owner.Position;
            var numericRot = Owner.Rotation;
            var pitch = numericRot.X;
            var yaw = numericRot.Y;

            ImGui.SeparatorText("Transform");
            if (ImGui.InputFloat3("Pos", ref numericPos))
            {
                Owner.Position = numericPos;
            }

            if (ImGui.InputFloat3("Rot", ref numericRot))
            {
                Owner.Rotation = numericRot;
            }

            ImGui.InputFloat("Pitch", ref pitch);
            ImGui.InputFloat("Yaw", ref yaw);

            var dxHist = _deltaHistoryX.ToArray();
            var dyHist = _deltaHistoryY.ToArray();

            ImGui.SeparatorText("Mouse");
            if (dxHist.Length > 0)
            {
                ImGui.PlotLines("dx", ref dxHist[0], dxHist.Length, 0, "", -0.010f, 0.01f, new Vector2(0, 50));
            }

            if (dyHist.Length > 0)
            {
                ImGui.PlotLines("dy", ref dyHist[0], dyHist.Length, 0, "", -0.010f, 0.01f, new Vector2(0, 50));
            }

            ImGui.SeparatorText("Camera");
            ImGui.SliderFloat("Speed", ref _MovementSpeed, 0.25f, 100.0f);
            ImGui.SliderFloat("Pan Speed", ref PanSpeed, 0.05f, 5.0f);

            ImGui.Text($"Projection: {(OrthoGraphic ? "Orthographic" : "Perspective")}");
            if (ImGui.Button("Perspective"))
            {
                SetProjectionMode(false);
            }

            ImGui.SameLine();
            if (ImGui.Button("Ortho"))
            {
                SetProjectionMode(true);
            }

            if (ImGui.DragFloat("FoV", ref FieldOfViewDegrees, 1.0f, 1.0f, 150.0f))
            {
                UpdateProjection();
            }

            if (ImGui.DragFloat("Ortho Height", ref OrthographicHeight, 1.0f, 1.0f, 1000.0f))
            {
                UpdateProjection();
            }

            ImGui.Checkbox("Touchpad Horizontal Pan In Ortho", ref TouchpadPanInOrthographic);

            ImGui.SeparatorText("Views");
            if (ImGui.Button("Front"))
            {
                SetViewPreset(CameraViewPreset.Front);
            }

            ImGui.SameLine();
            if (ImGui.Button("Back"))
            {
                SetViewPreset(CameraViewPreset.Back);
            }

            ImGui.SameLine();
            if (ImGui.Button("Left"))
            {
                SetViewPreset(CameraViewPreset.Left);
            }

            ImGui.SameLine();
            if (ImGui.Button("Right"))
            {
                SetViewPreset(CameraViewPreset.Right);
            }

            if (ImGui.Button("Top"))
            {
                SetViewPreset(CameraViewPreset.Top);
            }

            ImGui.SameLine();
            if (ImGui.Button("Bottom"))
            {
                SetViewPreset(CameraViewPreset.Bottom);
            }

            if (ImGui.CollapsingHeader("Advanced"))
            {
                ImGuiExt.InputMatrix4("Proj", ref _ProjectionMatrix);
                ImGui.Spacing();
                ImGui.Spacing();
                ImGuiExt.InputMatrix4("View", ref _ViewMatrix);
            }

            ImGui.End();
            UpdateViewMatrix();
        }


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
