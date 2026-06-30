namespace Mana.Code.Materials
{
    public class ManaMaterialChannel
    {
        public string Name { get; set; } = string.Empty;

        public ManaMaterialChannelTypes ChannelType = ManaMaterialChannelTypes.None;
        public int IntValue { get; set; }
        public bool BooleanValue { get; set; }
        public float ScalarValue { get; set; }
        public Vector4 VectorValue { get; set; }
        public ManaTexture Texture { get; set; } = null!;
        public string Value { get; set; } = string.Empty;

        public override string ToString()
        {
            string output = Name + " = ";
            switch (ChannelType)
            {
                case ManaMaterialChannelTypes.None: break;
                case ManaMaterialChannelTypes.IntValue: output += IntValue; break;
                case ManaMaterialChannelTypes.BooleanValue: output += BooleanValue; break;
                case ManaMaterialChannelTypes.ScalarValue: output += ScalarValue; break;
                case ManaMaterialChannelTypes.VectorValue: output += VectorValue; break;
                case ManaMaterialChannelTypes.TextureValue: output += Texture?.ToString(); break;
                case ManaMaterialChannelTypes.StringValue: output += Value; break;
            }

            return output;
        }
    }
}
