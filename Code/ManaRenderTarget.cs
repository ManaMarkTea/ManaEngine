using Silk.NET.OpenGL;

namespace Mana
{

    public class RenderBufferInfo
    {
        public bool RedFloatBuffer;
        public bool FloatBuffer;
        public bool HasAlpha;
        public bool HasCube;

        public bool HasMip;
        public bool MinLinearMip;
        public bool MaxLinearMip;

        public bool RgbBuffer;
        public bool RgbaBuffer;

        public bool Depth24;
        public bool Depth16;
        public bool Stencil8;

        public uint Samples;

        public string BufferName;
        public ManaTexture? Texture;

        public RenderBufferInfo()
        {
            RedFloatBuffer = false;
            FloatBuffer = false;
            HasAlpha = false;
            HasCube = false;
            RgbBuffer = false;
            RgbaBuffer = false;
            Depth24 = false;
            Depth16 = false;
            Stencil8 = false;
            Texture = null;
            Samples = 0;

            MinLinearMip = false;
            HasMip = false;

            BufferName = "";
        }
    };

    public class ManaRenderTarget : ManaObject
    {

        public uint Width { get; set; }
        public uint Height { get; set; }

        public Vector4 ClearColor { get; set; }

        public uint FrameBufferID { get; set; }

        private bool HasDepth { get; set; }

        private ManaShader DebugQuadShader;
        private ManaMaterial QuadMaterial;

        private uint VertexArrayObject;

        public Dictionary<string, RenderBufferInfo> bufferMap;
        public List<RenderBufferInfo> bufferInfos;

        private GLEnum[] attachments = new GLEnum[32];

        public ManaRenderTarget(ManaContext context, int width, int height, bool depth = true) : base(context)
        {
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

        public int MaxMips
        {
            get
            {
                int output = (int)(Math.Log2(MathF.Max(Width, Height))) + 1;
                return output;
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
            info.HasAlpha = true;
            AddBuffer(name, info);
        }

        public void AddFloatRgbCubeBuffer(string name)
        {
            var info = new RenderBufferInfo();
            info.FloatBuffer = true;
            info.HasAlpha = false;
            info.HasCube = true;
            AddBuffer(name, info);
        }

        public void AddFloatRgbBuffer(string name)
        {
            var info = new RenderBufferInfo();
            info.FloatBuffer = true;
            info.HasAlpha = false;
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

        public void Build()
        {
            var gl = Context.gl;

            FrameBufferID = gl.GenFramebuffer();
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, FrameBufferID);

            int i = 0;
            int colorIndex = 0;
            foreach (var bufferInfo in bufferInfos)
            {
                TextureMinFilter filterTypeMin = bufferInfo.MinLinearMip ? TextureMinFilter.LinearMipmapLinear : TextureMinFilter.Linear;
                TextureMagFilter filterTypeMag = TextureMagFilter.Linear;

                InternalFormat internalFormat = bufferInfo.FloatBuffer ? InternalFormat.Rgb16f : bufferInfo.RgbBuffer ? InternalFormat.Rgb : InternalFormat.Rgba;
                PixelFormat internalFormat2 = bufferInfo.HasAlpha ? PixelFormat.Rgba : PixelFormat.Rgb;

                GLEnum internalDataType = bufferInfo.FloatBuffer ? GLEnum.Float : GLEnum.UnsignedByte;

                if (bufferInfo.RedFloatBuffer)
                {
                    internalFormat = InternalFormat.Red;
                    internalFormat2 = PixelFormat.Rgb;
                    internalDataType = GLEnum.Float;
                    filterTypeMin = TextureMinFilter.Nearest;
                    filterTypeMag = TextureMagFilter.Nearest;
                }

                ManaTexture texture = new ManaTexture(Context);
                texture.Width = Width;
                texture.IsCube = bufferInfo.HasCube;
                texture.HasMipMaps = bufferInfo.HasMip;
                texture.Height = Height;
                texture.IsWrapU = false;
                texture.IsWrapV = false;
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


                if (!bufferInfo.HasCube)
                {
                    // attach texture to framebuffer
                    gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, texture.FboAttachmentType, TextureTarget.Texture2D, texture.Handle, 0);
                    //gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, bufferInfo.Samples, InternalFormat.Rgba8, Width, Height);
                }
                else
                {
                    int mips = bufferInfo.HasMip ? MaxMips : 1;
                    for (int cubeFace = 0; cubeFace < 6; ++cubeFace)
                    {
                        for (int mipIndex = 0; mipIndex < mips; ++mipIndex)
                        {
                            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, texture.FboAttachmentType, TextureTarget.TextureCubeMapPositiveX + cubeFace, texture.Handle, mipIndex);
                        }
                    }
                }

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
            var gl = Context.gl;

            Bind();

            gl.ClearColor(ClearColor.X, ClearColor.Y, ClearColor.Z, ClearColor.W);

            if (HasDepth)
            {
                gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
            }
            else
            {
                gl.Clear((uint)(ClearBufferMask.ColorBufferBit));
            }

            UnBind();
        }

        public void Bind(int index = 0, int mipIndex = 0)
        {
            var gl = Context.gl;
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, FrameBufferID);

            uint mipWidth = (uint)(Width * MathF.Pow(0.5f, mipIndex));
            uint mipHeight = (uint)(Height * MathF.Pow(0.5f, mipIndex));
            gl.Viewport(0, 0, mipWidth, mipHeight);

            foreach (var buffer in bufferInfos)
            {
                if (buffer.Texture != null)
                {
                    buffer.Texture.UnBind(0);
                    buffer.Texture.UnBind(1);
                    buffer.Texture.UnBind(2);
                    buffer.Texture.UnBind(3);
                }

                if (buffer.HasCube)
                {
                    // Bind the face of the cube we're drawing to
                    gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, buffer.Texture.FboAttachmentType, TextureTarget.TextureCubeMapPositiveX + index, buffer.Texture.Handle, mipIndex);
                }
            }
        }

        public void UnBind()
        {
            var gl = Context.gl;
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        /*
        void Blit();
        void Blit(int dstWidth, int dstHeight);
        void Blit(int srcWidth, int srchHeight, int dstWidth, int dstHeight);
        */

        public void DrawFullScreenQuad()
        {
            var gl = Context.gl;

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
