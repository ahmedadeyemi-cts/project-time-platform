static class ProjectPulseTimeEntryDescriptionValidation
{
    public static List<string> GetMissingDescriptionErrors(IEnumerable<TimesheetEntryRequest> entries)
    {
        var errors = new List<string>();

        var missing = entries
            .Where(entry => entry.Hours > 0 && string.IsNullOrWhiteSpace(entry.Description))
            .Take(5)
            .ToList();

        if (missing.Count == 0)
        {
            return errors;
        }

        errors.Add("A description/comment is required for every time entry with hours greater than zero.");

        foreach (var entry in missing)
        {
            errors.Add($"{entry.WorkDate}: Add a description/comment before saving or submitting this time entry.");
        }

        return errors;
    }
}
