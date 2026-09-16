namespace OpenClawTray.Chat;

internal readonly record struct TailNavigationRequest(int Index, string DisplayedTailKey);

internal static class TailNavigationPolicy
{
    public static bool TryCapture(
        int tailIndex,
        string? displayedTailKey,
        int itemCount,
        out TailNavigationRequest request)
    {
        if (tailIndex < 0 || tailIndex >= itemCount || string.IsNullOrEmpty(displayedTailKey))
        {
            request = default;
            return false;
        }

        request = new TailNavigationRequest(tailIndex, displayedTailKey);
        return true;
    }

    public static bool CanExecute(
        TailNavigationRequest request,
        int currentTailIndex,
        string? currentDisplayedTailKey,
        int itemCount) =>
        request.Index >= 0
        && request.Index < itemCount
        && request.Index == currentTailIndex
        && string.Equals(
            request.DisplayedTailKey,
            currentDisplayedTailKey,
            StringComparison.Ordinal);
}

internal readonly record struct RealizedTailNavigationGuardState(
    bool IsDisposed,
    bool ItemsViewLoaded,
    bool ScrollViewLoaded,
    bool Following,
    int CapturedVersion,
    int CurrentVersion,
    int CapturedGeneration,
    int CurrentGeneration,
    TailNavigationRequest Request,
    int CurrentTailIndex,
    string? CurrentDisplayedTailKey,
    int ItemCount,
    bool TargetRealized,
    bool CurrentElementMatchesCandidate,
    int CurrentElementIndex,
    bool IsExpectedElementType,
    bool ElementLoaded,
    double ActualWidth,
    double ActualHeight,
    bool HasPostCaptureLayout,
    bool CandidateInvalidated,
    bool AttemptCompleted);

internal static class RealizedTailNavigationGuard
{
    public static bool CanExecute(RealizedTailNavigationGuardState state) =>
        !state.IsDisposed
        && state.ItemsViewLoaded
        && state.ScrollViewLoaded
        && state.Following
        && state.CapturedVersion == state.CurrentVersion
        && state.CapturedGeneration == state.CurrentGeneration
        && TailNavigationPolicy.CanExecute(
            state.Request,
            state.CurrentTailIndex,
            state.CurrentDisplayedTailKey,
            state.ItemCount)
        && state.TargetRealized
        && state.CurrentElementMatchesCandidate
        && state.CurrentElementIndex == state.Request.Index
        && state.IsExpectedElementType
        && state.ElementLoaded
        && double.IsFinite(state.ActualWidth)
        && state.ActualWidth > 0
        && double.IsFinite(state.ActualHeight)
        && state.ActualHeight > 0
        && state.HasPostCaptureLayout
        && !state.CandidateInvalidated
        && !state.AttemptCompleted;
}

internal sealed class TailNavigationQueue
{
    private (int Version, TailNavigationRequest Request)? _pending;

    public bool Enqueue(int version, TailNavigationRequest request)
    {
        _pending = (version, request);
        if (IsScheduled)
            return false;

        IsScheduled = true;
        return true;
    }

    public bool TryDequeue(int currentVersion, out TailNavigationRequest request)
    {
        IsScheduled = false;
        var pending = _pending;
        _pending = null;
        if (pending is not { } value || value.Version != currentVersion)
        {
            request = default;
            return false;
        }

        request = value.Request;
        return true;
    }

    public void Clear() => _pending = null;

    public void SchedulingFailed()
    {
        IsScheduled = false;
        _pending = null;
    }

    public bool IsScheduled { get; private set; }
}
