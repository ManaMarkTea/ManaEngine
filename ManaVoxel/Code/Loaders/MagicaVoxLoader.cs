using Mana;
using Mana.Code.Shapes.Loaders;
using System.Collections.Generic;

namespace ManaVoxel.Code.Loaders
{
    public class MagicaVoxel
    {
        public Vector3 Position { get; set; }
        public Vector4 Color { get; set; }

        public override string ToString()
        {
            return $"{Position}";
        }
    }

    public class MagicaVoxLoader
    {
        public MagicaVoxLoader() 
        {
            
        
        }

        public Vector4[] ColorPalette = new Vector4[256]
        {
            0x00000000.ToColor4(), 0xffffffff.ToColor4(), 0xffccffff.ToColor4(), 0xff99ffff.ToColor4(), 0xff66ffff.ToColor4(), 0xff33ffff.ToColor4(), 0xff00ffff.ToColor4(), 0xffffccff.ToColor4(), 0xffccccff.ToColor4(), 0xff99ccff.ToColor4(), 0xff66ccff.ToColor4(), 0xff33ccff.ToColor4(), 0xff00ccff.ToColor4(), 0xffff99ff.ToColor4(), 0xffcc99ff.ToColor4(), 0xff9999ff.ToColor4(),
            0xff6699ff.ToColor4(), 0xff3399ff.ToColor4(), 0xff0099ff.ToColor4(), 0xffff66ff.ToColor4(), 0xffcc66ff.ToColor4(), 0xff9966ff.ToColor4(), 0xff6666ff.ToColor4(), 0xff3366ff.ToColor4(), 0xff0066ff.ToColor4(), 0xffff33ff.ToColor4(), 0xffcc33ff.ToColor4(), 0xff9933ff.ToColor4(), 0xff6633ff.ToColor4(), 0xff3333ff.ToColor4(), 0xff0033ff.ToColor4(), 0xffff00ff.ToColor4(),
            0xffcc00ff.ToColor4(), 0xff9900ff.ToColor4(), 0xff6600ff.ToColor4(), 0xff3300ff.ToColor4(), 0xff0000ff.ToColor4(), 0xffffffcc.ToColor4(), 0xffccffcc.ToColor4(), 0xff99ffcc.ToColor4(), 0xff66ffcc.ToColor4(), 0xff33ffcc.ToColor4(), 0xff00ffcc.ToColor4(), 0xffffcccc.ToColor4(), 0xffcccccc.ToColor4(), 0xff99cccc.ToColor4(), 0xff66cccc.ToColor4(), 0xff33cccc.ToColor4(),
            0xff00cccc.ToColor4(), 0xffff99cc.ToColor4(), 0xffcc99cc.ToColor4(), 0xff9999cc.ToColor4(), 0xff6699cc.ToColor4(), 0xff3399cc.ToColor4(), 0xff0099cc.ToColor4(), 0xffff66cc.ToColor4(), 0xffcc66cc.ToColor4(), 0xff9966cc.ToColor4(), 0xff6666cc.ToColor4(), 0xff3366cc.ToColor4(), 0xff0066cc.ToColor4(), 0xffff33cc.ToColor4(), 0xffcc33cc.ToColor4(), 0xff9933cc.ToColor4(),
            0xff6633cc.ToColor4(), 0xff3333cc.ToColor4(), 0xff0033cc.ToColor4(), 0xffff00cc.ToColor4(), 0xffcc00cc.ToColor4(), 0xff9900cc.ToColor4(), 0xff6600cc.ToColor4(), 0xff3300cc.ToColor4(), 0xff0000cc.ToColor4(), 0xffffff99.ToColor4(), 0xffccff99.ToColor4(), 0xff99ff99.ToColor4(), 0xff66ff99.ToColor4(), 0xff33ff99.ToColor4(), 0xff00ff99.ToColor4(), 0xffffcc99.ToColor4(),
            0xffcccc99.ToColor4(), 0xff99cc99.ToColor4(), 0xff66cc99.ToColor4(), 0xff33cc99.ToColor4(), 0xff00cc99.ToColor4(), 0xffff9999.ToColor4(), 0xffcc9999.ToColor4(), 0xff999999.ToColor4(), 0xff669999.ToColor4(), 0xff339999.ToColor4(), 0xff009999.ToColor4(), 0xffff6699.ToColor4(), 0xffcc6699.ToColor4(), 0xff996699.ToColor4(), 0xff666699.ToColor4(), 0xff336699.ToColor4(),
            0xff006699.ToColor4(), 0xffff3399.ToColor4(), 0xffcc3399.ToColor4(), 0xff993399.ToColor4(), 0xff663399.ToColor4(), 0xff333399.ToColor4(), 0xff003399.ToColor4(), 0xffff0099.ToColor4(), 0xffcc0099.ToColor4(), 0xff990099.ToColor4(), 0xff660099.ToColor4(), 0xff330099.ToColor4(), 0xff000099.ToColor4(), 0xffffff66.ToColor4(), 0xffccff66.ToColor4(), 0xff99ff66.ToColor4(),
            0xff66ff66.ToColor4(), 0xff33ff66.ToColor4(), 0xff00ff66.ToColor4(), 0xffffcc66.ToColor4(), 0xffcccc66.ToColor4(), 0xff99cc66.ToColor4(), 0xff66cc66.ToColor4(), 0xff33cc66.ToColor4(), 0xff00cc66.ToColor4(), 0xffff9966.ToColor4(), 0xffcc9966.ToColor4(), 0xff999966.ToColor4(), 0xff669966.ToColor4(), 0xff339966.ToColor4(), 0xff009966.ToColor4(), 0xffff6666.ToColor4(),
            0xffcc6666.ToColor4(), 0xff996666.ToColor4(), 0xff666666.ToColor4(), 0xff336666.ToColor4(), 0xff006666.ToColor4(), 0xffff3366.ToColor4(), 0xffcc3366.ToColor4(), 0xff993366.ToColor4(), 0xff663366.ToColor4(), 0xff333366.ToColor4(), 0xff003366.ToColor4(), 0xffff0066.ToColor4(), 0xffcc0066.ToColor4(), 0xff990066.ToColor4(), 0xff660066.ToColor4(), 0xff330066.ToColor4(),
            0xff000066.ToColor4(), 0xffffff33.ToColor4(), 0xffccff33.ToColor4(), 0xff99ff33.ToColor4(), 0xff66ff33.ToColor4(), 0xff33ff33.ToColor4(), 0xff00ff33.ToColor4(), 0xffffcc33.ToColor4(), 0xffcccc33.ToColor4(), 0xff99cc33.ToColor4(), 0xff66cc33.ToColor4(), 0xff33cc33.ToColor4(), 0xff00cc33.ToColor4(), 0xffff9933.ToColor4(), 0xffcc9933.ToColor4(), 0xff999933.ToColor4(),
            0xff669933.ToColor4(), 0xff339933.ToColor4(), 0xff009933.ToColor4(), 0xffff6633.ToColor4(), 0xffcc6633.ToColor4(), 0xff996633.ToColor4(), 0xff666633.ToColor4(), 0xff336633.ToColor4(), 0xff006633.ToColor4(), 0xffff3333.ToColor4(), 0xffcc3333.ToColor4(), 0xff993333.ToColor4(), 0xff663333.ToColor4(), 0xff333333.ToColor4(), 0xff003333.ToColor4(), 0xffff0033.ToColor4(),
            0xffcc0033.ToColor4(), 0xff990033.ToColor4(), 0xff660033.ToColor4(), 0xff330033.ToColor4(), 0xff000033.ToColor4(), 0xffffff00.ToColor4(), 0xffccff00.ToColor4(), 0xff99ff00.ToColor4(), 0xff66ff00.ToColor4(), 0xff33ff00.ToColor4(), 0xff00ff00.ToColor4(), 0xffffcc00.ToColor4(), 0xffcccc00.ToColor4(), 0xff99cc00.ToColor4(), 0xff66cc00.ToColor4(), 0xff33cc00.ToColor4(),
            0xff00cc00.ToColor4(), 0xffff9900.ToColor4(), 0xffcc9900.ToColor4(), 0xff999900.ToColor4(), 0xff669900.ToColor4(), 0xff339900.ToColor4(), 0xff009900.ToColor4(), 0xffff6600.ToColor4(), 0xffcc6600.ToColor4(), 0xff996600.ToColor4(), 0xff666600.ToColor4(), 0xff336600.ToColor4(), 0xff006600.ToColor4(), 0xffff3300.ToColor4(), 0xffcc3300.ToColor4(), 0xff993300.ToColor4(),
            0xff663300.ToColor4(), 0xff333300.ToColor4(), 0xff003300.ToColor4(), 0xffff0000.ToColor4(), 0xffcc0000.ToColor4(), 0xff990000.ToColor4(), 0xff660000.ToColor4(), 0xff330000.ToColor4(), 0xff0000ee.ToColor4(), 0xff0000dd.ToColor4(), 0xff0000bb.ToColor4(), 0xff0000aa.ToColor4(), 0xff000088.ToColor4(), 0xff000077.ToColor4(), 0xff000055.ToColor4(), 0xff000044.ToColor4(),
            0xff000022.ToColor4(), 0xff000011.ToColor4(), 0xff00ee00.ToColor4(), 0xff00dd00.ToColor4(), 0xff00bb00.ToColor4(), 0xff00aa00.ToColor4(), 0xff008800.ToColor4(), 0xff007700.ToColor4(), 0xff005500.ToColor4(), 0xff004400.ToColor4(), 0xff002200.ToColor4(), 0xff001100.ToColor4(), 0xffee0000.ToColor4(), 0xffdd0000.ToColor4(), 0xffbb0000.ToColor4(), 0xffaa0000.ToColor4(),
            0xff880000.ToColor4(), 0xff770000.ToColor4(), 0xff550000.ToColor4(), 0xff440000.ToColor4(), 0xff220000.ToColor4(), 0xff110000.ToColor4(), 0xffeeeeee.ToColor4(), 0xffdddddd.ToColor4(), 0xffbbbbbb.ToColor4(), 0xffaaaaaa.ToColor4(), 0xff888888.ToColor4(), 0xff777777.ToColor4(), 0xff555555.ToColor4(), 0xff444444.ToColor4(), 0xff222222.ToColor4(), 0xff111111.ToColor4()
        };
    
