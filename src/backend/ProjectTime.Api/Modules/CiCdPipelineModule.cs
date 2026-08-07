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

            object[] runs = Array.Empty<object>();
            string integrationStatus = ScmTokenConfigured()
                ? "configured"
                : "runtime_token_not_configured";
            string? integrationWarning = null;

            if (ScmTokenConfigured())
            {
                try
                {
                    runs = await ReadRecentRunsAsync();
                }
                catch (Exception ex)
                {
                    integrationStatus = "degraded";
                    integrationWarning = ex.Message;
                }
            }

            return Results.Ok(new
            {
                module = "058",
                status = "cicd_status_loaded",
                configured = true,
                degraded = integrationStatus != "configured",
                configuration = Configuration(),
                repository = new
                {
                    provider = Env("PROJECTPULSE_CICD_SCM_PROVIDER", "github"),
                    name = Repository(),
                    branch = DefaultBranch(),
                    sourceCommit = Env(
                        "PROJECTPULSE_CICD_SOURCE_COMMIT",
                        "Not configured"),
                    repositoryUrl = $"https://github.com/{Repository()}",
                    runtimeConnection = integrationStatus,
                    warning = integrationWarning
                },
                runtime = new
                {
                    apiRevision = Env(
                        "CONTAINER_APP_REVISION",
                        "Not configured"),
                    apiReplica = Env(
                        "CONTAINER_APP_REPLICA_NAME",
                        "Not configured"),
                    apiApplication = Env(
                        "PROJECTPULSE_CICD_API_APP",
                        Env("CONTAINER_APP_NAME", "ca-phd-test-api-westus3")),
                    webApplication = Env(
                        "PROJECTPULSE_CICD_WEB_APP",
                        "ca-phd-test-web-westus3"),
                    deploymentEnvironment = Env(
                        "PROJECTPULSE_CICD_ENVIRONMENT",
                        "test"),
                    registry = Env(
                        "PROJECTPULSE_CICD_REGISTRY",
                        "acrphdtest7825cc.azurecr.io")
                },
                integration = new
                {
                    scm = integrationStatus,
                    workflowDispatchEnabled = ScmTokenConfigured(),
                    oidcConfigured = false
                },
                recentRuns = runs
            });
        });

        app.MapPost("/api/cicd/dispatch", async (DispatchRequest request, HttpContext context) =>
        {
            var access = await RequireAdminAsync(context);
            if (access is not null) return access;

            var workflow = string.IsNullOrWhiteSpace(request.Workflow)
                ? "projectpulse-ci.yml"
                : request.Workflow.Trim();

            if (!string.Equals(workflow, "projectpulse-ci.yml", StringComparison.Ordinal))
                return Results.Json(new
                {
                    status = "protected_workflow_dispatch_required",
                    message = "Test, production, and rollback workflows require their workflow-specific release inputs and GitHub environment protections. Open the protected workflow in GitHub Actions."
                }, statusCode: StatusCodes.Status422UnprocessableEntity);

            if (request.Inputs is { Count: > 0 })
                return Results.BadRequest(new
                {
                    status = "validation_workflow_inputs_not_allowed",
                    message = "The in-application source validation workflow does not accept deployment inputs."
                });

            if (!ScmTokenConfigured())
                return Results.Json(new
                {
                    status = "scm_action_not_configured",
                    message = "Configure PROJECTPULSE_CICD_SCM_TOKEN before enabling in-application workflow dispatch."
                }, statusCode: 409);

            var result = await DispatchAsync(
                workflow,
                string.IsNullOrWhiteSpace(request.Ref) ? DefaultBranch() : request.Ref.Trim(),
                new Dictionary<string, string>());

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
            "projectpulse-ci.yml"
        },
        protectedWorkflows = new object[]
        {
            new
            {
                name = "projectpulse-deploy-test.yml",
                environment = "test",
                requiredInputs = new[] { "release_commit", "confirmation" },
                launch = "github_actions_only"
            },
            new
            {
                name = "projectpulse-deploy-production.yml",
                environment = "production",
                requiredInputs = new[] { "release_commit" },
                launch = "github_actions_only"
            },
            new
            {
                name = "projectpulse-rollback.yml",
                environment = "selected_environment",
                requiredInputs = new[] { "environment", "api_image", "web_image", "reason" },
                launch = "governed_rollback_endpoint"
            }
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
        if (context.Request.Method != HttpMethods.Get && GovernedOperationsReadModule.IsViewAs(context))
            return Results.Json(new { module = "058", status = "view_as_read_only", message = "CI/CD mutations are blocked while View-As is active." }, statusCode: StatusCodes.Status403Forbidden);

        return await GovernedOperationsReadModule.AuthorizeAsync(
            context,
            "058",
            ["SUPER_ADMINISTRATOR", "ADMINISTRATOR"],
            ["SYSTEM_ADMINISTRATION", "MANAGE_ALL"]);
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
            "main");

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
