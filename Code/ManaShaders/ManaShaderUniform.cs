using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.ManaShaders
{
    public class ManaShaderUniform       
    {
        public GL gl;
        public uint shaderLocation;
        public int location;
        public UniformType type;
        public string name;

        public void SetValue(int value)
        {
            var index = gl.GetUniformLocation(shaderLocation, name);
            gl.Uniform1(location, value);
        }

        public void SetValue(uint value)
        {
            var index = gl.GetUniformLocation(shaderLocation, name);
            gl.Uniform1(location, value);
        }
        public void SetValue(float value)
        {
            gl.Uniform1(location, value);
        }

        public void SetValue(float valueX, float valueY, float valueZ)
        {
            gl.Uniform3(location, new ReadOnlySpan<float>(new float[]{ valueX, valueY, valueZ }));
        }

        public void SetValue(Vector2 value)
        {
            gl.Uniform2(location, value);
        }

        public void SetValue(Vector3 value)
        {
            gl.Uniform3(location, value);
        }

        public void SetValue(Vector4 value)
        {
            gl.Uniform4(location, value);
        }

        public void SetValue(Matrix4x4 value)
        {
            gl.UniformMatrix4(location, 1, false, value.ToSpan());
        }

        public void SetValue(Silk.NET.Maths.Matrix3X3<float> value)
        {
            gl.UniformMatrix3(location, 1, false, value.ToSpan());
        }

        public override string ToString()
        {
            return $"{location}:{type} {name}";
        }
    }
}
