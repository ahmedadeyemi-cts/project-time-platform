using System.Collections;
using System.Globalization;
using System.Reflection;
using ClosedXML.Excel;
using ProjectTime.Api.Modules;

const decimal ExpectedTotal = 2377.26m;
var expectedCategories = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
{
    ["SP-Cust Pass Through - Airfare"] = 1416.81m,
    ["SP-Cust Pass Through - Rental"] = 44.98m,
    ["SP-Cust Pass Through-Hotel"] = 589.08m,
    ["SP-Cust Pass Through-Meals"] = 195.91m,
    ["SP-Cust Pass Through-Mileage"] = 72.52m,
    ["SP-Meals (All Employees,Cust)"] = 11.96m,
    ["SP-Travel, Lodging, Parking"] = 46m
};

var cases = new[]
{
    ParseCase("ExpensesByGLDim.xlsx", BuildGlWorkbook(), "gl_dimension", new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 31), 20),
    ParseCase("ExpensesByCategory.xlsx", BuildCategoryWorkbook(), "category_summary", new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 29), 7),
    ParseCase("ExpensesByGLDim.csv", BuildGlCsv(), "csv_gl_dimension", new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 31), 20),
    ParseCase("ExpensesByCategory.csv", BuildCategoryCsv(), "csv_category_summary", new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 29), 7)
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

Console.WriteLine("MODULE_005_EXPENSE_PARSER_TEST=PASS formats=4 total=2377.26 normalizedCategories=7 exactUploadedStructures=true");
return;

static ParsedCase ParseCase(string fileName, byte[] bytes, string expectedFormat, DateOnly expectedStart, DateOnly expectedEnd, int expectedLineCount)
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
    AssertEqual(expectedLineCount, lineCount, $"{fileName} line count");

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

    var row = 2;
    foreach (var entry in TransactionRows())
    {
        sheet.Cell(row, 1).Value = "Yes";
        sheet.Cell(row, 2).Value = "Engineer, Test";
        sheet.Cell(row, 3).Value = "SP - Resale Collaboration";
        sheet.Cell(row, 4).Value = "9321";
        sheet.Cell(row, 5).Value = entry.Category;
        sheet.Cell(row, 6).Value = entry.GlCode;
        sheet.Cell(row, 7).Value = entry.Date.ToDateTime(TimeOnly.MinValue);
        sheet.Cell(row, 8).Value = entry.Amount;
        sheet.Cell(row, 9).Value = "True";
        sheet.Cell(row, 10).Value = entry.Amount;
        sheet.Cell(row, 11).Value = "USD";
        sheet.Cell(row, 12).Value = entry.Reason;
        row++;
    }

    for (var column = 1; column <= 7; column++) sheet.Cell(row, column).Value = "43";
    sheet.Cell(row, 8).Value = ExpectedTotal;
    sheet.Cell(row, 9).Value = "43";
    sheet.Cell(row, 10).Value = ExpectedTotal;
    sheet.Cell(row, 11).Value = "43";
    sheet.Cell(row, 12).Value = "43";

    using var stream = new MemoryStream();
    workbook.SaveAs(stream);
    return stream.ToArray();
}

static byte[] BuildCategoryWorkbook()
{
    using var workbook = new XLWorkbook();
    var sheet = workbook.AddWorksheet("Expenses by Category");
    sheet.Cell("A1").Value = "Expenses by Category";
    sheet.Cell("A2").Value = "Parameter Values";
    sheet.Cell("A3").Value = "Search By: Processed Date";
    sheet.Cell("A4").Value = "Start Date: 6/1/2026";
    sheet.Cell("A5").Value = "End Date: 7/29/2026";
    sheet.Cell("A6").Value = "Employee: Engineer Test (engineer@example.test)";
    sheet.Cell("A7").Value = "Summarize By: Expense Category";

    var categories = CategoryRows().ToArray();
    sheet.Cell(9, 1).Value = "Employee";
    for (var index = 0; index < categories.Length; index++) sheet.Cell(9, index + 2).Value = categories[index].Category;
    sheet.Cell(9, categories.Length + 2).Value = "Total";

    sheet.Cell(11, 1).Value = "Engineer, Test (81790)";
    for (var index = 0; index < categories.Length; index++) sheet.Cell(11, index + 2).Value = categories[index].Amount;
    sheet.Cell(11, categories.Length + 2).Value = ExpectedTotal;

    sheet.Cell(12, 1).Value = "Total";
    for (var index = 0; index < categories.Length; index++) sheet.Cell(12, index + 2).Value = categories[index].Amount;
    sheet.Cell(12, categories.Length + 2).Value = ExpectedTotal;

    using var stream = new MemoryStream();
    workbook.SaveAs(stream);
    return stream.ToArray();
}

static byte[] BuildGlCsv()
{
    var builder = new System.Text.StringBuilder();
    builder.AppendLine("Processed,Employee,Department Name,Department Code,Category,GL Code,Date,Amount,Reimbursable,Reimb Amount,Currency,Reason");
    foreach (var entry in TransactionRows())
    {
        builder.AppendLine(string.Join(',', new[]
        {
            "Yes", Quote("Engineer, Test"), Quote("SP - Resale Collaboration"), "9321", Quote(entry.Category),
            entry.GlCode, entry.Date.ToString("M/d/yyyy", CultureInfo.InvariantCulture), entry.Amount.ToString(CultureInfo.InvariantCulture),
            "True", entry.Amount.ToString(CultureInfo.InvariantCulture), "USD", Quote(entry.Reason)
        }));
    }
    builder.AppendLine($"43,43,43,43,43,43,43,{ExpectedTotal.ToString(CultureInfo.InvariantCulture)},43,{ExpectedTotal.ToString(CultureInfo.InvariantCulture)},43,43");
    return System.Text.Encoding.UTF8.GetBytes(builder.ToString());
}

