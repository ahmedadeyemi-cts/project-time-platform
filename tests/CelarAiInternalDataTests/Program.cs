using ProjectTime.Api.Ai;
using Npgsql;
using System.Reflection;

AssertQuery(
    "How many projects does Kevin Damisch have assigned to him?",
    CelarAiInternalDataQueryKind.PersonProjectCount,
    "Kevin Damisch",
    true);
AssertQuery(
    "How many active projects are assigned to kevin.damisch@example.com?",
    CelarAiInternalDataQueryKind.PersonProjectCount,
    "kevin.damisch@example.com",
    true);
AssertQuery(
    "List projects assigned to Kevin Damisch",
    CelarAiInternalDataQueryKind.PersonProjectList,
    "Kevin Damisch",
    false);
AssertQuery(
    "Which projects does Kevin Damisch manage?",
    CelarAiInternalDataQueryKind.PersonProjectList,
    "Kevin Damisch",
    false);
AssertQuery(
    "How many tasks are assigned to Kevin Damisch?",
    CelarAiInternalDataQueryKind.PersonTaskCount,
    "Kevin Damisch",
    true);
AssertQuery(
    "Show me tasks for Kevin Damisch",
    CelarAiInternalDataQueryKind.PersonTaskList,
    "Kevin Damisch",
    false);

Require(
    PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("How many projects does Kevin Damisch have assigned to him?"),
    "named person project count remains internal");
Require(
    PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("Who is Kevin Damisch?"),
    "ambiguous named-person question fails private/internal");
Require(
    PulseAiSystemKnowledgeCatalog.Analyze("What is Kevin Damisch?").IntentCode != "general_knowledge",
    "intent analysis preserves named-person capitalization privacy guard");
Require(
    PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("What does Kevin Damisch work on?"),
    "named-person workload question remains internal");
Require(
    PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("how busy is kevin?"),
    "lowercase ambiguous workload question remains internal");
Require(
    PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("How many employees do we have?"),
    "enterprise people count remains internal");
Require(
    PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("What is my current utilization?"),
    "internal metric remains internal");
Require(
    !PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("What is the capital of France?"),
    "clearly public capital question is external eligible");
Require(
    !PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("Who is the US President?"),
    "public US officeholder question is external eligible");
Require(
    PulseAiSystemKnowledgeCatalog.Analyze("Who is the US President?").IntentCode == "general_knowledge",
    "public US officeholder question resolves to general knowledge");
Require(
    !PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("Who is the president of the United States?"),
    "spelled-out public officeholder question is external eligible");
Require(
    PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("Who is the president of Acme Corp?"),
    "named-organization officeholder question remains private");
Require(
    PulseAiSystemKnowledgeCatalog.Analyze("Who is the president of Acme Corp?").IntentCode != "general_knowledge",
    "named-organization officeholder question cannot enter the public-provider route");
Require(
    PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("Who is the project manager for our project?"),
    "internal project-role question remains private");
Require(
    !PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("What is zero trust?"),
    "generic definition without Pulse context is external eligible");
Require(
    !PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("How do I bake bread?"),
    "generic how-to without internal context is external eligible");
Require(
    !PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("Outside Pulse, who is Kevin Damisch?"),
    "explicit outside-Pulse override is external eligible");

var integrationConnectionString = Environment.GetEnvironmentVariable("CELAR_AI_TEST_CONNECTION_STRING");
if (!string.IsNullOrWhiteSpace(integrationConnectionString))
{
    await AssertDatabaseResolverAsync(integrationConnectionString);
}

Console.WriteLine("CELAR_AI_INTERNAL_DATA_PARSER_AND_BOUNDARY=PASS");

static void AssertQuery(
    string question,
    CelarAiInternalDataQueryKind expectedKind,
    string expectedPerson,
    bool expectedCount)
{
    var actual = CelarAiInternalDataService.ParseQuestion(question);
    Require(actual is not null, $"query parsed: {question}");
    Require(actual!.Kind == expectedKind, $"query kind: {question}");
    Require(actual.PersonReference == expectedPerson, $"person reference: {question}");
    Require(actual.CountRequested == expectedCount, $"count/list mode: {question}");
}

