using System;
using System.Collections.Generic;
using System.Text;
using OpenClaw.Chat;
using OpenClawTray.Services;

namespace OpenClawTray.Chat;

/// <summary>
/// Minimal instrumentation logger used by the S1/S2 binary-search experiment.
///
/// S1: record a structured history <see cref="ChatEvent"/> immediately before
///     it is fed into <c>ChatTimelineReducer.Apply</c> inside
///     <c>ChatHistoryLoader.BuildPlan</c>'s replay loop.
///
/// S2: enumerate the final <see cref="ChatTimelineState"/> built by
///     <c>ChatHistoryLoader.BuildPlan</c> once, emit a single line per
///     <see cref="ChatTimelineItemKind.ToolCall"/> entry, then emit nothing
///     more for non-tool entries.
///
/// S3: emit a single summary record immediately before the chat-root
///     presentation projection executes.
///
/// S4: emit a single marker record immediately before
///     <c>ReactorChatTimeline.Render</c> calls <c>BuildRows</c>. Carries only
///     the cached <c>historyRevision</c> scalar; no enumeration, no payload
///     access. <c>D1</c> variant adds seven fixed payload-free
///     <see cref="OpenClawTray.Services.Logger.Debug(string)"/> calls so the
///     per-S4 total equals <c>1 + entries.Count</c> matching the count the
///     old <c>33ca38a</c> full logger produced for a 7-entry fixture.
///
/// Intentionally narrow scope:
///   - supports S1, S2, S3 and S4 only;
///   - no <see cref="System.Reflection"/> usage;
///   - no <c>RuntimeHelpers</c> usage;
///   - no reference-identity comparisons;
///   - no UI / presentation projection;
///   - does not copy or replace the timeline;
///   - never mutates any business data passed in.
///
/// Logging is delegated to <see cref="OpenClawTray.Services.Logger.Debug(string)"/>,
/// which queues writes onto a bounded channel; the call site never blocks on
/// disk I/O and never holds a process-wide lock.
/// </summary>
internal static class ChatHistoryReducerSnapshotLogger
{
    private const string Prefix = "CHAT_PERTURB";
    private const string StageS1 = "S1";
    private const string StageS2 = "S2";
    private const string StageS3 = "S3";
    private const string StageS4 = "S4";
    private const string NullToken = "<null>";
    private const string PresentToken = "<present>";

    /// <summary>
    /// Emit one <c>S1</c> record for a structured history <paramref name="evt"/>
    /// that is about to enter the reducer. Only the minimum fields required by
    /// the experiment are written; event-type discrimination uses C# pattern
    /// matching (no reflection, no <c>RuntimeHelpers</c>).
    /// </summary>
    public static void LogS1BeforeApply(ChatEvent evt)
    {
        string eventType = ClassifyEventType(evt);
        string toolName = NullToken;
        string toolCallId = NullToken;
        string toolArgs = NullToken;

        switch (evt)
        {
            case ChatToolStartEvent s:
                toolName = s.ToolName ?? NullToken;
                toolCallId = s.ToolCallId ?? NullToken;
                toolArgs = s.ToolArgs is null ? NullToken : PresentToken;
                break;
            case ChatToolPresentationEvent p:
                toolName = p.ToolName ?? NullToken;
                toolCallId = p.ChildToolCallId ?? NullToken;
                toolArgs = p.ToolArgs is null ? NullToken : PresentToken;
                break;
            case ChatToolOutputEvent o:
                toolCallId = o.ToolCallId ?? NullToken;
                break;
            case ChatToolErrorEvent er:
                toolCallId = er.ToolCallId ?? NullToken;
                break;
        }

        Append(StageS1, new[]
        {
            "event_type=" + eventType,
            "tool_name=" + toolName,
            "tool_call_id=" + toolCallId,
            "tool_args=" + toolArgs,
        });
    }

    /// <summary>
    /// Emit the <c>S2</c> summary header plus one record per tool entry in the
    /// final <paramref name="timeline"/>. Plain user / assistant entries are
    /// intentionally skipped. The timeline is enumerated exactly once and is
    /// never copied or projected to presentation.
    /// </summary>
    public static void LogS2FinalTimeline(ChatTimelineState timeline)
    {
        var entries = timeline.Entries;
        int count = entries.Count;

        Append(StageS2, new[] { "entries_count=" + count });

        for (int index = 0; index < count; index++)
        {
            var entry = entries[index];
            if (entry.Kind != ChatTimelineItemKind.ToolCall)
            {
                continue;
            }

            Append(StageS2, new[]
            {
                "entries_count=" + count,
                "index=" + index,
                "kind=" + entry.Kind,
                "id=" + (entry.Id ?? NullToken),
                "tool_name=" + (entry.ToolName ?? NullToken),
                "tool_call_id=" + (entry.ToolCallId ?? NullToken),
                "tool_args=" + (entry.ToolArgs is null ? NullToken : PresentToken),
                "tool_result=" + (entry.ToolResult?.ToString() ?? NullToken),
            });
        }
    }


