using System.Text;
using ImGuiNET;
using ManaCore.Graphics;

namespace ManaVoxel.Code.UI
{
    public sealed class VoxelEditorAboutPanelView
    {
        private const float ResizeEdgeThickness = 6.0f;
        private const float MinimumWindowHeight = 280.0f;

        private object? _backendContext;
        private ManaGraphicsDiagnosticsProvider? _diagnosticsProvider;
        private ManaGraphicsDiagnosticsSnapshot _snapshot = ManaGraphicsDiagnosticsSnapshot.Empty;
        private bool _hasWindowGeometry;
        private bool _resizingFromTop;
        private Vector2 _lastWindowPosition;
        private Vector2 _lastWindowSize;
        private Vector2 _resizeStartMouse;
        private Vector2 _resizeStartPosition;
        private Vector2 _resizeStartSize;

        public void Draw(
            VoxelEditorViewModel viewModel,
            ManaContext context,
            ManaWebGpuContext? webGpuContext)
        {
            if (!viewModel.ShowAboutWindow)
            {
                _resizingFromTop = false;
                return;
            }

            ImGui.GetIO().ConfigWindowsResizeFromEdges = true;
            ImGui.SetNextWindowSize(new Vector2(780.0f, 680.0f), ImGuiCond.FirstUseEver);
            var mousePosition = ImGui.GetMousePos();
            var topEdgeHoveredBeforeBegin = _hasWindowGeometry && IsNearTopEdge(
                mousePosition,
                _lastWindowPosition,
                _lastWindowSize);
            var windowFlags = topEdgeHoveredBeforeBegin || _resizingFromTop
                ? ImGuiWindowFlags.NoMove
                : ImGuiWindowFlags.None;
            var isOpen = viewModel.ShowAboutWindow;
            if (!ImGui.Begin("About Mana", ref isOpen, windowFlags))
            {
                viewModel.ShowAboutWindow = isOpen;
                ImGui.End();
                return;
            }

            viewModel.ShowAboutWindow = isOpen;
            UpdateWindowResizeAndCursor();
            ImGui.TextUnformatted("Mana Renderer");
            ImGui.SameLine();
            ImGui.TextDisabled($"{context.GraphicsBackend} diagnostics and runtime capabilities");
            ImGui.Separator();

            var backendContext = (object?)webGpuContext ?? context.OpenGl;
            if (backendContext == null)
            {
                _backendContext = null;
                _diagnosticsProvider = null;
                _snapshot = ManaGraphicsDiagnosticsSnapshot.Empty;
                ImGui.TextWrapped(
                    "Graphics diagnostics are unavailable because the active backend does not expose a diagnostics provider.");
                ImGui.End();
                return;
            }

            if (!ReferenceEquals(_backendContext, backendContext))
            {
                _backendContext = backendContext;
                _diagnosticsProvider = ManaGraphicsDiagnosticsProviderFactory.Create(context, webGpuContext);
                _snapshot = _diagnosticsProvider?.DiagnosticsSnapshot ?? ManaGraphicsDiagnosticsSnapshot.Empty;
                if (_snapshot.Sections.Count == 0 && _diagnosticsProvider != null)
                {
                    _snapshot = _diagnosticsProvider.RefreshDiagnosticsSnapshot();
                }
            }

            if (ImGui.Button("Refresh live values") && _diagnosticsProvider != null)
            {
                _snapshot = _diagnosticsProvider.RefreshLiveDiagnosticsSnapshot();
            }

            ImGui.SameLine();
            if (ImGui.Button("Copy report"))
            {
                ImGui.SetClipboardText(BuildClipboardText(_snapshot));
            }

            ImGui.SameLine();
            ImGui.TextDisabled($"Captured {_snapshot.CapturedAt:yyyy-MM-dd HH:mm:ss zzz}");
            ImGui.TextDisabled($"Snapshot phase: {_snapshot.CapturePhase}");
            ImGui.TextDisabled(
                "Memory counters depend on backend and driver support and can change as graphics resources are created.");
            DrawScopeLegend(viewModel.UseLightTheme);
            ImGui.Spacing();

            for (var sectionIndex = 0; sectionIndex < _snapshot.Sections.Count; sectionIndex++)
            {
                var section = _snapshot.Sections[sectionIndex];
                var defaultOpen = section.Name is "Graphics Adapter" or "Video Memory" or
                    "Display and Rendering" or "Common Graphics Limits";
                var flags = defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
                var sectionScope = GetExclusiveSectionScope(section);
                if (sectionScope != ManaGraphicsDiagnosticScope.Common)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, GetScopeColor(sectionScope, viewModel.UseLightTheme));
                }

