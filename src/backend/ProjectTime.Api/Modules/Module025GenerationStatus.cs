using System.Text.Json;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

internal sealed record Module025GenerationEvent(string EventType, int Revision, JsonElement Evidence, DateTimeOffset CreatedAt);
internal sealed record Module025TimedGenerationProgress(Module025GenerationProgress Progress, DateTimeOffset RecordedAt);
internal sealed record Module025PhaseTimeline(string Phase, int Ordinal, string Status,
    DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, int? ElapsedSeconds, bool Resumed);

// Public status is a projection of committed events, never a guess based on a
// browser timer or provider latency. It contains no generated draft/checkpoints.
internal sealed class Module025GenerationStatus
{
    internal required string Stage { get; init; }
    internal required bool Terminal { get; init; }
    internal required bool Completed { get; init; }
    internal required bool PreviousGenerationCompleted { get; init; }
    internal required bool ScopeMatches { get; init; }
    internal required bool CanRetry { get; init; }
    internal required bool CanResume { get; init; }
    internal required string[] ResumablePhases { get; init; }
    internal required DateTimeOffset QueuedAt { get; init; }
    internal DateTimeOffset? StartedAt { get; init; }
    internal DateTimeOffset? CompletedAt { get; init; }
    internal required DateTimeOffset ServerNow { get; init; }
    internal required int ElapsedSeconds { get; init; }
    internal required Module025PhaseTimeline[] PhaseTimeline { get; init; }
    internal required Module025TimedGenerationProgress[] Progress { get; init; }
    internal Module025GenerationEvent? TerminalEvent { get; init; }

    internal static bool IsTerminal(Module025GenerationEvent item) => item.EventType is
        "ai_generation_completed" or "ai_generation_failed" or "ai_generation_obsolete";

    internal static Module025GenerationStatus Project(IReadOnlyList<Module025GenerationEvent> events,
        int currentRevision, bool editable, string currentSourceHash, DateTimeOffset now)
    {
        if (events.Count == 0) throw new ArgumentException("Generation events are required.", nameof(events));
        var queued = events.FirstOrDefault(item => item.EventType == "ai_generation_queued") ?? events[0];
        var terminalEvent = events.LastOrDefault(IsTerminal);
        var completed = terminalEvent?.EventType == "ai_generation_completed";
        var queuedHash = ReadString(queued.Evidence, "sourceHash");
        var knownHashes = events.Where(item => item.EventType == "ai_generation_progress")
            .Select(item => ReadString(item.Evidence, "sourceHash")).Where(hash => hash.Length > 0);
        var scopeMatches = (completed ? terminalEvent!.Revision : queued.Revision) == currentRevision
            && (completed || ((queuedHash.Length == 0 || queuedHash == currentSourceHash)
                && knownHashes.All(hash => hash == currentSourceHash)));
        var obsolete = terminalEvent?.EventType == "ai_generation_obsolete" || !scopeMatches;
        var terminal = terminalEvent is not null || obsolete;
        var progress = events.Where(item => item.EventType == "ai_generation_progress")
            .Select(ReadProgress).Where(item => item is not null).Select(item => item!).ToArray();
        var started = events.FirstOrDefault(item => item.EventType == "ai_generation_started")?.CreatedAt;
        var end = terminalEvent?.CreatedAt ?? (obsolete ? events[^1].CreatedAt : now);
        var current = progress.LastOrDefault()?.Progress;
        var stage = obsolete ? "obsolete" : completed ? "completed" : terminal ? "failed"
            : current?.Phase == "assembly" ? "assembly"
            : current is not null ? (IsRetry(current) ? "retry" : "phase")
            : started.HasValue ? "preparing" : "queued";
        var timeline = Module025GenerationEngine.Phases.Select((phase, index) =>
        {
            var phaseEvents = progress.Where(item => item.Progress.Phase == phase).ToArray();
            var phaseStart = phaseEvents.FirstOrDefault(item => item.Progress.Stage == "phase_started")?.RecordedAt;
            var saved = phaseEvents.LastOrDefault(item => item.Progress.Stage is "phase_completed" or "phase_resumed");
            var last = phaseEvents.LastOrDefault();
            var resumed = saved?.Progress.Stage == "phase_resumed";
            var phaseEnd = saved?.RecordedAt
                ?? (last?.Progress.Stage == "phase_failed" ? last.RecordedAt : null);
            var status = saved is not null ? (resumed ? "resumed" : "completed")
                : phaseStart is null ? "pending"
                : obsolete ? "interrupted"
                : terminal || last?.Progress.Stage == "phase_failed" ? "failed"
                : last is not null && IsRetry(last.Progress) ? "retrying" : "running";
            // Reused phases were generated in an earlier attempt. Do not present
            // a zero-cost reuse event as the time spent generating that phase.
            return new Module025PhaseTimeline(phase, index + 1, status, resumed ? null : phaseStart,
                saved is not null || (terminal && phaseStart.HasValue) ? phaseEnd ?? end : phaseEnd,
                resumed || phaseStart is null ? null : Seconds(phaseStart.Value, phaseEnd ?? end), resumed);
        }).ToArray();
        var compatibleProgress = events.Where(item => item.EventType == "ai_generation_progress").All(item =>
            ReadString(item.Evidence, "sourceHash") == currentSourceHash
            && ReadString(item.Evidence, "contractVersion") == Module025GenerationEngine.ContractVersion);
        var compatibleQueue = queuedHash.Length == 0 || (queuedHash == currentSourceHash
            && ReadString(queued.Evidence, "contractVersion") == Module025GenerationEngine.ContractVersion);
        var canRetry = terminal && !completed && !obsolete && scopeMatches && editable;
        var resumablePhases = canRetry && currentSourceHash.Length > 0 && compatibleQueue && compatibleProgress
            ? timeline.TakeWhile(item => item.Status is "completed" or "resumed").Select(item => item.Phase).ToArray()
            : Array.Empty<string>();
        return new Module025GenerationStatus
        {
            Stage = stage, Terminal = terminal, Completed = completed && !obsolete,
            PreviousGenerationCompleted = completed && obsolete,
            ScopeMatches = scopeMatches, CanRetry = canRetry,
            CanResume = resumablePhases.Length > 0, ResumablePhases = resumablePhases,
            QueuedAt = queued.CreatedAt, StartedAt = started, CompletedAt = terminal ? end : null,
            ServerNow = now, ElapsedSeconds = Seconds(queued.CreatedAt, end),
            PhaseTimeline = timeline, Progress = progress, TerminalEvent = terminalEvent
        };
    }

    private static Module025TimedGenerationProgress? ReadProgress(Module025GenerationEvent item)
    {
        if (!item.Evidence.TryGetProperty("progress", out var value) || value.ValueKind != JsonValueKind.Object) return null;
        try
        {
            var progress = value.Deserialize<Module025GenerationProgress>();
            return progress is null ? null : new(progress, item.CreatedAt);
        }
        catch (JsonException) { return null; }
    }

    private static bool IsRetry(Module025GenerationProgress value) => value.Attempt > 1
        && value.Stage is "provider_started" or "provider_finished" or "provider_completed";
    private static int Seconds(DateTimeOffset start, DateTimeOffset end) =>
        (int)Math.Clamp(Math.Ceiling((end - start).TotalSeconds), 0, int.MaxValue);
    private static string ReadString(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
