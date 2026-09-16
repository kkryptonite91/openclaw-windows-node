using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using System.Runtime.CompilerServices;
using WinUIAnnotatedScrollBar = Microsoft.UI.Xaml.Controls.AnnotatedScrollBar;
using WinUIItemContainer = Microsoft.UI.Xaml.Controls.ItemContainer;
using WinUIItemsRepeater = Microsoft.UI.Xaml.Controls.ItemsRepeater;
using WinUIItemsRepeaterElementClearingEventArgs = Microsoft.UI.Xaml.Controls.ItemsRepeaterElementClearingEventArgs;
using WinUIItemsRepeaterElementIndexChangedEventArgs = Microsoft.UI.Xaml.Controls.ItemsRepeaterElementIndexChangedEventArgs;
using WinUIItemsRepeaterElementPreparedEventArgs = Microsoft.UI.Xaml.Controls.ItemsRepeaterElementPreparedEventArgs;
using WinUIItemsView = Microsoft.UI.Xaml.Controls.ItemsView;
using WinUIScrollView = Microsoft.UI.Xaml.Controls.ScrollView;

namespace OpenClawTray.Chat;

file sealed record ItemsViewVerticalScrollControllerElement(
    Element Child,
    ElementRef<WinUIAnnotatedScrollBar> ScrollBarRef,
    int InitialTailIndex,
    int ItemCount,
    string InitialTailRequestKey,
    string? DisplayedTailKey) : Element
{
    static ItemsViewVerticalScrollControllerElement() =>
        ControlRegistry.RegisterDecorator<ItemsViewVerticalScrollControllerElement>(
            static () => new ItemsViewVerticalScrollControllerHandler());
}

file sealed class ItemsViewVerticalScrollControllerHandler
    : IDecoratorElementHandler<ItemsViewVerticalScrollControllerElement>
{
    private static readonly ConditionalWeakTable<WinUIItemsView, InitialTailPositioner> Positioners = new();

    public UIElement Mount(MountContext context, ItemsViewVerticalScrollControllerElement element)
    {
        var control = context.MountChild(element.Child);
        if (control is not WinUIItemsView itemsView)
            throw new InvalidOperationException("ItemsView scroll controller binding requires an ItemsView child.");

        context.BindFor(itemsView, element).Reference(
            get: static value => value.ScrollBarRef,
            set: static (value, scrollBar) =>
                ((WinUIItemsView)value).VerticalScrollController = scrollBar?.ScrollController);
        var positioner = new InitialTailPositioner(itemsView);
        Positioners.Add(itemsView, positioner);
        positioner.ReconciliationCompleted();
        positioner.Request(
            element.InitialTailIndex,
            element.ItemCount,
            element.InitialTailRequestKey,
            element.DisplayedTailKey);
        return itemsView;
    }

    public UIElement Update(
        UpdateContext context,
        ItemsViewVerticalScrollControllerElement oldElement,
        ItemsViewVerticalScrollControllerElement newElement,
        UIElement control)
    {
        var updated = context.ReconcileChild(oldElement.Child, newElement.Child, control);
        if (updated is not WinUIItemsView itemsView)
            throw new InvalidOperationException("ItemsView scroll controller binding requires an ItemsView child.");
        if (!Positioners.TryGetValue(itemsView, out var positioner))
            throw new InvalidOperationException("ItemsView tail positioner is unavailable during reconciliation.");

        positioner.ReconciliationCompleted();
        if (!string.Equals(oldElement.InitialTailRequestKey, newElement.InitialTailRequestKey, StringComparison.Ordinal))
            positioner.Request(
                newElement.InitialTailIndex,
                newElement.ItemCount,
                newElement.InitialTailRequestKey,
                newElement.DisplayedTailKey);
        else
            positioner.UpdateTail(
                newElement.InitialTailIndex,
                newElement.ItemCount,
                newElement.DisplayedTailKey);
        return itemsView;
    }

    public V1UnmountDisposition Unmount(UnmountContext context, ItemsViewVerticalScrollControllerElement? element, UIElement control)
    {
        if (control is WinUIItemsView itemsView && Positioners.TryGetValue(itemsView, out var positioner))
        {
            Positioners.Remove(itemsView);
            positioner.Dispose();
        }
        return V1UnmountDisposition.ContinueDefaultTraversal;
    }
}

