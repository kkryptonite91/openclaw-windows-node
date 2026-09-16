using OpenClaw.Chat;
using OpenClawTray.Chat;
using System.Collections.Immutable;
using System.Text.Json.Nodes;

namespace OpenClaw.Tray.Tests;

public sealed class ChatTimelinePresentationTests
{
    [Fact]
    public void HistoryExecProjection_IsolatedFromBusinessTimelineAndStable()
    {
        var args = new JsonObject { ["query"] = "test" };
        var correlationIds = ImmutableHashSet.Create("call-1");
        IReadOnlyList<ChatTimelineItem> source =
        [
            new ChatTimelineItem(
                "e1",
                ChatTimelineItemKind.ToolCall,
                "test",
                ToolName: "exec",
                ToolResult: ChatToolCallStatus.Success,
                ToolOutput: "OK",
                IntentSummary: "test",
                ToolArgs: args,
                ToolCallId: "call-1",
                ToolIdentityStrength: ChatToolIdentityStrength.Explicit,
                ToolCorrelationIds: correlationIds),
        ];
        var metadata = new Dictionary<string, ChatEntryMetadata>
        {
            ["e1"] = new(null, null, IsHistoryReplay: true),
        };
        var cache = new ChatHistoryReplayPresentationCache();

        var first = cache.Project(source, metadata, historyRevision: 7);
        var second = cache.Project(source, metadata, historyRevision: 7);

        Assert.Equal("exec", source[0].ToolName);
        Assert.NotSame(source, first);
        Assert.Same(first, second);
        var presentation = Assert.Single(first);
        Assert.Equal("Command", presentation.ToolName);
        Assert.NotSame(source[0], presentation);
        Assert.Same(presentation, second[0]);
        Assert.Equal(source[0].Id, presentation.Id);
        Assert.Equal(source[0].Text, presentation.Text);
        Assert.Same(args, presentation.ToolArgs);
        Assert.Equal(source[0].ToolCallId, presentation.ToolCallId);
        Assert.Same(correlationIds, presentation.ToolCorrelationIds);
        Assert.Equal(source[0].ToolIdentityStrength, presentation.ToolIdentityStrength);
        Assert.Equal(source[0].ToolResult, presentation.ToolResult);
        Assert.Equal(source[0].ToolOutput, presentation.ToolOutput);
        Assert.Equal(source[0].ToolRunId, presentation.ToolRunId);

        var afterRevisionChange = cache.Project(source, metadata, historyRevision: 8);
        Assert.NotSame(first, afterRevisionChange);
        Assert.NotSame(presentation, afterRevisionChange[0]);

        var replacementSource = source.ToArray();
        var afterSourceChange = cache.Project(replacementSource, metadata, historyRevision: 8);
        Assert.NotSame(afterRevisionChange, afterSourceChange);
        Assert.NotSame(afterRevisionChange[0], afterSourceChange[0]);
    }

    [Fact]
    public void HistoryMemorySearchAndLiveExec_KeepBusinessIdentityAndReference()
    {
        var cache = new ChatHistoryReplayPresentationCache();
        IReadOnlyList<ChatTimelineItem> historyMemorySearch =
        [
            new("e1", ChatTimelineItemKind.ToolCall, "test", ToolName: "memory_search"),
        ];
        IReadOnlyList<ChatTimelineItem> liveExec =
        [
            new("e2", ChatTimelineItemKind.ToolCall, "test", ToolName: "exec"),
        ];
        var historyMetadata = new Dictionary<string, ChatEntryMetadata>
        {
            ["e1"] = new(null, null, IsHistoryReplay: true),
        };
        var liveMetadata = new Dictionary<string, ChatEntryMetadata>
        {
            ["e2"] = new(null, null),
        };

        var projectedHistoryMemorySearch = cache.Project(
            historyMemorySearch,
            historyMetadata,
            historyRevision: 1);
        var projectedLiveExec = cache.Project(
            liveExec,
            liveMetadata,
            historyRevision: 2);

        Assert.Same(historyMemorySearch, projectedHistoryMemorySearch);
        Assert.Same(historyMemorySearch[0], projectedHistoryMemorySearch[0]);
        Assert.Equal("memory_search", projectedHistoryMemorySearch[0].ToolName);
        Assert.Same(liveExec, projectedLiveExec);
        Assert.Same(liveExec[0], projectedLiveExec[0]);
        Assert.Equal("exec", projectedLiveExec[0].ToolName);
    }

