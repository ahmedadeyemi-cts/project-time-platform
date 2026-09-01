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
AssertQuery(
    "What is Kevin Damisch working on?",
    CelarAiInternalDataQueryKind.PersonTaskList,
    "Kevin Damisch",
    false);
AssertQuery(
    "How many active projects and how many tasks does Kevin Damisch have?",
    CelarAiInternalDataQueryKind.PersonWorkSummary,
    "Kevin Damisch",
    true);
AssertQuery(
    "How many tasks and how many active projects does Kevin Damisch have?",
    CelarAiInternalDataQueryKind.PersonWorkSummary,
    "Kevin Damisch",
    true);
AssertProjectQuery(
    "Who is the Account Executive for project P-D?",
    CelarAiInternalDataQueryKind.ProjectStakeholderLookup,
    "P-D",
    "account_executive");
AssertProjectQuery(
    "Who is the sales person for P-D?",
    CelarAiInternalDataQueryKind.ProjectStakeholderLookup,
    "P-D",
    "account_executive");
AssertProjectQuery(
    "Who is the Solution Architect assigned to project P-D?",
    CelarAiInternalDataQueryKind.ProjectStakeholderLookup,
    "P-D",
    "solution_architect");
AssertProjectQuery(
    "Who is the project manager for P-D?",
    CelarAiInternalDataQueryKind.ProjectStakeholderLookup,
    "P-D",
    "project_manager");
AssertProjectQuery(
    "Show me the historical context for project P-D",
    CelarAiInternalDataQueryKind.ProjectHistory,
    "P-D",
    "");
AssertProjectQuery(
    "What happened with project P-D?",
    CelarAiInternalDataQueryKind.ProjectHistory,
    "P-D",
    "");

Require(
    PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("How many projects does Kevin Damisch have assigned to him?"),
    "named person project count remains internal");
Require(
    PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("How many active projects and how many tasks does Kevin Damisch have?"),
    "combined named-person project/task count remains internal");
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
    !PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("Who is the president of Jordan?"),
    "country officeholder question is external eligible");
Require(
    PulseAiSystemKnowledgeCatalog.Analyze("Who is the president of Jordan?").IntentCode == "general_knowledge",
    "country officeholder question resolves to general knowledge");
Require(
    !PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("Who is the king of Jordan?"),
    "country monarch question is external eligible");
Require(
    !PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("Who is the prime minister of Canada?"),
    "country prime-minister question is external eligible");
Require(
    PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("Who is the president of Project Jordan?"),
    "project-named officeholder question remains internal");
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
    PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("Who is the Account Executive for project P-D?"),
    "project Account Executive question remains internal");
Require(
    PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("Show me the historical context for project P-D"),
    "project history question remains internal");
Require(
    !PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("What is zero trust?"),
    "generic definition without Pulse context is external eligible");
Require(
    !PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("How do I bake bread?"),
    "generic how-to without internal context is external eligible");
Require(
    !PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("Outside Pulse, who is Kevin Damisch?"),
    "explicit outside-Pulse override is external eligible");

Require(
    ExternalLooksLikeNonAnswer("I don't have access to real-time information, so I cannot determine who currently holds that office."),
    "current-information access disclaimer is a semantic non-answer");
Require(
    ExternalLooksLikeNonAnswer("I'm sorry, but I can't access current officeholder information."),
    "apologetic current-officeholder disclaimer is a semantic non-answer");
Require(
    ExternalLooksLikeNonAnswer("As an AI language model, I do not have access to live information."),
    "model live-information disclaimer is a semantic non-answer");
Require(
    !ExternalLooksLikeNonAnswer("In C#, code cannot access a private member from an unrelated type because access modifiers enforce encapsulation."),
    "substantive access-control explanation remains an answer");
Require(
    !ExternalLooksLikeNonAnswer("I cannot access a private member from an unrelated type because C# access modifiers enforce encapsulation."),
    "first-person substantive access-control explanation remains an answer");
Require(
    !ExternalLooksLikeNonAnswer("The current officeholder is Example Person. I cannot access private Pulse records, but no such context was needed."),
    "substantive officeholder answer with a later limitation remains an answer");

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

static void AssertProjectQuery(
    string question,
    CelarAiInternalDataQueryKind expectedKind,
    string expectedProject,
    string expectedRole)
{
    var actual = CelarAiInternalDataService.ParseQuestion(question);
    Require(actual is not null, $"project query parsed: {question}");
    Require(actual!.Kind == expectedKind, $"project query kind: {question}");
    Require(actual.ProjectReference == expectedProject, $"project reference: {question}");
    Require(actual.RequestedProjectRole == expectedRole, $"project role: {question}");
}

static void Require(bool condition, string evidence)
{
    if (!condition)
        throw new InvalidOperationException($"Celar AI internal-data assertion failed: {evidence}.");
}

static bool ExternalLooksLikeNonAnswer(string content)
{
    var qualityType = typeof(CelarAiInternalDataService).Assembly.GetType(
        "ProjectTime.Api.Ai.CelarAiExternalAnswerQuality")
        ?? throw new InvalidOperationException("Celar AI external answer-quality type was not found.");
    var method = qualityType.GetMethod(
        "LooksLikeNonAnswer",
        BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("Celar AI external answer-quality method was not found.");
    return (bool)(method.Invoke(null, [content])
        ?? throw new InvalidOperationException("Celar AI external answer-quality method returned no result."));
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
            projectTotal = reader.GetInt64(9);
        }
    }
    Require(projectTotal == 4, "distinct Kevin project total across assignment and project-role authorities");
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

    await using (var project = new NpgsqlCommand(PrivateSql("ExactProjectSql"), connection))
    {
        AddScope(project, effectiveUserId);
        project.Parameters.AddWithValue("normalized_project", "pd");
        await using var reader = await project.ExecuteReaderAsync();
        Require(await reader.ReadAsync(), "P-D resolves inside authorized project scope");
        Require(reader.GetString(1) == "P-D", "P-D project code returned");
        Require(reader.GetString(9) == "Kevin Damish", "P-D project manager returned");
        Require(reader.GetString(10) == "Sales Owner", "P-D Account Executive / Sales owner returned");
        Require(reader.GetString(11) == "Solution Architect", "P-D Solution Architect returned");
        Require(!await reader.ReadAsync(), "P-D exact project resolution is unambiguous");
    }

    await using (var history = new NpgsqlCommand(PrivateSql("ProjectHistorySql"), connection))
    {
        history.Parameters.AddWithValue("project_id", Guid.Parse("20000000-0000-0000-0000-000000000004"));
        await using var reader = await history.ExecuteReaderAsync();
        Require(await reader.ReadAsync(), "P-D immutable lifecycle history returned");
        Require(reader.GetString(2) == "project_updated", "P-D lifecycle event type returned");
        Require(reader.GetString(5).Contains("stakeholder", StringComparison.OrdinalIgnoreCase), "P-D lifecycle event summary returned");
    }

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
