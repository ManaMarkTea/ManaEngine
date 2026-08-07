using System;
using System.Runtime.InteropServices;
using System.Text;
using ImGuiNET;
using Silk.NET.Input;

namespace ManaGraphics.Code.Graphics
{
    internal unsafe sealed class ManaImGuiClipboardBridge : IDisposable
    {
        private readonly IKeyboard? _keyboard;
        private readonly GetClipboardTextCallback _getClipboardText;
        private readonly SetClipboardTextCallback _setClipboardText;
        private readonly GCHandle _selfHandle;
        private nint _clipboardTextBuffer;
        private bool _disposed;

        public ManaImGuiClipboardBridge(IKeyboard? keyboard)
        {
            _keyboard = keyboard;
            _getClipboardText = GetClipboardText;
            _setClipboardText = SetClipboardText;
            _selfHandle = GCHandle.Alloc(this);

            var io = ImGui.GetIO();
            io.NativePtr->GetClipboardTextFn = Marshal.GetFunctionPointerForDelegate(_getClipboardText);
            io.NativePtr->SetClipboardTextFn = Marshal.GetFunctionPointerForDelegate(_setClipboardText);
            io.NativePtr->ClipboardUserData = (void*)GCHandle.ToIntPtr(_selfHandle);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte* GetClipboardTextCallback(void* userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetClipboardTextCallback(void* userData, byte* text);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            var handle = GCHandle.ToIntPtr(_selfHandle);
            var io = ImGui.GetIO();
            if (io.NativePtr->ClipboardUserData == (void*)handle)
            {
                io.NativePtr->GetClipboardTextFn = 0;
                io.NativePtr->SetClipboardTextFn = 0;
                io.NativePtr->ClipboardUserData = null;
            }

            FreeClipboardTextBuffer();
            _selfHandle.Free();
            _disposed = true;
        }

        private static byte* GetClipboardText(void* userData)
        {
            var bridge = TryGetBridge(userData);
            return bridge == null ? null : bridge.GetClipboardText();
        }

        private static void SetClipboardText(void* userData, byte* text)
        {
            TryGetBridge(userData)?.SetClipboardText(text);
        }

        private static ManaImGuiClipboardBridge? TryGetBridge(void* userData)
        {
            if (userData == null)
            {
                return null;
            }

            return GCHandle.FromIntPtr((nint)userData).Target as ManaImGuiClipboardBridge;
        }

        private byte* GetClipboardText()
        {
            FreeClipboardTextBuffer();

            var text = string.Empty;
            try
            {
                text = _keyboard?.ClipboardText ?? string.Empty;
            }
            catch
            {
                // Clipboard access is best-effort; ImGui expects callbacks not to throw.
            }

            var byteCount = Encoding.UTF8.GetByteCount(text);
            _clipboardTextBuffer = Marshal.AllocHGlobal(byteCount + 1);

            var bytes = new Span<byte>((void*)_clipboardTextBuffer, byteCount + 1);
            Encoding.UTF8.GetBytes(text, bytes);
            bytes[byteCount] = 0;

            return (byte*)_clipboardTextBuffer;
        }

        private void SetClipboardText(byte* text)
        {
            if (_keyboard == null || text == null)
            {
                return;
            }

            try
            {
                _keyboard.ClipboardText = Marshal.PtrToStringUTF8((nint)text) ?? string.Empty;
            }
            catch
            {
                // Clipboard access is best-effort; ImGui expects callbacks not to throw.
            }
        }

        private void FreeClipboardTextBuffer()
        {
            if (_clipboardTextBuffer == 0)
            {
                return;
            }

            Marshal.FreeHGlobal(_clipboardTextBuffer);
            _clipboardTextBuffer = 0;
        }
    }
}
