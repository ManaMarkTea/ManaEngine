using ImGuiNET;
using System.Numerics;

namespace Mana.Code
{
    public abstract partial class BaseGameMode
    {
        protected void ConfigImStyle()
        {
            ApplyLightImGuiTheme();
        }

        public void ApplyLightImGuiTheme()
        {
            var color_for_text = new Vector3(52.0f / 255.0f, 68.0f / 255.0f, 86.0f / 255.0f);
            var color_for_head = new Vector3(41.0f / 255.0f, 133.0f / 255.0f, 220.0f / 255.0f);
            var color_for_area = new Vector3(232.0f / 255.0f, 239.0f / 255.0f, 247.0f / 255.0f);
            var color_for_body = new Vector3(247.0f / 255.0f, 250.0f / 255.0f, 253.0f / 255.0f);
            var color_for_pops = new Vector3(255.0f / 255.0f, 255.0f / 255.0f, 255.0f / 255.0f);
            ConfigImStyle(color_for_text, color_for_head, color_for_area, color_for_body, color_for_pops);
        }

        public void ApplyDarkImGuiTheme()
        {
            var color_for_text = new Vector3(236.0f / 255.0f, 240.0f / 255.0f, 241.0f / 255.0f);
            var color_for_head = new Vector3(61.0f / 255.0f, 128.0f / 255.0f, 185.0f / 255.0f);
            var color_for_area = new Vector3(57.0f / 255.0f, 79.0f / 255.0f, 105.0f / 255.0f);
            var color_for_body = new Vector3(44.0f / 255.0f, 62.0f / 255.0f, 80.0f / 255.0f);
            var color_for_pops = new Vector3(33.0f / 255.0f, 46.0f / 255.0f, 60.0f / 255.0f);
            ConfigImStyle(color_for_text, color_for_head, color_for_area, color_for_body, color_for_pops);
        }