static byte[] BuildCategoryCsv()
{
    var categories = CategoryRows().ToArray();
    var builder = new System.Text.StringBuilder();
    builder.AppendLine("Expenses by Category");
    builder.AppendLine("Parameter Values");
    builder.AppendLine("Search By: Processed Date");
    builder.AppendLine("Start Date: 6/1/2026");
    builder.AppendLine("End Date: 7/29/2026");
    builder.AppendLine(Quote("Employee: Engineer Test (engineer@example.test)"));
    builder.AppendLine("Summarize By: Expense Category");
    builder.AppendLine();
    builder.Append("Employee");
    foreach (var entry in categories) builder.Append(',').Append(Quote(entry.Category));
    builder.AppendLine(",Total");
    builder.AppendLine();
    builder.Append(Quote("Engineer, Test (81790)"));
    foreach (var entry in categories) builder.Append(',').Append(entry.Amount.ToString(CultureInfo.InvariantCulture));
    builder.Append(',').Append(ExpectedTotal.ToString(CultureInfo.InvariantCulture)).AppendLine();
    builder.Append("Total");
    foreach (var entry in categories) builder.Append(',').Append(entry.Amount.ToString(CultureInfo.InvariantCulture));
    builder.Append(',').Append(ExpectedTotal.ToString(CultureInfo.InvariantCulture)).AppendLine();
    return System.Text.Encoding.UTF8.GetBytes(builder.ToString());
}

static IEnumerable<TransactionFixture> TransactionRows()
{
    yield return new(new DateOnly(2026, 5, 15), "SP-Cust Pass Through-Meals", "6220", 12.71m, "Meal");
    yield return new(new DateOnly(2026, 5, 15), "SP-Cust Pass Through-Mileage", "6215", 18.13m, "Travel to airport");
    yield return new(new DateOnly(2026, 5, 15), "SP-Travel, Lodging, Parking", "6205", 23m, "Airport Parking");
    yield return new(new DateOnly(2026, 5, 15), "SP-Cust Pass Through-Meals", "6220", 47.35m, "Meal + 1");
    yield return new(new DateOnly(2026, 5, 16), "SP-Cust Pass Through-Meals", "6220", 55.20m, "Meal");
    yield return new(new DateOnly(2026, 5, 16), "SP-Meals (All Employees,Cust)", "6220", 5.98m, "Drinks at customer");
    yield return new(new DateOnly(2026, 5, 17), "SP-Meals (All Employees,Cust)", "6220", 5.98m, "Drinks at customer");
    yield return new(new DateOnly(2026, 5, 17), "SP-Cust Pass Through-Meals", "6220", 33m, "Meal");
    yield return new(new DateOnly(2026, 5, 17), "SP-Cust Pass Through-Hotel", "6205", 266.40m, "Hotel");
    yield return new(new DateOnly(2026, 5, 17), "SP-Cust Pass Through - Rental", "6590", 44.98m, "Trip to airport");
    yield return new(new DateOnly(2026, 5, 18), "SP-Cust Pass Through-Mileage", "6215", 18.13m, "Travel from airport");
    yield return new(new DateOnly(2026, 5, 24), "SP-Cust Pass Through - Airfare", "6205", 756.41m, "Customer flight 1");
    yield return new(new DateOnly(2026, 5, 24), "SP-Cust Pass Through - Airfare", "6205", 660.40m, "Customer flight 2");
    yield return new(new DateOnly(2026, 5, 29), "SP-Travel, Lodging, Parking", "6205", 23m, "Parking");
    yield return new(new DateOnly(2026, 5, 29), "SP-Cust Pass Through-Meals", "6220", 17.35m, "Meal");
    yield return new(new DateOnly(2026, 5, 29), "SP-Cust Pass Through-Mileage", "6215", 18.13m, "Travel to airport");
    yield return new(new DateOnly(2026, 5, 30), "SP-Cust Pass Through-Meals", "6220", 14.21m, "Meal");
    yield return new(new DateOnly(2026, 5, 31), "SP-Cust Pass Through-Hotel", "6205", 322.68m, "Hotel stay");
    yield return new(new DateOnly(2026, 5, 31), "SP-Cust Pass Through-Meals", "6220", 16.09m, "Meal");
    yield return new(new DateOnly(2026, 5, 31), "SP-Cust Pass Through-Mileage", "6215", 18.13m, "Travel from airport");
}

static IEnumerable<CategoryFixture> CategoryRows()
{
    yield return new("SP-Cust Pass Through - Airfare", 1416.81m);
    yield return new("SP-Cust Pass Through - Rental", 44.98m);
    yield return new("SP-Cust Pass Through-Hotel", 589.08m);
    yield return new("SP-Cust Pass Through-Meals", 195.91m);
    yield return new("SP-Cust Pass Through-Mileage", 72.52m);
    yield return new("SP-Meals (All Employees,Cust)", 11.96m);
    yield return new("SP-Travel, Lodging, Parking", 46m);
}

static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

sealed record TransactionFixture(DateOnly Date, string Category, string GlCode, decimal Amount, string Reason);
sealed record CategoryFixture(string Category, decimal Amount);
sealed record ParsedCase(string FileName, decimal TotalAmount, decimal ReimbursableAmount, Dictionary<string, decimal> CategoryTotals);
