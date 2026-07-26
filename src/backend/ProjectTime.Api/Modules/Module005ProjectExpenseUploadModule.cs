using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public static partial class Module005ProjectExpenseUploadModule
{
    private const long MaximumUploadBytes = 15L * 1024L * 1024L;
    private const string DefaultCertifyBaseUrl = "https://api.certify.com/v1/";

    private static readonly HashSet<string> SelfRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "ENGINEERING", "ENGINEERING_LEAD", "PROJECT_MANAGEMENT",
        "PROJECT_MANAGEMENT_LEAD", "SUPER_ADMINISTRATOR"
    };

    private static readonly HashSet<string> OnBehalfRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "PROJECT_MANAGEMENT", "PROJECT_MANAGEMENT_LEAD", "SUPER_ADMINISTRATOR"
    };

    private static readonly HashSet<string> CertifyAdminRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "ACCOUNTING", "SUPER_ADMINISTRATOR"
    };

    private static readonly HashSet<string> BillingRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "PROJECT_MANAGEMENT", "PROJECT_MANAGEMENT_LEAD", "PROJECT_TEAM_COORDINATOR",
        "ACCOUNTING", "SUPER_ADMINISTRATOR"
    };

    public static WebApplication MapModule005ProjectExpenseUploadEndpoints(this WebApplication app)
    {
        app.MapGet("/api/project-expenses/context", (Func<HttpContext, Task<IResult>>)GetContextAsync);
        app.MapGet("/api/project-expenses/uploads", (Func<HttpContext, Task<IResult>>)GetUploadsAsync);
        app.MapGet("/api/project-expenses/projects/{projectId:guid}/summary", (Func<Guid, HttpContext, Task<IResult>>)GetProjectSummaryAsync);
        app.MapPost("/api/project-expenses/upload", (Func<HttpContext, Task<IResult>>)UploadFileAsync);
        app.MapDelete("/api/project-expenses/uploads/{uploadId:guid}", (Func<Guid, ExpenseDeleteRequest, HttpContext, Task<IResult>>)DeleteUploadAsync);
        app.MapPost("/api/project-expenses/uploads/{uploadId:guid}/notification/retry", (Func<Guid, HttpContext, Task<IResult>>)RetryNotificationAsync);
        app.MapPost("/api/project-expenses/import/certify", (Func<CertifyImportRequest, HttpContext, Task<IResult>>)ImportFromCertifyAsync);
        return app;
    }

    public static WebApplication MapModule038CertifyConnectionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/certify/connection", (Func<HttpContext, Task<IResult>>)GetCertifyConnectionAsync);
        app.MapPut("/api/certify/connection", (Func<CertifyConnectionUpdateRequest, HttpContext, Task<IResult>>)UpdateCertifyConnectionAsync);
        app.MapPost("/api/certify/connection/test", (Func<HttpContext, Task<IResult>>)TestCertifyConnectionAsync);
        return app;
    }

    private static async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var settings = new Dictionary<string, string?>
        {
            ["PTP_DB_HOST"] = Environment.GetEnvironmentVariable("PTP_DB_HOST"),
            ["PTP_DB_PORT"] = Environment.GetEnvironmentVariable("PTP_DB_PORT"),
            ["PTP_DB_NAME"] = Environment.GetEnvironmentVariable("PTP_DB_NAME"),
            ["PTP_DB_USER"] = Environment.GetEnvironmentVariable("PTP_DB_USER"),
            ["PTP_DB_PASSWORD"] = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD")
        };

        string connectionString;
        if (settings.Any(pair => !string.IsNullOrWhiteSpace(pair.Value)))
        {
            var missing = settings.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Key).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException($"ProjectPulse database configuration is incomplete: {string.Join(", ", missing)}.");

            connectionString = new NpgsqlConnectionStringBuilder
            {
                Host = settings["PTP_DB_HOST"],
                Port = int.TryParse(settings["PTP_DB_PORT"], out var port) ? port : 5432,
                Database = settings["PTP_DB_NAME"],
                Username = settings["PTP_DB_USER"],
                Password = settings["PTP_DB_PASSWORD"],
                Pooling = true,
                MaxPoolSize = 20,
                IncludeErrorDetail = false
            }.ConnectionString;
        }
        else
        {
            connectionString = new[]
            {
                "ConnectionStrings__DefaultConnection", "ConnectionStrings__ProjectPulse",
                "ConnectionStrings__ProjectTime", "PROJECTPULSE_CONNECTION_STRING",
                "PROJECTTIME_DATABASE_CONNECTION"
            }.Select(Environment.GetEnvironmentVariable)
             .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
             ?? throw new InvalidOperationException("ProjectPulse database connection is not configured.");
        }

        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<ExpenseActor?> LoadActorAsync(NpgsqlConnection connection, HttpContext context)
    {
        var actual = ReadGuid(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        if (actual is null) return null;
        var effective = ReadGuid(context, "ProjectPulseEffectiveUserId") ?? actual.Value;
        var isViewAs = context.Items.TryGetValue("ProjectPulseIsViewAs", out var flag) && flag is bool value && value;

        const string sql = """
            SELECT COALESCE(u.display_name, u.email, ''), COALESCE(u.email, ''),
                   COALESCE(array_agg(DISTINCT upper(r.role_code)) FILTER (WHERE r.role_code IS NOT NULL), ARRAY[]::text[])
            FROM app_users u
            LEFT JOIN app_user_role_assignments ura ON ura.user_id=u.user_id AND ura.is_active=TRUE
            LEFT JOIN app_roles r ON r.app_role_id=ura.app_role_id AND r.is_active=TRUE
            WHERE u.user_id=@user_id AND u.is_active=TRUE
            GROUP BY u.user_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", effective);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var roles = reader.GetFieldValue<string[]>(2)
            .Select(ScopedRolePolicyModule.CanonicalRole)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new ExpenseActor(actual.Value, effective, reader.GetString(0), reader.GetString(1), roles, isViewAs);
    }

    private static Guid? ReadGuid(HttpContext context, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid guid) return guid;
            if (Guid.TryParse(Convert.ToString(value), out var parsed)) return parsed;
        }
        return null;
    }

    private static IResult SessionRequired() => Results.Json(new
    {
        status = "session_required",
        message = "A valid ProjectPulse session is required."
    }, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult AccessDenied(string message) => Results.Json(new
    {
        status = "access_denied",
        message
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult ViewAsReadOnly() => Results.Json(new
    {
        status = "view_as_read_only",
        message = "Exit Administrator View-As before changing project expense data."
    }, statusCode: StatusCodes.Status403Forbidden);

    private static bool HasRole(ExpenseActor actor, IEnumerable<string> allowed) =>
        actor.RoleCodes.Any(role => allowed.Contains(role, StringComparer.OrdinalIgnoreCase));

    private static string BillingTreatment(string? contractType)
    {
        var value = (contractType ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Contains("fixed")) return "included_fixed_price";
        if (value.Contains("time") || value.Contains("material") || value.Contains("t&m") || value.Contains("t & m"))
            return "pass_through_invoice";
        return "internal_nonbillable";
    }

    private static DateOnly? ReadDate(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateOnly>(ordinal);

    public sealed record ExpenseDeleteRequest(string Reason);
    public sealed record CertifyImportRequest(Guid ProjectId, Guid ExpenseOwnerUserId, string CertifyReportId, DateOnly? PeriodStart, DateOnly? PeriodEnd);
    public sealed record CertifyConnectionUpdateRequest(string? EnvironmentName, string? BaseUrl, string? ApiKeyEnvironmentName, string? ApiSecretEnvironmentName, string? CompanyId, bool AutomaticSyncEnabled, string? SyncCadence);

    private sealed record ExpenseActor(Guid ActualUserId, Guid EffectiveUserId, string DisplayName, string Email, string[] RoleCodes, bool IsViewAs);
    private sealed record ExpenseProject(Guid ProjectId, Guid? ClientId, string CustomerName, string ProjectCode, string ProjectName, string ContractType, string Status, Guid? ProjectManagerUserId, string ProjectManagerName);
    private sealed record ExpenseOwner(Guid UserId, string DisplayName, string Email, string[] RoleCodes);
    private sealed record ExpenseUploadRecord(Guid UploadId, Guid ProjectId, Guid OwnerUserId, Guid UploadedByUserId, DateOnly? PeriodStart, DateOnly? PeriodEnd);
    private sealed record CertifyProfile(Guid Id, string EnvironmentName, string BaseUrl, string ApiKeyEnvironmentName, string ApiSecretEnvironmentName, string CompanyId, string ConnectionStatus, bool AutomaticSyncEnabled, string SyncCadence, DateTimeOffset? LastTestedAt, string LastTestResult, DateTimeOffset? LastSuccessfulSyncAt);
    private sealed record ParsedExpenseLine(int LineNumber, string EmployeeName, string EmployeeEmail, string DepartmentName, string DepartmentCode, DateOnly? ExpenseDate, string Category, string GlCode, decimal Amount, bool Reimbursable, decimal ReimbursableAmount, string Currency, string Reason, bool IsSummaryLine, string SourceJson);
    private sealed record ParsedExpenseFile(string FormatCode, List<ParsedExpenseLine> Lines, DateOnly? PeriodStart, DateOnly? PeriodEnd, string Currency, decimal TotalAmount, decimal ReimbursableAmount, string? SourceReportId);
    private sealed record CertifyCall(bool Success, int StatusCode, string Message, JsonElement? Json);
    private sealed record MailOutboxRow(Guid OutboxId, string[] To, string[] Cc, string Subject, string TextBody, string HtmlBody);
    private sealed record MailDelivery(bool Success, string Status, string Provider, string ProviderMessageId, string Message);
}
