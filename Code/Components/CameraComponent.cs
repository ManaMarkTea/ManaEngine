using ImGuiNET;
using Mana.Code;
using Silk.NET.Maths;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana
{
    public class CameraComponent : ManaComponent
    {
        //            ProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(55.0f.ToRadians(), aspect, 0.001f, 1000.0f);

        public float NearClipPlane = 0.1f;
        public float FarClipPlane = 1000.0f;
        public float AspectRatio = 1.0f;
        public float FieldOfViewDegrees = 55.0f;
        public Vector2D<int> DebugPointCount = new Vector2D<int>(10, 10);


        public Matrix4x4 _ProjectionMatrix;
        public Matrix4x4 _ViewMatrix;
        public Matrix4x4 _WorldInverseMatrix;

        float _MovementSpeed = 1;
        int _MouseAverageSize = 4;
        Vector2 _MouseSensitivity = new(8.0f, 8.0f);
        Vector2 _LastMousePos;

        Key _MouseLookKey = Key.AltLeft;

        Queue<float> _deltaHistoryX = new Queue<float>();
        Queue<float> _deltaHistoryY = new Queue<float>();

        Queue<float> _deltaAvgHistoryX = new Queue<float>();
        Queue<float> _deltaAvgHistoryY = new Queue<float>();

        bool first = true;

        public Vector3 ViewParams { get; internal set; }

        public CameraComponent(ManaSceneNode owner) : base(owner)
        {
            Game.Instance._Window.Resize += _Window_Resize;
            UpdateProjection();
        }

        private void _Window_Resize(Vector2D<int> obj)
        {
            UpdateProjection();
        }

        public void UpdateProjection()
        {
            AspectRatio = (float)Game.Instance._Window.Size.X / (float)Game.Instance._Window.Size.Y;
            _ProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(FieldOfViewDegrees.ToRadians(), AspectRatio, NearClipPlane, FarClipPlane);

            float planeHeight = NearClipPlane * MathF.Tan(FieldOfViewDegrees.ToRadians() * 0.5f) * 2.0f;
            float planeWidth = planeHeight * AspectRatio;

            ViewParams = new Vector3(planeWidth, planeHeight, NearClipPlane);
        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();

            if (first)
            {
                CameraRayTest();
                first = false;
            }
            

            var MouseState = Game.Instance._Input.Mice.First();
            var KeyboardState = Game.Instance._Input.Keyboards.First();

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
                rotationEuler.Y += dxAvg;
                rotationEuler.X += dyAvg;
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

            //ImGui.PlotLines("dx", ref dxHist, dxHist.Length, 0, "", -5.0f, 5.0f, new System.Numerics.Vector2(100, 100));
            //ImGui.PlotLines("dy", &dyHist.front(), dyHist.size(), 0, NULL, -5.0f, 5.0f, ImVec2(100, 100));
          
            Owner.Position = position;
            Owner.Rotation = rotationEuler;
                        

            _ViewMatrix = Matrix4x4.CreateLookAt(position, position + forward, new Vector3(0, 1, 0));


            ImGui.Text(_ViewMatrix.ToPrettyMatrixString());


            var numericPos = position;
            var numericRot = rotationEuler;

            ImGui.InputFloat3("POS", ref numericPos);
            ImGui.InputFloat3("ROT", ref numericRot);
            ImGui.Text($"Pitch {pitch.ToDegrees()}");
            ImGui.Text($"Yaw {yaw.ToDegrees()}");
            ImGui.InputFloat("Yaw", ref yaw);

            ImGui.SliderFloat("dx", ref dxAvg, -5.0f, 5.0f);
            ImGui.SliderFloat("dy", ref dyAvg, -5.0f, 5.0f);
            ImGui.SliderFloat("CameraSpeed", ref _MovementSpeed, 0.25f, 100.0f);


            ImGui.End();

        }


        void UpdateAverage(float deltaTime, ref float dxAvg, ref float dyAvg)
        {

            var MouseState = Game.Instance._Input.Mice.First();
            var KeyboardState = Game.Instance._Input.Keyboards.First();

            var mouseDelta = MouseState.Position - _LastMousePos;
            _LastMousePos = MouseState.Position;

            float dx = -mouseDelta.X * deltaTime * _MouseSensitivity.X;
            float dy = -mouseDelta.Y * deltaTime * _MouseSensitivity.Y;
            _deltaHistoryX.Enqueue(dx);
            _deltaHistoryY.Enqueue(dy);

            int avgSize = Math.Min(_MouseAverageSize, _deltaHistoryX.Count);
            dxAvg = _deltaHistoryX.Average();
            dyAvg = _deltaHistoryY.Average();
            _deltaAvgHistoryX.Enqueue(dx);
            _deltaAvgHistoryY.Enqueue(dy);

            if (_deltaHistoryX.Count > 5)
            {
                _deltaHistoryX.Dequeue();
                _deltaHistoryY.Dequeue();
                _deltaAvgHistoryX.Dequeue();
                _deltaAvgHistoryY.Dequeue();
            }
            
        }

        void CameraRayTest()
        {
            var transform = Owner.WorldTransform;

            Game.Instance._World.AddCube(Vector3.Zero, Colors.Aqua, 0.002f);
            Game.Instance._World.AddCube(Vector3.UnitX * 0.004f, Colors.Red, 0.002f);
            Game.Instance._World.AddCube(Vector3.UnitY * 0.004f, Colors.Green, 0.002f);
            Game.Instance._World.AddCube(Vector3.UnitZ * 0.004f, Colors.Blue, 0.002f);


            Game.Instance._World.AddCube(transform.Translation, Colors.Red, 0.002f);

            {
                float planeHeight = NearClipPlane * MathF.Tan(FieldOfViewDegrees.ToRadians() * 0.5f) * 2.0f;
                float planeWidth = planeHeight * AspectRatio;

                ViewParams = new Vector3(planeWidth, planeHeight, NearClipPlane);

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

                        Game.Instance._World.AddCube(point, Colors.White, 0.002f);
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

                        Game.Instance._World.AddCube(point, Colors.White, 2f);
                    }
                }
            }
        }

    }
}