                var sectionOpen = ImGui.CollapsingHeader(section.Name, flags);
                if (sectionScope != ManaGraphicsDiagnosticScope.Common)
                {
                    ImGui.PopStyleColor();
                }

                if (!sectionOpen)
                {
                    continue;
                }

                DrawSectionTable(section, sectionIndex, viewModel.UseLightTheme);
                ImGui.Spacing();
            }

            ImGui.End();
        }

        private void UpdateWindowResizeAndCursor()
        {
            var windowPosition = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();
            var mousePosition = ImGui.GetMousePos();
            var windowHovered = ImGui.IsWindowHovered(
                ImGuiHoveredFlags.RootAndChildWindows |
                ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
            var topEdgeHovered = windowHovered && IsNearTopEdge(mousePosition, windowPosition, windowSize);

            if (!_resizingFromTop && topEdgeHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _resizingFromTop = true;
                _resizeStartMouse = mousePosition;
                _resizeStartPosition = windowPosition;
                _resizeStartSize = windowSize;
            }

            if (_resizingFromTop)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNS);
                if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
                {
                    var mouseDelta = mousePosition.Y - _resizeStartMouse.Y;
                    var resizedHeight = MathF.Max(MinimumWindowHeight, _resizeStartSize.Y - mouseDelta);
                    var fixedBottom = _resizeStartPosition.Y + _resizeStartSize.Y;
                    var viewportTop = ImGui.GetMainViewport().WorkPos.Y;
                    var resizedTop = MathF.Max(viewportTop, fixedBottom - resizedHeight);
                    resizedHeight = fixedBottom - resizedTop;
                    var resizedPosition = new Vector2(_resizeStartPosition.X, resizedTop);
                    ImGui.SetWindowPos(resizedPosition, ImGuiCond.Always);
                    ImGui.SetWindowSize(new Vector2(_resizeStartSize.X, resizedHeight), ImGuiCond.Always);
                    windowPosition = resizedPosition;
                    windowSize = new Vector2(_resizeStartSize.X, resizedHeight);
                }
                else
                {
                    _resizingFromTop = false;
                }
            }
            else if (windowHovered)
            {
                SetWindowInteractionCursor(mousePosition, windowPosition, windowSize);
            }

            _lastWindowPosition = windowPosition;
            _lastWindowSize = windowSize;
            _hasWindowGeometry = true;
        }

        private static void SetWindowInteractionCursor(
            Vector2 mousePosition,
            Vector2 windowPosition,
            Vector2 windowSize)
        {
            var windowMaximum = windowPosition + windowSize;
            var nearLeft = MathF.Abs(mousePosition.X - windowPosition.X) <= ResizeEdgeThickness;
            var nearRight = MathF.Abs(mousePosition.X - windowMaximum.X) <= ResizeEdgeThickness;
            var nearTop = MathF.Abs(mousePosition.Y - windowPosition.Y) <= ResizeEdgeThickness;
            var nearBottom = MathF.Abs(mousePosition.Y - windowMaximum.Y) <= ResizeEdgeThickness;

            if (nearLeft && nearTop || nearRight && nearBottom)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNWSE);
            }
            else if (nearRight && nearTop || nearLeft && nearBottom)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNESW);
            }
            else if (nearTop || nearBottom)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNS);
            }
            else if (nearLeft || nearRight)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
            }
            else
            {
                var titleBarBottom = windowPosition.Y + ImGui.GetFrameHeight();
                var beforeCloseButton = mousePosition.X < windowMaximum.X - ImGui.GetFrameHeight();
                if (mousePosition.Y >= windowPosition.Y &&
                    mousePosition.Y <= titleBarBottom &&
                    beforeCloseButton)
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
                }
            }
        }

        private static bool IsNearTopEdge(
            Vector2 mousePosition,
            Vector2 windowPosition,
            Vector2 windowSize)
        {
            return mousePosition.X >= windowPosition.X - ResizeEdgeThickness &&
                mousePosition.X <= windowPosition.X + windowSize.X + ResizeEdgeThickness &&
                MathF.Abs(mousePosition.Y - windowPosition.Y) <= ResizeEdgeThickness;
        }

        private static void DrawSectionTable(
            ManaGraphicsDiagnosticSection section,
            int sectionIndex,
            bool useLightTheme)
        {
            const ImGuiTableFlags flags =
                ImGuiTableFlags.BordersInnerH |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.Resizable |
                ImGuiTableFlags.SizingStretchProp;
            var hasComparisonColumn = !string.IsNullOrWhiteSpace(section.ComparisonHeading);
            var columnCount = hasComparisonColumn ? 3 : 2;

            if (!ImGui.BeginTable($"AboutDiagnostics##{sectionIndex}", columnCount, flags))
            {
                return;
            }

            ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 190.0f);
            ImGui.TableSetupColumn(
                hasComparisonColumn ? section.ValueHeading : "Value",
                ImGuiTableColumnFlags.WidthStretch);
            if (hasComparisonColumn)
            {
                ImGui.TableSetupColumn(section.ComparisonHeading!, ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();
            }

            foreach (var entry in section.Entries)
            {
                var hasScopeColor = entry.Scope != ManaGraphicsDiagnosticScope.Common;
                if (hasScopeColor)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, GetScopeColor(entry.Scope, useLightTheme));
                }

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.Label);
                ImGui.TableNextColumn();
                ImGui.PushTextWrapPos(0.0f);
                ImGui.TextUnformatted(entry.Value);
                ImGui.PopTextWrapPos();
                if (hasComparisonColumn)
                {
                    ImGui.TableNextColumn();
                    ImGui.PushTextWrapPos(0.0f);
                    ImGui.TextUnformatted(entry.ComparisonValue ?? "—");
                    ImGui.PopTextWrapPos();
                }

                if (hasScopeColor)
                {
                    ImGui.PopStyleColor();
                }
            }

            ImGui.EndTable();
        }

        private static void DrawScopeLegend(bool useLightTheme)
        {
            ImGui.TextDisabled("Row colors:");
            ImGui.SameLine();
            ImGui.TextColored(
                GetScopeColor(ManaGraphicsDiagnosticScope.OpenGl, useLightTheme),
                "OpenGL-specific");
            ImGui.SameLine();
            ImGui.TextDisabled("|");
            ImGui.SameLine();
            ImGui.TextColored(
                GetScopeColor(ManaGraphicsDiagnosticScope.WebGpu, useLightTheme),
                "WebGPU-specific");
            ImGui.SameLine();
            ImGui.TextDisabled("| Common rows use the normal text color");
        }

        private static ManaGraphicsDiagnosticScope GetExclusiveSectionScope(
            ManaGraphicsDiagnosticSection section)
        {
            if (section.Entries.Count == 0)
            {
                return ManaGraphicsDiagnosticScope.Common;
            }

            var scope = section.Entries[0].Scope;
            if (scope == ManaGraphicsDiagnosticScope.Common)
            {
                return ManaGraphicsDiagnosticScope.Common;
            }

            return section.Entries.All(entry => entry.Scope == scope)
                ? scope
                : ManaGraphicsDiagnosticScope.Common;
        }

        private static Vector4 GetScopeColor(ManaGraphicsDiagnosticScope scope, bool useLightTheme)
        {
            return (scope, useLightTheme) switch
            {
                (ManaGraphicsDiagnosticScope.OpenGl, true) => new Vector4(0.05f, 0.34f, 0.72f, 1.0f),
                (ManaGraphicsDiagnosticScope.OpenGl, false) => new Vector4(0.36f, 0.74f, 1.0f, 1.0f),
                (ManaGraphicsDiagnosticScope.WebGpu, true) => new Vector4(0.68f, 0.28f, 0.03f, 1.0f),
                (ManaGraphicsDiagnosticScope.WebGpu, false) => new Vector4(1.0f, 0.68f, 0.28f, 1.0f),
                _ => ImGui.GetStyle().Colors[(int)ImGuiCol.Text]
            };
        }

        private static string BuildClipboardText(ManaGraphicsDiagnosticsSnapshot snapshot)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Mana Graphics Diagnostics");
            builder.AppendLine($"Captured: {snapshot.CapturedAt:O}");
            builder.AppendLine($"Snapshot phase: {snapshot.CapturePhase}");

            foreach (var section in snapshot.Sections)
            {
                builder.AppendLine();
                builder.AppendLine(section.Name);
                foreach (var entry in section.Entries)
                {
                    if (string.IsNullOrWhiteSpace(section.ComparisonHeading))
                    {
                        builder.AppendLine($"  {entry.Label}: {entry.Value}");
                    }
                    else
                    {
                        builder.AppendLine(
                            $"  {entry.Label}: {section.ValueHeading}={entry.Value}; " +
                            $"{section.ComparisonHeading}={entry.ComparisonValue ?? "—"}");
                    }
                }
            }

            return builder.ToString();
        }
    }
}
