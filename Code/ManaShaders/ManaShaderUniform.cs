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
        public int location;
        public UniformType type;
        public string name;

        public void SetValue(int value)
        {
            gl.Uniform1(location, value);
        }
        public void SetValue(float value)
        {
            gl.Uniform1(location, value);
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


        public override string ToString()
        {
            return $"{location}:{type} {name}";
        }
    }
}
