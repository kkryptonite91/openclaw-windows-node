using OpenClaw.Chat;

namespace OpenClawTray.Chat;

/// <summary>
/// Produces a stable, WinUI-facing view of history entries without changing
/// the business timeline that owns tool identity and correlation.
/// </summary>
internal sealed class ChatHistoryReplayPresentationCache
{
    private IReadOnlyList<ChatTimelineItem>? _sourceEntries;
    private long _historyRevision = long.MinValue;
    private IReadOnlyList<ChatTimelineItem>? _presentationEntries;

    internal IReadOnlyList<ChatTimelineItem> Project(
        IReadOnlyList<ChatTimelineItem> sourceEntries,
        IReadOnlyDictionary<string, ChatEntryMetadata>? metadata,
        long historyRevision)
    {
        if (ReferenceEquals(_sourceEntries, sourceEntries)
            && _historyRevision == historyRevision
            && _presentationEntries is not null)
        {
            return _presentationEntries;
        }

        ChatTimelineItem[]? presentationEntries = null;
        for (var index = 0; index < sourceEntries.Count; index++)
        {
            var entry = sourceEntries[index];
            if (!IsHistoryExec(entry, metadata))
                continue;

            presentationEntries ??= sourceEntries.ToArray();
            presentationEntries[index] = entry with { ToolName = "Command" };
        }

        _sourceEntries = sourceEntries;
        _historyRevision = historyRevision;
        _presentationEntries = presentationEntries ?? sourceEntries;
        return _presentationEntries;
    }

    private static bool IsHistoryExec(
        ChatTimelineItem entry,
        IReadOnlyDictionary<string, ChatEntryMetadata>? metadata) =>
        metadata?.TryGetValue(entry.Id, out var entryMetadata) == true
        && entryMetadata.IsHistoryReplay
        && string.Equals(entry.ToolName, "exec", StringComparison.Ordinal);
}
