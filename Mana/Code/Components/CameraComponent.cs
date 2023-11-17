using ImGuiNET;
using Mana.Code.Nodes;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace Mana
{
    public class CameraComponent : ManaComponent
    {
        public bool OrthoGraphic = false;
        public float NearClipPlane = 0.1f;
        public float FarClipPlane = 1000.0f;
        public float AspectRatio = 1.0f;
        public float FieldOfViewDegrees = 55.0f;
        public Vector2D<int> DebugPointCount = new Vector2D<int>(5, 3);


        public Matrix4x4 _ProjectionMatrix;
        public Matrix4x4 _ViewMatrix;
        public Matrix4x4 _WorldInverseMatrix;
        public Vector3 ViewParams { get; internal set; }
        public Vector3[] ViewAxes { get; internal set; } = new Vector3[3];


        float _MovementSpeed = 200;
        int _MouseAverageSize = 4;
        Vector2 _MouseSensitivity = new(8.0f, 8.0f);
        Vector2 _LastMousePos;

        Key _MouseLookKey = Key.AltLeft;

        Queue<float> _deltaHistoryX = new Queue<float>();
        Queue<float> _deltaHistoryY = new Queue<float>();

        Queue<float> _deltaAvgHistoryX = new Queue<float>();
        Queue<float> _deltaAvgHistoryY = new Queue<float>();

        bool first = true;

        public CameraComponent(ManaSceneNode owner) : base(owner)
        {
            owner.Context.Window.Resize += _Window_Resize;
            UpdateProjection();
        }

        private void _Window_Resize(Vector2D<int> obj)
        {
            UpdateProjection();
        }

        public void UpdateProjection()
        {
            var window = Context.Window;

            AspectRatio = (float)window.Size.X / (float)window.Size.Y;
            if (OrthoGraphic)
            {
                _ProjectionMatrix = Matrix4x4.CreateOrthographic(window.Size.X / 100, window.Size.Y / 100, NearClipPlane, FarClipPlane);
            }
            else
            {
                _ProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(FieldOfViewDegrees.ToRadians(), AspectRatio, NearClipPlane, FarClipPlane);
            }

            float planeHeight = NearClipPlane * MathF.Tan(FieldOfViewDegrees.ToRadians() * 0.5f) * 2.0f;
            float planeWidth = planeHeight * AspectRatio;

            ViewParams = new Vector3(planeWidth, planeHeight, NearClipPlane);
        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();

            var MouseState = Context.Input.Mice.First();
            var KeyboardState = Context.Input.Keyboards.First();

            var position = Owner.Position;
            var rotation = Owner.Rotation;
            var rotationEuler = rotation;

            float pitch = rotationEuler.X;
            float yaw = rotationEuler.Y;
            float roll = rotationEuler.Z;

            Vector3 forward = -Vector3.UnitZ;
            forward = forward.TransformNormal(Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll));

            var up = new Vector3(0, 1, 0);
            var right = Vector3.Cross(forward, up);

            float delta = 0.001f;
            float dxAvg = 0, dyAvg = 0;
            UpdateAverage(delta, ref dxAvg, ref dyAvg);

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
                position += up * +delta * _MovementSpeed;
            }

            if (KeyboardState.IsKeyPressed(Key.Q))
            {
                position += up * -delta * _MovementSpeed;
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
            else
            {

            }

            if (MouseState.IsButtonPressed(MouseButton.Right) && MouseState.ScrollWheels[0].Y != 0)
            {
                _MovementSpeed += MouseState.ScrollWheels[0].Y;
                _MovementSpeed = _MovementSpeed.Clamp(1.0f, 1000.0f);
            }

            ImGui.Begin("Camera");
            
            Owner.Position = position;
            Owner.Rotation = rotationEuler;
            _ViewMatrix = Matrix4x4.CreateLookAt(position, position + forward, new Vector3(0, 1, 0));

            
            var numericPos = position;
            var numericRot = rotationEuler;


            ImGui.SeparatorText("Transform");
            {
                if (ImGui.InputFloat3("Pos", ref numericPos)) { Owner.Position = numericPos; }
                if (ImGui.InputFloat3("Rot", ref numericRot)) { Owner.Rotation = rotationEuler; }
            }

            
            ImGui.InputFloat("Pitch", ref pitch);
            ImGui.InputFloat("Yaw", ref yaw);

            var dxHist = _deltaHistoryX.ToArray();
            var dyHist = _deltaHistoryY.ToArray();

            ImGui.SeparatorText("Mouse");
            {
                ImGui.PlotLines("dx", ref dxHist[0], dxHist.Length, 0, "", -0.010f, 0.01f, new Vector2(0, 50));
                ImGui.PlotLines("dy", ref dyHist[0], dyHist.Length, 0, "", -0.010f, 0.01f, new Vector2(0, 50));
            }

            ImGui.SeparatorText("Camera");
            {
                ImGui.SliderFloat("Speed", ref _MovementSpeed, 0.25f, 100.0f);

                if (ImGui.DragFloat("FoV", ref FieldOfViewDegrees, 1.0f, 1.0f, 150.0f))
                {
                    UpdateProjection();
                }

                if (ImGui.Button("Ortho"))
                {
                    OrthoGraphic = !OrthoGraphic;
                    UpdateProjection();
                }


                if (ImGui.CollapsingHeader("Advanced"))
                {
                    ImGuiExt.InputMatrix4("Proj", ref _ProjectionMatrix);
                    ImGui.Spacing();
                    ImGui.Spacing();
                    ImGuiExt.InputMatrix4("View", ref _ViewMatrix);
                }

            }


            ImGui.End();

        }


        void UpdateAverage(float deltaTime, ref float dxAvg, ref float dyAvg)
        {
            var MouseState = Context.Input.Mice.First();
            var KeyboardState = Context.Input.Keyboards.First();

            var mouseDelta = MouseState.Position - _LastMousePos;
            _LastMousePos = MouseState.Position;

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

        }

        public Ray ProjectScreenToWorld(in Vector3 MousePositionStart,  in Vector3 MousePositionEnd)
        {            
            Matrix4x4.Invert(_ViewMatrix * _ProjectionMatrix, out Matrix4x4 inverseWorldMatrix);
            Matrix4x4.Invert(_ProjectionMatrix, out Matrix4x4 inverseProjectionMatrix);
            Matrix4x4.Invert(_ViewMatrix, out Matrix4x4 inverseViewMatrix);

            Matrix4x4 world = _ViewMatrix * _ProjectionMatrix;

            var windowSize = Context.Window.Size;

            Vector4 viewportCoordinate;
            viewportCoordinate.X = 2.0f * MousePositionStart.X / (float)windowSize.X - 1;
            viewportCoordinate.Y = -(2.0f * MousePositionStart.Y / (float)windowSize.Y - 1);
            viewportCoordinate.Z = MousePositionStart.Z;
            viewportCoordinate.W = 1.0f;

            Vector4 viewportCoordinateEnd;
            viewportCoordinateEnd.X = 2.0f * MousePositionEnd.X / (float)windowSize.X - 1;
            viewportCoordinateEnd.Y = -(2.0f * MousePositionEnd.Y / (float)windowSize.Y - 1);
            viewportCoordinateEnd.Z = MousePositionEnd.Z;
            viewportCoordinateEnd.W = 1.0f;
            
            Vector4 eyeSpaceCoordinate = Vector4.Transform(viewportCoordinate, inverseProjectionMatrix);
            Vector4 eyeSpaceCoordinateEnd = Vector4.Transform(viewportCoordinateEnd, inverseProjectionMatrix);

            Vector4 worldCoordinate = Vector4.Transform(eyeSpaceCoordinate, inverseViewMatrix);            
            Vector4 worldCoordinateEnd = Vector4.Transform(eyeSpaceCoordinateEnd, inverseViewMatrix);

            worldCoordinate /= worldCoordinate.W;
            worldCoordinateEnd /= worldCoordinateEnd.W;
            
            return new Ray(worldCoordinate.ToVector3(), (worldCoordinateEnd - worldCoordinate).ToVector3().Normalized());
        }

        public void AddDebugCameraRayTest(ManaShapeFactory shapes)
        {
            var transform = Owner.WorldTransform;

            float pegSize = 0.25f * 0.5f;
 
            shapes.AddCube(Vector3.UnitX * 0.5f, Colors.Red, new Vector3(1, pegSize, pegSize));
            shapes.AddCube(Vector3.UnitY * 0.5f, Colors.Green, new Vector3(pegSize, 1, pegSize));
            shapes.AddCube(Vector3.UnitZ * 0.5f, Colors.Blue, new Vector3(pegSize, pegSize, 1));

            shapes.AddPyramid(Vector3.UnitX * 1f, Colors.Red, 0.25f).Rotation = new Vector3(0, 0, -90.0f.ToRadians());
            shapes.AddPyramid(Vector3.UnitY * 1f, Colors.Green, 0.25f).Rotation = new Vector3(0, 0, 0);
            shapes.AddPyramid(Vector3.UnitY * 0.75f, Colors.Green, 0.25f).Rotation = new Vector3(0, 0, 0);
            shapes.AddPyramid(Vector3.UnitZ * 1f, Colors.Blue, 0.25f).Rotation = new Vector3(90.0f.ToRadians(), 0, 0);

            shapes.AddQuad(Vector3.Zero, Colors.Silver, Vector3.One);

            shapes.AddSphere(transform.Translation, Colors.Red, 0.004f);
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

            if (false)
            {
                float planeHeight = FarClipPlane * MathF.Tan(FieldOfViewDegrees.ToRadians() * 0.5f) * 2.0f;
                float planeWidth = planeHeight * AspectRatio;

                Vector3 bottomLeftLocal = new Vector3(-planeWidth / 2, -planeHeight / 2, NearClipPlane);

                for (int x = 0; x < DebugPointCount.X; x++)
                {
                    for (int y = 0; y < DebugPointCount.Y; y++)
                    {
                        float tx = x / (DebugPointCount.X - 1.0f);
                        float ty = y / (DebugPointCount.Y - 1.0f);

                        Vector3 pointLocal = bottomLeftLocal + new Vector3(planeWidth * tx, planeHeight * ty, 0);
                        Vector3 point = Owner.Position + Owner.Right * pointLocal.X + Owner.Up * pointLocal.Y + Owner.Forward * pointLocal.Z;

                        shapes.AddCube(point, Colors.White, 2f);
                    }
                }
            }
        }

    }
}