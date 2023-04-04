using Mana.Code.ManaShaders;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Mana
{

    public class RenderBufferInfo
    {
        public bool RedFloatBuffer;
        public bool FloatBuffer;
        public bool RgbBuffer;
        public bool RgbaBuffer;

        public bool Depth24;
        public bool Depth16;
        public bool Stencil8;

        public string BufferName;
        public ManaTexture? Texture;

        public RenderBufferInfo()
        {
            RedFloatBuffer = false;
            FloatBuffer = false;
            RgbBuffer = false;
            RgbaBuffer = false;
            Depth24 = false;
            Depth16 = false;
            Stencil8 = false;
            Texture = null;
            BufferName = "";
        }
    };

    public class ManaRenderTarget
    {
        public ManaWorld World { get; set; }

        public uint Width { get; set; }
        public uint Height { get; set; }

        public Vector4 ClearColor { get; set; }

        public uint FrameBufferID { get; set; }

        private bool HasDepth { get; set; }

        private ManaShader DebugQuadShader;
        private ManaMaterial QuadMaterial;

        private uint VertexArrayObject;

        Dictionary<string, RenderBufferInfo> bufferMap;
        List<RenderBufferInfo> bufferInfos;        
        
        private GLEnum[] attachments = new GLEnum[32];

        public ManaRenderTarget(ManaWorld world, int width, int height, bool depth = true)
        {
            this.World = world;
            this.Width = (uint)width;
            this.Height = (uint)height;

            ClearColor = new Vector4(100 / 255.0f, 149 / 255.0f, 237 / 255.0f, 255 / 255.0f);
            bufferInfos = new List<RenderBufferInfo>();
            bufferMap = new Dictionary<string, RenderBufferInfo>();

            for (int colorAttachment = 0; colorAttachment < attachments.Length; colorAttachment++)
            {
                attachments[colorAttachment] = (GLEnum)(int)((GLEnum.ColorAttachment0) + colorAttachment);
            }
        }

        public void Resize(int width, int height)
        {
            this.Width = (uint)width;
            this.Height = (uint)height;
            
            foreach (var attachments in bufferInfos)
            {
                if (attachments.Texture != null)
                {
                    attachments.Texture.Width = this.Width;
                    attachments.Texture.Height = this.Height;
                    attachments.Texture.UpdateTextureSize();
                }                
            }

        }

        public void AddBuffer(string name, RenderBufferInfo info)
        {
            info.BufferName = name;
            bufferInfos.Add(info);
            bufferMap[name] = info;
        }
        public void AddDepthBuffer(string name)
        {
            var info = new RenderBufferInfo();
            info.Depth24 = true;
            info.Stencil8 = true;
            HasDepth = true;
            AddBuffer(name, info);
        }
        public void AddFloatBuffer(string name)
        {
            var info = new RenderBufferInfo();
            info.FloatBuffer = true;
            AddBuffer(name, info);
        }
        public void AddRedFloatBuffer(string name)
        {
            var info = new RenderBufferInfo();
            info.RedFloatBuffer = true;
            AddBuffer(name, info);
        }
        public void AddRgbBuffer(string name)
        {
            var info = new RenderBufferInfo();
            info.RgbBuffer = true;
            AddBuffer(name, info);
        }
        public void AddRgbaBuffer(string name)
        {
            var info = new RenderBufferInfo();
            info.RgbaBuffer = true;
            AddBuffer(name, info);
        }

        public unsafe void Build()
        {
            var gl = Game.Instance.gl;
            
            DebugQuadShader = new ManaShaderRayTrace(World);
            //DebugQuadShader.LoadShader("Assets/Shaders/FullScreenQuad/empty.vert", "Assets/Shaders/FullScreenQuad/Edge.frag", "Assets/Shaders/FullScreenQuad/full_screen_quad.geom");
            DebugQuadShader.LoadShader("Assets/Shaders/FullScreenQuad/empty.vert", "Assets/Shaders/FullScreenQuad/RayTracing_001.frag", "Assets/Shaders/FullScreenQuad/full_screen_quad.geom");

            QuadMaterial = new ManaMaterial(World);
            QuadMaterial.Shader = DebugQuadShader;

            VertexArrayObject = gl.GenVertexArray();

            FrameBufferID = gl.GenFramebuffer();
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, FrameBufferID);            
            
            int bufferCount = bufferInfos.Count;
            uint[] colorBuffers = new uint[bufferCount];
            gl.GenTextures((uint)bufferCount, colorBuffers);

            int i = 0;
            int colorIndex = 0;
            foreach (var bufferInfo in bufferInfos)
            {
                TextureMinFilter filterTypeMin = TextureMinFilter.Linear;
                TextureMagFilter filterTypeMag = TextureMagFilter.Linear;

                InternalFormat internalFormat = bufferInfo.FloatBuffer ? InternalFormat.Rgb16f : bufferInfo.RgbBuffer ? InternalFormat.Rgb : InternalFormat.Rgba;
                PixelFormat internalFormat2 = bufferInfo.FloatBuffer ? PixelFormat.Rgba : PixelFormat.Rgb;

                GLEnum internalDataType = bufferInfo.FloatBuffer ? GLEnum.Float: GLEnum.UnsignedByte;

                if (bufferInfo.RedFloatBuffer)
                {
                    internalFormat = InternalFormat.Red;
                    internalFormat2 = PixelFormat.Rgb;
                    internalDataType = GLEnum.Float;
                    filterTypeMin = TextureMinFilter.Nearest;
                    filterTypeMag = TextureMagFilter.Nearest;
                }

                ManaTexture texture = new ManaTexture();
                texture.Width = Width;
                texture.Height = Height;
                texture.IsWrapU = false;
                texture.Handle = colorBuffers[i];                
                bufferInfo.Texture = texture;

                if (bufferInfo.Depth16 || bufferInfo.Depth24)
                {
                    texture.FboAttachmentType = GLEnum.DepthStencilAttachment;
                    texture.IsWrapU = false;
                    texture.IsWrapV = false;
                    texture.MinFilter = filterTypeMin;
                    texture.MagFilter = filterTypeMag;

                    texture.InternalDataFormat = InternalFormat.Depth24Stencil8;
                    texture.InternalPixelFormat = PixelFormat.DepthStencil;
                    texture.InternalPixelType = GLEnum.UnsignedInt248;
                    texture.Load();
                }
                else
                {
                    texture.FboAttachmentType = attachments[colorIndex++];
                   
                    texture.IsWrapU = false;
                    texture.IsWrapV = false;
                    texture.MinFilter = filterTypeMin;
                    texture.MagFilter = filterTypeMag;

                    texture.InternalDataFormat = internalFormat;
                    texture.InternalPixelFormat = internalFormat2;
                    texture.InternalPixelType = internalDataType;
                    texture.Load();
                }

                // attach texture to framebuffer
                gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, texture.FboAttachmentType, TextureTarget.Texture2D, colorBuffers[i], 0);

                i++;
            }

            /* Check for errors */
            GLEnum status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != GLEnum.FramebufferComplete)
            {
                throw new Exception("Bad Frame buffer");
            }            
        }

        public void Clear()
        {
            var gl = Game.Instance.gl;
            gl.ClearColor(ClearColor.X, ClearColor.Y, ClearColor.Z, ClearColor.W);

            if (HasDepth)
            {                
                gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
            }
            else
            {
                gl.Clear((uint)(ClearBufferMask.ColorBufferBit));                
            }

            //gl.Color4f(1, 1, 1, 1);
        }

        public void Bind()
        {
            var gl = Game.Instance.gl;
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, FrameBufferID);            
            gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
        }

        public void UnBind()
        {
            var gl = Game.Instance.gl;
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        /*
        void Blit();
        void Blit(int dstWidth, int dstHeight);
        void Blit(int srcWidth, int srchHeight, int dstWidth, int dstHeight);
        */

        public void DrawFullScreenQuad()
        {
            var gl = Game.Instance.gl;

            UnBind();
            Clear();

            QuadMaterial.Bind(Matrix4x4.Identity);

            gl.Viewport(0, 0, Width, Height);

            bufferInfos[0].Texture?.Bind(0);
            gl.BindVertexArray(VertexArrayObject);
            gl.DrawArrays(PrimitiveType.Points, 0, 1);            
        }
    }
}