/*var MouseState = Game.Instance._Input.Mice.First();
            var KeyboardState = Game.Instance._Input.Keyboards.First();

            var position = Owner.Position;
            var rotation = Owner.Rotation;
            var rotationEuler = rotation;

            float pitch = rotationEuler.X;
            float yaw = rotationEuler.Y - 90.0f.ToRadians();
            float roll = rotationEuler.Z;

            Vector3 forward = Vector3.Zero;
            forward.X = MathF.Cos(yaw) * MathF.Cos(pitch);
            forward.Y = MathF.Sin(pitch);
            forward.Z = MathF.Sin(yaw) * MathF.Cos(pitch);

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
                position += right * delta * _MovementSpeed;
            }

            if (KeyboardState.IsKeyPressed(Key.D))
            {
                position += right * -delta * _MovementSpeed;
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
                rotationEuler.Y += dxAvg;
                rotationEuler.X += dyAvg;
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

            //ImGui.PlotLines("dx", ref dxHist, dxHist.Length, 0, "", -5.0f, 5.0f, new System.Numerics.Vector2(100, 100));
            //ImGui.PlotLines("dy", &dyHist.front(), dyHist.size(), 0, NULL, -5.0f, 5.0f, ImVec2(100, 100));

            var numericPos = position;
            var numericRot = rotationEuler;

            
            pitch = rotationEuler.X;
            yaw = rotationEuler.Y - (90.0f).ToRadians();
            roll = rotationEuler.Z;
            
            forward.X = MathF.Cos(yaw) * MathF.Cos(pitch);
            forward.Y = MathF.Sin(pitch);
            forward.Z = MathF.Sin(yaw) * MathF.Cos(pitch);

            Owner.Position = position;
            Owner.Rotation = rotationEuler;

            
            var mat = Matrix4x4.CreateLookAt(position, position + forward, new Vector3(0, 1, 0));
            _WorldMatrix = mat;*/