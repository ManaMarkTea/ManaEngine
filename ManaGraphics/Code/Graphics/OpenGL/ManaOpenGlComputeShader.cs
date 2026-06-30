using ManaCore.Graphics;
using Silk.NET.OpenGL;

namespace ManaGraphics.Code.Graphics
{
    public sealed class ManaOpenGlComputeShader : ManaComputeShader
    {
        private readonly GL _gl;
        private uint _program;
        private uint _shader;

        public ManaOpenGlComputeShader(GL gl, string name, string source)
            : base(ManaGraphicsBackendKind.OpenGL, name)
        {
            _gl = gl;
            _shader = _gl.CreateShader(ShaderType.ComputeShader);
            _gl.ShaderSource(_shader, source);
            _gl.CompileShader(_shader);
            _gl.GetShader(_shader, ShaderParameterName.CompileStatus, out var success);
            if (success == 0)
            {
                var infoLog = _gl.GetShaderInfoLog(_shader);
                _gl.DeleteShader(_shader);
                _shader = 0;
                throw new InvalidOperationException($"OpenGL compute shader '{name}' failed to compile: {infoLog}");
            }

            _program = _gl.CreateProgram();
            _gl.AttachShader(_program, _shader);
            _gl.LinkProgram(_program);
            _gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out success);
            if (success == 0)
            {
                var infoLog = _gl.GetProgramInfoLog(_program);
                Dispose();
                throw new InvalidOperationException($"OpenGL compute shader '{name}' failed to link: {infoLog}");
            }
        }

        public void Dispatch(ManaComputeDispatchSize dispatchSize)
        {
            _gl.UseProgram(_program);
            _gl.DispatchCompute(dispatchSize.X, dispatchSize.Y, dispatchSize.Z);
            _gl.MemoryBarrier(
                MemoryBarrierMask.ShaderImageAccessBarrierBit |
                MemoryBarrierMask.TextureFetchBarrierBit);
            _gl.UseProgram(0);
        }

        public override void Dispose()
        {
            if (_program != 0)
            {
                _gl.DeleteProgram(_program);
                _program = 0;
            }

            if (_shader != 0)
            {
                _gl.DeleteShader(_shader);
                _shader = 0;
            }
        }
    }
}
