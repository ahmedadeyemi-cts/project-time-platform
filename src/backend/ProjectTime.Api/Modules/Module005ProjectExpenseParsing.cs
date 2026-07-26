using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;

namespace ProjectTime.Api.Modules;

public static partial class Module005ProjectExpenseUploadModule
{
    private static readonly string[] CertifyCategoryHeaderTokens =
    {
        "Airfare", "Rental", "Hotel", "Meal", "Mileage", "Parking", "Toll", "Misc"
    };

    private static ParsedExpenseFile ParseExpenseFile(string fileName, byte[] bytes)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        List<List<string>> rows;

        if (extension == ".csv")
        {
            rows = ParseCsv(DecodeText(bytes));
        }
        else if (extension is ".xlsx" or ".xlsm")
        {
            using var stream = new MemoryStream(bytes);
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheets.FirstOrDefault()
                ?? throw new InvalidOperationException("The workbook does not contain a worksheet.");
            var used = sheet.RangeUsed()
                ?? throw new InvalidOperationException("The worksheet does not contain expense rows.");
            rows = used.Rows().Select(row => row.Cells(1, used.ColumnCount())
                .Select(cell => cell.GetFormattedString().Trim()).ToList()).ToList();
        }
        else
        {
            throw new InvalidOperationException("Upload an .xlsx, .xlsm, or .csv expense export.");
        }

