using System.Text.Json;
using ProjectTime.Api.Ai;
using ProjectTime.Api.Modules;

internal static class Module025GenerationStatusTests
{
    internal static void Run()
    {
        static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("ASSERTION_FAILED module025_status_" + label);
            Console.WriteLine("ASSERTION_PASSED module025_status_" + label);
        }
        var at = DateTimeOffset.Parse("2026-09-20T12:00:00Z");
        const string hash = "saved-scope-sha";
        var events = new List<Module025GenerationEvent>();
        void Add(string type, int seconds, object? evidence = null, int revision = 7) =>
            events.Add(new(type, revision, JsonSerializer.SerializeToElement(evidence ?? new { }), at.AddSeconds(seconds)));
        void Progress(string stage, string phase, int seconds, int attempt = 0, long providerMilliseconds = 0,
            string source = hash, string? contract = null) => Add("ai_generation_progress", seconds,
                new { sourceHash = source, contractVersion = contract ?? Module025GenerationEngine.ContractVersion,
                    progress = new Module025GenerationProgress(stage, phase, "provider", attempt, ElapsedMilliseconds: providerMilliseconds) });
        Module025GenerationStatus Read(int seconds = 180, int revision = 7, bool editable = true, string source = hash) =>
            Module025GenerationStatus.Project(events, revision, editable, source, at.AddSeconds(seconds));

        Add("ai_generation_queued", 0, new { sourceHash = hash, contractVersion = Module025GenerationEngine.ContractVersion });
        var queued = Read(20);
        Check(queued.Stage == "queued" && !queued.Terminal && queued.ElapsedSeconds == 20, "queue_clock_uses_durable_time");
        Check(queued.PhaseTimeline.Select(item => item.Phase).SequenceEqual(Module025GenerationEngine.Phases)
            && queued.PhaseTimeline.Select(item => item.Ordinal).SequenceEqual(Enumerable.Range(1, 5))
            && queued.PhaseTimeline.All(item => item.Status == "pending" && item.ElapsedSeconds is null), "five_ordered_pending_phases");
        Add("ai_generation_started", 25);
        Check(Read(30).Stage == "preparing" && Read(30).StartedAt == at.AddSeconds(25), "worker_preparation_separate_from_queue_and_phase");
        Progress("phase_started", "Plan", 30);
        Progress("provider_started", "Plan", 35, 1);
        Check(Read(80).PhaseTimeline[0].ElapsedSeconds == 50 && Read(80).Stage == "phase", "phase_clock_runs_between_events");
        Progress("provider_finished", "Plan", 90, 1, providerMilliseconds: 55_000);
        Progress("provider_started", "Plan", 95, 2);
        Check(Read(100).Stage == "retry" && Read(100).PhaseTimeline[0].Status == "retrying", "provider_fallback_visible_as_retry");
        Progress("provider_finished", "Plan", 105, 2, providerMilliseconds: 10_000);
        Progress("phase_completed", "Plan", 110);
        Progress("phase_started", "Design", 115);
        var working = Read(180);
        Check(working.PhaseTimeline[0].ElapsedSeconds == 80 && working.PhaseTimeline[1].ElapsedSeconds == 65,
            "completed_phase_freezes_and_active_phase_includes_all_provider_wall_time");
        Check(working.Progress.Last().RecordedAt == at.AddSeconds(115), "progress_exposes_persisted_event_time");
        Progress("phase_failed", "Design", 200);
        Add("ai_generation_failed", 205);
        var failed = Read(10_000);
        Check(failed.Stage == "failed" && failed.Terminal && failed.ElapsedSeconds == 205
            && failed.CompletedAt == at.AddSeconds(205), "failed_generation_clock_freezes");
        Check(failed.PhaseTimeline[1].Status == "failed" && failed.PhaseTimeline[1].ElapsedSeconds == 85
            && failed.PhaseTimeline[2].Status == "pending", "failed_phase_freezes_without_starting_later_phases");
        Check(failed.CanResume && failed.CanRetry && failed.ScopeMatches, "failed_matching_scope_can_resume_saved_phases");
        Check(!Read(editable: false).CanResume && !Read(editable: false).CanRetry, "read_only_viewer_cannot_retry");
        Check(Read(revision: 8).Stage == "obsolete" && !Read(revision: 8).CanResume && !Read(revision: 8).CanRetry,
            "edited_revision_is_obsolete_and_cannot_resume");
        Check(!Read(source: "changed").CanResume && Read(source: "changed").Stage == "obsolete",
            "different_source_hash_cannot_offer_resume");

