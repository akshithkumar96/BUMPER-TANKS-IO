namespace BumperTanks.Gameplay
{
    public enum OverlayLoadStatus
    {
        NotStarted,
        Loading,
        Loaded,
        Empty,
        Failed
    }

    /// <summary>Tracks loading, empty, and failure states for asynchronously populated overlays.</summary>
    public sealed class OverlayLoadState
    {
        public OverlayLoadStatus Status { get; private set; }
        public string Error { get; private set; }
        public bool IsLoading => Status == OverlayLoadStatus.Loading;
        public bool HasCompleted => Status == OverlayLoadStatus.Loaded || Status == OverlayLoadStatus.Empty;

        public void Begin()
        {
            Status = OverlayLoadStatus.Loading;
            Error = string.Empty;
        }

        public void Complete(bool isEmpty)
        {
            Status = isEmpty ? OverlayLoadStatus.Empty : OverlayLoadStatus.Loaded;
            Error = string.Empty;
        }

        public void Fail(string error)
        {
            Status = OverlayLoadStatus.Failed;
            Error = string.IsNullOrWhiteSpace(error) ? "Unable to load data." : error;
        }
    }
}
