using ManaCore.Graphics;
using Silk.NET.OpenGL;

namespace Mana.Code.Graphics
{
    public sealed class ManaOpenGlTexture : IOpenGlGpuTexture
    {
        private readonly ManaTexture _texture;

        public ManaOpenGlTexture(ManaTexture texture)
        {
            _texture = texture;
        }

        public ManaGraphicsBackendKind Backend => ManaGraphicsBackendKind.OpenGL;
        public uint Width => _texture.Width;
        public uint Height => _texture.Height;
        public bool IsLoaded => _texture.IsLoaded;
        public uint Handle => _texture.Handle;

        public void Load()
        {
            if (_texture.Context.OpenGl == null)
            {
                return;
            }
            var gl = _texture.Context.RequireOpenGl();

            if (_texture.IsLoaded)
            {
                return;
            }

            if (_texture.Handle == 0)
            {
                _texture.Handle = gl.GenTexture();
            }

            gl.BindTexture(_texture.TextureTargetType, _texture.Handle);

            _texture.UpdateParameters();
            _texture.LoadOpenGlTextureData();

            if (_texture.HasMipMaps)
            {
                gl.GenerateMipmap(_texture.TextureTargetType);
            }

            _texture.IsLoaded = true;
        }

        public void UpdateTextureSize()
        {
            if (_texture.Context.OpenGl == null)
            {
                return;
            }
            var gl = _texture.Context.RequireOpenGl();

            _texture.SizeInBytes = (int)_texture.Width * (int)_texture.Height * 4;
            gl.BindTexture(_texture.TextureTargetType, _texture.Handle);

            if (!_texture.IsCube)
            {
                _texture.AllocateEmptyTextImage2D(gl, _texture.TextureTargetType, _texture.Width, _texture.Height);
                return;
            }

            for (int i = 0; i < 6; i++)
            {
                _texture.AllocateEmptyTextImage2D(gl, TextureTarget.TextureCubeMapPositiveX + i, _texture.Width, _texture.Height);
            }
        }

        public void Bind(int channel)
        {
            if (_texture.Context.OpenGl == null)
            {
                return;
            }
            var gl = _texture.Context.RequireOpenGl();
            ActivateTexture(gl, channel);

            if (_texture.Handle > 0)
            {
                gl.BindTexture(_texture.TextureTargetType, _texture.Handle);
            }
        }

        public void UnBind(int channel)
        {
            if (_texture.Context.OpenGl == null)
            {
                return;
            }
            var gl = _texture.Context.RequireOpenGl();
            ActivateTexture(gl, channel);
            gl.BindTexture(_texture.TextureTargetType, 0);
            gl.ActiveTexture(TextureUnit.Texture0);
        }

        public void Dispose()
        {
        }

        private static void ActivateTexture(GL gl, int channel)
        {
            gl.ActiveTexture(channel switch
            {
                0 => TextureUnit.Texture0,
                1 => TextureUnit.Texture1,
                2 => TextureUnit.Texture2,
                3 => TextureUnit.Texture3,
                4 => TextureUnit.Texture4,
                5 => TextureUnit.Texture5,
                6 => TextureUnit.Texture6,
                7 => TextureUnit.Texture7,
                8 => TextureUnit.Texture8,
                9 => TextureUnit.Texture9,
                _ => TextureUnit.Texture0
            });
        }
    }
}
