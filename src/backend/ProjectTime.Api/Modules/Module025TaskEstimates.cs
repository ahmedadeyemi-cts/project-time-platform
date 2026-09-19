using System.Text.Json;

namespace ProjectTime.Api.Modules;

public sealed record Module025TaskEstimate(string TaskId, string Description, decimal? Hours, string? Notes = null);

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
            if (task.Hours is < 0 or > 100000 || (task.Hours.HasValue && decimal.Round(task.Hours.Value, 2) != task.Hours.Value))
                return "Task hours must be between 0 and 100000, with at most two decimal places.";
        }
        if (tasks.Sum(t => t.Hours ?? 0) > 999999.99m) return "Phase task hours exceed the supported total.";
        return null;
    }

    internal static bool Complete(IReadOnlyList<Module025TaskEstimate>? tasks) =>
        tasks is { Count: > 0 } && Validate(tasks) is null && tasks.All(t => t.Hours.HasValue);

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
            && (engagement.Phases.Count != 5 || engagement.Phases.Any(p => !Reconciled(p))))
            return "Review task descriptions and hours in all five phases. Task totals must equal the reviewed phase hours.";
        return null;
    }
}
