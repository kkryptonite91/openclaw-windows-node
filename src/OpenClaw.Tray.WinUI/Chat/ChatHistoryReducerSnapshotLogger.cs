using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
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
///     <c>D2</c> variant keeps the same 8 calls per S4 boundary but the
///     seven extra logs become runtime-constructed 24-field joined strings
///     (allocation + string-formatting weight), with no Timeline access.
///     <c>D3</c> adds the Timeline Entries indexer walk plus per-entry
///     property reads (no JSON). <c>D4</c> is D3 plus <c>SafeJson</c> /
///     <c>JsonNode.ToJsonString()</c> serialization of non-null ToolArgs.
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
    private const int MaxArgsChars = 200;

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
    /// <c>ReactorChatTimeline.Render</c> calls <c>BuildRows</c>. Carries the
    /// cached <c>historyRevision</c> scalar plus a read-only walk of the
    /// <c>presentation</c> object. The <c>D3</c> variant, plus the <c>D4</c>
    /// ToolArgs serialization:
    ///   - reads <c>presentation.Entries.Count</c>,
    ///   - reads <c>presentation.TimelineGeneration</c>,
    ///   - reads <c>presentation.ShowToolCalls</c>,
    ///   - walks <c>presentation.Entries</c> via indexer (one full
    ///     <c>for</c> loop, no LINQ, no deferred enumeration),
    ///   - per entry reads ordinary properties:
    ///     <c>Kind</c>, <c>Id</c>, <c>ToolName</c>, <c>ToolCallId</c>,
    ///     <c>ToolIdentityStrength</c>, <c>ToolRunId</c>, <c>ToolOutput</c>,
    ///     <c>ToolResult</c>, <c>ToolArgs</c> (<c>SafeJson</c> /
    ///     <c>JsonNode.ToJsonString()</c> when non-null), <c>ToolCorrelationIds</c>
    ///     (<see cref="string.Join(string, IEnumerable{string})"/> only when
    ///     non-null),
    ///   - emits one <c>Logger.Debug</c> call per entry (matching
    ///     <c>1 + Entries.Count</c>; no hard-coded count).
    /// The <c>D4</c> variant is D3 plus <c>SafeJson</c> /
    /// <c>JsonNode.ToJsonString()</c> on non-null <c>ToolArgs</c>; no LINQ,
    /// no new logger, no sleep, no GC.Collect, no async. BuildRows body and
    /// call-site line position are unchanged.
    /// </summary>
    public static void LogS4BeforeBuildRows(
        long historyRevision,
        ChatTimelinePresentationContext presentation)
    {
        Append(StageS4, new[]
        {
            "history_revision=" + historyRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "build_rows_pending=true",
        });
        var entryCount = presentation.Entries.Count;
        var tlGen = presentation.TimelineGeneration;
        var showTools = presentation.ShowToolCalls;
        for (int i = 0; i < entryCount; i++)
        {
            var entry = presentation.Entries[i];
            var hrText = historyRevision.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var idxText = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var entryCountText = entryCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var tlGenText = tlGen.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var showToolsText = showTools ? "true" : "false";
            var kindText = entry.Kind.ToString();
            var idText = entry.Id ?? "null";
            var toolNameText = entry.ToolName ?? "null";
            var toolCallIdText = entry.ToolCallId ?? "null";
            var identityText = entry.ToolIdentityStrength.ToString();
            var toolRunIdText = entry.ToolRunId ?? "null";
            var toolOutputText = entry.ToolOutput ?? "null";
            var toolResultText = entry.ToolResult?.ToString() ?? "null";
            var toolArgsText = entry.ToolArgs is null
                ? "null"
                : Truncate(SafeJson(entry.ToolArgs), MaxArgsChars);
            string correlationText;
            if (entry.ToolCorrelationIds is null)
            {
                correlationText = toolCallIdText;
            }
            else
            {
                correlationText = string.Join(",", entry.ToolCorrelationIds);
            }
            var payload = new[]
            {
                "CHAT_PERTURB",
                "stage=S4d4",
                "source=ReactorChatTimeline.Render",
                "revision=" + hrText,
                "entries=" + entryCountText,
                "summary=idx=" + idxText + "|kind=" + kindText + "|id=" + idText,
                "toolName=" + toolNameText,
                "isHistoryReplay=null",
                "text=null",
                "toolArgs=" + toolArgsText,
                "toolCallId=" + toolCallIdText,
                "correlationIds=" + correlationText,
                "identityStrength=" + identityText,
                "toolResult=" + toolResultText,
                "toolOutput=" + toolOutputText,
                "toolRunId=" + toolRunIdText,
                "status=ok",
                "tlGen=" + tlGenText,
                "showTools=" + showToolsText,
                "k1=v",
                "k2=v",
                "k3=v",
                "k4=v",
            };
            OpenClawTray.Services.Logger.Debug(string.Join("|", payload));
        }
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

    /// <summary>
    /// Serialize a <see cref="JsonObject"/> tool-argument payload via
    /// <c>ToJsonString()</c>. Mirrors the old <c>33ca38a</c> full logger's
    /// <c>SafeJson</c>: the only exception protection is a catch that returns
    /// <c>"&lt;unprintable&gt;"</c>. This is the sole D4 behavior added on top
    /// of the D3 per-entry walk; the serialized text replaces the D3
    /// <c>toolArgs_present</c> boolean field.
    /// </summary>
    private static string SafeJson(JsonObject obj)
    {
        try { return obj.ToJsonString(); }
        catch { return "<unprintable>"; }
    }

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max)
            return value.Replace('\n', ' ').Replace('\r', ' ');
        return string.Concat(value.AsSpan(0, max).ToString(), "...").Replace('\n', ' ').Replace('\r', ' ');
    }
}