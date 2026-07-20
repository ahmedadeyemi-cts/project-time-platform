using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 065 owns privileged Microsoft Entra application-credential lifecycle
/// governance. Module 010 remains the owner of tenant settings and user sync.
/// The default adapter is deliberately locked and performs no external change.
/// </summary>
public static class EntraSecretAdministrationModule
{
    private const string ModuleNumber = "065";
    private const string ContractVersion = "2026-07-19.2";
    private const string ImplementationBaseline = "2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4";
    private const string DelegatedPermission = "MANAGE_ENTRA_SECRET";
    private const int MaximumSecretBytes = 4096;
    private static readonly TimeSpan StepUpLifetime = TimeSpan.FromMinutes(5);

    public static WebApplication MapEntraSecretAdministrationEndpoints(
        this WebApplication app,
        IEntraSecretRotationAdapter? rotationAdapter = null)
    {
        var adapter = rotationAdapter ?? LockedEntraSecretRotationAdapter.Instance;

        app.MapGet(
            "/api/entra-secret-administration/capabilities",
            (Func<HttpContext, Task<IResult>>)(context => GetCapabilitiesAsync(context, adapter)));
        app.MapGet(
            "/api/entra-secret-administration/metadata",
            (Func<HttpContext, Task<IResult>>)GetMetadataAsync);
        app.MapGet(
            "/api/entra-secret-administration/readiness",
            (Func<HttpContext, Task<IResult>>)(context => GetReadinessAsync(context, adapter)));
        app.MapGet(
            "/api/entra-secret-administration/workflow-contract",
            (Func<HttpContext, Task<IResult>>)GetWorkflowContractAsync);
        app.MapGet(
            "/api/entra-secret-administration/audit-contract",
            (Func<HttpContext, Task<IResult>>)GetAuditContractAsync);

        // Mutation contracts are intentionally registered in a fail-closed state.
        // Access, external authorization, adapter, and recent server-established
        // step-up checks all run before any request body is read.
        app.MapPost(
            "/api/entra-secret-administration/rotations/prepare",
            (Func<HttpContext, Task<IResult>>)(context => PrepareRotationAsync(context, adapter)));
        app.MapPost(
            "/api/entra-secret-administration/rotations/{operationId:guid}/approve",
            (Guid operationId, HttpContext context) => ApproveRotationAsync(operationId, context, adapter));
        app.MapPut(
            "/api/entra-secret-administration/rotations/{operationId:guid}/secret",
            (Guid operationId, HttpContext context) => StageSecretAsync(operationId, context, adapter));
        app.MapPost(
            "/api/entra-secret-administration/rotations/{operationId:guid}/test",
            (Guid operationId, HttpContext context) => TestRotationAsync(operationId, context, adapter));
        app.MapPost(
            "/api/entra-secret-administration/rotations/{operationId:guid}/activate",
            (Guid operationId, HttpContext context) => ActivateRotationAsync(operationId, context, adapter));
        app.MapPost(
            "/api/entra-secret-administration/rotations/{operationId:guid}/rollback",
            (Guid operationId, HttpContext context) => RollbackRotationAsync(operationId, context, adapter));

        return app;
    }

    private static async Task<IResult> GetCapabilitiesAsync(
        HttpContext context,
        IEntraSecretRotationAdapter adapter)
    {
        var access = await ResolveAccessAsync(context);
        if (access.Failure is not null) return access.Failure;
        var gate = RotationGate(adapter);

        return Results.Ok(new
        {
            module = ModuleNumber,
            moduleName = "Entra Secret Administration",
            status = "capabilities_loaded",
            contractVersion = ContractVersion,
            implementationBaseline = ImplementationBaseline,
            access = AccessResponse(access.Context!, context, gate),
            phase = "065_COMPLETE_SOURCE_LOCKED_RUNTIME",
            moduleOwnership = new
            {
                module010 = "tenant settings and Entra user synchronization",
                module057 = "consumer of identity and organization data",
                module062 = "shared identity profile abstraction",
                module065 = "application credential lifecycle governance"
            },
            rotation = new
            {
                gate.Enabled,
                gate.MutationSwitchEnabled,
                gate.ExternalAuthorizationRecorded,
                gate.ApprovedAdapterConfigured,
                adapter = adapter.AdapterCode,
                stepUpLifetimeMinutes = (int)StepUpLifetime.TotalMinutes,
                requiredControls = RotationControls()
            },
            secretBoundary = new
            {
                requestBodyReadBeforeGate = false,
                stageContentType = "application/octet-stream",
                maximumBytes = MaximumSecretBytes,
                returnedByApi = false,
                storedInBrowser = false,
                exported = false,
                logged = false,
                includedInAudit = false,
                inMemoryBufferZeroed = true
            },
            externalChanges = new { azure = false, entra = false, database = false, deployment = false }
        });
    }

