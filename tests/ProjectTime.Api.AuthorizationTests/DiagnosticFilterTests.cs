using Npgsql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ProjectTime.Api.Modules;

internal static class DiagnosticFilterTests
{
    internal static async Task RunAsync()
    {
        var checks = 0;
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        foreach (var path in new[] { "/api/db-config-check", "/api/db-health", "/api/schema/tables",
                     "/api/production-data-readiness", "/api/production/data-readiness" })
        foreach (var spelling in new[] { path, path + "/", path.ToUpperInvariant() + "/" })
        foreach (var mode in new[] { "denied", "allowed", "view_as", "unavailable" })
        {
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Path = spelling;
            context.Response.Body = new MemoryStream();
            if (mode == "view_as") context.Items["ProjectPulseIsViewAs"] = true;
            var filter = new DiagnosticAdministratorFilter(_ => mode == "unavailable"
                ? Task.FromException<bool>(new InvalidOperationException("test-only provider failure"))
                : Task.FromResult(mode != "denied"));
            var reached = false;
            var result = await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(context), _ =>
            {
                reached = true;
                return ValueTask.FromResult<object?>(Results.StatusCode(204));
            });
            await ((IResult)result!).ExecuteAsync(context);
            var expected = mode == "allowed" ? 204 : mode == "unavailable" ? 503 : 403;
            if (context.Response.StatusCode != expected || reached != (mode == "allowed"))
                throw new Exception("Diagnostic endpoint filter failed: " + mode);
            checks++;
        }
        var db = Environment.GetEnvironmentVariable("SECURITY_TEST_DB");
        if (!string.IsNullOrWhiteSpace(db))
        {
            await using var connection = new NpgsqlConnection(db);
            await connection.OpenAsync();
            async Task Exec(string sql)
            {
                await using var command = new NpgsqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync();
            }
            await Exec("""
                CREATE TEMP TABLE app_users(user_id uuid, is_active bool);
                CREATE TEMP TABLE app_roles(app_role_id uuid, role_code text, is_active bool);
                CREATE TEMP TABLE app_user_role_assignments(user_id uuid, app_role_id uuid, is_active bool);
                INSERT INTO app_users VALUES ('11111111-1111-1111-1111-111111111111',TRUE);
                INSERT INTO app_roles VALUES ('22222222-2222-2222-2222-222222222222','ADMINISTRATOR',TRUE);
                INSERT INTO app_user_role_assignments VALUES (
                    '11111111-1111-1111-1111-111111111111','22222222-2222-2222-2222-222222222222',TRUE);
                """);
            var context = new DefaultHttpContext();
            context.Items["ProjectPulseSessionUserId"] = Guid.Parse("11111111-1111-1111-1111-111111111111");
            async Task Check(bool expected)
            {
                if (await DiagnosticAdministratorFilter.IsAdministratorAsync(context, connection) != expected)
                    throw new Exception("Diagnostic actual-session database authority failed.");
                if (context.Items.ContainsKey("ProjectPulsePermanentFullControl"))
                    throw new Exception("Ordinary diagnostic access must not grant permanent Super Administrator authority.");
                checks++;
            }
            await Check(true);
            await Exec("UPDATE app_roles SET role_code='SUPER_ADMINISTRATOR'"); await Check(true);
            await Exec("UPDATE app_roles SET role_code='ENGINEER'"); await Check(false);
            await Exec("UPDATE app_roles SET role_code='ADMINISTRATOR'; UPDATE app_user_role_assignments SET is_active=FALSE");
            await Check(false);
            await Exec("UPDATE app_user_role_assignments SET is_active=TRUE; UPDATE app_roles SET is_active=FALSE");
            await Check(false);
            await Exec("UPDATE app_roles SET is_active=TRUE; UPDATE app_users SET is_active=FALSE");
            await Check(false);
            await Exec("UPDATE app_users SET is_active=TRUE");
            context.Items["ProjectPulseIsViewAs"] = true; await Check(false);
            context.Items.Remove("ProjectPulseIsViewAs");
            context.Items["ProjectPulseSessionUserId"] = Guid.NewGuid(); await Check(false);
        }
        else if (Environment.GetEnvironmentVariable("SECURITY_COMPLETION_REQUIRED") == "true")
            throw new Exception("Isolated database required for diagnostic administrator positive and negative cases.");
        Console.WriteLine($"DIAGNOSTIC_ENDPOINT_FILTER=PASS assertions={checks}");
    }
}
