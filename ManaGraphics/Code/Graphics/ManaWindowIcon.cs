using Silk.NET.Core;
using Silk.NET.Windowing;
using ManaCore.Imaging;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>
    /// Sets a Silk.NET window's titlebar/taskbar/alt-tab icon from a .ico file.
    /// <para>
    /// This is separate from a project's <c>ApplicationIcon</c> MSBuild property, which only stamps the
    /// exe's own resources -- what Explorer and a Start Menu tile show. GLFW never reads that resource; a
    /// window it creates shows the platform's bare default glyph until <see cref="IWindow.SetWindowIcon"/>
    /// is called explicitly, which is what this does.
    /// </para>
    /// </summary>
    internal static class ManaWindowIcon
    {
        public static void Apply(IWindow window, string? icoPath)
        {
            if (string.IsNullOrEmpty(icoPath) || !File.Exists(icoPath))
            {
                return;
            }

            try
            {
                var icons = LoadRawImages(icoPath);
                if (icons.Length > 0)
                {
                    window.SetWindowIcon(icons);

                    // On Windows, GLFW applies the new icon by posting WM_SETICON, which updates the title
                    // bar immediately -- but the taskbar button only picks it up if the message gets pumped
                    // within roughly half a second. Load() routinely runs slow, uninterrupted work (device
                    // setup, asset loading) right after this call and well past that window, so without an
                    // explicit pump here the taskbar is left showing the default icon even though the title
                    // bar is already correct. A no-op on backends without a message queue (e.g. browser).
                    window.DoEvents();
                }
            }
            catch
            {
                // The icon is cosmetic and must never stop the window from opening.
            }
        }

        /// <summary>
        /// The PNG-encoded entries of an .ico, decoded to raw RGBA. Modern .ico files (this app's included)
        /// store each size as a plain PNG stream rather than the legacy BMP DIB format, so only those
        /// entries are read; a DIB-only .ico yields no icons and <see cref="Apply"/> leaves the window alone.
        /// </summary>
        private static RawImage[] LoadRawImages(string icoPath)
        {
            var file = File.ReadAllBytes(icoPath);
            if (file.Length < 6)
            {
                return Array.Empty<RawImage>();
            }

            var count = BitConverter.ToUInt16(file, 4);
            var images = new List<RawImage>(count);

            for (var i = 0; i < count; i++)
            {
                var entryOffset = 6 + i * 16;
                if (entryOffset + 16 > file.Length)
                {
                    break;
                }

                var bytesInRes = BitConverter.ToUInt32(file, entryOffset + 8);
                var imageOffset = BitConverter.ToUInt32(file, entryOffset + 12);
                if (imageOffset + bytesInRes > file.Length)
                {
                    continue;
                }

                var entry = file.AsSpan((int)imageOffset, (int)bytesInRes);
                if (entry.Length < 8 || entry[0] != 0x89 || entry[1] != 0x50) // PNG signature
                {
                    continue;
                }

                var decoded = ManaImageCodec.Load(entry);
                images.Add(new RawImage(decoded.Width, decoded.Height, decoded.Pixels));
            }

            return images.ToArray();
        }
    }
}
