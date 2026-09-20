using System.Text.Json;

namespace ProjectTime.Api.Modules;

public sealed record Module025TaskEstimate(
    string TaskId, string Description, decimal? Hours, string? Notes = null,
    decimal? RegularHours = null, decimal? AfterHours = null,
    bool AfterHoursRequired = false, bool AfterHoursSuggested = false,
    string? AfterHoursReason = null, bool? Reviewed = null, string? EstimateBasis = null);

internal static class Module025TaskEstimates
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static IReadOnlyList<Module025TaskEstimate> Read(JsonElement sections, string phase)
    {
        if (sections.ValueKind != JsonValueKind.Object || !sections.TryGetProperty("reviewedTasks", out var all)
            || all.ValueKind != JsonValueKind.Object || !all.TryGetProperty(phase, out var tasks))
            return Array.Empty<Module025TaskEstimate>();
        return JsonSerializer.Deserialize<Module025TaskEstimate[]>(tasks.GetRawText(), Json)
            ?? Array.Empty<Module025TaskEstimate>();
    }

    internal static string? Validate(IReadOnlyList<Module025TaskEstimate>? tasks)
    {
        if (tasks is null) return null; // Older clients may omit allocations; preserve them.
        if (tasks.Count > 200) return "Each phase supports up to 200 reviewed tasks.";
        var ids = new HashSet<Guid>();
        foreach (var task in tasks)
        {
            if (task is null || !Guid.TryParse(task.TaskId, out var taskId) || !ids.Add(taskId))
                return "Each task must have a unique task identifier.";
            if (string.IsNullOrWhiteSpace(task.Description) || task.Description.Length > 6000)
                return "Each task needs a description of at most 6000 characters.";
            if (task.Notes?.Length > 4000) return "Task notes must be at most 4000 characters.";
            if (!ValidHours(task.Hours) || !ValidHours(task.RegularHours) || !ValidHours(task.AfterHours))
                return "Task hours must be between 0 and 100000, with at most two decimal places.";
            if (task.RegularHours.HasValue != task.AfterHours.HasValue)
                return "Enter both regular and after-hours allocations, or leave both unspecified.";
            if (task.RegularHours.HasValue && (!task.Hours.HasValue || task.RegularHours + task.AfterHours != task.Hours))
                return "Regular and after-hours allocations must equal the task's total hours.";
            if (task.AfterHours is > 0 && !task.AfterHoursRequired)
                return "Select after-hours required when allocating after-hours effort.";
            if (task.AfterHoursReason?.Length > 2000) return "After-hours reasons must be at most 2000 characters.";
            if (task.EstimateBasis?.Length > 4000) return "Task estimate basis must be at most 4000 characters.";
        }
        if (tasks.Sum(t => t.Hours ?? 0) > 999999.99m) return "Phase task hours exceed the supported total.";
        return null;
    }

    internal static bool Complete(IReadOnlyList<Module025TaskEstimate>? tasks) =>
        tasks is { Count: > 0 } && Validate(tasks) is null && tasks.All(t => t.Hours.HasValue && t.Reviewed != false
            && (!t.AfterHoursRequired || t.AfterHours is > 0));

    // Legacy allocations remain regular hours until an SA explicitly classifies them.
    internal static decimal? RegularAllocation(Module025TaskEstimate task) => task.RegularHours ?? task.Hours;
    internal static decimal? AfterHoursAllocation(Module025TaskEstimate task) => task.Hours.HasValue ? task.AfterHours ?? 0m : null;
    private static bool ValidHours(decimal? hours) => hours is null ||
        (hours >= 0 && hours <= 100000 && decimal.Round(hours.Value, 2) == hours.Value);

    internal static string? PhaseReadiness(Module025PhaseRow phase)
    {
        if (phase.Tasks is not { Count: > 0 }) return $"{phase.PhaseCode}: Generate task estimates or add the task breakdown.";
        var validation = Validate(phase.Tasks);
        if (validation is not null) return $"{phase.PhaseCode}: {validation}";
        for (var index = 0; index < phase.Tasks.Count; index++)
        {
            var task = phase.Tasks[index];
            if (!task.Hours.HasValue) return $"{phase.PhaseCode}, task {index + 1}: Enter estimated hours.";
            if (task.AfterHoursRequired && task.AfterHours is not > 0)
                return $"{phase.PhaseCode}, task {index + 1}: Allocate after-hours effort or clear after-hours required.";
            if (task.Reviewed == false) return $"{phase.PhaseCode}, task {index + 1}: Review and accept the proposed task estimate.";
        }
        return phase.Tasks.Sum(task => task.Hours!.Value) == phase.FinalHours ? null
            : $"{phase.PhaseCode}: Task totals must equal the phase hours.";
    }

    internal static bool Reconciled(Module025PhaseRow phase) => Complete(phase.Tasks)
        && phase.Tasks!.Sum(t => t.Hours!.Value) == phase.FinalHours;

    internal static string? Readiness(Module025EngagementRow engagement)
    {
        if (string.IsNullOrWhiteSpace(engagement.CustomerName)) return "Customer Name is required.";
        if (string.IsNullOrWhiteSpace(engagement.ProjectName)) return "Project Name is required.";
        if (engagement.CommercialModel is not ("fixed" or "time_and_materials")) return "Contract Type is required.";
        if (string.IsNullOrWhiteSpace(engagement.OwnerDisplayName)) return "Solution Architect is required.";
        if (!engagement.AccountExecutiveUserId.HasValue || string.IsNullOrWhiteSpace(engagement.AccountExecutiveName)) return "Account Executive is required.";
        if (!engagement.ResaleUserId.HasValue || string.IsNullOrWhiteSpace(engagement.ResaleName)) return "SAA / Inside Sales is required.";
        if (engagement.GsdTemplateKey == Module025SowGsdDocumentExporter.StandardGsdTemplateKey
            || engagement.Phases.Any(phase => phase.Tasks is { Count: > 0 }))
        {
            if (engagement.Phases.Count != 5) return "Review task descriptions and hours in all five phases.";
            foreach (var phase in engagement.Phases.OrderBy(phase => phase.SortOrder))
                if (PhaseReadiness(phase) is { } issue) return issue;
        }
        return null;
    }
}
