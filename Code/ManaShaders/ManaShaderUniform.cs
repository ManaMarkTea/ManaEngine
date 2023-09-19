using Silk.NET.OpenGL;

namespace Mana.Code.ManaShaders
{
    public class ManaShaderUniform
    {
        public GL gl;
        public uint shaderLocation;
        public int location;
        public int size;
        public UniformType type;
        public string name;

        public void SetValue(int value, int arrayIndex = 0)
        {
            gl.Uniform1(location + arrayIndex, value);
        }

        public void SetValue(uint value, int arrayIndex = 0)
        {
            gl.Uniform1(location + arrayIndex, value);
        }
        public void SetValue(float value, int arrayIndex = 0)
        {
            gl.Uniform1(location + arrayIndex, value);
        }

        public void SetValue(double value, int arrayIndex = 0)
        {
            gl.Uniform1(location + arrayIndex, (float)value);
        }

        public void SetValue(float valueX, float valueY, float valueZ, int arrayIndex = 0)
        {
            gl.Uniform3(location + arrayIndex, new ReadOnlySpan<float>(new float[] { valueX, valueY, valueZ }));
        }

        public void SetValue(Vector2 value, int arrayIndex = 0)
        {
            gl.Uniform2(location + arrayIndex, value);
        }

        public void SetValue(Vector3 value, int arrayIndex = 0)
        {
            gl.Uniform3(location + arrayIndex, value);
        }

        public void SetValue(Vector4 value, int arrayIndex = 0)
        {
            gl.Uniform4(location + arrayIndex, value);
        }

        public void SetValue(Matrix4x4 value, int arrayIndex = 0)
        {
            gl.UniformMatrix4(location + arrayIndex, 1, false, value.ToSpan());
        }

        public void SetValue(Silk.NET.Maths.Matrix3X3<float> value, int arrayIndex = 0)
        {
            gl.UniformMatrix3(location + arrayIndex, 1, false, value.ToSpan());
        }

        public override string ToString()
        {
            return $"{location}:{type} {name}";
        }
    }
}
