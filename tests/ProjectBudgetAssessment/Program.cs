using ProjectTime.Api.Modules;
var cases = new (decimal? Labor, decimal? Expense, decimal? Forecast, string Expected)[] {
    (100, null, 50, "missing_financial_information"),
    (null, 20, 50, "missing_financial_information"),
    (100, 0, null, "missing_financial_information"),
    (100, 0, 84.99m, "on_track"),
    (100, 0, 85, "approaching_budget"),
    (100, 20, 110, "approaching_budget"),
    (100, 20, 120.01m, "over_budget"),
    (0, 0, 1, "over_budget"),
    (0, 0, 0, "on_track"),
    (-1, 20, 10, "missing_financial_information")
};
foreach (var row in cases) {
    var actual = ProjectBudgetAssessment.Classify(ProjectBudgetAssessment.CompleteTotal(row.Labor, row.Expense), row.Forecast);
    if (actual != row.Expected) throw new Exception($"Expected {row.Expected}, got {actual}: {row}");
}
Console.WriteLine($"{cases.Length} project budget boundary cases passed.");