static void Require(bool condition, string evidence)
{
    if (!condition)
        throw new InvalidOperationException($"Celar AI internal-data assertion failed: {evidence}.");
}

static async Task AssertDatabaseResolverAsync(string connectionString)
{
    var effectiveUserId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    var expectedPersonId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    var readinessSql = PrivateSql("SourceReadinessSql");
    await using (var readiness = new NpgsqlCommand(readinessSql, connection))
    {
        var problems = await readiness.ExecuteScalarAsync() as string[] ?? [];
        Require(problems.Length == 0, $"database source readiness: {string.Join(",", problems)}");
    }

    Guid personId;
    await using (var identity = new NpgsqlCommand(PrivateSql("ExactPersonSql"), connection))
    {
        AddScope(identity, effectiveUserId);
        identity.Parameters.AddWithValue("normalized_person", "kevindamisch");
        identity.Parameters.AddWithValue("person_lower", "kevin damisch");
        await using var reader = await identity.ExecuteReaderAsync();
        Require(await reader.ReadAsync(), "known Kevin alias resolves");
        personId = reader.GetGuid(0);
        Require(personId == expectedPersonId, "known Kevin alias resolves to expected identity");
        Require(reader.GetBoolean(3), "known Kevin match is a verified alias");
        Require(!await reader.ReadAsync(), "known Kevin alias is unambiguous");
    }

    var projectCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    long projectTotal = 0;
    await using (var projects = new NpgsqlCommand(PrivateSql("PersonProjectsSql"), connection))
    {
        AddScope(projects, effectiveUserId);
        projects.Parameters.AddWithValue("person_user_id", personId);
        await using var reader = await projects.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            projectCodes.Add(reader.GetString(1));
            projectTotal = reader.GetInt64(7);
        }
    }
    Require(projectTotal == 4, "distinct Kevin project total across assignment authorities");
    Require(projectCodes.SetEquals(new[] { "P-A", "P-B", "P-C", "P-D" }), "Kevin project identities and closed-project exclusion");

    var taskCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var taskSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    long taskTotal = 0;
    long taskProjectTotal = 0;
    await using (var tasks = new NpgsqlCommand(PrivateSql("PersonTasksSql"), connection))
    {
        AddScope(tasks, effectiveUserId);
        tasks.Parameters.AddWithValue("person_user_id", personId);
        await using var reader = await tasks.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var taskCode = reader.GetString(4);
            taskCodes.Add(taskCode);
            taskSources[taskCode] = reader.GetString(9);
            taskTotal = reader.GetInt64(10);
            taskProjectTotal = reader.GetInt64(11);
        }
    }
    Require(taskTotal == 2, "deduplicated Kevin active task total");
    Require(taskProjectTotal == 2, "complete distinct project total for Kevin tasks");
    Require(taskCodes.SetEquals(new[] { "TASK-A", "TASK-B" }), "Kevin task identities and closed-project exclusion");
    Require(taskSources["TASK-A"] == "work_register_task_assignment_history", "Work Register row takes precedence over mirrored canonical assignment");

    Console.WriteLine("CELAR_AI_INTERNAL_DATA_DATABASE_RESOLVER=PASS");
}

static string PrivateSql(string fieldName) =>
    typeof(CelarAiInternalDataService)
        .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)?
        .GetValue(null) as string
    ?? throw new InvalidOperationException($"Celar AI SQL contract field was not found: {fieldName}.");

static void AddScope(NpgsqlCommand command, Guid effectiveUserId)
{
    command.Parameters.AddWithValue("effective_user_id", effectiveUserId);
    command.Parameters.AddWithValue("is_broad_scope", true);
    command.Parameters.AddWithValue("can_view_managed_projects", true);
    command.Parameters.AddWithValue("can_view_team_scope", true);
}