    [Fact]
    public void HistoryExecPresentation_SourceContractPreservesBusinessIdentity()
    {
        var loader = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Chat",
            "ChatHistoryLoader.cs"));
        var reducer = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Chat",
            "ChatTimelineReducer.cs"));
        var root = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Chat",
            "OpenClawReactorChatRoot.cs"));

        Assert.Contains("IsHistoryReplay: true", loader);
        Assert.DoesNotContain("memory_search", loader);
        Assert.DoesNotContain("\"exec\"", reducer);
        Assert.Contains("new ChatHistoryReplayPresentationCache()", root);
        Assert.Contains("presentationEntries,", root);
    }

    [Fact]
    public void ReactorTimeline_UsesNonSelectableItemsViewContainersAndAnnotatedScrollBar()
    {
        var timeline = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Chat",
            "ReactorChatTimeline.cs"));

        Assert.Contains("ItemsView(", timeline);
        Assert.Contains("ItemContainer(", timeline);
        Assert.Contains("static row => row.Key", timeline);
        Assert.Contains(".WithKey(row.Key)", timeline);
        Assert.Contains("SelectionMode = ItemsViewSelectionMode.None", timeline);
        Assert.Contains("IsItemInvokedEnabled = false", timeline);
        Assert.Contains("itemContainer.IsSelected = false", timeline);
        Assert.Contains("ItemContainerPointerOverBackground", timeline);
        Assert.Contains("ItemContainerPressedBackground", timeline);
        Assert.Contains("ItemContainerSelectedBackground", timeline);
        Assert.Contains("ItemContainerSelectedPointerOverBackground", timeline);
        Assert.Contains("ItemContainerSelectedPressedBackground", timeline);
        Assert.Contains("ItemContainerSelectionVisualPointerOverBackground", timeline);
        Assert.Contains("AnnotatedScrollBar()", timeline);
        Assert.Contains(".BindVerticalScrollController(", timeline);
        Assert.Contains("annotatedScrollBarRef,", timeline);
        Assert.Contains("rows.Count - 1", timeline);
        Assert.Contains("rows.Count,", timeline);
        Assert.Contains("initialTailRequestKey", timeline);
        Assert.Contains("var displayedTailKey = rows.Count > 0 ? rows[^1].Key : null", timeline);
        Assert.DoesNotContain("ItemsRepeater(", timeline);
        Assert.DoesNotContain("ScrollView(", timeline);
    }

    [Fact]
    public void ReactorTimeline_UsesReactiveAnnotatedScrollBarControllerBinding()
    {
        var binding = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Chat",
            "ReactorItemsViewScrollController.cs"));

        Assert.Contains("context.BindFor(itemsView, element).Reference", binding);
        Assert.Contains("VerticalScrollController = scrollBar?.ScrollController", binding);
        Assert.DoesNotContain(".Current", binding);
    }

    [Fact]
    public void ReactorTimeline_UsesStableBottomAnchoringAndDiscreteTailRequests()
    {
        var binding = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Chat",
            "ReactorItemsViewScrollController.cs"));

        Assert.Contains("itemsView.Loaded += OnLoaded", binding);
        Assert.Contains("itemsView.LayoutUpdated += OnLayoutUpdated", binding);
        Assert.Contains("itemsView.DispatcherQueue.TryEnqueue", binding);
        Assert.Contains("itemsView.StartBringItemIntoView(", binding);
        Assert.Contains("itemsView.ScrollView?.Content as WinUIItemsRepeater", binding);
        Assert.Contains("repeater.TryGetElement(_attemptRequest.Index)", binding);
        Assert.Contains("repeater.GetElementIndex(candidate)", binding);
        Assert.Contains("repeater.ElementPrepared += OnElementPrepared", binding);
        Assert.Contains("repeater.ElementClearing += OnElementClearing", binding);
        Assert.Contains("repeater.ElementIndexChanged += OnElementIndexChanged", binding);
        Assert.Contains("_candidateHasPostCaptureLayout = true", binding);
        Assert.Contains("CapturedGeneration: capturedGeneration", binding);
        Assert.Contains("CurrentGeneration: _reconciliationGeneration", binding);
        Assert.Contains("RealizedTailNavigationGuard.CanExecute(guardState)", binding);
        Assert.Contains("VerticalAlignmentRatio = 1.0", binding);
        Assert.Contains("!string.Equals(_displayedTailKey, displayedTailKey, StringComparison.Ordinal)", binding);
        Assert.Contains("_following = IsNearBottom(sender)", binding);
        Assert.Contains("_scrollView.VerticalAnchorRatio = 1.0", binding);
        Assert.Contains("_scrollView.VerticalAnchorRatio = double.NaN", binding);
        Assert.Contains("_valid = TailNavigationPolicy.TryCapture", binding);
        Assert.Contains("_itemCount = itemCount", binding);
        Assert.Contains("itemsView.Unloaded += OnUnloaded", binding);
        Assert.Contains("itemsView.Loaded -= OnLoaded", binding);
        Assert.Contains("itemsView.LayoutUpdated -= OnLayoutUpdated", binding);
        Assert.Contains("_attemptRepeater.ElementPrepared -= OnElementPrepared", binding);
        Assert.Contains("_attemptRepeater.ElementClearing -= OnElementClearing", binding);
        Assert.Contains("_attemptRepeater.ElementIndexChanged -= OnElementIndexChanged", binding);
        Assert.DoesNotContain("ChangeView", binding);
        Assert.DoesNotContain("UpdateLayout", binding);
        Assert.DoesNotContain("TailSettle", binding);
        Assert.DoesNotContain("ScrollTo(", binding);
        Assert.DoesNotContain("ScrollCompleted", binding);
        Assert.DoesNotContain("DispatcherTimer", binding);
        Assert.DoesNotContain("TextLength != current.TextLength", binding);
        Assert.DoesNotContain("ReactorStreamingTailState", binding);
        Assert.DoesNotContain("QueueBottomAnchoringUpdate", binding);
        Assert.DoesNotContain("ApplyBottomAnchoring", binding);
        Assert.Equal(1, binding.Split("itemsView.DispatcherQueue.TryEnqueue", StringSplitOptions.None).Length - 1);

        var updateStart = binding.IndexOf("public UIElement Update(", StringComparison.Ordinal);
        var reconcile = binding.IndexOf("context.ReconcileChild(", updateStart, StringComparison.Ordinal);
        var generationAdvance = binding.IndexOf("positioner.ReconciliationCompleted();", reconcile, StringComparison.Ordinal);
        Assert.True(updateStart >= 0 && reconcile > updateStart && generationAdvance > reconcile);

        var finalGuard = binding.IndexOf("RealizedTailNavigationGuard.CanExecute(guardState)", StringComparison.Ordinal);
        var startBring = binding.IndexOf("itemsView.StartBringItemIntoView(", StringComparison.Ordinal);
        Assert.True(finalGuard >= 0 && startBring > finalGuard);

        var viewChangedStart = binding.IndexOf("private void OnViewChanged", StringComparison.Ordinal);
        var tailRequestStart = binding.IndexOf("private void AttachAttemptRepeater", viewChangedStart, StringComparison.Ordinal);
        var viewChanged = binding[viewChangedStart..tailRequestStart];
        Assert.DoesNotContain("VerticalAnchorRatio", viewChanged);
        Assert.DoesNotContain("StartBringItemIntoView", viewChanged);
    }

    [Fact]
    public void TailNavigationPolicy_RejectsQueuedRequestAfterValidTailBecomesEmpty()
    {
        Assert.True(TailNavigationPolicy.TryCapture(2, "assistant-3", 3, out var queued));
        Assert.False(TailNavigationPolicy.TryCapture(-1, null, 0, out _));

        Assert.False(TailNavigationPolicy.CanExecute(
            queued,
            currentTailIndex: -1,
            currentDisplayedTailKey: null,
            itemCount: 0));
    }

    [Fact]
    public void TailNavigationPolicy_AllowsNewRequestAfterEmptyTailBecomesValid()
    {
        Assert.False(TailNavigationPolicy.TryCapture(-1, null, 0, out _));
        Assert.True(TailNavigationPolicy.TryCapture(0, "assistant-1", 1, out var queued));

        Assert.True(TailNavigationPolicy.CanExecute(
            queued,
            currentTailIndex: 0,
            currentDisplayedTailKey: "assistant-1",
            itemCount: 1));
    }

    [Fact]
    public void TailNavigationPolicy_RejectsStaleIdentityAndOutOfRangeIndex()
    {
        Assert.True(TailNavigationPolicy.TryCapture(1, "assistant-2", 2, out var queued));

        Assert.False(TailNavigationPolicy.CanExecute(
            queued,
            currentTailIndex: 1,
            currentDisplayedTailKey: "assistant-3",
            itemCount: 2));
        Assert.False(TailNavigationPolicy.CanExecute(
            queued,
            currentTailIndex: 1,
            currentDisplayedTailKey: "assistant-2",
            itemCount: 1));
    }

    [Fact]
    public void RealizedTailGuard_RejectsUnrealizedTarget()
    {
        var state = ValidRealizedTailGuardState() with
        {
            TargetRealized = false,
            CurrentElementMatchesCandidate = false,
            CurrentElementIndex = -1,
        };

        Assert.False(RealizedTailNavigationGuard.CanExecute(state));
    }

    [Fact]
    public void RealizedTailGuard_AllowsValidTarget()
    {
        Assert.True(RealizedTailNavigationGuard.CanExecute(ValidRealizedTailGuardState()));
    }

    [Fact]
    public void RealizedTailGuard_RejectsChangedReconciliationGeneration()
    {
        var state = ValidRealizedTailGuardState() with { CurrentGeneration = 8 };

        Assert.False(RealizedTailNavigationGuard.CanExecute(state));
    }

    [Fact]
    public void RealizedTailGuard_RejectsStaleElementMapping()
    {
        var state = ValidRealizedTailGuardState();

        Assert.False(RealizedTailNavigationGuard.CanExecute(state with
        {
            CurrentElementMatchesCandidate = false,
        }));
        Assert.False(RealizedTailNavigationGuard.CanExecute(state with
        {
            CurrentElementIndex = 1,
        }));
    }

    [Fact]
    public void RealizedTailGuard_RejectsClearedOrReindexedCandidate()
    {
        var state = ValidRealizedTailGuardState();

        Assert.False(RealizedTailNavigationGuard.CanExecute(state with
        {
            CandidateInvalidated = true,
        }));
        Assert.False(RealizedTailNavigationGuard.CanExecute(state with
        {
            CurrentElementIndex = state.Request.Index - 1,
        }));
    }

    [Fact]
    public void RealizedTailGuard_RejectsPreparedCandidateBeforePostCaptureLayout()
    {
        var state = ValidRealizedTailGuardState() with { HasPostCaptureLayout = false };

        Assert.False(RealizedTailNavigationGuard.CanExecute(state));
        Assert.True(RealizedTailNavigationGuard.CanExecute(state with
        {
            HasPostCaptureLayout = true,
        }));
    }

    [Fact]
    public void RealizedTailGuard_RejectsInvalidGeometry()
    {
        var state = ValidRealizedTailGuardState();
        var invalidValues = new[]
        {
            double.NaN,
            double.PositiveInfinity,
            double.NegativeInfinity,
            0d,
            -1d,
        };

        foreach (var invalid in invalidValues)
        {
            Assert.False(RealizedTailNavigationGuard.CanExecute(state with { ActualWidth = invalid }));
            Assert.False(RealizedTailNavigationGuard.CanExecute(state with { ActualHeight = invalid }));
        }

        Assert.True(RealizedTailNavigationGuard.CanExecute(state));
    }

    [Fact]
    public void RealizedTailGuard_ConsumesAnAttemptAtMostOnce()
    {
        var state = ValidRealizedTailGuardState();

        Assert.True(RealizedTailNavigationGuard.CanExecute(state));
        Assert.False(RealizedTailNavigationGuard.CanExecute(state with { AttemptCompleted = true }));
    }

    [Fact]
    public void RealizedTailGuard_PreservesExistingStaleRequestGuards()
    {
        var state = ValidRealizedTailGuardState();

        Assert.False(RealizedTailNavigationGuard.CanExecute(state with { CurrentVersion = 12 }));
        Assert.False(RealizedTailNavigationGuard.CanExecute(state with { Following = false }));
        Assert.False(RealizedTailNavigationGuard.CanExecute(state with { CurrentTailIndex = 1 }));
        Assert.False(RealizedTailNavigationGuard.CanExecute(state with
        {
            CurrentDisplayedTailKey = "assistant-2",
        }));
    }

    [Fact]
    public void ReactorTimeline_CleansUpRealizationHandlersOnEveryTerminalPath()
    {
        var binding = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Chat",
            "ReactorItemsViewScrollController.cs"));

        Assert.Contains("private void DetachTailAttemptHandlers()", binding);
        Assert.Contains("_attemptRepeater.ElementPrepared -= OnElementPrepared", binding);
        Assert.Contains("_attemptRepeater.ElementClearing -= OnElementClearing", binding);
        Assert.Contains("_attemptRepeater.ElementIndexChanged -= OnElementIndexChanged", binding);
        Assert.Contains("private void OnUnloaded", binding);
        Assert.Contains("public void Dispose()", binding);
        Assert.Contains("public void ReconciliationCompleted()", binding);

        var generationBody = SliceBetween(
            binding,
            "public void ReconciliationCompleted()",
            "public void Request(");
        var unloadBody = SliceBetween(
            binding,
            "private void OnUnloaded",
            "private void DetachLayout");
        var disposeBody = binding[binding.IndexOf("public void Dispose()", StringComparison.Ordinal)..];
        var clearingBody = SliceBetween(
            binding,
            "private void OnElementClearing",
            "private void OnElementIndexChanged");
        var reindexBody = SliceBetween(
            binding,
            "private void OnElementIndexChanged",
            "private bool TryCaptureCandidate");

        Assert.Contains("CancelTailAttempt();", generationBody);
        Assert.Contains("CancelTailAttempt();", unloadBody);
        Assert.Contains("CancelTailAttempt();", disposeBody);
        Assert.Contains("FailTailAttempt();", clearingBody);
        Assert.Contains("FailTailAttempt();", reindexBody);
    }

    private static RealizedTailNavigationGuardState ValidRealizedTailGuardState() => new(
        IsDisposed: false,
        ItemsViewLoaded: true,
        ScrollViewLoaded: true,
        Following: true,
        CapturedVersion: 11,
        CurrentVersion: 11,
        CapturedGeneration: 7,
        CurrentGeneration: 7,
        Request: new TailNavigationRequest(2, "assistant-3"),
        CurrentTailIndex: 2,
        CurrentDisplayedTailKey: "assistant-3",
        ItemCount: 3,
        TargetRealized: true,
        CurrentElementMatchesCandidate: true,
        CurrentElementIndex: 2,
        IsExpectedElementType: true,
        ElementLoaded: true,
        ActualWidth: 640,
        ActualHeight: 48,
        HasPostCaptureLayout: true,
        CandidateInvalidated: false,
        AttemptCompleted: false);

    private static string SliceBetween(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    [Fact]
    public void TailNavigationQueue_OldCallbackConsumesNewestMatchingGeneration()
    {
        var queue = new TailNavigationQueue();
        var first = new TailNavigationRequest(1, "assistant-2");
        var replacement = new TailNavigationRequest(0, "assistant-1");

        Assert.True(queue.Enqueue(version: 1, first));
        queue.Clear();
        Assert.False(queue.Enqueue(version: 2, replacement));

        Assert.True(queue.TryDequeue(currentVersion: 2, out var dequeued));
        Assert.Equal(replacement, dequeued);
        Assert.False(queue.IsScheduled);
    }

    [Fact]
    public void TailNavigationQueue_RejectsPendingRequestFromStaleGeneration()
    {
        var queue = new TailNavigationQueue();
        Assert.True(queue.Enqueue(
            version: 1,
            new TailNavigationRequest(1, "assistant-2")));

        Assert.False(queue.TryDequeue(currentVersion: 2, out _));
        Assert.False(queue.IsScheduled);
    }

    [Fact]
    public void ReactorTimeline_RequeuesOnlyForCompletedHistoryReplacement()
    {
        var provider = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Chat",
            "OpenClawChatDataProvider.cs"));
        var state = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Chat",
            "ChatConversationState.cs"));
        var historyState = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Chat",
            "ChatHistoryState.cs"));
        var projector = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Chat",
            "ChatSnapshotProjector.cs"));
        var root = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Chat",
            "OpenClawReactorChatRoot.cs"));
        var timeline = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Chat",
            "ReactorChatTimeline.cs"));

        Assert.Contains("_revisions[token.ThreadId]", historyState);
        Assert.Contains("new Dictionary<string, long>(_revisions)", historyState);
        Assert.Contains("_history.SnapshotRevisions()", state);
        Assert.Contains("_historyLoader.LoadAsync(", provider);
        Assert.Contains("HistoryRevisions: input.HistoryRevisions", projector);
        Assert.Contains("snapshot.HistoryRevisions", root);
        Assert.Contains("HistoryRevision: historyRevision", root);
        Assert.Contains("props.HistoryRevision", timeline);
        Assert.DoesNotContain("|{props.Mode}", timeline);
    }

    [Fact]
    public void ReactorComposer_OffsetsPickerChevronRightAndUp()
    {
        var composer = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Chat",
            "ReactorChatComposer.cs"));

        Assert.Contains(".Margin(2, 4, 0, 0)", composer);
    }

    [Fact]
    public void ReactorComposer_GatesClickableControlsUntilLayoutIsUsable()
    {
        var composer = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Chat",
            "ReactorChatComposer.cs"));

        Assert.Contains("internal static class ComposerAutomationVisibility", composer);
        Assert.Contains("control.IsHitTestVisible = false;", composer);
        Assert.Contains("control.IsLoaded", composer);
        Assert.Contains("control.ActualWidth > 0", composer);
        Assert.Contains("control.ActualHeight > 0", composer);
        Assert.Contains("AccessibilityView.Raw", composer);
        Assert.Contains("AccessibilityView.Control", composer);
        Assert.True(
            composer.Split("AccessibilityView.Raw", StringSplitOptions.None).Length - 1 >= 4);
        Assert.Contains(".AutomationId(\"ChatComposerInput\")", composer);
        Assert.Contains(".AutomationId(automationId)", composer);
        Assert.Contains("RaisePropertyChangedEvent(", composer);
        Assert.Contains("AutomationElementIdentifiers.IsOffscreenProperty", composer);
        Assert.Equal(
            4,
            composer.Split(
                "ComposerAutomationVisibility.Prepare(",
                StringSplitOptions.None).Length - 1);
        Assert.Contains("\"ChatComposerAttach\"", composer);
        Assert.Contains("\"ChatComposerSpeakerToggle\"", composer);
        Assert.Contains("\"ChatComposerSessionPicker\"", composer);
        Assert.Contains("\"ChatComposerModelPicker\"", composer);
        Assert.Contains("\"ChatComposerReasoningPicker\"", composer);
        Assert.Contains("\"ChatComposerVoice\"", composer);
        Assert.Contains("\"ChatComposerSettings\"", composer);
        Assert.Contains("\"ChatComposerPrimaryAction\"", composer);
    }

    [Fact]
    public void ReactorComposer_BoundsAndAnnouncesQueuedMessages()
    {
        var composer = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Chat",
            "ReactorChatComposer.cs"));

        Assert.Contains("ScrollView(VStack(4, queuedRows))", composer);
        Assert.Contains(".MaxHeight(props.IsCompact ? 144 : 220)", composer);
        Assert.Contains("AutomationLiveSetting.Polite", composer);
    }

    [Fact]
    public void ReactorComposer_ReattachesStableImagePasteHandlerAfterRemount()
    {
        var composer = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Chat",
            "ReactorChatComposer.cs"));

        const string callbackRef =
            "var controllerRef = UseRef(controller);";
        const string callbackAssignment =
            "controllerRef.Current = controller;";
        const string handlerRef =
            "var pasteHandler = UseRef<TextControlPasteEventHandler>(async (_, args) =>";
        const string mount =
            "textBox.Paste += pasteHandler.Current;";
        const string unmount =
            "textBox.Paste -= pasteHandler.Current;";

        var callbackRefIndex = composer.IndexOf(callbackRef, StringComparison.Ordinal);
        var callbackAssignmentIndex = composer.IndexOf(callbackAssignment, StringComparison.Ordinal);
        var handlerRefIndex = composer.IndexOf(handlerRef, StringComparison.Ordinal);
        var mountIndex = composer.IndexOf(mount, StringComparison.Ordinal);
        var unmountIndex = composer.IndexOf(unmount, StringComparison.Ordinal);

        Assert.True(callbackRefIndex >= 0);
        Assert.True(callbackAssignmentIndex > callbackRefIndex);
        Assert.True(handlerRefIndex > callbackAssignmentIndex);
        Assert.True(mountIndex > handlerRefIndex);
        Assert.True(unmountIndex > mountIndex);
        Assert.Equal(1, composer.Split(handlerRef, StringSplitOptions.None).Length - 1);
        var pasteHandlerBody = composer[handlerRefIndex..mountIndex];
        Assert.Contains(
            "if (GetBitmapClipboardContent() is not { } clipboardContent)",
            pasteHandlerBody);
        Assert.DoesNotContain(
            "Windows.ApplicationModel.DataTransfer.Clipboard.GetContent()",
            pasteHandlerBody);
        Assert.Contains(
            "await controllerRef.Current.PasteImageAsync(clipboardContent);",
            composer);
        Assert.DoesNotContain("TryReadImageFromClipboardAsync", composer);
        Assert.DoesNotContain("pasteHooked", composer);
    }

    [Fact]
    public void ReactorComposer_UsesBitmapOnlyContextMenuThatReentersStablePastePath()
    {
        var composer = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Chat",
            "ReactorChatComposer.cs"));

        Assert.Contains("textBox.ContextFlyout = CreateComposerContextFlyout(", composer);
        Assert.Contains("textBox.ContextFlyout = null;", composer);
        Assert.DoesNotContain("ContextRequested", composer);
        Assert.Contains("StandardUICommandKind.Undo", composer);
        Assert.Contains("StandardUICommandKind.Redo", composer);
        Assert.Contains("StandardUICommandKind.Cut", composer);
        Assert.Contains("StandardUICommandKind.Copy", composer);
        Assert.Contains("StandardUICommandKind.Paste", composer);
        Assert.Contains("StandardUICommandKind.SelectAll", composer);
        Assert.Contains("\"ChatComposerPasteMenuItem\"", composer);
        Assert.Contains("var menu = new MenuFlyout();", composer);
        Assert.Contains("menu.Items.Add(undoItem);", composer);
        Assert.Contains("menu.Items.Add(redoItem);", composer);
        Assert.Contains("menu.Items.Add(cutItem);", composer);
        Assert.Contains("menu.Items.Add(copyItem);", composer);
        Assert.Contains("menu.Items.Add(pasteItem);", composer);
        Assert.Contains("menu.Items.Add(selectAllItem);", composer);
        Assert.Contains("menu.Opening += (_, _) =>", composer);
        Assert.Contains("var state = ChatComposerContextMenuState.Project(", composer);
        Assert.Contains("pasteItem.Visibility = ToVisibility(state.ShowPaste);", composer);
        Assert.Contains("textBox.PasteFromClipboard();", composer);
        Assert.DoesNotContain("TextCommandBarFlyout", composer);

        var menuStart = composer.IndexOf(
            "private static MenuFlyout CreateComposerContextFlyout(",
            StringComparison.Ordinal);
        var standardItemStart = composer.IndexOf(
            "private static MenuFlyoutItem CreateStandardMenuItem(",
            StringComparison.Ordinal);
        var menuFactory = composer[menuStart..standardItemStart];

        Assert.DoesNotContain("TryReadImageFromClipboardAsync", menuFactory);
        Assert.Contains("GetBitmapClipboardContent()", menuFactory);
        Assert.Contains(
            "_ = getController().PasteImageAsync(clipboardContent);",
            menuFactory);
        Assert.Equal(
            1,
            composer.Split(
                "await controllerRef.Current.PasteImageAsync(clipboardContent);",
                StringSplitOptions.None).Length - 1);
        Assert.Contains("PasteTextFromClipboard(textBox);", menuFactory);
        Assert.Contains("private static void PasteTextFromClipboard(TextBox textBox)", composer);
        Assert.Contains("catch (System.Runtime.InteropServices.COMException ex)", composer);
        Assert.Contains("clipboard text paste failed", composer);
        Assert.DoesNotContain("ClipboardContainsBitmap", composer);
    }

    [Fact]
    public void ChatComposerContextMenuState_ProjectsNativeCommandVisibility()
    {
        Assert.Equal(
            new ChatComposerContextMenuState(
                ShowUndo: false,
                ShowRedo: false,
                ShowCut: false,
                ShowCopy: false,
                ShowPaste: false,
                ShowSelectAll: false,
                ShowEditSeparator: false,
                ShowSelectAllSeparator: false),
            ChatComposerContextMenuState.Project(
                canUndo: false,
                canRedo: false,
                hasSelection: false,
                canPaste: false,
                hasText: false));

        Assert.Equal(
            new ChatComposerContextMenuState(
                ShowUndo: true,
                ShowRedo: true,
                ShowCut: true,
                ShowCopy: true,
                ShowPaste: true,
                ShowSelectAll: true,
                ShowEditSeparator: true,
                ShowSelectAllSeparator: true),
            ChatComposerContextMenuState.Project(
                canUndo: true,
                canRedo: true,
                hasSelection: true,
                canPaste: true,
                hasText: true));

        var pasteOnly = ChatComposerContextMenuState.Project(
            canUndo: false,
            canRedo: false,
            hasSelection: false,
            canPaste: true,
            hasText: false);
        Assert.True(pasteOnly.ShowPaste);
        Assert.False(pasteOnly.ShowEditSeparator);
        Assert.False(pasteOnly.ShowSelectAllSeparator);

        var selectedText = ChatComposerContextMenuState.Project(
            canUndo: false,
            canRedo: false,
            hasSelection: true,
            canPaste: false,
            hasText: true);
        Assert.True(selectedText.ShowCut);
        Assert.True(selectedText.ShowCopy);
        Assert.True(selectedText.ShowSelectAll);
        Assert.True(selectedText.ShowSelectAllSeparator);
    }

    [Fact]
    public void ReactorComposer_UsesReactorThemeResourcesWithoutManualThemeObservation()
    {
        var composer = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Chat",
            "ReactorChatComposer.cs"));

        Assert.Contains("UseColorScheme()", composer);
        Assert.Contains(".Background(Theme.ControlFill)", composer);
        Assert.Contains(".BorderBrush(Theme.ControlStroke)", composer);
        Assert.Contains("Theme.Ref(\"AcrylicBackgroundFillColorDefaultBrush\")", composer);
        Assert.Contains("Theme.Ref(\"SurfaceStrokeColorFlyoutBrush\")", composer);
        Assert.Contains("Theme.Ref(\"SubtleFillColorTertiaryBrush\")", composer);
        Assert.Contains("colorScheme);", composer);
        Assert.Contains("CreateSlashPopupHost(BuildSlashPopup(", composer);

        Assert.DoesNotContain("AccessibilitySettings", composer);
        Assert.DoesNotContain("HighContrastChanged", composer);
        Assert.DoesNotContain("ConditionalWeakTable", composer);
        Assert.DoesNotContain("ApplyTheme(", composer);
        Assert.DoesNotContain("ResolveThemeBrush", composer);
        Assert.DoesNotContain("FindThemedResource", composer);
        Assert.DoesNotContain("SearchThemeDictionaries", composer);
        Assert.DoesNotContain("LookupResource", composer);
        Assert.DoesNotContain("Application.Current.Resources", composer);
    }

    [Fact]
    public void ReactorComposer_LocalizesSettingsTooltipInEveryLocale()
    {
        var stringsDirectory = Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Strings");

        foreach (var resourceFile in Directory.EnumerateFiles(
                     stringsDirectory,
                     "Resources.resw",
                     SearchOption.AllDirectories))
        {
            var resources = File.ReadAllText(resourceFile);
            Assert.Contains("Chat_Composer_Tooltip_Settings", resources);
        }
    }

    [Fact]
    public void ReactorRoot_SettlesWelcomeEligibilityBeforeShowingEmptyState()
    {
        var root = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Chat",
            "OpenClawReactorChatRoot.cs"));

        Assert.Contains("var welcomeEligible = isEmptyConversation", root);
        Assert.Contains("var welcomeEligibilityKey =", root);
        Assert.Contains("var welcomeEligibilityKeyRef = UseRef<string?>", root);
        Assert.Contains("var (settledWelcomeKey, setSettledWelcomeKey) = UseState<string?>", root);
        Assert.Contains("await Task.Delay(800)", root);
        Assert.Contains("welcomeEligibilityKeyRef.Current", root);
        Assert.Contains("settledWelcomeKey,", root);
        Assert.Contains("welcomeEligibilityKey,", root);
        Assert.Contains("var emptyConversationIsAuthoritative = welcomeEligibilityKey is not null", root);
        Assert.Contains("isEmptyConversation && !emptyConversationIsAuthoritative", root);
    }

    [Fact]
    public void ReactorTimeline_ProjectsActivityInSourceChronology()
    {
        var timeline = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Chat",
            "ReactorChatTimeline.cs"));

        Assert.Contains("var chronologicalEntries = props.Timeline.Entries;", timeline);
        Assert.Contains("ChatToolActivityPresentation.Project(", timeline);
        Assert.Contains("ChatTimelineAssistantRuns.Describe(chronologicalEntries)", timeline);
        Assert.DoesNotContain("OrderEntriesForPresentation", timeline);
        Assert.Contains("includeMetadata: row.IsAssistantRunEnd", timeline);
    }

    [Fact]
    public void ReactorTimeline_UsesCanonicalToolActivityKeyForStandaloneAndGroupedRows()
    {
        var timeline = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src", "OpenClaw.Tray.WinUI", "Chat", "ReactorChatTimeline.cs"));

        Assert.Contains("entry.Kind == ChatTimelineItemKind.ToolCall", timeline);
        Assert.Contains("ChatToolActivityPresentation.ActivityKey(", timeline);
        Assert.Contains("ReactorChatTimeline.RowKey(props.Timeline, entry)", timeline);
    }

    [Fact]
    public void ReactorTimeline_DelegatesToolAndActivityRenderingToFocusedOwner()
    {
        var timeline = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src", "OpenClaw.Tray.WinUI", "Chat", "ReactorChatTimeline.cs"));
        var renderer = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src", "OpenClaw.Tray.WinUI", "Chat", "ToolCallCardRenderer.cs"));

        Assert.Contains("ToolCallCardRenderer.BuildStandalone", timeline);
        Assert.Contains("ToolCallCardRenderer.BuildActivity", timeline);
        Assert.DoesNotContain("private static Element BuildTool", timeline);
        Assert.Contains("public static Element BuildStandalone", renderer);
        Assert.Contains("public static Element BuildActivity", renderer);
        Assert.Contains("FormatToolDisplayArgs(entry.ToolArgs)", renderer);
        Assert.Contains("private const int ToolDetailMaxChars = 4000;", renderer);
        Assert.Contains("\"Chat_Tool_InputSection\"", renderer);
        Assert.Contains("\"Chat_Tool_OutputLabel\"", renderer);
        Assert.Contains(".Padding(18, 8, 18, 10)", renderer);
        Assert.Contains("var body = RichTextBlock(content)", renderer);
        Assert.Contains(".MaxHeight(240)", renderer);
        Assert.Contains("text.IsTextSelectionEnabled = true", renderer);
        Assert.DoesNotContain("var stateText =", renderer);
        Assert.DoesNotContain("var glyph =", renderer);
        Assert.Contains(".AutomationId(", renderer);
        Assert.Contains("ChatToolActivity_", renderer);
        Assert.Contains("ChatToolCall_", renderer);
        Assert.Contains("internal sealed class ToolActivityCard : Component<ToolActivityCardProps>", renderer);
        Assert.Contains("Element details = isExpanded", renderer);
        Assert.Contains("? VStack(", renderer);
        Assert.Contains(".MinHeight(28)", renderer);
        Assert.Contains(".FontSize(12)", renderer);
        Assert.Contains(".BorderThickness(isNested ? 0 : 1)", renderer);
        Assert.Contains(".Margin(isNested ? 0 : 68, isNested ? 0 : 4, isNested ? 0 : 40, isNested ? 0 : 4)", renderer);
        Assert.Contains("? \"SubtleFillColorTransparentBrush\"", renderer);
        Assert.Contains(": Empty();", renderer);
        Assert.DoesNotContain("activity.Tools.Select(BuildStandalone)", renderer);
    }

    [Fact]
    public void ReactorTimeline_RendersStructuredCompactionCard()
    {
        var timeline = File.ReadAllText(Path.Combine(
            TestRepositoryPaths.GetRepositoryRoot(),
            "src",
            "OpenClaw.Tray.WinUI",
            "Chat",
            "ReactorChatTimeline.cs"));

        Assert.Contains("ChatTimelineItemKind.Status => BuildStatus(row, entry)", timeline);
        Assert.Contains("ChatCompactionPresenter.TryCreateForEntry(", timeline);
        Assert.Contains("Chat_Compaction_Title", timeline);
        Assert.Contains("Chat_Compaction_FallbackDetail", timeline);
        Assert.Contains("Chat_Compaction_OpenCheckpoints", timeline);
        Assert.Contains("row.Props.OnOpenCheckpoints!(sessionKey!)", timeline);
        Assert.Contains(".BorderThickness(1)", timeline);
        Assert.DoesNotContain("ReactorChatComposer.IsHighContrast", timeline);
        Assert.Contains(".AutomationName(presentation.AutomationName)", timeline);
    }
}
