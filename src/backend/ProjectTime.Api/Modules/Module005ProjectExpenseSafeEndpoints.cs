using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class Module005ProjectExpenseUploadModule
{
    public static WebApplication MapModule005ProjectExpenseUploadEndpointsSafe(this WebApplication app)
    {
        app.MapGet("/api/project-expenses/readiness", (Func<Task<IResult>>)GetProjectExpenseReadinessAsync);
        app.MapGet("/api/project-expenses/context", (Func<HttpContext, Task<IResult>>)GetContextAsync);
        app.MapGet("/api/project-expenses/uploads", (Func<HttpContext, Task<IResult>>)GetUploadsAsync);
        app.MapGet("/api/project-expenses/projects/{projectId:guid}/summary", (Func<Guid, HttpContext, Task<IResult>>)GetProjectSummaryAsync);
        app.MapPost("/api/project-expenses/upload", (Func<HttpContext, Task<IResult>>)UploadFileAsync);
        app.MapDelete("/api/project-expenses/uploads/{uploadId:guid}", (Func<Guid, HttpContext, Task<IResult>>)DeleteUploadFromRequestAsync);
        app.MapPost("/api/project-expenses/uploads/{uploadId:guid}/notification/retry", (Func<Guid, HttpContext, Task<IResult>>)RetryAuthorizedNotificationAsync);
        app.MapPost("/api/project-expenses/import/certify", (Func<CertifyImportRequest, HttpContext, Task<IResult>>)ImportFromCertifyAsync);
        return app;
    }

    private static async Task<IResult> DeleteUploadFromRequestAsync(Guid uploadId, HttpContext context)
    {
        ExpenseDeleteRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<ExpenseDeleteRequest>(
                context.Request.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                context.RequestAborted);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new
            {
                status = "invalid_delete_request",
                message = "Provide a JSON request body containing the required deletion reason."
            });
        }

        return await DeleteUploadAsync(
            uploadId,
            request ?? new ExpenseDeleteRequest(string.Empty),
            context);
    }

    private static async Task<IResult> GetProjectExpenseReadinessAsync()
    {
        try
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = new NpgsqlCommand("""
                SELECT
                    (SELECT COUNT(*) FROM schema_migrations
                     WHERE migration_id IN (
                       '044_project_expense_upload_certify_connection',
                       '044a_project_expense_self_certify_permission')),
                    (SELECT COUNT(*) FROM pg_tables
                     WHERE schemaname='public'
                       AND tablename IN (
                         'project_expense_uploads',
                         'project_expense_lines',
                         'project_expense_events',
                         'project_expense_mail_outbox',
                         'certify_connection_profiles',
                         'certify_expense_import_runs')),
                    (SELECT COUNT(*) FROM certify_connection_profiles
                     WHERE profile_name='default'
                       AND automatic_sync_enabled=FALSE),
                    (SELECT COUNT(*) FROM app_permissions
                     WHERE permission_code IN (
                       'VIEW_PROJECT_EXPENSE_UPLOAD',
                       'UPLOAD_PROJECT_EXPENSE_SELF',
                       'UPLOAD_PROJECT_EXPENSE_ON_BEHALF',
                       'DELETE_PROJECT_EXPENSE_UPLOAD',
                       'IMPORT_PROJECT_EXPENSE_CERTIFY',
                       'VIEW_PROJECT_EXPENSE_INVOICE_CONTEXT',
                       'MANAGE_CERTIFY_CONNECTION'));
                """, connection);
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            var migrationCount = Convert.ToInt32(reader.GetInt64(0));
            var tableCount = Convert.ToInt32(reader.GetInt64(1));
            var safeProfileCount = Convert.ToInt32(reader.GetInt64(2));
            var permissionCount = Convert.ToInt32(reader.GetInt64(3));
            var ready = migrationCount == 2
                && tableCount == 6
                && safeProfileCount == 1
                && permissionCount == 7;

            return Results.Json(new
            {
                status = ready ? "project_expense_runtime_ready" : "project_expense_runtime_incomplete",
                module005 = "Project Expense Upload",
                module038 = "Certify Connection & Sync Center",
                migrationCount,
                tableCount,
                safeProfileCount,
                permissionCount,
                automaticSyncEnabled = false,
                secretsReturned = false
            }, statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception exception)
        {
            return Results.Json(new
            {
                status = "project_expense_runtime_unavailable",
                errorType = exception.GetType().Name,
                message = "The Project Expense and Certify runtime could not verify its database connection."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
