using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectTime.Api.Modules;

// Materializes the existing generation result; no additional model request is needed.
// Estimated labor is always a proposal, never an assertion that an SA reviewed it.
internal static class Module025TaskDrafts
{
    internal static IReadOnlyList<Module025TaskEstimate> FromWorkPackages(IEnumerable<JsonElement> phaseWorkPackages, string phaseCode)
    {
        var tasks = new List<Module025TaskEstimate>();
        var packageIndex = 0;
        foreach (var package in phaseWorkPackages)
        {
            packageIndex++;
            var name = Text(package, "Name");
            var description = Text(package, "Description");
            var packageHours = Hours(package, "EstimatedHours");
            var steps = Array(package, "DetailedSteps").Select(step => new Step(
                step.ValueKind == JsonValueKind.String ? step.GetString()!.Trim() : Text(step, "Description"),
                step.ValueKind == JsonValueKind.Object ? Hours(step, "EstimatedHours") : null))
                .Where(step => step.Description.Length > 0).ToArray();
            if (steps.Length == 0)
                steps = new[] { new Step(description.Length > 0 ? description : name, packageHours) };
            steps = steps.Where(step => step.Description.Length > 0).ToArray();
            if (steps.Length == 0) continue; // Do not manufacture work absent from source scope.
            var explicitStepHours = steps.All(step => step.Hours.HasValue);
            var explicitTotal = steps.Sum(step => step.Hours ?? 0m);
            // Explicit per-step estimates are used only when they reconcile with the package.
            // Conflicting totals require review instead of silently changing scope/effort.
            var explicitConsistent = explicitStepHours && (!packageHours.HasValue || explicitTotal == packageHours);
            var allocations = explicitConsistent ? steps.Select(step => step.Hours).ToArray()
                : steps.Any(step => step.Hours.HasValue) ? Enumerable.Repeat<decimal?>(null, steps.Length).ToArray()
                : Split(packageHours, steps.Length);
            var basis = explicitConsistent
                ? "Proposed labor estimate supplied for this work-package task. SA review is required; elapsed duration is not used as effort."
                : steps.Any(step => step.Hours.HasValue)
                    ? "Per-task estimates are incomplete or conflict with the work-package total. Hours remain unknown until the SA reconciles the estimate."
                    : packageHours.HasValue
                        ? $"Proposed equal allocation of {packageHours.Value.ToString("0.##", CultureInfo.InvariantCulture)} work-package labor hours across {steps.Length} detailed tasks. This is a starting allocation, not a measured task estimate; the SA must review complexity and adjust."
                        : "The source contains no usable labor-hour estimate. The SA must estimate this task; elapsed duration is not converted to effort.";
            for (var index = 0; index < steps.Length; index++)
            {
                var text = name.Length > 0 && !string.Equals(name, steps[index].Description, StringComparison.OrdinalIgnoreCase)
                    ? name + ": " + steps[index].Description : steps[index].Description;
                tasks.Add(Create(phaseCode, $"package:{packageIndex}:step:{index}", text, allocations[index], basis));
            }
        }
        if (tasks.Count > 200) throw new InvalidOperationException("The generated phase exceeds 200 task allocations; consolidate work packages before generating again.");
        return tasks;
    }

    internal static IReadOnlyList<Module025TaskEstimate> ProposeFromPhase(Module025PhaseRow phase)
    {
        if (phase.Tasks is { Count: > 0 }) return phase.Tasks; // Includes unreviewed edits: never silently replace them.
        var descriptions = (phase.TechnicalTasks.Count > 0 ? phase.TechnicalTasks : phase.DetailedActivities)
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (descriptions.Length == 0 && !string.IsNullOrWhiteSpace(phase.Objective)) descriptions = new[] { phase.Objective.Trim() };
        if (descriptions.Length > 200) throw new InvalidOperationException("This phase exceeds 200 task allocations; consolidate the saved activities first.");
        // A pristine zero-hour phase is not a source estimate. Explicit zero is retained only
        // for phases backed by an existing generation, rather than inventing free work.
        decimal? total = phase.FinalHours > 0 || phase.AiGenerated ? phase.FinalHours : null;
        var hours = Split(total, descriptions.Length);
        return descriptions.Select((description, index) => Create(phase.PhaseCode, $"saved:{index}", description, hours[index],
            total.HasValue ? $"Proposed equal allocation of {total.Value.ToString("0.##", CultureInfo.InvariantCulture)} saved phase labor hours across {descriptions.Length} tasks. Review complexity and adjust; this is not a measured task estimate."
                : "No saved phase labor estimate is available. The SA must estimate this task.")).ToArray();
    }

    internal static IReadOnlyList<Module025TaskEstimate> PreserveExisting(
        IReadOnlyList<Module025TaskEstimate>? existing, IReadOnlyList<Module025TaskEstimate> proposals) =>
        existing is { Count: > 0 } ? existing : proposals;

    private static Module025TaskEstimate Create(string phase, string key, string description, decimal? hours, string basis)
    {
        var clean = description.Length > 6000 ? description[..6000] : description;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"module025-task-draft:{phase}:{key}:{clean}"));
        var taskId = new Guid(bytes.AsSpan(0, 16)).ToString();
        var suggestion = SuggestAfterHours(clean);
        return new(taskId, clean, hours, RegularHours: hours, AfterHours: hours.HasValue ? 0m : null,
            AfterHoursRequired: false, AfterHoursSuggested: suggestion is not null,
            AfterHoursReason: suggestion, Reviewed: false, EstimateBasis: basis);
    }

    private static string? SuggestAfterHours(string description)
    {
        // A conservative keyword suggestion is deliberately not an approved schedule or rate.
        foreach (var token in new[] { "cutover", "cut-over", "service restart", "production restart", "production upgrade", "production reboot", "service outage", "maintenance window", "after-hours", "after hours" })
            if (description.Contains(token, StringComparison.OrdinalIgnoreCase))
                return $"This task mentions '{token}', which may affect service availability. Confirm the customer maintenance window before marking after-hours required; no schedule or premium rate is assumed.";
        return null;
    }

    private static decimal?[] Split(decimal? total, int count)
    {
        if (count == 0) return System.Array.Empty<decimal?>();
        if (!total.HasValue || total < 0 || total > 999999.99m) return Enumerable.Repeat<decimal?>(null, count).ToArray();
        var rounded = decimal.Round(total.Value, 2, MidpointRounding.AwayFromZero);
        var cents = (long)(rounded * 100m);
        var perTask = cents / count;
        var remainder = cents % count;
        return Enumerable.Range(0, count).Select(index => (decimal?)(perTask + (index < remainder ? 1 : 0)) / 100m).ToArray();
    }

    private static JsonElement Property(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object
        ? value.EnumerateObject().FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)).Value : default;
    private static string Text(JsonElement value, string name) { var field = Property(value, name); return field.ValueKind == JsonValueKind.String ? field.GetString()!.Trim() : ""; }
    private static IEnumerable<JsonElement> Array(JsonElement value, string name) { var field = Property(value, name); return field.ValueKind == JsonValueKind.Array ? field.EnumerateArray().ToArray() : System.Array.Empty<JsonElement>(); }
    private static decimal? Hours(JsonElement value, string name)
    {
        var field = Property(value, name);
        return field.ValueKind == JsonValueKind.Number && field.TryGetDecimal(out var number) && number >= 0 && number <= 100000
            ? decimal.Round(number, 2, MidpointRounding.AwayFromZero) : null;
    }
    private sealed record Step(string Description, decimal? Hours);
}
