namespace ManaCore.Graphics
{
    public readonly record struct ManaComputeDispatchSize(uint X, uint Y, uint Z);

    public interface IManaComputeShader : IDisposable
    {
        ManaGraphicsBackendKind Backend { get; }
        string Name { get; }
    }

    public abstract class ManaComputeShader : IManaComputeShader
    {
        protected ManaComputeShader(ManaGraphicsBackendKind backend, string name)
        {
            Backend = backend;
            Name = name;
        }

        public ManaGraphicsBackendKind Backend { get; }
        public string Name { get; }

        public abstract void Dispose();
    }
}
