using Mana.Code;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana
{

    public class ManaVertexBufferObject : ManaObject
    {
        public static uint Vector2_SizeInBytes() { return sizeof(float) * 2; }
        public static uint Vector3_SizeInBytes() { return sizeof(float) * 3;}
        public static uint Vector4_SizeInBytes() { return sizeof(float) * 4; }

        private GL gl;

        public uint[] indexes;
        public Vector3[] positions;
        public Vector3[] normals;
        public Vector4[] colors;
        public Vector2[] textureCoordinates0;
        public Vector2[] textureCoordinates1;

        int maxIndexes;
        int maxVerts;
        uint vertexData;
        uint indexData;
        int validIndex;
        uint vertexArrayObject = 0;

        /*
         * https://stackoverflow.com/questions/16380005/opengl-3-4-glvertexattribpointer-stride-and-offset-miscalculation
         *    The stride is the distance from 1 element to the next of the same type.
         * |=======================================|
         * | Vertex 1   | Vertex 2   | Vertex 3   |
         * | XYZ RGB UV | XYZ RGB UV | XYZ RGB UV |
         * | ---------->|     |
         *    STRIDE          |
         *       |----------->|         
         *       Our stride is 0 since we stack our vertexes side by side  (XYZXYZXYZ)|(RGBRGBRGB) etc
         *       (Technically it seems to be 3*sizeof(float) but opengl accepts zero in our case
         */
        int stride = 0;

        int attribVertex = -1;
        int attribNormal = -1;
        int attribColor = -1;
        int attribUv0 = -1;
        int attribUv1 = -1;

        nint offsetVertex = 0;
        nint offsetNormal = 1;
        nint offsetColor = 2;
        nint offsetUv0 = 3;
        nint offsetUv1 = 4;

        public ManaVertexBufferObject(
            ManaContext context,
            uint[] indexes,
            Vector3[] positions,
            Vector3[] normals,
            Vector4[] colors,
            Vector2[] textureCoordinates0,
            Vector2[] textureCoordinates1
        ) : base(context)
        {
            gl = Context.gl;
            this.indexes = indexes;
            this.positions = positions;
            this.normals = normals;
            this.colors = colors;
            this.textureCoordinates0 = textureCoordinates0;
            this.textureCoordinates1 = textureCoordinates1;

            if (colors.Length == 0)
            {
                this.colors = new Vector4[positions.Length];
                Parallel.For(0, this.colors.Length, c => this.colors[c] = Vector4.One);
            }
        }


        public bool ConstructVBO()
        {
            gl.GetInteger(GetPName.MaxElementsIndices, out maxIndexes);
            gl.GetInteger(GetPName.MaxElementsVertices, out maxVerts);

            uint buffersNeed = 0;
            uint dataSize = 0;
            stride = 0;

            uint vSize = Vector3_SizeInBytes() * (uint)positions.Length;
            uint nSize = Vector3_SizeInBytes() * (uint)normals.Length;
            uint cSize = Vector4_SizeInBytes() * (uint)colors.Length;
            uint t0Size = Vector2_SizeInBytes() * (uint)textureCoordinates0.Length;
            uint t1Size = Vector2_SizeInBytes() * (uint)textureCoordinates1.Length;

            // 
            int vertexSize = 0;
            stride = 0;

            if (positions.Length != 0)
            {
                dataSize += vSize;
                vertexSize += 3 * sizeof(float);
            }
            if (normals.Length != 0)
            {
                dataSize += nSize;
                vertexSize += 3 * sizeof(float);
            }
            if (colors.Length != 0)
            {
                dataSize += cSize;
                vertexSize += 4 * sizeof(float);
            }
            if (textureCoordinates0.Length != 0)
            {
                dataSize += t0Size;
                vertexSize += 2 * sizeof(float);
            }
            if (textureCoordinates1.Length != 0)
            {
                dataSize += t1Size;
                vertexSize += 2 * sizeof(float);
            }

            if (indexes.Length != 0) { buffersNeed++; }

            if (buffersNeed == 0) { return false; }

            gl.GenVertexArrays(1, out vertexArrayObject);
            gl.BindVertexArray(vertexArrayObject);
            gl.GenBuffers(1, out vertexData);
            gl.GenBuffers(1, out indexData);

            BufferUsageARB drawType = BufferUsageARB.StaticDraw;
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, vertexData);
            {                
                byte[] bogusBuffer = new byte[dataSize];
                gl.BufferData<byte>(BufferTargetARB.ArrayBuffer, (nuint)dataSize, bogusBuffer, drawType); // reserve space
            }

            nint offset = 0;
            int attribOffset = 0;

            if (positions.Length != 0)
            {
                offsetVertex = offset;
                gl.BufferSubData<Vector3>(BufferTargetARB.ArrayBuffer, offset, vSize, positions);
                offset += (nint)vSize;
                attribVertex = attribOffset;
            }
            attribOffset++;

            if (normals.Length != 0)
            {
                offsetNormal = offset;
                gl.BufferSubData<Vector3>(BufferTargetARB.ArrayBuffer, offset, nSize, normals);
                offset += (nint)nSize;
                attribNormal = attribOffset;
            }
            attribOffset++;

            if (textureCoordinates0.Length != 0)
            {
                offsetUv0 = offset;
                gl.BufferSubData<Vector2>(BufferTargetARB.ArrayBuffer, offset, t0Size, textureCoordinates0);
                offset += (nint)t0Size;
                attribUv0 = attribOffset;
            }
            attribOffset++;

            if (colors.Length != 0)
            {
                offsetColor = offset;
                gl.BufferSubData<Vector4>(BufferTargetARB.ArrayBuffer, offset, cSize, colors);
                offset += (nint)cSize;
                attribColor = attribOffset;
            }
            attribOffset++;

            if (textureCoordinates1.Length != 0)
            {
                offsetUv1 = offset;
                gl.BufferSubData<Vector2>(BufferTargetARB.ArrayBuffer, offset, t1Size, textureCoordinates1);
                offset += (nint)t1Size;
                attribUv1 = attribOffset;
            }
            attribOffset++;
            
            if (indexes.Length != 0)
            {
                gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, indexData);
                gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer, (nuint)(sizeof(uint) * indexes.Length), indexes, BufferUsageARB.StaticDraw);
            }

            this.validIndex = indexes.Length;
            return true;
        }

        public unsafe void Bind()
        {
            // bind VAO and VBOs before drawing
            gl.BindVertexArray(vertexArrayObject);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, vertexData);
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, indexData);

            // activate vertex attribs
            if (attribVertex != -1)
            {                
                gl.VertexAttribPointer((uint)attribVertex, 3, VertexAttribPointerType.Float, false, (uint)stride, (void*)offsetVertex);
                gl.EnableVertexAttribArray((uint)attribVertex);
            }
            if (attribNormal != -1)
            {
                gl.VertexAttribPointer((uint)attribNormal, 3, VertexAttribPointerType.Float, false, (uint)stride, (void*)offsetNormal);
                gl.EnableVertexAttribArray((uint)attribNormal);
            }
            if (attribColor != -1)
            {
                gl.VertexAttribPointer((uint)attribColor, 4, VertexAttribPointerType.Float, false, (uint)stride, (void*)offsetColor);
                gl.EnableVertexAttribArray((uint)attribColor);
            }
            if (attribUv0 != -1)
            {
                gl.VertexAttribPointer((uint)attribUv0, 2, VertexAttribPointerType.Float, false, (uint)stride, (void*)offsetUv0);
                gl.EnableVertexAttribArray((uint)attribUv0);

            }
            if (attribUv1 != -1)
            {
                gl.VertexAttribPointer((uint)attribUv1, 2, VertexAttribPointerType.Float, false, (uint)stride, (void*)offsetUv1);
                gl.EnableVertexAttribArray((uint)attribUv1);
            }
        }

        public unsafe void DrawElements()
        {
            gl.DrawElements(PrimitiveType.Triangles, (uint)validIndex, DrawElementsType.UnsignedInt, null);
        }

        public void UnBind()
        {
            // activate vertex attribs
            if (attribVertex != 0)
            {
                gl.DisableVertexAttribArray((uint)attribVertex);
            }
            if (attribNormal != -1)
            {
                gl.DisableVertexAttribArray((uint)attribNormal);
            }
            if (attribColor != -1)
            {
                gl.DisableVertexAttribArray((uint)attribColor);
            }
            if (attribUv0 != -1)
            {
                gl.DisableVertexAttribArray((uint)attribUv0);
            }
            if (attribUv1 != -1)
            {
                gl.DisableVertexAttribArray((uint)attribUv1);
            }

            // unbind
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);
            gl.BindVertexArray(0);
        }
    }
}
