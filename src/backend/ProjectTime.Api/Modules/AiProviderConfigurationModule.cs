using Npgsql;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 064 is the single sanitized configuration, health, and routing
/// boundary for every Celar AI consumer. Provider secrets remain
/// encrypted, write-only, and never appear in health or routing responses.
/// </summary>
public static class AiProviderConfigurationModule
{
    private static readonly SemaphoreSlim GeminiModelChangeLock = new(1, 1);
    private static readonly HashSet<string> AdditionalModuleAdministratorRoles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "SYSTEM_ADMINISTRATOR"
        };

    public static WebApplication MapAiProviderConfigurationEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/ai-configuration",
            (Func<HttpContext, ProjectPulseAiConfiguration, ProjectPulseAiSecretStore, ProjectPulseAiHealthRegistry, ProjectPulseAiHealthCoordinator, CancellationToken, Task<IResult>>)GetConfigurationAsync);
        app.MapGet(
            "/api/ai-configuration/health",
            (Func<HttpContext, ProjectPulseAiConfiguration, ProjectPulseAiHealthRegistry, Task<IResult>>)GetHealthAsync);
        app.MapGet(
            "/api/ai-configuration/providers/{providerCode}/models",
            (Func<string, HttpContext, ProjectPulseAiModelCatalog, CancellationToken, Task<IResult>>)GetModelsAsync);
        app.MapPost(
            "/api/ai-configuration/health/refresh",
            (Func<HttpContext, ProjectPulseAiConfiguration, ProjectPulseAiHealthCoordinator, CancellationToken, Task<IResult>>)RefreshHealthAsync);
        app.MapPut(
            "/api/ai-configuration/providers/{providerCode}/secret",
            (Func<string, HttpContext, ProjectPulseAiConfiguration, ProjectPulseAiSecretStore, ProjectPulseAiHealthRegistry, ProjectPulseAiHealthCoordinator, CancellationToken, Task<IResult>>)ReplaceSecretAsync);
        app.MapPut(
            "/api/ai-configuration/providers/{providerCode}/model",
            (Func<string, HttpContext, ProjectPulseAiConfiguration, ProjectPulseAiSecretStore, ProjectPulseAiHealthRegistry, ProjectPulseAiHealthCoordinator, CancellationToken, Task<IResult>>)ReplaceModelAsync);
        app.MapPut(
            "/api/ai-configuration/providers/{providerCode}/enabled",
            (Func<string, HttpContext, ProjectPulseAiConfiguration, ProjectPulseAiSecretStore, ProjectPulseAiHealthRegistry, ProjectPulseAiHealthCoordinator, CancellationToken, Task<IResult>>)SetEnabledAsync);

        return app;
    }

    private static async Task<IResult> GetModelsAsync(
        string providerCode, HttpContext context, ProjectPulseAiModelCatalog catalog, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context);
        if (authorization is not null) return authorization;
        providerCode = providerCode.Trim().ToLowerInvariant();
        if (providerCode != ProjectPulseAiProviders.Gemini)
            return Results.BadRequest(new { status = "unsupported_provider", message = "Live model discovery is available for Gemini." });
        var refresh = bool.TryParse(context.Request.Query["refresh"], out var requested) && requested;
        return Results.Ok(await catalog.GetAsync(providerCode, refresh, cancellationToken));
    }

    private static async Task<IResult> ReplaceModelAsync(
        string providerCode,
        HttpContext context,
        ProjectPulseAiConfiguration configuration,
        ProjectPulseAiSecretStore store,
        ProjectPulseAiHealthRegistry healthRegistry,
        ProjectPulseAiHealthCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context);
        if (authorization is not null) return authorization;
        if (!SameOrigin(context))
            return Results.Json(
                new { status = "origin_rejected", message = "The request origin is not allowed." },
                statusCode: StatusCodes.Status403Forbidden);
        if (ReleaseConfigurationMutationBlocked() is { } blocked) return blocked;

        providerCode = providerCode.Trim().ToLowerInvariant();
        if (!ProjectPulseAiProviders.Remote.Contains(providerCode, StringComparer.OrdinalIgnoreCase))
            return Results.BadRequest(new { status = "invalid_provider", message = "Select a registered remote AI provider." });

        ReplaceModelRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<ReplaceModelRequest>(cancellationToken);
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.BadRequest(new { status = "invalid_request", message = "A valid JSON request is required." });
        }

        var model = request?.Model?.Trim();
        var current = configuration.Provider(providerCode);
        if (providerCode == ProjectPulseAiProviders.Gemini)
            return await ReplaceGeminiModelAsync(model, context, configuration, store, healthRegistry, cancellationToken);
        if (string.IsNullOrWhiteSpace(model)
            || !current.ApprovedModels.Contains(model, StringComparer.OrdinalIgnoreCase))
            return Results.BadRequest(new { status = "model_not_approved", message = "Select a model from the approved list." });
        if (!current.Configured)
            return Results.BadRequest(new { status = "provider_not_configured", message = "Save the provider API key before changing its model." });

        var previousModel = current.Model;
        try
        {
            await store.SaveModelAsync(
                providerCode,
                model,
                ActualSessionUserId(context)!.Value,
                cancellationToken);
            configuration.ApplyStoredModel(providerCode, model);
            healthRegistry.ApplyConfiguration(configuration.Provider(providerCode));
            var snapshots = await coordinator.RefreshAsync(true, cancellationToken);
            var probe = snapshots.First(item => string.Equals(
                item.Provider,
                providerCode,
                StringComparison.OrdinalIgnoreCase));
            if (probe.ProbeStatus != "available")
            {
                await store.SaveModelAsync(
                    providerCode,
                    previousModel,
                    ActualSessionUserId(context)!.Value,
                    cancellationToken);
                configuration.ApplyStoredModel(providerCode, previousModel);
                healthRegistry.ApplyConfiguration(configuration.Provider(providerCode));
                return Results.BadRequest(new
                {
                    status = "model_test_failed",
                    message = $"{model} could not be verified with the saved key. The previous model remains active.",
                    activeModel = previousModel
                });
            }

            return Results.Ok(new
            {
                status = "model_changed",
                provider = providerCode,
                model,
                tested = true,
                probeStatus = probe.ProbeStatus,
                message = $"{model} was verified and is now active."
            });
        }
        catch (Exception exception)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("AiProviderConfigurationModule")
                .LogError(exception, "Module 064 failed to change the {Provider} model.", providerCode);
            return Results.Json(
                new { status = "model_change_error", message = "The model could not be saved and tested." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> ReplaceGeminiModelAsync(
        string? model, HttpContext context, ProjectPulseAiConfiguration configuration,
        ProjectPulseAiSecretStore store, ProjectPulseAiHealthRegistry health, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model) || !ProjectPulseAiConfiguration.GeminiModelPermitted(model))
            return Results.BadRequest(new { status = "model_not_approved", message = "Select an available Gemini model allowed by this deployment." });
        if (!await GeminiModelChangeLock.WaitAsync(0, cancellationToken))
            return Results.Conflict(new { status = "model_test_in_progress", message = "A Gemini model is already being verified. Try again after it finishes." });
        try
        {
            var current = configuration.Provider(ProjectPulseAiProviders.Gemini);
            if (!current.Configured)
                return Results.BadRequest(new { status = "provider_not_configured", message = "Save the Gemini API key before changing its model." });
            if (current.Secret.Source != "encrypted_database" || string.IsNullOrEmpty(current.Secret.Version))
                return Results.BadRequest(new { status = "provider_key_must_be_saved", message = "Save the Gemini key securely in Module 064 before changing its model, so every API instance can verify the same credential." });
            var snapshot = health.Snapshot(ProjectPulseAiProviders.Gemini);
            if (snapshot.RetryAfterUtc is { } retryAt && retryAt > DateTimeOffset.UtcNow)
                return Results.Json(new { status = "provider_rate_limited", retryAfterUtc = retryAt,
                    message = "Google asked Pulse to pause requests. Wait until the retry time, then test the model." }, statusCode: 429);
            var catalog = context.RequestServices.GetRequiredService<ProjectPulseAiModelCatalog>();
            if (!await catalog.IsAvailableAsync(ProjectPulseAiProviders.Gemini, model, cancellationToken))
                return Results.BadRequest(new { status = "model_unavailable", message = "This model could not be confirmed in Google's current model catalogue. Refresh available models and try again." });
            var candidate = current with
            {
                Model = model,
                Enabled = true,
                ApprovedModels = current.ApprovedModels.Append(model).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
            var provider = context.RequestServices.GetRequiredService<ProjectPulseGeminiProvider>();
            // Test an isolated candidate. Running jobs and other API replicas
            // must never consume a model that has not yet been verified.
            var probe = await provider.ProbeModelAsync(candidate, cancellationToken);
            var latest = configuration.Provider(ProjectPulseAiProviders.Gemini);
            if (latest.ApiKey != current.ApiKey || latest.Secret.Version != current.Secret.Version
                || latest.Secret.Source != current.Secret.Source || latest.Model != current.Model
                || latest.Enabled != current.Enabled || latest.Endpoint != current.Endpoint)
                return Results.Conflict(new { status = "provider_changed", message = "Gemini settings changed while the model was being tested. Refresh the page and try again." });
            if (!probe.Available)
            {
                if (probe.RetryAfterUtc is { } candidateRetryAt)
                    health.RecordProviderCooldown(ProjectPulseAiProviders.Gemini, candidateRetryAt);
                return Results.BadRequest(new { status = "model_test_failed", activeModel = current.Model,
                    diagnostic = probe.Code, retryAfterUtc = probe.RetryAfterUtc,
                    message = "The selected model failed its inference test. The active model was preserved. " + probe.Message });
            }
            if (!await store.TrySaveVerifiedModelAsync(ProjectPulseAiProviders.Gemini, model, current,
                ActualSessionUserId(context)!.Value, cancellationToken)
                || !configuration.TryApplyVerifiedModel(current, model))
                return Results.Conflict(new { status = "provider_changed", message = "Gemini settings changed before the verified model could be activated. Refresh the page to see the current saved settings and try again." });
            health.ApplyConfiguration(configuration.Provider(ProjectPulseAiProviders.Gemini));
            if (current.Enabled) health.RecordProbe(probe);
            return Results.Ok(new { status = "model_changed", provider = ProjectPulseAiProviders.Gemini, model,
                tested = true, probeStatus = "available", message = $"{model} was verified and is now selected." });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Never include provider response bodies, credentials or URLs in errors.
            return Results.Json(new { status = "model_change_error", message = "The Gemini model could not be saved and tested. Refresh the page to confirm its active model." }, statusCode: 503);
        }
        finally { GeminiModelChangeLock.Release(); }
    }

    private static async Task<IResult> SetEnabledAsync(
        string providerCode,
        HttpContext context,
        ProjectPulseAiConfiguration configuration,
        ProjectPulseAiSecretStore store,
        ProjectPulseAiHealthRegistry healthRegistry,
        ProjectPulseAiHealthCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context);
        if (authorization is not null) return authorization;
        if (!SameOrigin(context))
            return Results.Json(
                new { status = "origin_rejected", message = "The request origin is not allowed." },
                statusCode: StatusCodes.Status403Forbidden);
        if (ReleaseConfigurationMutationBlocked() is { } blocked) return blocked;

        providerCode = providerCode.Trim().ToLowerInvariant();
        if (!ProjectPulseAiProviders.Remote.Contains(providerCode, StringComparer.OrdinalIgnoreCase))
            return Results.BadRequest(new { status = "invalid_provider", message = "Select a registered remote AI provider." });

        SetEnabledRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<SetEnabledRequest>(cancellationToken);
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.BadRequest(new { status = "invalid_request", message = "A valid JSON request is required." });
        }

        var enabled = request?.Enabled;
        if (!enabled.HasValue)
            return Results.BadRequest(new { status = "invalid_request", message = "Enabled must be true or false." });

        var provider = configuration.Provider(providerCode);
        if (enabled.Value && !provider.Configured)
            return Results.BadRequest(new { status = "provider_not_configured", message = "Save an API key before enabling this provider." });

        try
        {
            await store.SaveEnabledAsync(
                providerCode,
                enabled.Value,
                provider.Model,
                ActualSessionUserId(context)!.Value,
                cancellationToken);
            configuration.ApplyStoredEnabled(providerCode, enabled.Value);
            healthRegistry.ApplyConfiguration(configuration.Provider(providerCode));
            var probeStatus = enabled.Value
                ? (await coordinator.RefreshAsync(true, cancellationToken))
                    .First(item => string.Equals(item.Provider, providerCode, StringComparison.OrdinalIgnoreCase))
                    .ProbeStatus
                : "disabled";

            return Results.Ok(new
            {
                status = enabled.Value ? "provider_enabled" : "provider_disabled",
                provider = providerCode,
                enabled = enabled.Value,
                probeStatus,
                message = enabled.Value
                    ? $"{provider.DisplayName} is enabled and its health was checked automatically."
                    : $"{provider.DisplayName} is now disabled. The saved key and model were preserved."
            });
        }
        catch (Exception exception)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("AiProviderConfigurationModule")
                .LogError(exception, "Module 064 failed to change the {Provider} enabled state.", providerCode);
            return Results.Json(
                new { status = "provider_state_error", message = "The provider state could not be changed." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> ReplaceSecretAsync(
        string providerCode,
        HttpContext context,
        ProjectPulseAiConfiguration configuration,
        ProjectPulseAiSecretStore secretStore,
        ProjectPulseAiHealthRegistry healthRegistry,
        ProjectPulseAiHealthCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context);
        if (authorization is not null) return authorization;
        if (!SameOrigin(context))
            return Results.Json(
                new { status = "origin_rejected", message = "The request origin is not allowed." },
                statusCode: StatusCodes.Status403Forbidden);
        if (ReleaseConfigurationMutationBlocked() is { } blocked) return blocked;

        providerCode = providerCode.Trim().ToLowerInvariant();
        if (!ProjectPulseAiProviders.Remote.Contains(providerCode, StringComparer.OrdinalIgnoreCase))
            return Results.BadRequest(new { status = "invalid_provider", message = "Select a registered remote AI provider." });
        if (!secretStore.Available)
            return Results.Json(
                new { status = "secure_store_unavailable", message = secretStore.UnavailableReason },
                statusCode: StatusCodes.Status503ServiceUnavailable);

        ReplaceSecretRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<ReplaceSecretRequest>(cancellationToken);
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.BadRequest(new { status = "invalid_request", message = "A valid JSON request is required." });
        }

        var apiKey = request?.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            return Results.BadRequest(new { status = "invalid_secret", message = "API key is required." });
        if (apiKey.Any(char.IsWhiteSpace))
            return Results.BadRequest(new { status = "invalid_secret", message = "API key cannot contain whitespace." });

        try
        {
            var stored = await secretStore.SaveAsync(
                providerCode,
                apiKey,
                ActualSessionUserId(context)!.Value,
                cancellationToken);
            configuration.ApplyStoredSecret(
                stored.ProviderCode,
                stored.ApiKey,
                stored.Version,
                stored.RotatedAt);
            healthRegistry.ApplyConfiguration(configuration.Provider(providerCode));

            if (!configuration.Provider(providerCode).Enabled)
                return Results.Ok(new
                {
                    status = "secret_replaced_provider_disabled", provider = providerCode,
                    configured = true, tested = false, available = false, valueReturned = false,
                    message = $"{configuration.Provider(providerCode).DisplayName} credential was saved securely. Select Enable to activate and test this provider. Other providers remain independently enabled."
                });

            var snapshots = await coordinator.RefreshAsync(true, cancellationToken);
            var probe = snapshots.First(item => string.Equals(
                item.Provider,
                providerCode,
                StringComparison.OrdinalIgnoreCase));
            var available = probe.ProbeStatus == "available";

            return Results.Ok(new
            {
                status = available ? "secret_replaced_and_verified" : "secret_replaced_verification_failed",
                provider = providerCode,
                configured = true,
                tested = true,
                available,
                probeStatus = probe.ProbeStatus,
                probeFailureCode = probe.LastProbeFailureCode,
                version = stored.Version,
                rotatedAt = stored.RotatedAt,
                valueReturned = false,
                message = available
                    ? $"{configuration.Provider(providerCode).DisplayName} API key was saved securely and verified automatically. The value cannot be viewed after saving."
                    : $"{configuration.Provider(providerCode).DisplayName} API key was saved securely, but the provider health check did not pass. The value cannot be viewed after saving."
            });
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.CheckViolation
            && exception.ConstraintName is "ck_ai_provider_secrets_provider_code" or "ai_provider_secrets_provider_code_check")
        {
            return Results.Json(new
            {
                status = "provider_schema_outdated",
                message = "The provider database restriction is out of date. Apply the Module 064 optional-provider migration repair, then save the credential again. No credential was saved."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { status = "invalid_secret", message = exception.Message });
        }
        catch (Exception exception)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("AiProviderConfigurationModule")
                .LogError(exception, "Module 064 failed to replace the {Provider} secret.", providerCode);
            return Results.Json(
                new { status = "secret_store_error", message = "The API key could not be saved securely." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> GetConfigurationAsync(
        HttpContext context,
        ProjectPulseAiConfiguration configuration,
        ProjectPulseAiSecretStore store,
        ProjectPulseAiHealthRegistry health,
        ProjectPulseAiHealthCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context);
        if (authorization is not null) return authorization;

        var release = ProjectPulseAiReleaseRuntimePolicy.RequireValid();
        if (store.Available && !release.IsReleaseScoped)
        {
            foreach (var secret in await store.LoadAsync(cancellationToken))
                configuration.ApplyStoredSecret(
                    secret.ProviderCode,
                    secret.ApiKey,
                    secret.Version,
                    secret.RotatedAt);
            foreach (var setting in await store.LoadModelsAsync(cancellationToken))
                configuration.ApplyStoredModel(setting.Key, setting.Value);
            foreach (var setting in await store.LoadEnabledAsync(cancellationToken))
                configuration.ApplyStoredEnabled(setting.Key, setting.Value);
        }

        health.ApplyConfiguration(configuration.DeepSeek);
        health.ApplyConfiguration(configuration.Claude);
        health.ApplyConfiguration(configuration.OpenAi);
        var snapshots = await coordinator.RefreshAsync(false, cancellationToken);

        return Results.Ok(new
        {
            status = snapshots.Any(item => item.ProbeStatus == "checking")
                ? "configuration_loaded_health_checking"
                : "configuration_loaded",
            generatedAt = DateTimeOffset.UtcNow,
            healthCheckedAutomatically = true,
            configuration = configuration.ToSanitizedResponse(),
            health = snapshots,
            release = new
            {
                phase = release.PhaseCode,
                deploymentManaged = release.IsReleaseScoped,
                configurationSourceCommit = release.ConfigurationSourceCommit,
                configurationSha256 = release.ExpectedConfigurationDigest
            },
            governance = GovernanceState()
        });
    }

    private static async Task<IResult> GetHealthAsync(
        HttpContext context,
        ProjectPulseAiConfiguration configuration,
        ProjectPulseAiHealthRegistry health)
    {
        var authorization = await AuthorizeAdministratorAsync(context);
        if (authorization is not null) return authorization;

        health.ApplyConfiguration(configuration.DeepSeek);
        health.ApplyConfiguration(configuration.Claude);
        health.ApplyConfiguration(configuration.OpenAi);
        var snapshots = health.Snapshots();
        return Results.Ok(new
        {
            module = "064",
            status = OverallStatus(snapshots),
            generatedAt = DateTimeOffset.UtcNow,
            healthIntervalSeconds = configuration.HealthIntervalSeconds,
            requestTimeoutSeconds = configuration.RequestTimeoutSeconds,
            retryCount = configuration.RetryCount,
            maxOutputTokens = configuration.MaxOutputTokens,
            providers = snapshots
        });
    }

    private static async Task<IResult> RefreshHealthAsync(
        HttpContext context,
        ProjectPulseAiConfiguration configuration,
        ProjectPulseAiHealthCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeAdministratorAsync(context);
        if (authorization is not null) return authorization;

        var health = await coordinator.RefreshAsync(true, cancellationToken);
        return Results.Ok(new
        {
            module = "064",
            status = OverallStatus(health),
            message = "Configured provider health checks completed. Disabled or unconfigured providers were not contacted.",
            generatedAt = DateTimeOffset.UtcNow,
            healthIntervalSeconds = configuration.HealthIntervalSeconds,
            providers = health
        });
    }

    private static object GovernanceState() => new
    {
        sourcePhase = "full_shared_runtime_and_automatic_health_center",
        defaultPriority = new[]
        {
            ProjectPulseAiProviders.DeepSeek,
            ProjectPulseAiProviders.Claude,
            ProjectPulseAiProviders.OpenAi,
            ProjectPulseAiProviders.Local
        },
        providerAvailabilityChecked = true,
        automaticStartupHealthCheck = true,
        automaticPeriodicHealthCheck = true,
        liveConfigurationReconciledBeforeRouting = true,
        sanitizedExternalExecutionEnabled = RuntimeFlag("PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION"),
        enterpriseSanitizedExternalFallbackEnabled = RuntimeFlag("PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED"),
        unavailableProvidersSkipped = true,
        safetyRefusalFailover = false,
        secretValuesReturned = false,
        sharedRouterRequiredForAllConsumers = true,
        configurationMutation = "administrator_write_only_secret_replacement",
        secretRotation = "replace_in_place_with_encrypted_version",
        activationAndRollback = "replacement_tested_immediately_rollback_not_exposed",
        immutableAudit = "sanitized_database_audit_enabled",
        azureChanged = false,
        databaseChanged = false,
        entraChanged = false
    };

    private static IResult? ReleaseConfigurationMutationBlocked()
    {
        var release = ProjectPulseAiReleaseRuntimePolicy.Snapshot();
        if (!release.IsReleaseScoped) return null;
        return Results.Json(new
        {
            module = "064",
            status = "deployment_managed_configuration_read_only",
            message = "Public-provider secrets, models, and enabled state are deployment-managed and cannot be changed in candidate or active release phases.",
            configurationSourceCommit = release.ConfigurationSourceCommit,
            stateChanged = false
        }, statusCode: StatusCodes.Status423Locked);
    }

    private static bool RuntimeFlag(string name) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var enabled) && enabled;

    private static string OverallStatus(IReadOnlyList<ProjectPulseAiProviderHealthSnapshot> health)
    {
        var remotes = health.Where(item =>
            !string.Equals(
                item.Provider,
                ProjectPulseAiProviders.Local,
                StringComparison.OrdinalIgnoreCase)).ToArray();

        if (remotes.Any(item => item.ProbeStatus == "available")) return "healthy";
        if (remotes.Any(item => item.Enabled && item.Configured && item.ProbeStatus == "checking"))
            return "checking";
        if (remotes.Any(item => item.Enabled && item.Configured)) return "degraded";
        return "local_fallback_only";
    }

    private static async Task<IResult?> AuthorizeAdministratorAsync(HttpContext context)
    {
        var userId = ActualSessionUserId(context);
        if (userId is null)
        {
            return Results.Json(new
            {
                status = "session_required",
                message = "A ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (ProjectPulseActualSessionAuthority.HasPermanentAdministratorAuthority(
                context,
                Array.Empty<string>()))
            return null;

        var connectionString = ConnectionString();
        if (connectionString is null)
        {
            return Results.Json(new
            {
                status = "configuration_unavailable",
                message = "Administrator authorization could not be verified."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            if (await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(
                    context,
                    cancellationToken: context.RequestAborted))
                return null;

            // Preserve the pre-existing Module 064 SYSTEM_ADMINISTRATOR grant
            // without promoting that role to permanent platform-wide control.
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(context.RequestAborted);
            const string sql = """
                SELECT COALESCE(string_agg(DISTINCT r.role_code, ','), '')
                FROM app_users u
                LEFT JOIN app_user_role_assignments ura
                    ON ura.user_id = u.user_id
                   AND ura.is_active = TRUE
                LEFT JOIN app_roles r
                    ON r.app_role_id = ura.app_role_id
                   AND r.is_active = TRUE
                WHERE u.user_id = @user_id
                  AND u.is_active = TRUE;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("user_id", userId.Value);
            var roleText = (await command.ExecuteScalarAsync(context.RequestAborted))?.ToString() ?? string.Empty;
            var roles = roleText.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (roles.Any(AdditionalModuleAdministratorRoles.Contains)) return null;
        }
        catch (Exception exception)
        {
            var logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("AiProviderConfigurationModule");
            logger.LogWarning(
                exception,
                "Module 064 could not verify administrator authorization.");

            return Results.Json(new
            {
                status = "authorization_unavailable",
                message = "Administrator authorization could not be verified."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Json(new
        {
            status = "access_denied",
            message = "AI Provider Configuration Center is restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    private static Guid? ActualSessionUserId(HttpContext context)
    {
        foreach (var key in new[] { "ProjectPulseActualUserId", "ProjectPulseSessionUserId" })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid userId) return userId;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }

        return null;
    }

    internal static bool SameOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin)
            || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("https" or "http")) return false;

        var fetchSite = context.Request.Headers["Sec-Fetch-Site"].ToString();
        if (string.Equals(fetchSite, "same-origin", StringComparison.OrdinalIgnoreCase))
            return true;

        var forwardedHost = context.Request.Headers["X-Forwarded-Host"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        var publicHost = !string.IsNullOrWhiteSpace(forwardedHost)
            ? HostString.FromUriComponent(forwardedHost)
            : context.Request.Host;

        if (!string.Equals(uri.Host, publicHost.Host, StringComparison.OrdinalIgnoreCase))
            return false;
        return publicHost.Port is null || uri.Port == publicHost.Port;
    }

    private sealed record ReplaceSecretRequest(string? ApiKey);
    private sealed record ReplaceModelRequest(string? Model);
    private sealed record SetEnabledRequest(bool? Enabled);

    private static string? ConnectionString()
    {
        try
        {
            return ProjectPulseAiDatabaseConnection.Resolve();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