        public void ConfigImStyle(Vector3 color_for_text, Vector3 color_for_head, Vector3 color_for_area, Vector3 color_for_body, Vector3 color_for_pops)
        {
            ImGuiStylePtr style = ImGui.GetStyle();

            style.WindowRounding = 8.0f;
            style.ChildRounding = 8.0f;
            style.FrameRounding = 7.0f;
            style.GrabRounding = 7.0f;
            style.TabRounding = 7.0f;
            style.PopupRounding = 8.0f;
            style.ScrollbarRounding = 8.0f;
            style.FrameBorderSize = 0.0f;
            style.WindowBorderSize = 1.0f;
            style.WindowPadding = new Vector2(10.0f, 10.0f);
            style.FramePadding = new Vector2(8.0f, 6.0f);
            style.ItemSpacing = new Vector2(8.0f, 7.0f);

            style.Colors[(int)ImGuiCol.Text] = new Vector4(color_for_text.X, color_for_text.Y, color_for_text.Z, 1.00f);
            style.Colors[(int)ImGuiCol.TextDisabled] = new Vector4(color_for_text.X, color_for_text.Y, color_for_text.Z, 0.56f);
            style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(color_for_body.X, color_for_body.Y, color_for_body.Z, 0.98f);
            style.Colors[(int)ImGuiCol.ChildBg] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 0.82f);
            style.Colors[(int)ImGuiCol.Border] = new Vector4(0.78f, 0.84f, 0.91f, 0.65f);
            style.Colors[(int)ImGuiCol.BorderShadow] = new Vector4(0, 0, 0, 0.00f);
            style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 1.00f);
            style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.82f, 0.90f, 0.98f, 1.00f);
            style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.74f, 0.86f, 0.97f, 1.00f);
            style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.90f, 0.94f, 0.99f, 1.00f);
            style.Colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.94f, 0.96f, 0.99f, 0.86f);
            style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.84f, 0.91f, 0.98f, 1.00f);
            style.Colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.93f, 0.96f, 0.99f, 1.00f);
            style.Colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 1.00f);
            style.Colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.69f, 0.79f, 0.91f, 0.70f);
            style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.55f, 0.71f, 0.89f, 0.88f);
            style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.95f);
            
            style.Colors[(int)ImGuiCol.CheckMark] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            style.Colors[(int)ImGuiCol.SliderGrab] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.75f);
            style.Colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            style.Colors[(int)ImGuiCol.Button] = new Vector4(0.82f, 0.90f, 0.98f, 1.00f);
            style.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.74f, 0.86f, 0.97f, 1.00f);
            style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.90f);
            style.Colors[(int)ImGuiCol.Header] = new Vector4(0.79f, 0.88f, 0.98f, 0.95f);
            style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.70f, 0.84f, 0.97f, 1.00f);
            style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.90f);
            style.Colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 1.00f);
            style.Colors[(int)ImGuiCol.TableBorderStrong] = new Vector4(0.77f, 0.83f, 0.90f, 0.95f);
            style.Colors[(int)ImGuiCol.TableBorderLight] = new Vector4(0.78f, 0.84f, 0.91f, 0.45f);
            style.Colors[(int)ImGuiCol.TableRowBg] = new Vector4(0.0f, 0.0f, 0.0f, 0.00f);
            style.Colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 0.40f);
            
            style.Colors[(int)ImGuiCol.PlotLines] = new Vector4(0.26f, 0.48f, 0.73f, 0.90f);
            style.Colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            style.Colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.35f, 0.56f, 0.82f, 0.92f);
            style.Colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            style.Colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.25f);
            style.Colors[(int)ImGuiCol.PopupBg] = new Vector4(color_for_pops.X, color_for_pops.Y, color_for_pops.Z, 0.98f);
            style.Colors[(int)ImGuiCol.Tab] = new Vector4(0.92f, 0.96f, 0.99f, 1.00f);
            style.Colors[(int)ImGuiCol.TabHovered] = new Vector4(0.82f, 0.91f, 0.99f, 1.00f);
            style.Colors[(int)ImGuiCol.TabActive] = new Vector4(0.78f, 0.89f, 0.98f, 1.00f);
            style.Colors[(int)ImGuiCol.TabUnfocused] = new Vector4(0.93f, 0.96f, 0.99f, 1.00f);
            style.Colors[(int)ImGuiCol.TabUnfocusedActive] = new Vector4(0.86f, 0.92f, 0.98f, 1.00f);
            style.Colors[(int)ImGuiCol.DockingPreview] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.22f);
            style.Colors[(int)ImGuiCol.Separator] = new Vector4(0.77f, 0.83f, 0.90f, 0.95f);
            style.Colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.62f, 0.76f, 0.92f, 0.30f);
            style.Colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(0.50f, 0.69f, 0.89f, 0.65f);
            style.Colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.85f);

            /*
            style.Colors[(int)ImGuiCol.ComboBg] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 1.00f);
            style.Colors[(int)ImGuiCol.Column] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.32f);
            style.Colors[(int)ImGuiCol.ColumnHovered] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.78f);
            style.Colors[(int)ImGuiCol.ColumnActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            style.Colors[(int)ImGuiCol.Res.ZeGrip] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.15f);
            style.Colors[(int)ImGuiCol.Res.ZeGripHovered] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.78f);
            style.Colors[(int)ImGuiCol.Res.ZeGripActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            style.Colors[(int)ImGuiCol.CloseButton] = new Vector4(color_for_text.X, color_for_text.Y, color_for_text.Z, 0.16f);
            style.Colors[(int)ImGuiCol.CloseButtonHovered] = new Vector4(color_for_text.X, color_for_text.Y, color_for_text.Z, 0.39f);
            style.Colors[(int)ImGuiCol.CloseButtonActive] = new Vector4(color_for_text.X, color_for_text.Y, color_for_text.Z, 1.00f);
            style.Colors[(int)ImGuiCol.ModalWindowDarkening] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 0.73f);*/
        }


    }
}

