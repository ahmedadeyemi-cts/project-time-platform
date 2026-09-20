namespace ProjectTime.Api.Modules;

/// <summary>Shared forecast classification. Unknown components never become zero.</summary>
public static class ProjectBudgetAssessment
{
    public const decimal WarningFraction = 0.85m;

    public static decimal? CompleteTotal(decimal? labor, decimal? expenses) =>
        labor.HasValue && expenses.HasValue && labor >= 0 && expenses >= 0
            ? labor + expenses : null;

    public static string Classify(decimal? budget, decimal? forecast)
    {
        if (!budget.HasValue || budget < 0 || !forecast.HasValue || forecast < 0)
            return "missing_financial_information";
        if (forecast > budget) return "over_budget";
        if (budget > 0 && forecast >= budget * WarningFraction) return "approaching_budget";
        return "on_track";
    }
}
