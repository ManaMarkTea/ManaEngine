using Silk.NET.OpenGL;

namespace Mana.Code.Materials
{
    public class ManaMaterialUniformValue
    {
        public UniformType Type { get; set; }
        public int IntValue { get; set; }
        public bool BoolValue { get; set; }
        public float FloatValue { get; set; }
        public Vector2 Vector2Value { get; set; }
        public Vector3 Vector3Value { get; set; }
        public Vector4 Vector4Value { get; set; }

        public ManaMaterialUniformValue Clone()
        {
            return (ManaMaterialUniformValue)MemberwiseClone();
        }
    }
}
