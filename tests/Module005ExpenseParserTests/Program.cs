using System.Collections;
using System.Globalization;
using System.Reflection;
using ClosedXML.Excel;
using ProjectTime.Api.Modules;

const decimal ExpectedTotal = 2377.26m;
var expectedCategories = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
{
    ["SP-Cust Pass Through - Airfare"] = 500m,
    ["SP-Cust Pass Through - Rental"] = 200m,
    ["SP-Cust Pass Through-Hotel"] = 800m,
    ["SP-Cust Pass Through-Meals"] = 300m,
    ["SP-Cust Pass Through-Mileage"] = 200m,
    ["SP-Travel, Lodging, Parking"] = 177.26m,
    ["SP-Meals (All Employees,Cust)"] = 200m
};

var cases = new[]
{
    ParseCase("ExpensesByGLDim.xlsx", BuildGlWorkbook(), "gl_dimension", new DateOnly(2026, 6, 2), new DateOnly(2026, 7, 20)),
    ParseCase("ExpensesByCategory.xlsx", BuildCategoryWorkbook(), "category_summary", new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 29)),
    ParseCase("ExpensesByGLDim.csv", BuildGlCsv(), "csv_gl_dimension", new DateOnly(2026, 6, 2), new DateOnly(2026, 7, 20)),
    ParseCase("ExpensesByCategory.csv", BuildCategoryCsv(), "csv_category_summary", new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 29))
};

foreach (var parsed in cases)
{
    AssertEqual(ExpectedTotal, parsed.TotalAmount, $"{parsed.FileName} total");
    AssertEqual(ExpectedTotal, parsed.ReimbursableAmount, $"{parsed.FileName} reimbursable total");
    AssertEqual(expectedCategories.Count, parsed.CategoryTotals.Count, $"{parsed.FileName} category count");
    foreach (var expected in expectedCategories)
    {
        if (!parsed.CategoryTotals.TryGetValue(expected.Key, out var actual))
            throw new InvalidOperationException($"{parsed.FileName} missing normalized category {expected.Key}.");
        AssertEqual(expected.Value, actual, $"{parsed.FileName} category {expected.Key}");
    }
}

AssertCategoryTotalsEqual(cases[0], cases[1], "Excel formats");
AssertCategoryTotalsEqual(cases[2], cases[3], "CSV formats");
AssertCategoryTotalsEqual(cases[0], cases[2], "GL Excel and CSV");
AssertCategoryTotalsEqual(cases[1], cases[3], "Category Excel and CSV");

Console.WriteLine("MODULE_005_EXPENSE_PARSER_TEST=PASS formats=4 total=2377.26 normalizedCategories=7");
return;

static ParsedCase ParseCase(string fileName, byte[] bytes, string expectedFormat, DateOnly expectedStart, DateOnly expectedEnd)
{
    var type = typeof(Module005ProjectExpenseUploadModule);
    var method = type.GetMethod("ParseExpenseFile", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Module 005 parser method was not found.");
    object parsed;
    try
    {
        parsed = method.Invoke(null, new object[] { fileName, bytes })
            ?? throw new InvalidOperationException($"{fileName} parser returned null.");
    }
    catch (TargetInvocationException exception) when (exception.InnerException is not null)
    {
        throw new InvalidOperationException($"{fileName} parser failed: {exception.InnerException.Message}", exception.InnerException);
    }

    var format = Property<string>(parsed, "FormatCode");
    AssertEqual(expectedFormat, format, $"{fileName} format");
    AssertEqual(expectedStart, DateProperty(parsed, "PeriodStart"), $"{fileName} period start");
    AssertEqual(expectedEnd, DateProperty(parsed, "PeriodEnd"), $"{fileName} period end");

    var categories = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
    var lines = Property<IEnumerable>(parsed, "Lines");
    var lineCount = 0;
    foreach (var line in lines)
    {
        if (line is null) continue;
        lineCount++;
        var category = Property<string>(line, "Category");
        var amount = Property<decimal>(line, "Amount");
        categories[category] = categories.GetValueOrDefault(category) + amount;
    }
    AssertEqual(7, lineCount, $"{fileName} line count");

    return new ParsedCase(
        fileName,
        Property<decimal>(parsed, "TotalAmount"),
        Property<decimal>(parsed, "ReimbursableAmount"),
        categories);
}

static DateOnly DateProperty(object instance, string name)
{
    var property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException($"Property {name} was not found on {instance.GetType().FullName}.");
    return property.GetValue(instance) is DateOnly date
        ? date
        : throw new InvalidOperationException($"Property {name} was null or not a DateOnly.");
}

static T Property<T>(object instance, string name)
{
    var property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException($"Property {name} was not found on {instance.GetType().FullName}.");
    var value = property.GetValue(instance);
    if (value is null && default(T) is null) return default!;
    return value is T typed
        ? typed
        : throw new InvalidOperationException($"Property {name} had unexpected type {value?.GetType().FullName ?? "null"}.");
}

static void AssertCategoryTotalsEqual(ParsedCase left, ParsedCase right, string label)
{
    AssertEqual(left.CategoryTotals.Count, right.CategoryTotals.Count, $"{label} category count");
    foreach (var entry in left.CategoryTotals)
    {
        if (!right.CategoryTotals.TryGetValue(entry.Key, out var other))
            throw new InvalidOperationException($"{label} missing category {entry.Key}.");
        AssertEqual(entry.Value, other, $"{label} category {entry.Key}");
    }
}

static void AssertEqual<T>(T expected, T actual, string label) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"ASSERTION_FAILED {label} expected={expected} actual={actual}");
    Console.WriteLine($"ASSERTION_PASSED {label}={actual}");
}

