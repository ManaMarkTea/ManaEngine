using ImGuiNET;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Mana.Code
{
    public static class ImGuiDockNative
    {
        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderSplitNode")]
        private static extern uint DockBuilderSplitNodeNative(uint nodeId, ImGuiDir splitDir, float sizeRatioForNodeAtDir, out uint outIdAtDir, out uint outIdAtOppositeDir);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderAddNode")]
        private static extern uint DockBuilderAddNodeNative(uint nodeId, ImGuiDockNodeFlags flags);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderRemoveNode")]
        private static extern void DockBuilderRemoveNodeNative(uint nodeId);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderDockWindow")]
        private static extern void DockBuilderDockWindowNative(byte[] windowName, uint nodeId);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderSetNodeSize")]
        private static extern void DockBuilderSetNodeSizeNative(uint nodeId, Vector2 size);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderFinish")]
        private static extern void DockBuilderFinishNative(uint nodeId);

        public static uint DockBuilderSplitNode(uint nodeId, ImGuiDir splitDir, float sizeRatioForNodeAtDir, out uint outIdAtDir, out uint outIdAtOppositeDir)
        {
            return DockBuilderSplitNodeNative(nodeId, splitDir, sizeRatioForNodeAtDir, out outIdAtDir, out outIdAtOppositeDir);
        }

        public static uint DockBuilderAddNode(uint nodeId, ImGuiDockNodeFlags flags)
        {
            return DockBuilderAddNodeNative(nodeId, flags);
        }

        public static void DockBuilderRemoveNode(uint nodeId)
        {
            DockBuilderRemoveNodeNative(nodeId);
        }

        public static void DockBuilderDockWindow(string windowName, uint nodeId)
        {
            var utf8Name = Encoding.UTF8.GetBytes(windowName + "\0");
            DockBuilderDockWindowNative(utf8Name, nodeId);
        }

        public static void DockBuilderSetNodeSize(uint nodeId, Vector2 size)
        {
            DockBuilderSetNodeSizeNative(nodeId, size);
        }

        public static void DockBuilderFinish(uint nodeId)
        {
            DockBuilderFinishNative(nodeId);
        }
    }


}

