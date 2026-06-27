using System;

namespace ManaCore.Graphics
{
    public interface IManaImGuiController : IDisposable
    {
        void Update(float deltaTime);
        void Render();
        void RecreateFontDeviceTexture();
    }
}
