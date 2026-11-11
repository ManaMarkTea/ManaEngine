#if !MANA_BROWSER
using System.Runtime.InteropServices;
using ManaUI;
using Silk.NET.Windowing;

namespace Mana.Code.UI.ManaUi
{
    /// <summary>
    /// Windows Ink for ManaUI: GLFW sees a pen only as a mouse, so this subclasses the window to read WM_POINTER pen
    /// messages -- pressure, eraser, and every sample Windows coalesced -- and hands them to the input router just before
    /// the mouse message the same input becomes. Every message still goes on to GLFW unchanged, so clicks, capture and
    /// drags behave exactly as with a mouse. It also turns off press-and-hold (a right click while drawing slowly).
    /// </summary>
    internal sealed class ManaWindowsPen : IDisposable
    {
        private const int GwlpWndProc = -4;
        private const uint WmPointerUpdate = 0x0245;
        private const uint WmPointerDown = 0x0246;
        private const uint WmPointerUp = 0x0247;
        private const uint WmPointerEnter = 0x0249;
        private const uint WmPointerLeave = 0x024A;
        private const uint WmTabletQuerySystemGestureStatus = 0x02CC;
        private const uint PtPen = 3;
        private const uint PointerFlagInRange = 0x2;
        private const uint PenFlagInverted = 0x2;
        private const uint PenFlagEraser = 0x4;
        private const uint PenMaskPressure = 0x1;

        // Press-and-hold, tap feedback, barrel feedback and flicks off: a drawing surface wants the raw pen.
        private const nint TabletGestureFlags = 0x1 | 0x8 | 0x10 | 0x10000;

        private readonly UiRoot _root;
        private readonly nint _hwnd;
        private readonly WndProc _proc;
        private readonly nint _original;
        private PointerPenInfo[] _history = new PointerPenInfo[16];

        private delegate nint WndProc(nint hwnd, uint message, nint wParam, nint lParam);

        private ManaWindowsPen(UiRoot root, nint hwnd)
        {
            _root = root;
            _hwnd = hwnd;
            _proc = Handle;
            _original = SetWindowLongPtrW(hwnd, GwlpWndProc, Marshal.GetFunctionPointerForDelegate(_proc));
        }

        /// <summary>Hooks the window on Windows; null elsewhere, or when the window has no Win32 handle.</summary>
        public static ManaWindowsPen? TryAttach(IWindow window, UiRoot root)
        {
            if (!OperatingSystem.IsWindows() || window.Native?.Win32 is not { } win32 || win32.Hwnd == 0)
            {
                return null;
            }
            var pen = new ManaWindowsPen(root, win32.Hwnd);
            if (pen._original == 0)
            {
                return null;
            }
            ManaCore.Diagnostics.ManaLog.WriteDiagnostic("[Pen] Windows Ink attached to the window.");
            return pen;
        }

        private nint Handle(nint hwnd, uint message, nint wParam, nint lParam)
        {
            try
            {
                switch (message)
                {
                    case WmTabletQuerySystemGestureStatus:
                        return TabletGestureFlags;
                    case WmPointerUpdate:
                    case WmPointerDown:
                    case WmPointerUp:
                    case WmPointerEnter:
                        ReadPen((uint)(wParam.ToInt64() & 0xFFFF));
                        break;
                    case WmPointerLeave:
                        if (GetPointerType((uint)(wParam.ToInt64() & 0xFFFF), out var type) && type == PtPen)
                        {
                            _root.Input.InjectPenState(false, 0.0f, false);
                        }
                        break;
                }
            }
            catch (Exception exception)
            {
                // Pen data is extra: a failure must never break the window's own input.
                ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"[Pen] {exception.Message}");
            }
            return CallWindowProcW(_original, hwnd, message, wParam, lParam);
        }

        private void ReadPen(uint pointerId)
        {
            if (!GetPointerType(pointerId, out var type) || type != PtPen || !GetPointerPenInfo(pointerId, out var latest))
            {
                return;
            }

            // Windows coalesces pen moves into one message; the history holds them all, newest first.
            var count = Math.Max(1u, latest.PointerInfo.HistoryCount);
            if (count > 1)
            {
                if (_history.Length < count)
                {
                    _history = new PointerPenInfo[count];
                }
                var read = count;
                if (GetPointerPenInfoHistory(pointerId, ref read, _history))
                {
                    for (var i = (int)read - 1; i >= 0; i--)
                    {
                        AddSample(_history[i]);
                    }
                }
                else
                {
                    AddSample(latest);
                }
            }
            else
            {
                AddSample(latest);
            }

            var inRange = (latest.PointerInfo.PointerFlags & PointerFlagInRange) != 0;
            var eraser = (latest.PenFlags & (PenFlagEraser | PenFlagInverted)) != 0;
            _root.Input.InjectPenState(inRange, Pressure(latest), eraser);
        }

        private void AddSample(PointerPenInfo info)
        {
            var point = info.PointerInfo.PixelLocation;
            ScreenToClient(_hwnd, ref point);
            var scale = MathF.Max(_root.ScreenScale, 0.1f);
            _root.Input.InjectPenSample(new System.Numerics.Vector2(point.X / scale, point.Y / scale), Pressure(info));
        }

        // 0 to 1024 when the pen reports it; a pen without pressure reads as a firm press.
        private static float Pressure(PointerPenInfo info)
        {
            if ((info.PenMask & PenMaskPressure) == 0)
            {
                return 1.0f;
            }
            return info.Pressure / 1024.0f;
        }

        public void Dispose()
        {
            if (_original != 0)
            {
                SetWindowLongPtrW(_hwnd, GwlpWndProc, _original);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PointerInfoData
        {
            public uint PointerType;
            public uint PointerId;
            public uint FrameId;
            public uint PointerFlags;
            public nint SourceDevice;
            public nint HwndTarget;
            public Point PixelLocation;
            public Point HimetricLocation;
            public Point PixelLocationRaw;
            public Point HimetricLocationRaw;
            public uint Time;
            public uint HistoryCount;
            public int InputData;
            public uint KeyStates;
            public ulong PerformanceCount;
            public int ButtonChangeType;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PointerPenInfo
        {
            public PointerInfoData PointerInfo;
            public uint PenFlags;
            public uint PenMask;
            public uint Pressure;
            public uint Rotation;
            public int TiltX;
            public int TiltY;
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);

        [DllImport("user32.dll")]
        private static extern nint CallWindowProcW(nint previous, nint hwnd, uint message, nint wParam, nint lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPointerType(uint pointerId, out uint pointerType);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPointerPenInfo(uint pointerId, out PointerPenInfo penInfo);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPointerPenInfoHistory(uint pointerId, ref uint entriesCount, [Out] PointerPenInfo[] penInfo);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ScreenToClient(nint hwnd, ref Point point);
    }
}
#endif
