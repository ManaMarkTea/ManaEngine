using ManaCore.Loading;

namespace ManaGraphics.Code.Graphics;

// Background jobs own CPU data only. Render-thread consumers take the newest generation's result.
internal sealed class ManaWebGpuEnvironmentLoad<T> where T : class
{
    internal sealed record Result(T? Value, Exception? Error);
    private sealed class LoadProgress(Action<ManaLoadingProgress> report) : IProgress<ManaLoadingProgress>
    {
        public void Report(ManaLoadingProgress value) => report(value);
    }

    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private long _generation;
    private bool _retired;
    private Result? _ready;
    private ManaLoadingProgress? _progress;
    public Task Completion { get; private set; } = Task.CompletedTask;

    public void Start(Func<IProgress<ManaLoadingProgress>, CancellationToken, Task<T>> prepare)
    {
        lock (_gate)
        {
            if (_retired)
            {
                return;
            }
            InvalidateLocked();
            var generation = _generation;
            var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            Completion = Task.Run(async () =>
            {
                var progress = new LoadProgress(value =>
                {
                    lock (_gate)
                    {
                        if (!_retired && generation == _generation)
                        {
                            _progress = value;
                        }
                    }
                });
                Result? result = null;
                try
                {
                    var value = await prepare(progress, cancellation.Token).ConfigureAwait(false);
                    cancellation.Token.ThrowIfCancellationRequested();
                    result = new Result(value, null);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    result = new Result(null, ex);
                }
                finally
                {
                    lock (_gate)
                    {
                        if (!_retired && generation == _generation)
                        {
                            _ready = result;
                            _cancellation = null;
                        }
                        cancellation.Dispose();
                    }
                }
            });
        }
    }

    public Result? TakeResult()
    {
        lock (_gate)
        {
            var result = _ready;
            _ready = null;
            return result;
        }
    }

    public ManaLoadingProgress? TakeProgress()
    {
        lock (_gate)
        {
            var progress = _progress;
            _progress = null;
            return progress;
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            InvalidateLocked();
        }
    }

    private void InvalidateLocked()
    {
        _generation++;
        _cancellation?.Cancel();
        _cancellation = null;
        _ready = null;
        _progress = null;
    }

    public void Retire()
    {
        lock (_gate)
        {
            _retired = true;
            InvalidateLocked();
        }
    }
}
