using System.Drawing;
using Silk.NET.Windowing;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using System.Numerics;
using Silk.NET.Maths;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Mana.Code;
using Mana.Code.Shapes;
using ImGuiNET;
using Mana.Code.Materials;
using Silk.NET.GLFW;
using SixLabors.ImageSharp;

namespace Mana
{
    public class Program
    {                           
        static void Main(string[] args)
        {
            SceneViewerMode game = new SceneViewerMode();
            game.Run();
        }
    }

}