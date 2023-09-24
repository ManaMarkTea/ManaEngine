using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.Shapes.Loaders
{
    public static class ManaRiffHelpers
    {
        public static bool EqualsString(this byte[] bytes, string text)
        {
            if (bytes.Length != text.Length) return false;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != text[i])
                {
                    return false;
                }
            }

            return true;
        }

        public static string ToAsciiString(this byte[] bytes)
        {
            return System.Text.ASCIIEncoding.UTF8.GetString(bytes, 0, bytes.Length);
        }
    }

    public class ManaRiffChunk
    {
        public string Id = "NONE";
        public uint Size { get; set; }
        public uint ChildSize { get; set; }
        public byte[] Data { get; set; }

        public ManaRiffChunk(BinaryReader reader)
        {
            byte[] header = reader.ReadBytes(4);
            Id = header.ToAsciiString();
            Size = reader.ReadUInt32();
            ChildSize = reader.ReadUInt32();

            Data = reader.ReadBytes((int)Size);


        }

        public override string ToString()
        {
            return $"{Id} ({Size})";
        }
    }

    public class ManaRiff
    {
        public string FilePath { get; set; }
        
        public ManaRiff(string path)
        {
            FilePath = path;
        }

        public List<ManaRiffChunk> Chunks = new List<ManaRiffChunk>();


        public void Load()
        {
            byte[] riffBuffer = new byte[4];

            if (File.Exists(FilePath))
            {
                using (BinaryReader reader = new BinaryReader(File.Open(FilePath, FileMode.Open, FileAccess.Read)))
                {
                    int bytesRead = reader.Read(riffBuffer, 0, 4);
                    int version = reader.ReadInt32();

                    if (bytesRead != 4 || !riffBuffer.EqualsString("VOX "))
                    {
                        return;
                    }

                    while (reader.BaseStream.Position < reader.BaseStream.Length)
                    {
                        ManaRiffChunk newChunk = new ManaRiffChunk(reader);
                        Chunks.Add(newChunk);
                    }
                }
            }
        }
    }

}