        events.Clear();
        Add("ai_generation_queued", 0);
        Add("ai_generation_started", 1);
        Progress("phase_completed", "Plan", 60, contract: "earlier-contract");
        Add("ai_generation_failed", 65);
        Check(!Read().CanResume && Read().CanRetry, "changed_contract_requires_fresh_generation");
        events.Clear();
        Add("ai_generation_queued", 0);
        Add("ai_generation_started", 1);
        Progress("phase_completed", "Design", 60);
        Add("ai_generation_failed", 65);
        Check(!Read().CanResume && Read().ResumablePhases.Length == 0,
            "noncontiguous_checkpoint_cannot_offer_resume");
        events.Clear();
        Add("ai_generation_queued", 0);
        Add("ai_generation_started", 1);
        Progress("phase_completed", "Plan", 30);
        Progress("phase_completed", "Implement", 60);
        Add("ai_generation_failed", 65);
        Check(Read().CanResume && Read().ResumablePhases.SequenceEqual(new[] { "Plan" }),
            "resume_count_stops_at_first_missing_phase");
        events.Clear();
        Add("ai_generation_queued", 0);
        Add("ai_generation_started", 1);
        Progress("phase_resumed", "Plan", 2);
        Progress("phase_started", "Design", 3);
        var resumed = Read(20);
        Check(resumed.PhaseTimeline[0].Status == "resumed" && resumed.PhaseTimeline[0].Resumed
            && resumed.PhaseTimeline[0].ElapsedSeconds is null && resumed.PhaseTimeline[0].StartedAt is null,
            "reused_checkpoint_never_fabricates_generation_duration");
        Add("ai_generation_started", 40);
        Progress("phase_started", "Design", 41);
        Check(Read(60).PhaseTimeline[1].ElapsedSeconds == 57 && Read(60).ElapsedSeconds == 60,
            "worker_restart_preserves_first_phase_and_queue_wall_time");
        Add("ai_generation_obsolete", 65);
        Check(Read().Stage == "obsolete" && Read().PhaseTimeline[1].Status == "interrupted" && !Read().CanRetry,
            "obsolete_worker_stops_active_clock_without_retry_offer");

        events.Clear();
        Add("ai_generation_queued", 0);
        Add("ai_generation_started", 1);
        Progress("phase_started", "Plan", 2);
        Progress("phase_failed", "Plan", 20);
        // The worker can die between phase failure and terminal persistence.
        // Its recovered attempt still owns the same durable generation.
        Add("ai_generation_started", 30);
        Progress("phase_started", "Plan", 31);
        Check(Read(40).PhaseTimeline[0].Status == "running" && Read(40).PhaseTimeline[0].ElapsedSeconds == 38,
            "worker_recovery_after_uncommitted_terminal_restarts_live_phase_clock");

        events.Clear();
        Add("ai_generation_queued", 0);
        Add("ai_generation_started", 1);
        foreach (var (phase, index) in Module025GenerationEngine.Phases.Select((phase, index) => (phase, index)))
        {
            Progress("phase_started", phase, 2 + index * 10);
            Progress("phase_completed", phase, 10 + index * 10);
        }
        Progress("assembly_started", "assembly", 51);
        Check(Read(55).Stage == "assembly" && Read(55).PhaseTimeline.All(item => item.Status == "completed"),
            "assembly_distinct_from_five_completed_phases");
        Progress("assembly_completed", "assembly", 60);
        Check(Read(61).Stage == "assembly" && !Read(61).Terminal, "await_atomic_publication_after_assembly");
        Add("ai_generation_completed", 65, revision: 8);
        var completed = Read(10_000, revision: 8);
        Check(completed.Stage == "completed" && completed.Completed && completed.ScopeMatches && completed.ElapsedSeconds == 65
            && !completed.CanRetry, "own_success_revision_increment_is_not_obsolete");
        Check(Read(revision: 9).Stage == "obsolete" && !Read(revision: 9).Completed && Read(revision: 9).PreviousGenerationCompleted,
            "later_edit_preserves_historical_success_without_claiming_current_revision_generated");
        // A late diagnostic event cannot turn a terminal generation into an active one.
        Add("ai_generation_progress", 70, new { progress = "malformed" }, revision: 8);
        Check(Read(revision: 8).Stage == "completed" && Read(revision: 8).ElapsedSeconds == 65,
            "late_or_malformed_progress_cannot_restart_terminal_clock");
        Check(Read(seconds: -1, revision: 8).PhaseTimeline.All(item => item.ElapsedSeconds >= 0),
            "durations_never_negative");
    }
}