    private static async Task<IResult> GetMetadataAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context);
        if (access.Failure is not null) return access.Failure;

        var metadata = await CredentialMetadataAsync(access.Context!.ConnectionString, context);
        if (metadata is null) return DependencyUnavailable("credential_metadata_unavailable");

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "credential_metadata_loaded",
            metadata.ApplicationName,
            metadata.Environment,
            metadata.TenantId,
            metadata.ClientId,
            credentialType = "client_secret",
            metadata.ActiveVersion,
            metadata.Fingerprint,
            metadata.LastRotationAt,
            metadata.ExpiresAt,
            metadata.DaysUntilExpiration,
            metadata.Health,
            metadata.SecretConfigured,
            valueVisibility = "never_returned",
            tenantMetadataSource = metadata.TenantMetadataSource,
            credentialMetadataSource = "approved non-secret runtime metadata",
            module010Preserved = true
        });
    }

    private static async Task<IResult> GetReadinessAsync(
        HttpContext context,
        IEntraSecretRotationAdapter adapter)
    {
        var access = await ResolveAccessAsync(context);
        if (access.Failure is not null) return access.Failure;

        var metadata = await CredentialMetadataAsync(access.Context!.ConnectionString, context);
        if (metadata is null) return DependencyUnavailable("rotation_readiness_unavailable");

        var approvedStoreConfigured = Has("PROJECTPULSE_ENTRA_SECRET_STORE_REFERENCE");
        var stepUpPolicyConfigured = Has("PROJECTPULSE_ENTRA_SECRET_STEP_UP_POLICY");
        var dualApprovalPolicyConfigured = Has("PROJECTPULSE_ENTRA_SECRET_DUAL_APPROVAL_POLICY");
        var gate = RotationGate(adapter);

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "rotation_readiness_loaded",
            readyForAuthorizedAdapterReview = metadata.SecretConfigured
                && !string.IsNullOrWhiteSpace(metadata.ActiveVersion)
                && !string.IsNullOrWhiteSpace(metadata.Fingerprint)
                && approvedStoreConfigured
                && stepUpPolicyConfigured,
            readyForMutation = gate.Enabled,
            checks = new[]
            {
                Check("tenant_metadata", !string.IsNullOrWhiteSpace(metadata.TenantId), "Module 010 or approved runtime configuration supplies a tenant ID."),
                Check("client_metadata", !string.IsNullOrWhiteSpace(metadata.ClientId), "Module 010 or approved runtime configuration supplies a client ID."),
                Check("active_secret", metadata.SecretConfigured, "An active secret is configured; Module 065 returns only this boolean signal."),
                Check("version_metadata", !string.IsNullOrWhiteSpace(metadata.ActiveVersion), "A non-sensitive active-version identifier is configured."),
                Check("fingerprint_metadata", !string.IsNullOrWhiteSpace(metadata.Fingerprint), "A non-sensitive externally calculated fingerprint is configured."),
                Check("expiration_metadata", metadata.ExpiresAt is not null, "An expiration timestamp is configured."),
                Check("approved_store", approvedStoreConfigured, "An approved secret-store reference is configured but never returned."),
                Check("step_up_policy", stepUpPolicyConfigured, "A server-side step-up policy reference is configured."),
                Check("dual_approval_policy", dualApprovalPolicyConfigured, "A dual-approval policy reference is configured when required."),
                Check("external_authorization", gate.ExternalAuthorizationRecorded, "An external Azure/Entra authorization record is configured."),
                Check("approved_adapter", gate.ApprovedAdapterConfigured, "A reviewed credential-store rotation adapter is injected."),
                Check("mutation_switch", gate.MutationSwitchEnabled, "The explicit credential-mutation switch is enabled.")
            },
            mutationBodyReadWhenLocked = false,
            defaultAdapterLocked = adapter is LockedEntraSecretRotationAdapter
        });
    }

    private static async Task<IResult> GetWorkflowContractAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context);
        if (access.Failure is not null) return access.Failure;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "rotation_workflow_contract_loaded",
            stateMachine = new[]
            {
                State("prepared", "A non-secret rotation plan has been recorded."),
                State("awaiting_approval", "A second eligible actor must approve when dual approval is required."),
                State("approved", "Required approvals are complete and cannot be supplied by the initiating actor."),
                State("secret_staged", "The write-only value exists only in the approved credential store."),
                State("validated", "A sanitized token-acquisition test passed without exposing a token."),
                State("active_overlap", "The new version is explicitly active while the prior version remains available for rollback."),
                State("active", "The overlap window completed and the new version remains active."),
                State("rolled_back", "The prior approved version was explicitly restored."),
                State("failed", "A sanitized terminal failure was recorded without provider payload or exception text.")
            },
            transitions = new[]
            {
                Transition("prepare", "none", "prepared"),
                Transition("request approval", "prepared", "awaiting_approval"),
                Transition("approve", "awaiting_approval", "approved"),
                Transition("stage write-only secret", "prepared or approved", "secret_staged"),
                Transition("token-acquisition test", "secret_staged", "validated"),
                Transition("explicit activation", "validated", "active_overlap"),
                Transition("complete overlap", "active_overlap", "active"),
                Transition("rollback", "active_overlap", "rolled_back")
            },
            invariants = new[]
            {
                "The initiating actor cannot satisfy a required second approval.",
                "Activation cannot precede a successful sanitized token-acquisition test.",
                "The previous version remains available during the approved overlap window.",
                "A rollback target is an approved previous version, never arbitrary secret material.",
                "Secret values and tokens never appear in responses, audit, logs, exports, or browser storage.",
                "Every mutation uses the actual session identity and blocks View-As authority transfer."
            },
            secretTransport = new
            {
                method = "PUT",
                contentType = "application/octet-stream",
                metadataHeaders = new[]
                {
                    "X-ProjectPulse-Secret-Version"
                },
                responseContainsSecret = false
            }
        });
    }

    private static async Task<IResult> GetAuditContractAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context);
        if (access.Failure is not null) return access.Failure;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "audit_contract_loaded",
            appendOnly = true,
            immutable = true,
            secretValueRecorded = false,
            requiredFields = new[]
            {
                "actorUserId",
                "actorEmail",
                "action",
                "timestamp",
                "environment",
                "priorVersionIdentifier",
                "newVersionIdentifier",
                "approvalActorUserId",
                "validationResult",
                "activationResult",
                "rollbackResult",
                "correlationId"
            },
            prohibitedFields = new[]
            {
                "clientSecret",
                "accessToken",
                "refreshToken",
                "authorizationCode",
                "secretStoreReference",
                "providerPayload",
                "providerRequest",
                "exceptionText",
                "connectionString"
            },
            persistence = "requires separately approved append-only audit implementation"
        });
    }

    private static async Task<IResult> PrepareRotationAsync(
        HttpContext context,
        IEntraSecretRotationAdapter adapter)
    {
        var mutation = await ResolveMutationAccessAsync(context, adapter, "Rotation preparation");
        if (mutation.Failure is not null) return mutation.Failure;

        RotationPreparationRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<RotationPreparationRequest>(
                cancellationToken: context.RequestAborted);
        }
        catch
        {
            return InvalidRequest("A valid non-secret rotation preparation body is required.");
        }

        var validation = ValidatePreparation(request);
        if (validation is not null) return validation;

        var command = new EntraSecretPreparation(
            request!.Environment.Trim(),
            request.ProposedVersion.Trim(),
            request.ExpiresAt!.Value,
            request.OverlapHours,
            request.DualApprovalRequired,
            request.Reason.Trim());

        return await ExecuteAdapterAsync(context, () => adapter.PrepareAsync(
            command,
            Actor(mutation.Context!, context),
            context.RequestAborted));
    }

    private static async Task<IResult> ApproveRotationAsync(
        Guid operationId,
        HttpContext context,
        IEntraSecretRotationAdapter adapter)
    {
        var mutation = await ResolveMutationAccessAsync(context, adapter, "Rotation approval");
        if (mutation.Failure is not null) return mutation.Failure;

        RotationApprovalRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<RotationApprovalRequest>(
                cancellationToken: context.RequestAborted);
        }
        catch
        {
            return InvalidRequest("A valid approval body is required.");
        }

        var decision = request?.Decision?.Trim().ToLowerInvariant();
        if (decision is not ("approve" or "reject"))
        {
            return InvalidRequest("Decision must be approve or reject.");
        }
        if ((request?.Note?.Length ?? 0) > 500)
        {
            return InvalidRequest("Approval note cannot exceed 500 characters.");
        }

        return await ExecuteAdapterAsync(context, () => adapter.ApproveAsync(
            operationId,
            new EntraSecretApproval(decision, request?.Note?.Trim() ?? string.Empty),
            Actor(mutation.Context!, context),
            context.RequestAborted));
    }

    private static async Task<IResult> StageSecretAsync(
        Guid operationId,
        HttpContext context,
        IEntraSecretRotationAdapter adapter)
    {
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["Pragma"] = "no-cache";

        var mutation = await ResolveMutationAccessAsync(context, adapter, "Secret staging");
        if (mutation.Failure is not null) return mutation.Failure;

        if (context.Request.ContentType is null
            || !context.Request.ContentType.StartsWith("application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "write_only_content_type_required",
                message = "Use application/octet-stream for the write-only secret body."
            }, statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        var proposedVersion = context.Request.Headers["X-ProjectPulse-Secret-Version"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(proposedVersion) || proposedVersion.Length > 100)
        {
            return InvalidRequest("X-ProjectPulse-Secret-Version is required and cannot exceed 100 characters.");
        }

        var body = new byte[MaximumSecretBytes + 1];
        var length = 0;
        try
        {
            while (length < body.Length)
            {
                var read = await context.Request.Body.ReadAsync(
                    body.AsMemory(length, body.Length - length),
                    context.RequestAborted);
                if (read == 0) break;
                length += read;
            }

            if (length == 0)
            {
                return InvalidRequest("A write-only secret body is required.");
            }
            if (length > MaximumSecretBytes)
            {
                return Results.Json(new
                {
                    module = ModuleNumber,
                    status = "secret_body_too_large",
                    message = $"The write-only secret body cannot exceed {MaximumSecretBytes} bytes."
                }, statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            using var lease = new SensitiveSecretLease(body, length);
            body = Array.Empty<byte>();
            return await ExecuteAdapterAsync(context, () => adapter.StageSecretAsync(
                operationId,
                proposedVersion,
                lease,
                Actor(mutation.Context!, context),
                context.RequestAborted));
        }
        finally
        {
            if (body.Length > 0) System.Security.Cryptography.CryptographicOperations.ZeroMemory(body);
        }
    }

    private static async Task<IResult> TestRotationAsync(
        Guid operationId,
        HttpContext context,
        IEntraSecretRotationAdapter adapter)
    {
        var mutation = await ResolveMutationAccessAsync(context, adapter, "Token-acquisition testing");
        if (mutation.Failure is not null) return mutation.Failure;

        return await ExecuteAdapterAsync(context, () => adapter.TestAsync(
            operationId,
            Actor(mutation.Context!, context),
            context.RequestAborted));
    }

    private static async Task<IResult> ActivateRotationAsync(
        Guid operationId,
        HttpContext context,
        IEntraSecretRotationAdapter adapter)
    {
        var mutation = await ResolveMutationAccessAsync(context, adapter, "Credential activation");
        if (mutation.Failure is not null) return mutation.Failure;

        return await ExecuteAdapterAsync(context, () => adapter.ActivateAsync(
            operationId,
            Actor(mutation.Context!, context),
            context.RequestAborted));
    }

    private static async Task<IResult> RollbackRotationAsync(
        Guid operationId,
        HttpContext context,
        IEntraSecretRotationAdapter adapter)
    {
        var mutation = await ResolveMutationAccessAsync(context, adapter, "Credential rollback");
        if (mutation.Failure is not null) return mutation.Failure;

        RotationRollbackRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<RotationRollbackRequest>(
                cancellationToken: context.RequestAborted);
        }
        catch
        {
            return InvalidRequest("A valid rollback body is required.");
        }

        if (string.IsNullOrWhiteSpace(request?.TargetVersion)
            || request.TargetVersion.Length > 100
            || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Length > 500)
        {
            return InvalidRequest("TargetVersion and a reason of at most 500 characters are required.");
        }

        return await ExecuteAdapterAsync(context, () => adapter.RollbackAsync(
            operationId,
            new EntraSecretRollback(request.TargetVersion.Trim(), request.Reason.Trim()),
            Actor(mutation.Context!, context),
            context.RequestAborted));
    }

    private static IResult? ValidatePreparation(RotationPreparationRequest? request)
    {
        if (request is null) return InvalidRequest("A rotation preparation body is required.");
        if (request.Environment?.Trim().ToLowerInvariant() is not ("test" or "production"))
        {
            return InvalidRequest("Environment must be test or production.");
        }
        if (string.IsNullOrWhiteSpace(request.ProposedVersion) || request.ProposedVersion.Length > 100)
        {
            return InvalidRequest("ProposedVersion is required and cannot exceed 100 characters.");
        }
        if (request.ExpiresAt is null || request.ExpiresAt <= DateTimeOffset.UtcNow.AddDays(1))
        {
            return InvalidRequest("ExpiresAt must be more than one day in the future.");
        }
        if (request.OverlapHours is < 1 or > 168)
        {
            return InvalidRequest("OverlapHours must be between 1 and 168.");
        }
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500)
        {
            return InvalidRequest("A reason of at most 500 characters is required.");
        }
        return null;
    }

    private static async Task<AccessOutcome> ResolveMutationAccessAsync(
        HttpContext context,
        IEntraSecretRotationAdapter adapter,
        string action)
    {
        var access = await ResolveAccessAsync(context);
        if (access.Failure is not null) return access;

        if (IsViewAs(context))
        {
            return new(null, Results.Json(new
            {
                module = ModuleNumber,
                status = "actual_session_required",
                message = "View-As cannot grant Entra credential-mutation authority."
            }, statusCode: StatusCodes.Status403Forbidden));
        }

        var gate = RotationGate(adapter);
        if (!gate.Enabled)
        {
            return new(null, Results.Json(new
            {
                module = ModuleNumber,
                status = "external_authorization_required",
                action,
                bodyRead = false,
                gate.MutationSwitchEnabled,
                gate.ExternalAuthorizationRecorded,
                gate.ApprovedAdapterConfigured,
                message = "Azure/Entra mutation remains locked. The request body was not read."
            }, statusCode: 423));
        }

        var stepUpAt = StepUpAuthenticatedAt(context);
        if (stepUpAt is null)
        {
            return new(null, Results.Json(new
            {
                module = ModuleNumber,
                status = "recent_step_up_required",
                bodyRead = false,
                lifetimeMinutes = (int)StepUpLifetime.TotalMinutes,
                message = "A recent server-established step-up authentication context is required."
            }, statusCode: 428));
        }

        return access with { Context = access.Context! with { StepUpAuthenticatedAt = stepUpAt } };
    }

    private static async Task<AccessOutcome> ResolveAccessAsync(HttpContext context)
    {
        var actualUserId = SessionUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        var effectiveUserId = SessionUserId(context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId");
        if (actualUserId is null || effectiveUserId is null)
        {
            return new(null, Results.Json(new
            {
                module = ModuleNumber,
                status = "session_required",
                message = "A valid ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return new(null, AuthorizationUnavailable());

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT DISTINCT
                    upper(COALESCE(r.role_code, '')) AS role_code,
                    upper(COALESCE(p.permission_code, '')) AS permission_code
                FROM app_user_role_assignments ura
                JOIN app_roles r
                  ON r.app_role_id = ura.app_role_id
                 AND r.is_active = TRUE
                LEFT JOIN app_role_permissions rp
                  ON rp.app_role_id = r.app_role_id
                LEFT JOIN app_permissions p
                  ON p.app_permission_id = rp.app_permission_id
                WHERE ura.user_id = @user_id
                  AND ura.is_active = TRUE;
                """, connection);
            command.Parameters.AddWithValue("user_id", actualUserId.Value);

            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0) && !string.IsNullOrWhiteSpace(reader.GetString(0))) roles.Add(reader.GetString(0));
                if (!reader.IsDBNull(1) && !string.IsNullOrWhiteSpace(reader.GetString(1))) permissions.Add(reader.GetString(1));
            }

            var allowed =
                roles.Contains("SUPER_ADMINISTRATOR")
                || roles.Contains("ADMINISTRATOR")
                || permissions.Contains(DelegatedPermission);
            if (!allowed)
            {
                return new(null, Results.Json(new
                {
                    module = ModuleNumber,
                    status = "entra_secret_administration_required",
                    permission = DelegatedPermission,
                    message = "Super Administrator, Administrator, or explicitly delegated Entra-secret administration access is required."
                }, statusCode: StatusCodes.Status403Forbidden));
            }

            return new(new(
                actualUserId.Value,
                effectiveUserId.Value,
                ActualEmail(context),
                roles,
                permissions,
                connectionString,
                null), null);
        }
        catch
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("EntraSecretAdministrationModule")
                .LogWarning("Module 065 authorization dependency is unavailable; raw exception detail was suppressed.");
            return new(null, AuthorizationUnavailable());
        }
    }

    private static async Task<CredentialState?> CredentialMetadataAsync(
        string connectionString,
        HttpContext context)
    {
        var tenantId = string.Empty;
        var clientId = string.Empty;
        var source = "runtime_environment";

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT COALESCE(tenant_id, ''), COALESCE(client_id, '')
                FROM azure_entra_settings
                ORDER BY created_at
                LIMIT 1;
                """, connection);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                tenantId = reader.GetString(0).Trim();
                clientId = reader.GetString(1).Trim();
                source = "module_010_azure_entra_settings";
            }
        }
        catch
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("EntraSecretAdministrationModule")
                .LogWarning("Module 065 could not read Module 010 metadata; raw exception detail was suppressed.");
        }

        if (string.IsNullOrWhiteSpace(tenantId)) tenantId = Env("PROJECTPULSE_ENTRA_TENANT_ID", string.Empty);
        if (string.IsNullOrWhiteSpace(clientId)) clientId = Env("PROJECTPULSE_ENTRA_CLIENT_ID", string.Empty);

        var expiresAt = ParseTimestamp("PROJECTPULSE_ENTRA_SECRET_EXPIRES_AT");
        var days = expiresAt is null
            ? (int?)null
            : (int)Math.Floor((expiresAt.Value - DateTimeOffset.UtcNow).TotalDays);
        var secretConfigured = Has("PROJECTPULSE_ENTRA_CLIENT_SECRET");
        var health = !secretConfigured
            ? "not_configured"
            : days is null
                ? "expiration_unknown"
                : days < 0
                    ? "expired"
                    : days <= 14
                        ? "critical"
                        : days <= 30
                            ? "warning"
                            : "healthy";

        return new(
            Env("PROJECTPULSE_ENTRA_APPLICATION_NAME", "ProjectPulse"),
            Env("PROJECTPULSE_ENTRA_MODE", "not_configured"),
            tenantId,
            clientId,
            Env("PROJECTPULSE_ENTRA_SECRET_VERSION", string.Empty),
            Env("PROJECTPULSE_ENTRA_SECRET_FINGERPRINT", string.Empty),
            ParseTimestamp("PROJECTPULSE_ENTRA_SECRET_ROTATED_AT"),
            expiresAt,
            days,
            health,
            secretConfigured,
            source);
    }

    private static object AccessResponse(
        AccessContext access,
        HttpContext context,
        RotationGateState gate) => new
    {
        actualUserId = access.ActualUserId,
        effectiveUserId = access.EffectiveUserId,
        roles = access.Roles.OrderBy(value => value),
        delegatedPermission = access.Permissions.Contains(DelegatedPermission),
        canView = true,
        canRotate = gate.Enabled && !IsViewAs(context),
        isViewAs = IsViewAs(context),
        authoritySource = "actual ProjectPulse session"
    };

    private static EntraSecretActor Actor(AccessContext access, HttpContext context) => new(
        access.ActualUserId,
        access.ActualEmail,
        access.StepUpAuthenticatedAt
            ?? throw new InvalidOperationException("Step-up context was not established."),
        CorrelationId(context));

    private static RotationGateState RotationGate(IEntraSecretRotationAdapter adapter)
    {
        var mutationSwitch = True("PROJECTPULSE_ENTRA_SECRET_MUTATION_ENABLED");
        var authorization = Has("PROJECTPULSE_ENTRA_SECRET_EXTERNAL_AUTHORIZATION_ID");
        return new(
            mutationSwitch && authorization && adapter.IsConfigured,
            mutationSwitch,
            authorization,
            adapter.IsConfigured);
    }

    private static DateTimeOffset? StepUpAuthenticatedAt(HttpContext context)
    {
        if (!context.Items.TryGetValue("ProjectPulseStepUpSatisfied", out var satisfied)
            || satisfied is not true
            || !context.Items.TryGetValue("ProjectPulseStepUpAuthenticatedAt", out var raw))
        {
            return null;
        }

        DateTimeOffset? timestamp = raw switch
        {
            DateTimeOffset value => value,
            DateTime value => new DateTimeOffset(value),
            _ when DateTimeOffset.TryParse(raw?.ToString(), out var parsed) => parsed,
            _ => null
        };

        var now = DateTimeOffset.UtcNow;
        return timestamp is not null
            && timestamp <= now.AddMinutes(1)
            && timestamp >= now.Subtract(StepUpLifetime)
                ? timestamp
                : null;
    }

    private static string[] RotationControls() =>
    [
        "actual-session least privilege",
        "recent server-established step-up authentication",
        "optional policy-driven dual approval with separation of duties",
        "write-only secret transport",
        "approved encrypted credential store adapter",
        "sanitized token-acquisition test",
        "explicit activation",
        "bounded overlap cutover",
        "approved previous-version rollback",
        "append-only sanitized audit evidence"
    ];

    private static object Check(string code, bool passed, string description) => new { code, passed, description };
    private static object State(string code, string description) => new { code, description };
    private static object Transition(string action, string from, string to) => new { action, from, to };

    private static async Task<IResult> ExecuteAdapterAsync(
        HttpContext context,
        Func<Task<EntraSecretOperationResult>> execute)
    {
        try
        {
            return OperationResponse(await execute());
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "request_cancelled",
                message = "The credential lifecycle request was cancelled.",
                secretReturned = false,
                tokenReturned = false
            }, statusCode: 499);
        }
        catch
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("EntraSecretAdministrationModule")
                .LogWarning("A Module 065 adapter operation failed; raw exception detail was suppressed.");
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "credential_adapter_failed",
                message = "The approved credential adapter could not complete the requested operation.",
                secretReturned = false,
                tokenReturned = false
            }, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static IResult OperationResponse(EntraSecretOperationResult result)
    {
        var status = SafeIdentifier(result.Status, result.Succeeded ? "operation_completed" : "operation_not_completed");
        var state = SafeIdentifier(result.State, string.Empty);
        return Results.Json(new
        {
            module = ModuleNumber,
            status,
            message = result.Succeeded
                ? "The governed credential lifecycle operation completed."
                : status == "external_authorization_required"
                    ? "The credential lifecycle operation remains locked pending external authorization."
                    : "The governed credential lifecycle operation did not complete.",
            result.OperationId,
            state = string.IsNullOrWhiteSpace(state) ? null : state,
            versionIdentifier = SafeIdentifier(result.VersionIdentifier, string.Empty),
            correlationId = SafeIdentifier(result.CorrelationId, string.Empty),
            result.RecordedAt,
            secretReturned = false,
            tokenReturned = false,
            adapterMessageReturned = false
        }, statusCode: result.Succeeded ? StatusCodes.Status200OK : 423);
    }

    private static string SafeIdentifier(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100) return fallback;
        return value.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '_' or '-' or '.' or ':')
            ? value
            : fallback;
    }

    private static IResult InvalidRequest(string message) => Results.BadRequest(new
    {
        module = ModuleNumber,
        status = "invalid_request",
        message
    });

    private static IResult AuthorizationUnavailable() => Results.Json(new
    {
        module = ModuleNumber,
        status = "authorization_dependency_unavailable",
        message = "Entra secret-administration authorization is temporarily unavailable."
    }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult DependencyUnavailable(string status) => Results.Json(new
    {
        module = ModuleNumber,
        status,
        message = "Required non-secret credential metadata is temporarily unavailable."
    }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static DateTimeOffset? ParseTimestamp(string name) =>
        DateTimeOffset.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : null;

    private static string Env(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static bool Has(string name) =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name));

    private static bool True(string name) =>
        string.Equals(Environment.GetEnvironmentVariable(name), "true", StringComparison.OrdinalIgnoreCase);

    private static Guid? SessionUserId(HttpContext context, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid id) return id;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static string ActualEmail(HttpContext context)
    {
        foreach (var key in new[] { "ProjectPulseActualEmail", "ProjectPulseSessionEmail" })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (!string.IsNullOrWhiteSpace(value?.ToString())) return value!.ToString()!.Trim().ToLowerInvariant();
        }
        return "unknown";
    }

    private static string CorrelationId(HttpContext context) =>
        string.IsNullOrWhiteSpace(context.TraceIdentifier)
            ? Guid.NewGuid().ToString("N")
            : context.TraceIdentifier;

    private static bool IsViewAs(HttpContext context) =>
        context.Items.TryGetValue("ProjectPulseIsViewAs", out var value)
        && value is bool isViewAs
        && isViewAs;

    private static string? BuildConnectionString()
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

        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password)) return null;

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MaxPoolSize = 5
        }.ConnectionString;
    }

    private sealed record RotationPreparationRequest(
        string Environment,
        string ProposedVersion,
        DateTimeOffset? ExpiresAt,
        int OverlapHours,
        bool DualApprovalRequired,
        string Reason);

    private sealed record RotationApprovalRequest(string Decision, string? Note);
    private sealed record RotationRollbackRequest(string TargetVersion, string Reason);
    private sealed record AccessOutcome(AccessContext? Context, IResult? Failure);
    private sealed record AccessContext(
        Guid ActualUserId,
        Guid EffectiveUserId,
        string ActualEmail,
        IReadOnlySet<string> Roles,
        IReadOnlySet<string> Permissions,
        string ConnectionString,
        DateTimeOffset? StepUpAuthenticatedAt);
    private sealed record RotationGateState(
        bool Enabled,
        bool MutationSwitchEnabled,
        bool ExternalAuthorizationRecorded,
        bool ApprovedAdapterConfigured);
    private sealed record CredentialState(
        string ApplicationName,
        string Environment,
        string TenantId,
        string ClientId,
        string ActiveVersion,
        string Fingerprint,
        DateTimeOffset? LastRotationAt,
        DateTimeOffset? ExpiresAt,
        int? DaysUntilExpiration,
        string Health,
        bool SecretConfigured,
        string TenantMetadataSource);
}