static byte[] BuildGlWorkbook()
{
    using var workbook = new XLWorkbook();
    var sheet = workbook.AddWorksheet("Expenses by GL Dimension");
    var headers = new[]
    {
        "Processed", "Employee", "Department Name", "Department Code", "Category", "GL Code",
        "Date", "Amount", "Reimbursable", "Reimb Amount", "Currency", "Reason"
    };
    for (var column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
    var date = new DateTime(2026, 6, 2);
    var row = 2;
    foreach (var entry in ExpenseRows())
    {
        sheet.Cell(row, 1).Value = "Yes";
        sheet.Cell(row, 2).Value = "Engineer Test (engineer@example.test)";
        sheet.Cell(row, 3).Value = "Engineering";
        sheet.Cell(row, 4).Value = "ENG";
        sheet.Cell(row, 5).Value = entry.SourceCategory;
        sheet.Cell(row, 6).Value = $"GL-{row:000}";
        sheet.Cell(row, 7).Value = date;
        sheet.Cell(row, 8).Value = entry.Amount;
        sheet.Cell(row, 9).Value = "Yes";
        sheet.Cell(row, 10).Value = entry.Amount;
        sheet.Cell(row, 11).Value = "USD";
        sheet.Cell(row, 12).Value = "Certify test expense";
        date = row == 7 ? new DateTime(2026, 7, 20) : date.AddDays(5);
        row++;
    }
    using var stream = new MemoryStream();
    workbook.SaveAs(stream);
    return stream.ToArray();
}

static byte[] BuildCategoryWorkbook()
{
    using var workbook = new XLWorkbook();
    var sheet = workbook.AddWorksheet("Expenses by Category");
    sheet.Cell("A1").Value = "Expenses by Category";
    sheet.Cell("A2").Value = "Start Date:";
    sheet.Cell("B2").Value = "6/1/2026";
    sheet.Cell("A3").Value = "End Date:";
    sheet.Cell("B3").Value = "7/29/2026";
    sheet.Cell("A5").Value = "Employee";
    var column = 2;
    foreach (var entry in ExpenseRows()) sheet.Cell(5, column++).Value = entry.SourceCategory;
    sheet.Cell(5, column).Value = "Total";
    sheet.Cell("A6").Value = "Engineer Test (engineer@example.test)";
    column = 2;
    foreach (var entry in ExpenseRows()) sheet.Cell(6, column++).Value = entry.Amount;
    sheet.Cell(6, column).Value = ExpectedTotal;
    using var stream = new MemoryStream();
    workbook.SaveAs(stream);
    return stream.ToArray();
}

static byte[] BuildGlCsv()
{
    var builder = new System.Text.StringBuilder();
    builder.AppendLine("Processed,Employee,Department Name,Department Code,Category,GL Code,Date,Amount,Reimbursable,Reimb Amount,Currency,Reason");
    var date = new DateOnly(2026, 6, 2);
    var row = 1;
    foreach (var entry in ExpenseRows())
    {
        builder.AppendLine(string.Join(',', new[]
        {
            "Yes", Quote("Engineer Test (engineer@example.test)"), "Engineering", "ENG", Quote(entry.SourceCategory),
            $"GL-{row:000}", date.ToString("M/d/yyyy", CultureInfo.InvariantCulture), entry.Amount.ToString(CultureInfo.InvariantCulture),
            "Yes", entry.Amount.ToString(CultureInfo.InvariantCulture), "USD", Quote("Certify test expense")
        }));
        date = row == 6 ? new DateOnly(2026, 7, 20) : date.AddDays(5);
        row++;
    }
    return System.Text.Encoding.UTF8.GetBytes(builder.ToString());
}

static byte[] BuildCategoryCsv()
{
    var rows = ExpenseRows().ToArray();
    var builder = new System.Text.StringBuilder();
    builder.AppendLine("Start Date:,6/1/2026");
    builder.AppendLine("End Date:,7/29/2026");
    builder.Append("Employee");
    foreach (var entry in rows) builder.Append(',').Append(Quote(entry.SourceCategory));
    builder.AppendLine(",Total");
    builder.Append(Quote("Engineer Test (engineer@example.test)"));
    foreach (var entry in rows) builder.Append(',').Append(entry.Amount.ToString(CultureInfo.InvariantCulture));
    builder.Append(',').Append(ExpectedTotal.ToString(CultureInfo.InvariantCulture)).AppendLine();
    return System.Text.Encoding.UTF8.GetBytes(builder.ToString());
}

static IEnumerable<ExpenseFixture> ExpenseRows()
{
    yield return new("Airfare", 500m);
    yield return new("Car Rental", 200m);
    yield return new("Hotel", 800m);
    yield return new("Meals", 300m);
    yield return new("Mileage", 200m);
    yield return new("Parking/Tolls", 177.26m);
    yield return new("Meals (All Employees,Cust)", 200m);
}

static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

sealed record ExpenseFixture(string SourceCategory, decimal Amount);
sealed record ParsedCase(string FileName, decimal TotalAmount, decimal ReimbursableAmount, Dictionary<string, decimal> CategoryTotals);