        public List<MagicaVoxel> Voxels = new List<MagicaVoxel>();

        public void Load(string? voxPath = null)
        {
            var tempPath = ResolveVoxPath(voxPath ?? @"Assets/Models/Vox/Captain.vox");
            //tempPath = @"Assets/Models/Vox/Ship_Pirate_01.vox"; // TOO BIG for now, needs Mesh Instancing and Shader Caching when its the same shader.
            ManaRiff riffer = new ManaRiff(tempPath);

            riffer.Load();
            Voxels.Clear();

            foreach (var chunk in riffer.Chunks)
            {
                if (chunk.Id == "RGBA")
                {
                    using (BinaryReader reader = new BinaryReader(new MemoryStream(chunk.Data)))
                    {
                        byte[] bytes = new byte[4];

                        for (int i = 1; i < 256; i++)
                        {
                            reader.Read(bytes, 0, 4);

                            var x = bytes[0];
                            var y = bytes[1];
                            var z = bytes[2];
                            var a = bytes[3];

                            ColorPalette[i] = new Vector4(x / 255.0f, y / 255.0f, z / 255.0f, a / 255.0f);
                        }
                    }
                }
            }

            foreach (var chunk in riffer.Chunks)
            {
                if ( chunk.Id == "XYZI")
                {
                    using (BinaryReader reader = new BinaryReader(new MemoryStream(chunk.Data)))
                    {
                        int numberOfVoxels = reader.ReadInt32();

                        for (int i = 0; i < numberOfVoxels; i++)
                        {
                            var xyzi = reader.ReadInt32();
                            byte byte1 = (byte)(xyzi & 0xFF);
                            byte byte2 = (byte)((xyzi >> 8) & 0xFF);
                            byte byte3 = (byte)((xyzi >> 16) & 0xFF);
                            byte byte4 = (byte)((xyzi >> 24) & 0xFF);
                            
                            Voxels.Add(new MagicaVoxel() { Position = new Vector3(byte1, byte2, byte3), Color = ColorPalette[byte4] });
                        }
                    }
                }
            }
        }

        private static string ResolveVoxPath(string path)
        {
            if (Path.IsPathRooted(path) && File.Exists(path))
            {
                return path;
            }

            string[] candidates =
            {
                path,
                Path.Combine(Environment.CurrentDirectory, path),
                Path.Combine(Environment.CurrentDirectory, "Mana", path),
                Path.Combine(Environment.CurrentDirectory, "..", "Mana", path),
                Path.Combine(AppContext.BaseDirectory, path),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Mana", path)
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            throw new FileNotFoundException($"Could not locate MagicaVoxel file '{path}'.");
        }
    }
}
