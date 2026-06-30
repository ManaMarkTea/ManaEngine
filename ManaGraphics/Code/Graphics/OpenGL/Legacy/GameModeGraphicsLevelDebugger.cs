using Silk.NET.OpenGL;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Mana.Code
{
    public class GameModeGraphicsLevelDebugger
    {
        private DebugProc _debugProcCallback;
        private GCHandle _debugProcCallbackHandle;

        public GameModeGraphicsLevelDebugger(GL GL)
        {
            _debugProcCallback = DebugCallback;
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Mac os only supports 4.1, debugging in in 4.3
                GL.Enable(EnableCap.DebugOutput);
                GL.Enable(EnableCap.DebugOutputSynchronous);
                GL.DebugMessageControl(DebugSource.DontCare, DebugType.DontCare, DebugSeverity.DontCare, 0, new uint[0],
                    true);

                _debugProcCallbackHandle = GCHandle.Alloc(_debugProcCallback);
                var userParam = IntPtr.Zero;
                GL.DebugMessageCallback(_debugProcCallback, in userParam);
            }
        }

        private void DebugCallback(GLEnum source, GLEnum type, int id, GLEnum severity, int length, IntPtr message, IntPtr userParam)
        {
            string messageString = Marshal.PtrToStringAnsi(message, length);

            if (type == GLEnum.DebugTypeOther)
            {
                return;
            }

            Trace.WriteLine($"{severity} {type} | {messageString}");

            if (type == GLEnum.DebugTypeError)
            {
                if (messageString.Contains("UniformMatrix*"))
                {
                    return;
                }

                if (messageString.Contains("linked"))
                {
                    return;
                }

                throw new Exception(messageString);
            }
        }
    }

}

