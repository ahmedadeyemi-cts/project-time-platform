using System.Collections;
using System.Reflection;
using System.Text.Json;
using Npgsql;
using ProjectTime.Api.Modules;

// Disposable PostgreSQL only. Temporary fixtures cannot touch application tables.
var settings = new NpgsqlConnectionStringBuilder {
    Host = "127.0.0.1", Port = 5432, Database = "enterprise_completion_test", Username = "postgres",
    Password = Environment.GetEnvironmentVariable("PGPASSWORD") ?? throw new Exception("Disposable PostgreSQL password required")
};
await using var connection = new NpgsqlConnection(settings.ConnectionString);
await connection.OpenAsync();
async Task Sql(string sql) { await using var command = new NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
await Sql("""
CREATE TEMP TABLE app_users(user_id uuid, display_name text, email text, is_active boolean, manager_email text);
CREATE TEMP TABLE app_roles(app_role_id uuid, role_code text, is_active boolean);
CREATE TEMP TABLE app_user_role_assignments(user_id uuid, app_role_id uuid, is_active boolean);
CREATE TEMP TABLE projects(project_id uuid, project_manager_user_id uuid, project_coordinator_user_id uuid);
CREATE TEMP TABLE time_entries(timesheet_id uuid, work_date date, project_id uuid);
INSERT INTO app_users SELECT ('00000000-0000-0000-0000-'||lpad(n::text,12,'0'))::uuid, 'User '||n, 'user'||n||'@example.invalid', n<>5, CASE WHEN n=1 THEN 'user7@example.invalid' ELSE '' END FROM generate_series(1,7) n;
INSERT INTO app_roles VALUES('00000000-0000-0000-0000-000000000010','PROJECT_TEAM_COORDINATOR',true);
INSERT INTO app_user_role_assignments VALUES('00000000-0000-0000-0000-000000000004','00000000-0000-0000-0000-000000000010',true);
INSERT INTO projects VALUES
 ('00000000-0000-0000-0000-000000000020','00000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000003'),
 ('00000000-0000-0000-0000-000000000021','00000000-0000-0000-0000-000000000005','00000000-0000-0000-0000-000000000002'),
 ('00000000-0000-0000-0000-000000000022','00000000-0000-0000-0000-000000000004','00000000-0000-0000-0000-000000000006');
INSERT INTO time_entries VALUES
 ('00000000-0000-0000-0000-000000000030','2026-09-20','00000000-0000-0000-0000-000000000020'),
 ('00000000-0000-0000-0000-000000000030','2026-09-20','00000000-0000-0000-0000-000000000020'),
 ('00000000-0000-0000-0000-000000000030','2026-09-20','00000000-0000-0000-0000-000000000021'),
 ('00000000-0000-0000-0000-000000000030','2026-09-19','00000000-0000-0000-0000-000000000022'),
 ('00000000-0000-0000-0000-000000000031','2026-09-20','00000000-0000-0000-0000-000000000022'),
 ('00000000-0000-0000-0000-000000000030','2026-09-21',null);
""");
var assembly = typeof(ModuleAvailabilityModule).Assembly;
Type Type(string name) => assembly.GetType("ProjectTime.Api.Modules." + name, true)!;
object Record(string name, Dictionary<string, object?> values) {
    var ctor = Type(name).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
    return ctor.Invoke(ctor.GetParameters().Select(p => values.TryGetValue(p.Name!, out var value) ? value
        : p.ParameterType == typeof(string) ? "" : p.ParameterType == typeof(JsonElement) ? JsonSerializer.SerializeToElement(new { })
        : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray());
}
async Task<(string Email, string Kind)[]> Resolve(string strategy, string? date, int subject = 1) {
    var policy = Record("EnterpriseNotificationPolicyRow", new() { ["RecipientStrategy"] = strategy });
    var entry = Record("EnterpriseNotificationEventRow", new() {
        ["EntityId"] = Guid.Parse("00000000-0000-0000-0000-000000000030"),
        ["SubjectUserId"] = Guid.Parse($"00000000-0000-0000-0000-{subject:D12}"),
        ["Payload"] = JsonSerializer.SerializeToElement(new { workDate = date }) });
    var method = Type("EnterpriseNotificationRecipientResolver").GetMethod("ResolveAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
    var task = (Task)method.Invoke(null, [connection, policy, entry, CancellationToken.None])!;
    await task;
    var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
    var rows = (IEnumerable)result.GetType().GetProperty("Recipients")!.GetValue(result)!;
    return rows.Cast<object>().Select(row => ((string)row.GetType().GetProperty("Email")!.GetValue(row)!, (string)row.GetType().GetProperty("RecipientType")!.GetValue(row)!)).ToArray();
}
void Expect((string Email, string Kind)[] actual, params (string Email, string Kind)[] expected) {
    if (!actual.OrderBy(x => x.Email).SequenceEqual(expected.OrderBy(x => x.Email))) throw new Exception("Incorrect notification recipients: " + JsonSerializer.Serialize(actual));
}
// Both project roles, multiple projects, duplicate entries/roles, inactive users,
// unrelated PTC, another work date and another timesheet are all exercised.
Expect(await Resolve("timesheet_engineer", "2026-09-20"), ("user1@example.invalid", "to"), ("user2@example.invalid", "cc"), ("user3@example.invalid", "cc"));
Expect(await Resolve("timesheet_engineer", "2026-09-21"), ("user1@example.invalid", "to"));
Expect(await Resolve("timesheet_engineer", null), ("user1@example.invalid", "to"));
Expect(await Resolve("timesheet_engineer", "2026-09-20", 2), ("user2@example.invalid", "to"), ("user3@example.invalid", "cc"));
Expect(await Resolve("timesheet_manager", "2026-09-20"), ("user7@example.invalid", "to"));
Console.WriteLine("5 PostgreSQL recipient scenarios passed; no email or Teams messages were sent.");
