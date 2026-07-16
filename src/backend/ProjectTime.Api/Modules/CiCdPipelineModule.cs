using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static class CiCdPipelineModule
{
    public static WebApplication MapCiCdPipelineEndpoints(this WebApplication app)
    {
        app.MapGet("/api/cicd/configuration", async (HttpContext context) =>
        {
            var access = await RequireAdminAsync(context);
            if (access is not null) return access;

            return Results.Ok(Configuration());
        });

        app.MapGet("/api/cicd/status", async (HttpContext context) =>
        {
            var access = await RequireAdminAsync(context);
            if (access is not null) return access;

            var runs = await ReadRecentRunsAsync();
            return Results.Ok(new
            {
                module = "058",
                status = "cicd_status_loaded",
                configuration = Configuration(),
                runtime = new
                {
                    apiRevision = Env("CONTAINER_APP_REVISION", "Not configured"),
                    apiReplica = Env("CONTAINER_APP_REPLICA_NAME", "Not configured"),
                    apiApplication = Env("CONTAINER_APP_NAME", "ca-phd-test-api-westus3"),
                    webApplication = Env("PROJECTPULSE_CICD_WEB_APP", "ca-phd-test-web-westus3"),
                    deploymentEnvironment = Env("PROJECTPULSE_CICD_ENVIRONMENT", "test")
                },
                recentRuns = runs
            });
        });

        app.MapPost("/api/cicd/dispatch", async (DispatchRequest request, HttpContext context) =>
        {
            var access = await RequireAdminAsync(context);
            if (access is not null) return access;

            if (!ScmTokenConfigured())
                return Results.Json(new
                {
                    status = "scm_action_not_configured",
                    message = "Configure PROJECTPULSE_CICD_SCM_TOKEN before enabling in-application workflow dispatch."
                }, statusCode: 409);

            var workflow = string.IsNullOrWhiteSpace(request.Workflow)
                ? "projectpulse-deploy-test.yml"
                : request.Workflow.Trim();

            var result = await DispatchAsync(
                workflow,
                string.IsNullOrWhiteSpace(request.Ref) ? DefaultBranch() : request.Ref.Trim(),
                request.Inputs ?? new Dictionary<string, string>());

            return result.Success
                ? Results.Accepted(value: new
                {
                    status = "workflow_dispatch_accepted",
                    workflow,
                    sourceRef = request.Ref ?? DefaultBranch()
                })
                : Results.Json(new
                {
                    status = "workflow_dispatch_failed",
                    result.HttpStatus,
                    result.Message
                }, statusCode: 502);
        });

        app.MapPost("/api/cicd/rollback", async (RollbackRequest request, HttpContext context) =>
        {
            var access = await RequireAdminAsync(context);
            if (access is not null) return access;

            if (!ScmTokenConfigured())
                return Results.Json(new
                {
                    status = "scm_action_not_configured",
                    message = "Configure PROJECTPULSE_CICD_SCM_TOKEN before enabling in-application rollback dispatch."
                }, statusCode: 409);

            if (string.IsNullOrWhiteSpace(request.ApiImage) ||
                string.IsNullOrWhiteSpace(request.WebImage))
                return Results.BadRequest(new
                {
                    status = "rollback_images_required",
                    message = "Both immutable API and web image references are required."
                });

            var inputs = new Dictionary<string, string>
            {
                ["environment"] = string.IsNullOrWhiteSpace(request.Environment) ? "test" : request.Environment.Trim(),
                ["api_image"] = request.ApiImage.Trim(),
                ["web_image"] = request.WebImage.Trim(),
                ["reason"] = request.Reason?.Trim() ?? "Administrative rollback"
            };

            var result = await DispatchAsync(
                "projectpulse-rollback.yml",
                DefaultBranch(),
                inputs);

            return result.Success
                ? Results.Accepted(value: new
                {
                    status = "rollback_dispatch_accepted",
                    environment = inputs["environment"]
                })
                : Results.Json(new
                {
                    status = "rollback_dispatch_failed",
                    result.HttpStatus,
                    result.Message
                }, statusCode: 502);
        });

        return app;
    }

    private static object Configuration() => new
    {
        module = "058",
        status = "cicd_configuration_loaded",
        access = "administrators_only",
        sourceControl = new
        {
            provider = Env("PROJECTPULSE_CICD_SCM_PROVIDER", "github"),
            repository = Repository(),
            defaultBranch = DefaultBranch(),
            apiBaseUrl = Env("PROJECTPULSE_CICD_SCM_API_BASE_URL", "https://api.github.com"),
            tokenConfigured = ScmTokenConfigured(),
            portableProviderContract = true
        },
        deployment = new
        {
            provider = Env("PROJECTPULSE_CICD_DEPLOYMENT_PROVIDER", "azure-container-apps"),
            futureProvider = "opencloud",
            environment = Env("PROJECTPULSE_CICD_ENVIRONMENT", "test"),
            apiApplication = Env("PROJECTPULSE_CICD_API_APP", "ca-phd-test-api-westus3"),
            webApplication = Env("PROJECTPULSE_CICD_WEB_APP", "ca-phd-test-web-westus3"),
            registry = Env("PROJECTPULSE_CICD_REGISTRY", "acrphdtest7825cc.azurecr.io"),
            portableOciArtifacts = true
        },
        workflows = new[]
        {
            "projectpulse-ci.yml",
            "projectpulse-deploy-test.yml",
            "projectpulse-deploy-production.yml",
            "projectpulse-rollback.yml"
        },
        safeguards = new[]
        {
            "OIDC workload identity",
            "Environment approvals",
            "One deployment at a time",
            "API before web",
            "Health validation",
            "Automatic rollback",
            "Immutable image digests",
            "No business-data mutation in smoke tests"
        }
    };

    private static async Task<IResult?> RequireAdminAsync(HttpContext context)
    {
        var userId = SessionUserId(context);
        if (userId is null)
            return Results.Json(new
            {
                status = "session_required",
                message = "A ProjectPulse session is required."
            }, statusCode: 401);

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM app_user_role_assignments ura
                JOIN app_roles r
                  ON r.role_id = ura.role_id
                WHERE ura.user_id = @user_id
                  AND COALESCE(
                        NULLIF(to_jsonb(ura)->>'is_active', '')::boolean,
                        TRUE
                      ) = TRUE
                  AND (
                        lower(COALESCE(
                          NULLIF(to_jsonb(r)->>'name', ''),
                          NULLIF(to_jsonb(r)->>'role_name', ''),
                          NULLIF(to_jsonb(r)->>'code', ''),
                          ''
                        )) IN (
                          'administrator',
                          'admin',
                          'super administrator',
                          'system administrator'
                        )
                     OR EXISTS (
                          SELECT 1
                          FROM app_role_permissions rp
                          JOIN app_permissions p
                            ON p.permission_id = rp.permission_id
                          WHERE rp.role_id = r.role_id
                            AND upper(COALESCE(
                              NULLIF(to_jsonb(p)->>'code', ''),
                              NULLIF(to_jsonb(p)->>'permission_code', ''),
                              NULLIF(to_jsonb(p)->>'name', ''),
                              ''
                            )) IN ('SYSTEM_ADMINISTRATION', 'MANAGE_ALL')
                     )
                  )
            );
            """, connection);

        command.Parameters.AddWithValue("user_id", userId.Value);

        try
        {
            var allowed = Convert.ToBoolean(await command.ExecuteScalarAsync());
            return allowed
                ? null
                : Results.Json(new
                {
                    status = "administrator_access_required",
                    message = "Module 058 is restricted to administrators."
                }, statusCode: 403);
        }
        catch (PostgresException)
        {
            await using var fallback = new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1
                    FROM app_user_role_assignments ura
                    JOIN app_roles r
                      ON r.role_id = ura.role_id
                    WHERE ura.user_id = @user_id
                      AND lower(COALESCE(
                        NULLIF(to_jsonb(r)->>'name', ''),
                        NULLIF(to_jsonb(r)->>'role_name', ''),
                        NULLIF(to_jsonb(r)->>'code', ''),
                        ''
                      )) IN (
                        'administrator',
                        'admin',
                        'super administrator',
                        'system administrator'
                      )
                );
                """, connection);
            fallback.Parameters.AddWithValue("user_id", userId.Value);
            var allowed = Convert.ToBoolean(await fallback.ExecuteScalarAsync());
            return allowed
                ? null
                : Results.Json(new
                {
                    status = "administrator_access_required",
                    message = "Module 058 is restricted to administrators."
                }, statusCode: 403);
        }
    }

    private static async Task<object[]> ReadRecentRunsAsync()
    {
        if (!ScmTokenConfigured()) return Array.Empty<object>();

        try
        {
            using var client = ScmClient();
            var response = await client.GetAsync(
                $"{ScmApiBase()}/repos/{Repository()}/actions/runs?per_page=10");

            if (!response.IsSuccessStatusCode) return Array.Empty<object>();

            var raw = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(raw);
            if (!document.RootElement.TryGetProperty("workflow_runs", out var values))
                return Array.Empty<object>();

            return values.EnumerateArray()
                .Select(run => (object)new
                {
                    id = Long(run, "id"),
                    name = Str(run, "name") ?? "Workflow",
                    eventName = Str(run, "event") ?? "",
                    status = Str(run, "status") ?? "",
                    conclusion = Str(run, "conclusion"),
                    branch = Str(run, "head_branch") ?? "",
                    commit = Str(run, "head_sha") ?? "",
                    createdAt = Str(run, "created_at") ?? "",
                    updatedAt = Str(run, "updated_at") ?? "",
                    url = Str(run, "html_url") ?? ""
                })
                .ToArray();
        }
        catch
        {
            return Array.Empty<object>();
        }
    }

    private static async Task<DispatchResult> DispatchAsync(
        string workflow,
        string sourceRef,
        Dictionary<string, string> inputs)
    {
        try
        {
            using var client = ScmClient();
            var body = JsonSerializer.Serialize(new
            {
                @ref = sourceRef,
                inputs
            });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(
                $"{ScmApiBase()}/repos/{Repository()}/actions/workflows/{Uri.EscapeDataString(workflow)}/dispatches",
                content);

            var raw = await response.Content.ReadAsStringAsync();
            return new DispatchResult(
                response.IsSuccessStatusCode,
                (int)response.StatusCode,
                response.IsSuccessStatusCode
                    ? "Accepted"
                    : SafeApiMessage(raw));
        }
        catch (Exception ex)
        {
            return new DispatchResult(false, 0, ex.Message);
        }
    }

    private static HttpClient ScmClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Required("PROJECTPULSE_CICD_SCM_TOKEN"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ProjectPulse-Module-058");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static Guid? SessionUserId(HttpContext context)
    {
        foreach (var key in new[]
                 {
                     "ProjectPulseEffectiveUserId",
                     "ProjectPulseSessionUserId",
                     "ProjectPulseActualUserId"
                 })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid guid) return guid;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static string ConnectionString()
    {
        foreach (var name in new[]
                 {
                     "ConnectionStrings__DefaultConnection",
                     "ConnectionStrings__ProjectPulse",
                     "ConnectionStrings__ProjectTime",
                     "PROJECTPULSE_CONNECTION_STRING",
                     "PROJECTTIME_DATABASE_CONNECTION"
                 })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        throw new InvalidOperationException(
            "ProjectPulse database connection is not configured.");
    }

    private static string Repository() =>
        Env("PROJECTPULSE_CICD_SCM_REPOSITORY",
            "ahmedadeyemi-cts/project-time-platform");

    private static string DefaultBranch() =>
        Env("PROJECTPULSE_CICD_SCM_DEFAULT_BRANCH",
            "source/module-058-cicd-pipeline-20260716");

    private static string ScmApiBase() =>
        Env("PROJECTPULSE_CICD_SCM_API_BASE_URL",
            "https://api.github.com").TrimEnd('/');

    private static bool ScmTokenConfigured() =>
        Has("PROJECTPULSE_CICD_SCM_TOKEN");

    private static string Env(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : fallback;

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} is not configured.");

    private static bool Has(string name) =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name));

    private static string? Str(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long Long(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) &&
        value.TryGetInt64(out var number)
            ? number
            : 0;

    private static string SafeApiMessage(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            return Str(document.RootElement, "message")
                   ?? "The source-control provider rejected the request.";
        }
        catch
        {
            return "The source-control provider rejected the request.";
        }
    }

    private sealed record DispatchRequest(
        string? Workflow,
        string? Ref,
        Dictionary<string, string>? Inputs);

    private sealed record RollbackRequest(
        string? Environment,
        string? ApiImage,
        string? WebImage,
        string? Reason);

    private sealed record DispatchResult(
        bool Success,
        int HttpStatus,
        string Message);
}
