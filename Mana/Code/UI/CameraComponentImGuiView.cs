using ImGuiNET;

namespace Mana.Code.UI
{
    public class CameraComponentImGuiView
    {
        public void Draw(
            CameraComponent camera,
            Vector3 position,
            Vector3 rotationEuler,
            ref float pitch,
            ref float yaw,
            float[] dxHist,
            float[] dyHist)
        {
            if (BaseGameMode.UtilityDockId != 0)
            {
                ImGui.SetNextWindowDockID(BaseGameMode.UtilityDockId, ImGuiCond.Always);
            }

            ImGui.Begin("Camera");

            var numericPos = position;
            var numericRot = rotationEuler;

            ImGui.SeparatorText("Transform");
            if (ImGui.InputFloat3("Pos", ref numericPos))
            {
                camera.Owner.Position = numericPos;
            }

            if (ImGui.InputFloat3("Rot", ref numericRot))
            {
                camera.Owner.Rotation = numericRot;
            }

            ImGui.InputFloat("Pitch", ref pitch);
            ImGui.InputFloat("Yaw", ref yaw);

            ImGui.SeparatorText("Mouse");
            if (dxHist.Length > 0)
            {
                ImGui.PlotLines("dx", ref dxHist[0], dxHist.Length, 0, string.Empty, -0.010f, 0.01f, new Vector2(0, 50));
            }

            if (dyHist.Length > 0)
            {
                ImGui.PlotLines("dy", ref dyHist[0], dyHist.Length, 0, string.Empty, -0.010f, 0.01f, new Vector2(0, 50));
            }

            ImGui.SeparatorText("Camera");
            var movementSpeed = camera.MovementSpeed;
            if (ImGui.SliderFloat("Speed", ref movementSpeed, 0.25f, 100.0f))
            {
                camera.MovementSpeed = movementSpeed;
            }

            ImGui.SliderFloat("Pan Speed", ref camera.PanSpeed, 0.05f, 5.0f);

            if (ImGui.DragFloat("FoV", ref camera.FieldOfViewDegrees, 1.0f, 1.0f, 150.0f))
            {
                camera.UpdateProjection();
            }

            ImGui.Text($"Projection: {(camera.OrthoGraphic ? "Orthographic" : "Perspective")}");
            if (ImGui.Button("Perspective"))
            {
                camera.SetProjectionMode(false);
            }

            ImGui.SameLine();
            if (ImGui.Button("Ortho"))
            {
                camera.SetProjectionMode(true);
            }

            if (ImGui.DragFloat("Ortho Height", ref camera.OrthographicHeight, 1.0f, 1.0f, 1000.0f))
            {
                camera.UpdateProjection();
            }

            ImGui.Checkbox("Touchpad Horizontal Pan In Ortho", ref camera.TouchpadPanInOrthographic);

            ImGui.SeparatorText("Views");
            if (ImGui.Button("Front"))
            {
                camera.SetViewPreset(CameraViewPreset.Front);
            }

            ImGui.SameLine();
            if (ImGui.Button("Back"))
            {
                camera.SetViewPreset(CameraViewPreset.Back);
            }

            ImGui.SameLine();
            if (ImGui.Button("Left"))
            {
                camera.SetViewPreset(CameraViewPreset.Left);
            }

            ImGui.SameLine();
            if (ImGui.Button("Right"))
            {
                camera.SetViewPreset(CameraViewPreset.Right);
            }

            if (ImGui.Button("Top"))
            {
                camera.SetViewPreset(CameraViewPreset.Top);
            }

            ImGui.SameLine();
            if (ImGui.Button("Bottom"))
            {
                camera.SetViewPreset(CameraViewPreset.Bottom);
            }

            if (ImGui.CollapsingHeader("Advanced"))
            {
                ImGuiExt.InputMatrix4("Proj", ref camera._ProjectionMatrix);
                ImGui.Spacing();
                ImGui.Spacing();
                ImGuiExt.InputMatrix4("View", ref camera._ViewMatrix);
            }

            camera.UpdateViewMatrix();
            ImGui.End();
        }
    }
}
