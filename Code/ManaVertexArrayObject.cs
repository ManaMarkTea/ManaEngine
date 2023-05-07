using Mana.Code;
using Silk.NET.Assimp;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana
{
    public class ManaVertexArrayObject<TVertexType, TIndexType> : ManaObject, IDisposable
        where TVertexType : unmanaged
        where TIndexType : unmanaged
    {
        private uint _handle;        

        public ManaVertexArrayObject(ManaContext context, ManaBufferObject<TVertexType> vbo, ManaBufferObject<TIndexType> ebo) : base(context)
        {
            var gl = Context.gl;

            _handle = gl.GenVertexArray();
            Bind();
            vbo.Bind();
            ebo.Bind();
        }

        public unsafe void VertexAttributePointer(uint index, int count, VertexAttribPointerType type, uint vertexSize, int offSet)
        {
            var gl = Context.gl;

            gl.VertexAttribPointer(index, count, type, false, vertexSize * (uint)sizeof(TVertexType), (void*)(offSet * sizeof(TVertexType)));
            gl.EnableVertexAttribArray(index);
        }

        public void Bind()
        {            
            Context.gl.BindVertexArray(_handle);
        }

        public void Draw()
        {

            
        }

        public void Dispose()
        {
            Context.gl.DeleteVertexArray(_handle);
        }
    }
}
