using ManaCore.Graphics;
using Silk.NET.OpenGL;

namespace Mana.Code.Graphics
{
    public sealed class ManaOpenGlComputeShader : ManaShader, IManaComputeShader
    {
        private readonly GL _gl;
        private readonly uint _program;
        private readonly uint _shader;

        public ManaOpenGlComputeShader(ManaContext context, string name, string source)
            : base(context)
        {
            Name = name;
            Backend = ManaGraphicsBackendKind.OpenGL;
            VertexSource = new ShaderSource();
            FragmentSource = new ShaderSource();
            GeometrySource = new ShaderSource();

            _gl = context.RequireOpenGl();
            _shader = _gl.CreateShader(ShaderType.ComputeShader);
            _gl.ShaderSource(_shader, source);
            _gl.CompileShader(_shader);
            _gl.GetShader(_shader, ShaderParameterName.CompileStatus, out var success);
            if (success == 0)
            {
                var infoLog = _gl.GetShaderInfoLog(_shader);
                _gl.DeleteShader(_shader);
                throw new InvalidOperationException($"OpenGL compute shader '{name}' failed to compile: {infoLog}");
            }

            _program = _gl.CreateProgram();
            _gl.AttachShader(_program, _shader);
            _gl.LinkProgram(_program);
            _gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out success);
            if (success == 0)
            {
                var infoLog = _gl.GetProgramInfoLog(_program);
                _gl.DeleteProgram(_program);
                _gl.DeleteShader(_shader);
                throw new InvalidOperationException($"OpenGL compute shader '{name}' failed to link: {infoLog}");
            }
        }

        public ManaGraphicsBackendKind Backend { get; }

        public override void BindShader()
        {
            _gl.UseProgram(_program);
        }

        public void Dispatch(ManaComputeDispatchSize dispatchSize)
        {
            BindShader();
            _gl.DispatchCompute(dispatchSize.X, dispatchSize.Y, dispatchSize.Z);
            _gl.MemoryBarrier(MemoryBarrierMask.ShaderImageAccessBarrierBit | MemoryBarrierMask.TextureFetchBarrierBit);
        }

        public override void UnbindShader()
        {
            _gl.UseProgram(0);
        }

        public void Dispose()
        {
            if (_program != 0)
            {
                _gl.DeleteProgram(_program);
            }

            if (_shader != 0)
            {
                _gl.DeleteShader(_shader);
            }
        }
    }
}
