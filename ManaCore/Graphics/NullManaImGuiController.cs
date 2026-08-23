namespace ManaCore.Graphics
{
    // No-op IManaImGuiController for targets that can't run real ImGui (browser-wasm: ImGui.NET
    // wraps a native cimgui binary with no browser-wasm runtime asset -- see WebPort-Plan.md M2).
    // BaseGameMode calls Update/Render on _ImGui unconditionally every frame regardless of game
    // mode, so this exists to keep that call site safe rather than null-checking it everywhere.
    public sealed class NullManaImGuiController : IManaImGuiController
    {
        public Func<bool>? IsPointerOwnedByOverlay { get; set; }
        public Func<bool>? IsKeyboardOwnedByOverlay { get; set; }

        public void Update(float deltaTime)
        {
        }

        public void Render()
        {
        }

        public void RecreateFontDeviceTexture()
        {
        }

        public void Dispose()
        {
        }
    }
}