file sealed class InitialTailPositioner : IDisposable
{
    private const double FollowThreshold = 60;

    private enum TailLayoutStage
    {
        None,
        InitialProbe,
        PostCapture,
    }

    private readonly WinUIItemsView itemsView;
    private string? _requestKey;
    private string? _displayedTailKey;
    private int _tailIndex;
    private int _itemCount;
    private int _version;
    private int _reconciliationGeneration;
    private bool _valid;
    private bool _needsTailNavigation;
    private bool _awaitingLayout;
    private TailLayoutStage _layoutStage;
    private WinUIScrollView? _awaitingScrollView;
    private WinUIScrollView? _scrollView;
    private WinUIItemsRepeater? _attemptRepeater;
    private WinUIItemContainer? _candidate;
    private TailNavigationRequest _attemptRequest;
    private int _attemptVersion;
    private int _attemptGeneration;
    private bool _attemptActive;
    private bool _candidateHasPostCaptureLayout;
    private bool _candidateInvalidated;
    private bool _attemptCompleted;
    private bool _finalCallbackQueued;
    private bool _following;
    private bool _disposed;

    public InitialTailPositioner(WinUIItemsView itemsView)
    {
        this.itemsView = itemsView;
        itemsView.Loaded += OnLoaded;
        itemsView.Unloaded += OnUnloaded;
    }

    public void ReconciliationCompleted()
    {
        if (_disposed)
            return;

        // ReconcileChild can replace or recycle the native index-to-element mapping
        // without changing the logical tail request identity.
        _reconciliationGeneration++;
        CancelTailAttempt();
    }

    public void Request(int tailIndex, int itemCount, string requestKey, string? displayedTailKey)
    {
        if (_disposed || string.Equals(_requestKey, requestKey, StringComparison.Ordinal))
            return;

        _requestKey = requestKey;
        _version++;
        CancelTailAttempt();
        _tailIndex = tailIndex;
        _itemCount = itemCount;
        _displayedTailKey = displayedTailKey;
        _following = true;
        _valid = TailNavigationPolicy.TryCapture(tailIndex, displayedTailKey, itemCount, out var request);
        _needsTailNavigation = _valid;
        if (!_valid)
            return;

        if (itemsView.IsLoaded)
            BeginTailAttempt(request);
    }

    public void UpdateTail(int tailIndex, int itemCount, string? displayedTailKey)
    {
        var changed = !string.Equals(_displayedTailKey, displayedTailKey, StringComparison.Ordinal);
        _tailIndex = tailIndex;
        _itemCount = itemCount;
        _displayedTailKey = displayedTailKey;
        _valid = TailNavigationPolicy.TryCapture(tailIndex, displayedTailKey, itemCount, out var request);
        if (!_valid)
        {
            _needsTailNavigation = false;
            CancelTailAttempt();
            return;
        }

        if (changed && _following)
            _needsTailNavigation = true;

        if (_needsTailNavigation && _following && itemsView.IsLoaded)
            BeginTailAttempt(request);
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_valid
            && _following
            && _needsTailNavigation
            && TailNavigationPolicy.TryCapture(_tailIndex, _displayedTailKey, _itemCount, out var request))
        {
            BeginTailAttempt(request);
        }
    }

    private void BeginTailAttempt(TailNavigationRequest request)
    {
        if (_disposed
            || !_valid
            || !_needsTailNavigation
            || !_following
            || !itemsView.IsLoaded
            || _attemptActive)
        {
            return;
        }

        _attemptRequest = request;
        _attemptVersion = _version;
        _attemptGeneration = _reconciliationGeneration;
        _attemptActive = true;
        _candidate = null;
        _candidateHasPostCaptureLayout = false;
        _candidateInvalidated = false;
        _attemptCompleted = false;
        _finalCallbackQueued = false;

        if (TryGetItemsRepeater() is { } repeater)
            AttachAttemptRepeater(repeater);

        AwaitLayout(TailLayoutStage.InitialProbe);
    }

    private void AwaitLayout(TailLayoutStage stage)
    {
        if (!IsCurrentAttempt() || !itemsView.IsLoaded)
            return;

        if (itemsView.ScrollView is { IsLoaded: false } scrollView)
        {
            if (ReferenceEquals(_awaitingScrollView, scrollView))
                return;

            if (_awaitingScrollView is { } previousScrollView)
                previousScrollView.Loaded -= OnScrollViewLoaded;
            _awaitingScrollView = scrollView;
            scrollView.Loaded += OnScrollViewLoaded;
            return;
        }

        _layoutStage = stage;
        if (_awaitingLayout)
            return;

        _awaitingLayout = true;
        itemsView.LayoutUpdated += OnLayoutUpdated;
    }

    private void OnScrollViewLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is WinUIScrollView scrollView)
            scrollView.Loaded -= OnScrollViewLoaded;

        _awaitingScrollView = null;
        if (!IsCurrentAttempt())
            return;

        if (TryGetItemsRepeater() is { } repeater)
            AttachAttemptRepeater(repeater);
        AwaitLayout(_candidate is null ? TailLayoutStage.InitialProbe : TailLayoutStage.PostCapture);
    }

    private void OnLayoutUpdated(object? sender, object args)
    {
        var stage = _layoutStage;
        DetachLayout();
        if (!IsCurrentAttempt() || itemsView.ScrollView is not { IsLoaded: true })
        {
            FailTailAttempt();
            return;
        }

        var repeater = TryGetItemsRepeater();
        if (repeater is null)
        {
            FailTailAttempt();
            return;
        }

        if (_attemptRepeater is null)
            AttachAttemptRepeater(repeater);
        else if (!ReferenceEquals(_attemptRepeater, repeater))
        {
            FailTailAttempt();
            return;
        }

        if (stage == TailLayoutStage.InitialProbe)
        {
            var element = repeater.TryGetElement(_attemptRequest.Index);
            if (element is null)
            {
                // Stay event-driven. Only a matching ElementPrepared can advance
                // this generation when the target is not yet realized.
                return;
            }

            if (!TryCaptureCandidate(repeater, element))
            {
                FailTailAttempt();
                return;
            }

            // The layout event that exposed this element occurred before capture.
            // A later layout must confirm that the captured container survived.
            AwaitLayout(TailLayoutStage.PostCapture);
            return;
        }

        if (stage != TailLayoutStage.PostCapture || _candidate is null)
        {
            FailTailAttempt();
            return;
        }

        _candidateHasPostCaptureLayout = true;
        QueueFinalTailNavigation();
    }

    private void AttachScrollView()
    {
        var nextScrollView = itemsView.ScrollView;
        if (ReferenceEquals(_scrollView, nextScrollView))
            return;

        DetachScrollView();
        _scrollView = nextScrollView;
        if (_scrollView is not null)
        {
            _scrollView.VerticalAnchorRatio = 1.0;
            _scrollView.ViewChanged += OnViewChanged;
        }
    }

    private void OnViewChanged(WinUIScrollView sender, object args)
    {
        _following = IsNearBottom(sender);
        if (!_following)
        {
            _needsTailNavigation = false;
            CancelTailAttempt();
        }
    }

    private void AttachAttemptRepeater(WinUIItemsRepeater repeater)
    {
        if (ReferenceEquals(_attemptRepeater, repeater))
            return;

        DetachAttemptRepeater();
        _attemptRepeater = repeater;
        repeater.ElementPrepared += OnElementPrepared;
        repeater.ElementClearing += OnElementClearing;
        repeater.ElementIndexChanged += OnElementIndexChanged;
    }

    private void OnElementPrepared(
        WinUIItemsRepeater sender,
        WinUIItemsRepeaterElementPreparedEventArgs args)
    {
        if (!IsCurrentAttempt()
            || !ReferenceEquals(sender, _attemptRepeater)
            || args.Index != _attemptRequest.Index
            || _candidate is not null)
        {
            return;
        }

        if (!TryCaptureCandidate(sender, args.Element))
        {
            FailTailAttempt();
            return;
        }

        AwaitLayout(TailLayoutStage.PostCapture);
    }

    private void OnElementClearing(
        WinUIItemsRepeater sender,
        WinUIItemsRepeaterElementClearingEventArgs args)
    {
        if (ReferenceEquals(sender, _attemptRepeater)
            && ReferenceEquals(args.Element, _candidate))
        {
            _candidateInvalidated = true;
            FailTailAttempt();
        }
    }

    private void OnElementIndexChanged(
        WinUIItemsRepeater sender,
        WinUIItemsRepeaterElementIndexChangedEventArgs args)
    {
        if (ReferenceEquals(sender, _attemptRepeater)
            && ReferenceEquals(args.Element, _candidate)
            && args.NewIndex != _attemptRequest.Index)
        {
            _candidateInvalidated = true;
            FailTailAttempt();
        }
    }

    private bool TryCaptureCandidate(WinUIItemsRepeater repeater, UIElement element)
    {
        if (element is not WinUIItemContainer candidate
            || !ReferenceEquals(repeater.TryGetElement(_attemptRequest.Index), candidate)
            || repeater.GetElementIndex(candidate) != _attemptRequest.Index)
        {
            return false;
        }

        _candidate = candidate;
        _candidateHasPostCaptureLayout = false;
        return true;
    }

    private void QueueFinalTailNavigation()
    {
        if (!IsCurrentAttempt() || _candidate is null || _finalCallbackQueued)
            return;

        _finalCallbackQueued = true;
        var capturedVersion = _attemptVersion;
        var capturedGeneration = _attemptGeneration;
        var request = _attemptRequest;
        var candidate = _candidate;
        var repeater = _attemptRepeater;

        if (!itemsView.DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsSameAttempt(capturedVersion, capturedGeneration, request, candidate, repeater))
                return;

            AttachScrollView();
            var currentRepeater = TryGetItemsRepeater();
            var currentElement = currentRepeater?.TryGetElement(request.Index);
            var guardState = new RealizedTailNavigationGuardState(
                IsDisposed: _disposed,
                ItemsViewLoaded: itemsView.IsLoaded,
                ScrollViewLoaded: itemsView.ScrollView is { IsLoaded: true },
                Following: _following,
                CapturedVersion: capturedVersion,
                CurrentVersion: _version,
                CapturedGeneration: capturedGeneration,
                CurrentGeneration: _reconciliationGeneration,
                Request: request,
                CurrentTailIndex: _tailIndex,
                CurrentDisplayedTailKey: _displayedTailKey,
                ItemCount: _itemCount,
                TargetRealized: currentElement is not null,
                CurrentElementMatchesCandidate: ReferenceEquals(currentElement, candidate)
                    && ReferenceEquals(currentRepeater, repeater),
                CurrentElementIndex: currentRepeater?.GetElementIndex(candidate) ?? -1,
                IsExpectedElementType: true,
                ElementLoaded: candidate.IsLoaded,
                ActualWidth: candidate.ActualWidth,
                ActualHeight: candidate.ActualHeight,
                HasPostCaptureLayout: _candidateHasPostCaptureLayout,
                CandidateInvalidated: _candidateInvalidated,
                AttemptCompleted: _attemptCompleted);
            if (!RealizedTailNavigationGuard.CanExecute(guardState))
            {
                FailTailAttempt();
                return;
            }

            _attemptCompleted = true;
            _attemptActive = false;
            _needsTailNavigation = false;
            DetachTailAttemptHandlers();
            _following = true;
            itemsView.StartBringItemIntoView(request.Index, new BringIntoViewOptions
            {
                AnimationDesired = false,
                VerticalAlignmentRatio = 1.0,
            });
        }))
        {
            FailTailAttempt();
        }
    }

    private bool IsCurrentAttempt() =>
        _attemptActive
        && !_disposed
        && !_attemptCompleted
        && _attemptVersion == _version
        && _attemptGeneration == _reconciliationGeneration;

    private bool IsSameAttempt(
        int capturedVersion,
        int capturedGeneration,
        TailNavigationRequest request,
        WinUIItemContainer candidate,
        WinUIItemsRepeater? repeater) =>
        IsCurrentAttempt()
        && capturedVersion == _attemptVersion
        && capturedGeneration == _attemptGeneration
        && request == _attemptRequest
        && ReferenceEquals(candidate, _candidate)
        && ReferenceEquals(repeater, _attemptRepeater);

    private WinUIItemsRepeater? TryGetItemsRepeater() =>
        itemsView.ScrollView?.Content as WinUIItemsRepeater;

    private void FailTailAttempt()
    {
        _attemptActive = false;
        DetachTailAttemptHandlers();
    }

    private void CancelTailAttempt()
    {
        _attemptActive = false;
        DetachTailAttemptHandlers();
        _candidateInvalidated = false;
        _attemptCompleted = false;
        _finalCallbackQueued = false;
    }

    private void DetachTailAttemptHandlers()
    {
        DetachLayout();
        DetachAttemptRepeater();
        _candidate = null;
        _candidateHasPostCaptureLayout = false;
        _layoutStage = TailLayoutStage.None;
    }

    private void DetachAttemptRepeater()
    {
        if (_attemptRepeater is null)
            return;

        _attemptRepeater.ElementPrepared -= OnElementPrepared;
        _attemptRepeater.ElementClearing -= OnElementClearing;
        _attemptRepeater.ElementIndexChanged -= OnElementIndexChanged;
        _attemptRepeater = null;
    }

    private static bool IsNearBottom(WinUIScrollView scrollView) =>
        scrollView.ScrollableHeight - scrollView.VerticalOffset <= FollowThreshold;

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _version++;
        _needsTailNavigation = _valid && _following;
        CancelTailAttempt();
        DetachScrollView();
    }

    private void DetachLayout()
    {
        if (_awaitingScrollView is { } scrollView)
        {
            scrollView.Loaded -= OnScrollViewLoaded;
            _awaitingScrollView = null;
        }

        if (_awaitingLayout)
        {
            itemsView.LayoutUpdated -= OnLayoutUpdated;
            _awaitingLayout = false;
        }
    }

    private void DetachScrollView()
    {
        if (_scrollView is not null)
        {
            _scrollView.VerticalAnchorRatio = double.NaN;
            _scrollView.ViewChanged -= OnViewChanged;
        }

        _scrollView = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _version++;
        _needsTailNavigation = false;
        CancelTailAttempt();
        DetachScrollView();
        itemsView.Loaded -= OnLoaded;
        itemsView.Unloaded -= OnUnloaded;
    }
}

internal static class ItemsViewScrollControllerExtensions
{
    public static Element BindVerticalScrollController<T>(
        this ItemsViewElement<T> itemsView,
        ElementRef<WinUIAnnotatedScrollBar> scrollBarRef,
        int initialTailIndex,
        int itemCount,
        string initialTailRequestKey,
        string? displayedTailKey) =>
        new ItemsViewVerticalScrollControllerElement(
            itemsView,
            scrollBarRef,
            initialTailIndex,
            itemCount,
            initialTailRequestKey,
            displayedTailKey);
}
