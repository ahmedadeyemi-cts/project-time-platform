namespace ProjectTime.Api.Modules;

// Pure policy shared by the source scanner and send-time validation. No provider calls.
internal static class ProjectFlowHiveNotificationPolicy
{
    internal sealed record Settings(bool Enabled, short[] LeadDays, bool IncludePm, bool IncludeTeam,
        bool IncludeOverdue, string Timezone, string Boundary, TimeSpan? QuietStart, TimeSpan? QuietEnd);
    internal sealed record Task(Guid Id, string Wbs, string Name, DateOnly Due, Guid[] Assignees);
    internal sealed record State(string Due, string DueToken, Dictionary<Guid, string> Assignments);
    internal sealed record Event(string Kind, Guid TaskId, string Wbs, string Name, DateOnly Due,
        Guid Recipient, string Token, string Key);
    internal sealed record Evaluation(Dictionary<Guid, State> State, Event[] Events);

    internal static readonly Settings Default = new(true, [3, 0], true, true, true,
        "America/Chicago", "test_only", TimeSpan.FromHours(20), TimeSpan.FromHours(6));

    internal static DateOnly LocalDate(DateTimeOffset now, Settings settings) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(settings.Timezone)).DateTime);

    internal static bool IsQuiet(DateTimeOffset now, Settings settings)
    {
        if (settings.QuietStart is not { } start || settings.QuietEnd is not { } end || start == end) return false;
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(settings.Timezone)).TimeOfDay;
        return start < end ? local >= start && local < end : local >= start || local < end;
    }

    internal static string? DueKind(DateOnly due, DateOnly today, Settings settings)
    {
        var days = due.DayNumber - today.DayNumber;
        if (days < 0) return settings.IncludeOverdue ? "overdue" : null;
        if (days == 0) return "due_today";
        return settings.LeadDays.Contains((short)Math.Min(short.MaxValue, days)) ? $"due_in_{days}_days" : null;
    }

    internal static Evaluation Evaluate(Guid projectId, string revision, IReadOnlyList<Task> tasks, Guid? pm,
        Settings settings, IReadOnlyDictionary<Guid, State> prior, DateTimeOffset now)
    {
        // Quiet hours defer both observations and delivery; a later scan still sees new assignments.
        if (!settings.Enabled || settings.Boundary == "locked" || IsQuiet(now, settings))
            return new(prior.ToDictionary(pair => pair.Key, pair => pair.Value), []);
        var today = LocalDate(now, settings);
        var states = new Dictionary<Guid, State>();
        var events = new List<Event>();
        foreach (var task in tasks)
        {
            prior.TryGetValue(task.Id, out var old);
            var due = task.Due.ToString("yyyy-MM-dd");
            var dueToken = old?.Due == due ? old.DueToken : revision;
            var assignments = task.Assignees.Distinct().ToDictionary(user => user,
                user => old?.Assignments.GetValueOrDefault(user) ?? revision);
            states.Add(task.Id, new(due, dueToken, assignments));
            foreach (var user in assignments.Keys)
                if (settings.IncludeTeam && old?.Assignments.ContainsKey(user) != true)
                    Add("assigned", user, assignments[user]);
            var kind = DueKind(task.Due, today, settings);
            if (kind is null) continue;
            var recipients = settings.IncludeTeam ? assignments.Keys.ToHashSet() : [];
            if (settings.IncludePm && pm.HasValue) recipients.Add(pm.Value);
            foreach (var user in recipients)
                Add(kind, user, dueToken + ":" + assignments.GetValueOrDefault(user, "pm"));
            void Add(string kind, Guid user, string token) => events.Add(new(kind, task.Id, task.Wbs,
                task.Name, task.Due, user, token,
                $"flowhive:{projectId:N}:{task.Id:N}:{user:N}:{kind}:{due}:{token}"));
        }
        return new(states, events.ToArray());
    }

    internal static Task[] Tasks(ProjectFlowHivePlanRequest plan, ProjectFlowHiveScheduleResult schedule)
    {
        if (!schedule.Valid) return [];
        var dates = schedule.Tasks.ToDictionary(task => task.WbsNumber, StringComparer.OrdinalIgnoreCase);
        return (plan.Tasks ?? []).Where(task => task.ClientTaskId.HasValue && !task.IsSummary && !task.IsMilestone
                && task.PercentComplete < 100 && (task.Status ?? "").ToLowerInvariant() is not
                    ("completed" or "complete" or "done" or "cancelled" or "canceled" or "archived")
                && task.WbsNumber is not null && dates.ContainsKey(task.WbsNumber))
            .Select(task => new Task(task.ClientTaskId!.Value, task.WbsNumber!, task.Name ?? "Task",
                dates[task.WbsNumber!].EndDate,
                (plan.Assignments ?? []).Where(a => a.ResourceUserId.HasValue && a.ResourceUserId != Guid.Empty
                    && string.Equals(a.TaskWbs, task.WbsNumber, StringComparison.OrdinalIgnoreCase))
                    .Select(a => a.ResourceUserId!.Value).Distinct().ToArray())).ToArray();
    }
}
