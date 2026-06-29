namespace ManaCore.Loading
{
    public enum ManaLoadingStage
    {
        NotStarted,
        BackgroundWork,
        InitializeRenderer,
        WaitingForDependencies,
        Completed,
        Failed
    }

    public sealed class ManaLoadingCoordinator
    {
        private readonly Action _initializeRenderer;
        private readonly Action _finalizeGpuUploads;
        private readonly Func<bool> _canComplete;
        private readonly Action _onCompleted;
        private volatile ManaLoadingStage _stage = ManaLoadingStage.NotStarted;

        public ManaLoadingCoordinator(
            Action initializeRenderer,
            Action finalizeGpuUploads,
            Func<bool> canComplete,
            Action onCompleted)
        {
            _initializeRenderer = initializeRenderer;
            _finalizeGpuUploads = finalizeGpuUploads;
            _canComplete = canComplete;
            _onCompleted = onCompleted;
        }

        public ManaLoadingViewModel Progress { get; } = new();
        public ManaLoadingStage Stage => _stage;
        public bool IsLoading => Progress.IsActive;

        public void Begin(string status = "Starting background load")
        {
            _stage = ManaLoadingStage.BackgroundWork;
            Progress.Start(status, 0.0f);
        }

        public void CompleteBackgroundWork()
        {
            if (_stage == ManaLoadingStage.BackgroundWork)
            {
                _stage = ManaLoadingStage.InitializeRenderer;
            }
        }

        public void Fail(Exception exception)
        {
            Fail(exception.Message);
        }

        public void Fail(string message)
        {
            _stage = ManaLoadingStage.Failed;
            Progress.Report(new ManaLoadingProgress($"Loading failed: {message}", Progress.Progress));
        }

        public void Update()
        {
            if (!IsLoading)
            {
                return;
            }

            try
            {
                switch (_stage)
                {
                    case ManaLoadingStage.InitializeRenderer:
                        Progress.Report(new ManaLoadingProgress("Initializing graphics renderer", 0.85f));
                        _initializeRenderer();
                        Progress.Report(new ManaLoadingProgress("Finalizing GPU uploads", 0.95f));
                        _stage = ManaLoadingStage.WaitingForDependencies;
                        break;

                    case ManaLoadingStage.WaitingForDependencies:
                        if (!_canComplete())
                        {
                            break;
                        }

                        _finalizeGpuUploads();
                        _onCompleted();
                        _stage = ManaLoadingStage.Completed;
                        Progress.Complete();
                        break;
                }
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }
    }
}