        return ParseMatrix(rows, extension == ".csv");
    }

    private static ParsedExpenseFile ParseMatrix(IReadOnlyList<List<string>> rows, bool csv)
    {
        var glHeader = FindHeader(rows, new[] { "Employee", "Department Name", "Date", "Category", "GL Code", "Amount" });
        if (glHeader >= 0) return ParseGlDimension(rows, glHeader, csv);

        var categoryHeader = FindCategoryHeader(rows);
        if (categoryHeader >= 0) return ParseCategorySummary(rows, categoryHeader, csv);

        throw new InvalidOperationException("The file is not a recognized Certify Expenses by GL Dimension or Expenses by Category export.");
    }

    private static ParsedExpenseFile ParseGlDimension(IReadOnlyList<List<string>> rows, int headerIndex, bool csv)
    {
        var headers = HeaderMap(rows[headerIndex]);
        var lines = new List<ParsedExpenseLine>();
        var lineNumber = 1;

        for (var index = headerIndex + 1; index < rows.Count; index++)
        {
            var row = rows[index];
            var employee = Cell(row, headers, "Employee");
            var category = NormalizeExpenseCategory(Cell(row, headers, "Category"));
            var amountText = Cell(row, headers, "Amount");
            if (employee.Equals("Total", StringComparison.OrdinalIgnoreCase)) break;
            if (string.IsNullOrWhiteSpace(employee) && category == "Uncategorized" && string.IsNullOrWhiteSpace(amountText)) continue;
            if (!TryDecimal(amountText, out var amount)) continue;

            var reimbursable = TryBoolean(Cell(row, headers, "Reimbursable"), true);
            var reimbursableAmount = TryDecimal(Cell(row, headers, "Reimb Amount"), out var reimbursement)
                ? reimbursement
                : reimbursable ? amount : 0m;
            var currency = NormalizeCurrency(Cell(row, headers, "Currency"));
            var source = headers.ToDictionary(pair => pair.Key, pair => Cell(row, headers, pair.Key), StringComparer.OrdinalIgnoreCase);

            lines.Add(new ParsedExpenseLine(
                lineNumber++, employee, ExtractEmail(employee),
                Cell(row, headers, "Department Name"), Cell(row, headers, "Department Code"),
                ParseDate(Cell(row, headers, "Date")),
                category,
                Cell(row, headers, "GL Code"), amount, reimbursable,
                reimbursableAmount, currency, Cell(row, headers, "Reason"), false,
                JsonSerializer.Serialize(source)));
        }

        if (lines.Count == 0) throw new InvalidOperationException("No expense transactions were found in the GL Dimension export.");
        return BuildParsed(csv ? "csv_gl_dimension" : "gl_dimension", lines, null, null, null);
    }

    private static ParsedExpenseFile ParseCategorySummary(IReadOnlyList<List<string>> rows, int headerIndex, bool csv)
    {
        var headers = rows[headerIndex].Select(value => value.Trim()).ToArray();
        var employeeIndex = Array.FindIndex(headers, value => value.Equals("Employee", StringComparison.OrdinalIgnoreCase));
        var totalIndex = Array.FindIndex(headers, value => value.Equals("Total", StringComparison.OrdinalIgnoreCase));
        var start = FindParameter(rows, headerIndex, "Start Date:");
        var end = FindParameter(rows, headerIndex, "End Date:");
        var lines = new List<ParsedExpenseLine>();
        var lineNumber = 1;

        for (var rowIndex = headerIndex + 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var employee = ValueAt(row, employeeIndex);
            if (employee.Equals("Total", StringComparison.OrdinalIgnoreCase)) break;
            if (string.IsNullOrWhiteSpace(employee)) continue;

            for (var column = 0; column < headers.Length; column++)
            {
                if (column == employeeIndex || column == totalIndex) continue;
                var category = NormalizeExpenseCategory(headers[column]);
                if (category == "Uncategorized" || !TryDecimal(ValueAt(row, column), out var amount) || amount == 0) continue;
                var source = new Dictionary<string, string>
                {
                    ["Employee"] = employee,
                    ["Category"] = category,
                    ["Source Category"] = headers[column],
                    ["Amount"] = amount.ToString(CultureInfo.InvariantCulture),
                    ["Start Date"] = start,
                    ["End Date"] = end
                };
                lines.Add(new ParsedExpenseLine(
                    lineNumber++, employee, ExtractEmail(employee), string.Empty, string.Empty,
                    null, category, string.Empty, amount, true, amount, "USD",
                    $"Category summary for {start} through {end}", true,
                    JsonSerializer.Serialize(source)));
            }
        }

        if (lines.Count == 0) throw new InvalidOperationException("No non-zero category totals were found in the category export.");
        return BuildParsed(csv ? "csv_category_summary" : "category_summary", lines, ParseDate(start), ParseDate(end), null);
    }

    private static ParsedExpenseFile ParseCertifyResponse(JsonElement root, string reportId, DateOnly? periodStart, DateOnly? periodEnd)
    {
        var elements = FindExpenseObjects(root).ToArray();
        var lines = new List<ParsedExpenseLine>();
        var line = 1;
        foreach (var item in elements)
        {
            var amount = JsonDecimal(item, "amount", "expenseAmount", "approvedAmount", "reimbursableAmount");
            if (amount is null) continue;
            var category = NormalizeExpenseCategory(JsonText(item, "category", "categoryName", "expenseCategory", "glCategory"));
            var reimbursable = JsonBoolean(item, true, "reimbursable", "isReimbursable", "billable");
            var reimbursement = JsonDecimal(item, "reimbursableAmount", "reimbursementAmount") ?? (reimbursable ? amount.Value : 0m);
            var employee = JsonText(item, "employeeName", "submitterName", "employee", "ownerName");
            lines.Add(new ParsedExpenseLine(
                line++, employee, JsonText(item, "employeeEmail", "submitterEmail", "email"),
                JsonText(item, "departmentName", "department"), JsonText(item, "departmentCode"),
                ParseDate(JsonText(item, "expenseDate", "date", "transactionDate")),
                category,
                JsonText(item, "glCode", "generalLedgerCode"), amount.Value, reimbursable,
                reimbursement, NormalizeCurrency(JsonText(item, "currency", "currencyCode")),
                JsonText(item, "reason", "description", "merchant"), false, item.GetRawText()));
        }

        if (lines.Count == 0) throw new InvalidOperationException("Certify returned no recognizable expense lines for this report.");
        return BuildParsed("certify_api", lines, periodStart, periodEnd, reportId);
    }

    private static ParsedExpenseFile BuildParsed(string format, List<ParsedExpenseLine> lines, DateOnly? start, DateOnly? end, string? reportId)
    {
        var dated = lines.Where(line => line.ExpenseDate is not null).Select(line => line.ExpenseDate!.Value).ToArray();
        start ??= dated.Length > 0 ? dated.Min() : null;
        end ??= dated.Length > 0 ? dated.Max() : null;
        var currency = lines.Select(line => line.Currency).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "USD";
        return new ParsedExpenseFile(
            format, lines, start, end, currency,
            decimal.Round(lines.Sum(line => line.Amount), 2),
            decimal.Round(lines.Sum(line => line.ReimbursableAmount), 2),
            reportId);
    }

    private static int FindHeader(IReadOnlyList<List<string>> rows, IEnumerable<string> required)
    {
        var requiredValues = required.ToArray();
        for (var index = 0; index < Math.Min(rows.Count, 30); index++)
        {
            var normalized = rows[index].Select(value => value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (requiredValues.All(normalized.Contains)) return index;
        }
        return -1;
    }

    private static int FindCategoryHeader(IReadOnlyList<List<string>> rows)
    {
        for (var index = 0; index < Math.Min(rows.Count, 30); index++)
        {
            var headers = rows[index].Select(value => value.Trim()).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
            var hasEmployee = headers.Any(value => value.Equals("Employee", StringComparison.OrdinalIgnoreCase));
            var hasTotal = headers.Any(value => value.Equals("Total", StringComparison.OrdinalIgnoreCase));
            var categoryCount = headers.Count(value => CertifyCategoryHeaderTokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase)));
            if (hasEmployee && hasTotal && categoryCount > 0) return index;
        }
        return -1;
    }

    private static string NormalizeExpenseCategory(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value)) return "Uncategorized";
        if (value.Contains("Meals (All Employees,Cust)", StringComparison.OrdinalIgnoreCase)) return "SP-Meals (All Employees,Cust)";
        if (value.Contains("Airfare", StringComparison.OrdinalIgnoreCase)) return "SP-Cust Pass Through - Airfare";
        if (value.Contains("Rental", StringComparison.OrdinalIgnoreCase)) return "SP-Cust Pass Through - Rental";
        if (value.Contains("Hotel", StringComparison.OrdinalIgnoreCase)) return "SP-Cust Pass Through-Hotel";
        if (value.Contains("Mileage", StringComparison.OrdinalIgnoreCase)) return "SP-Cust Pass Through-Mileage";
        if (value.Contains("Parking", StringComparison.OrdinalIgnoreCase) || value.Contains("Toll", StringComparison.OrdinalIgnoreCase)) return "SP-Travel, Lodging, Parking";
        if (value.Contains("Meal", StringComparison.OrdinalIgnoreCase)) return "SP-Cust Pass Through-Meals";
        if (value.Contains("Misc", StringComparison.OrdinalIgnoreCase)) return "Miscellaneous";
        return value;
    }

    private static Dictionary<string, int> HeaderMap(IReadOnlyList<string> row) =>
        row.Select((value, index) => new { value = value.Trim(), index })
           .Where(item => !string.IsNullOrWhiteSpace(item.value))
           .GroupBy(item => item.value, StringComparer.OrdinalIgnoreCase)
           .ToDictionary(group => group.Key, group => group.First().index, StringComparer.OrdinalIgnoreCase);

    private static string Cell(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> headers, string name) =>
        headers.TryGetValue(name, out var index) ? ValueAt(row, index) : string.Empty;

    private static string ValueAt(IReadOnlyList<string> row, int index) => index >= 0 && index < row.Count ? row[index].Trim() : string.Empty;

    private static string FindParameter(IReadOnlyList<List<string>> rows, int beforeRow, string label)
    {
        for (var row = 0; row < beforeRow; row++)
        {
            for (var column = 0; column < rows[row].Count; column++)
            {
                var value = rows[row][column].Trim();
                if (!value.StartsWith(label, StringComparison.OrdinalIgnoreCase)) continue;
                var inline = value[label.Length..].Trim();
                if (!string.IsNullOrWhiteSpace(inline)) return inline;
                if (column + 1 < rows[row].Count) return rows[row][column + 1].Trim();
            }
        }
        return string.Empty;
    }

    private static string DecodeText(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                if (quoted && index + 1 < text.Length && text[index + 1] == '"') { cell.Append('"'); index++; }
                else quoted = !quoted;
            }
            else if (character == ',' && !quoted) { row.Add(cell.ToString()); cell.Clear(); }
            else if ((character == '\r' || character == '\n') && !quoted)
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = new List<string>();
            }
            else cell.Append(character);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }

    private static bool TryDecimal(string? text, out decimal value)
    {
        var cleaned = (text ?? string.Empty).Trim().Replace("$", string.Empty).Replace(",", string.Empty);
        if (cleaned.StartsWith('(') && cleaned.EndsWith(')')) cleaned = "-" + cleaned[1..^1];
        return decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.InvariantCulture, out value)
            || decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.CurrentCulture, out value);
    }

    private static bool TryBoolean(string? text, bool fallback)
    {
        var value = (text ?? string.Empty).Trim().ToLowerInvariant();
        return value switch { "yes" or "y" or "true" or "1" => true, "no" or "n" or "false" or "0" => false, _ => fallback };
    }

    private static DateOnly? ParseDate(string? text)
    {
        if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)) return date;
        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var dateTime)) return DateOnly.FromDateTime(dateTime);
        return null;
    }

    private static string ExtractEmail(string? text)
    {
        var value = text ?? string.Empty;
        var open = value.IndexOf('(');
        var close = open >= 0 ? value.IndexOf(')', open + 1) : -1;
        if (open >= 0 && close > open)
        {
            var candidate = value[(open + 1)..close].Trim();
            if (candidate.Contains('@')) return candidate;
        }
        return value.Split(' ', ',', ';').FirstOrDefault(part => part.Contains('@'))?.Trim('(', ')') ?? string.Empty;
    }

    private static string NormalizeCurrency(string? text) => string.IsNullOrWhiteSpace(text) ? "USD" : text.Trim().ToUpperInvariant();

    private static IEnumerable<JsonElement> FindExpenseObjects(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray()) foreach (var item in FindExpenseObjects(child)) yield return item;
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.EnumerateObject().Any(property => property.Name.Contains("amount", StringComparison.OrdinalIgnoreCase))) yield return element;
            else foreach (var property in element.EnumerateObject()) foreach (var item in FindExpenseObjects(property.Value)) yield return item;
        }
    }

    private static string JsonText(JsonElement element, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
            if (names.Any(name => name.Equals(property.Name, StringComparison.OrdinalIgnoreCase)))
                return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : property.Value.ToString();
        return string.Empty;
    }

    private static decimal? JsonDecimal(JsonElement element, params string[] names) => TryDecimal(JsonText(element, names), out var value) ? value : null;
    private static bool JsonBoolean(JsonElement element, bool fallback, params string[] names) => TryBoolean(JsonText(element, names), fallback);
}