    /// <summary>
    /// Emit one <c>S3</c> summary record immediately before the chat-root
    /// presentation projection executes. Carries only a minimal summary;
    /// never enumerates <paramref name="entries"/> and never references
    /// identity, runtime helpers, or the projected payload.
    /// </summary>
    public static void LogS3BeforePresentationProjection(
        long revision,
        int entriesCount,
        long timelineGeneration)
    {
        Append(StageS3, new[]
        {
            "revision=" + revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "entries_count=" + entriesCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "timeline_generation=" + timelineGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "presentation_projection_pending=true",
        });
    }

    /// <summary>
    /// Emit one <c>S4</c> marker record immediately before
    /// <c>ReactorChatTimeline.Render</c> calls <c>BuildRows</c>. Carries only
    /// the cached <c>historyRevision</c> scalar; does not read
    /// <c>props.Timeline.Entries</c>, does not enumerate, does not access the
    /// presentation payload. The intent is to add a single observer-effect
    /// call at the <c>ReactorChatTimeline</c> / <c>BuildRows</c> /
    /// <c>ItemsView</c> boundary without restoring any other instrumentation.
    ///
    /// This <c>D1</c> variant additionally issues seven fixed, payload-free
    /// <see cref="OpenClawTray.Services.Logger.Debug(string)"/> calls so the
    /// per-S4 total matches the count the old full <c>33ca38a</c> logger
    /// produced for a 7-entry fixture (1 outer + 7 per-entry logs). The seven
    /// extra calls use only string-literal content, never read the timeline,
    /// never touch <c>Entries</c>, never serialize JSON, and never allocate a
    /// string array. The only variable under test is the
    /// <c>Logger.Debug</c> / channel-enqueue call count.
    /// </summary>
    public static void LogS4BeforeBuildRows(long historyRevision)
    {
        Append(StageS4, new[]
        {
            "history_revision=" + historyRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "build_rows_pending=true",
        });
        OpenClawTray.Services.Logger.Debug("CHAT_PERTURB S4x1");
        OpenClawTray.Services.Logger.Debug("CHAT_PERTURB S4x2");
        OpenClawTray.Services.Logger.Debug("CHAT_PERTURB S4x3");
        OpenClawTray.Services.Logger.Debug("CHAT_PERTURB S4x4");
        OpenClawTray.Services.Logger.Debug("CHAT_PERTURB S4x5");
        OpenClawTray.Services.Logger.Debug("CHAT_PERTURB S4x6");
        OpenClawTray.Services.Logger.Debug("CHAT_PERTURB S4x7");
    }
    private static string ClassifyEventType(ChatEvent evt)
    {
        return evt switch
        {
            ChatUserMessageEvent => "ChatUserMessageEvent",
            ChatThinkingEvent => "ChatThinkingEvent",
            ChatReasoningEvent => "ChatReasoningEvent",
            ChatReasoningDeltaEvent => "ChatReasoningDeltaEvent",
            ChatReasoningEndEvent => "ChatReasoningEndEvent",
            ChatMessageEvent => "ChatMessageEvent",
            ChatMessageDeltaEvent => "ChatMessageDeltaEvent",
            ChatTurnEndEvent => "ChatTurnEndEvent",
            ChatIntentEvent => "ChatIntentEvent",
            ChatToolStartEvent => "ChatToolStartEvent",
            ChatToolPresentationEvent => "ChatToolPresentationEvent",
            ChatToolOutputEvent => "ChatToolOutputEvent",
            ChatToolErrorEvent => "ChatToolErrorEvent",
            ChatToolReplayResetEvent => "ChatToolReplayResetEvent",
            ChatContextChangedEvent => "ChatContextChangedEvent",
            ChatStatusEvent => "ChatStatusEvent",
            ChatErrorEvent => "ChatErrorEvent",
            ChatRestoredEvent => "ChatRestoredEvent",
            ChatPermissionRequestEvent => "ChatPermissionRequestEvent",
            ChatModelChangedEvent => "ChatModelChangedEvent",
            ChatRawEvent => "ChatRawEvent",
            _ => "ChatEvent",
        };
    }

    private static void Append(string stage, IReadOnlyList<string> fields)
    {
        var sb = new StringBuilder(128);
        sb.Append(Prefix).Append(' ').Append(stage);
        for (int i = 0; i < fields.Count; i++)
        {
            sb.Append(' ').Append(fields[i]);
        }
        Logger.Debug(sb.ToString());
    }
}