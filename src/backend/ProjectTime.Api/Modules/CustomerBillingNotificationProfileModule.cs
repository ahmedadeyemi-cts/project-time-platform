using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public static class CustomerBillingNotificationProfileModule
{
    internal const string PolicyCode = "CUSTOMER_INVOICE_WORKFLOW_ACTION_REQUIRED";

    private static readonly string[] ManageCustomerRoles =
    [
        "SUPER_ADMINISTRATOR",
        "ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR"
    ];

    private static readonly string[] ManageCustomerPermissions =
    [
        "MANAGE_CUSTOMERS",
        "MANAGE_ALL",
        "SYSTEM_ADMINISTRATION"
    ];

    public static WebApplication MapCustomerBillingNotificationProfileEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/customers/{clientId:guid}/billing-notification-profile",
            (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)GetProfileAsync);
        app.MapPut(
            "/api/customers/{clientId:guid}/billing-notification-profile",
            (Func<Guid, CustomerBillingNotificationProfileUpdateRequest, HttpContext, CancellationToken, Task<IResult>>)UpdateProfileAsync);
        return app;
    }

    private static async Task<IResult> GetProfileAsync(
        Guid clientId,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var userId = SessionUserId(context);
        if (!userId.HasValue) return SessionRequired();

        await using var connection = await EnterpriseNotificationRepository.OpenConnectionAsync(cancellationToken);
        if (!await IsReadyAsync(connection, cancellationToken))
            return ProfileMigrationRequired();

        var customerName = await LoadCustomerNameAsync(connection, clientId, cancellationToken);
        if (customerName is null)
            return Results.NotFound(new { status = "customer_not_found", message = "Customer was not found." });

        var profile = await LoadProfileAsync(connection, clientId, cancellationToken);
        return Results.Ok(ToResponse(clientId, customerName, profile));
    }

    private static async Task<IResult> UpdateProfileAsync(
        Guid clientId,
        CustomerBillingNotificationProfileUpdateRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var userId = SessionUserId(context);
        if (!userId.HasValue) return SessionRequired();
        if (IsViewAs(context)) return Results.Forbid();

        var notes = (request.WorkflowNotes ?? string.Empty).Trim();
        if (notes.Length > 4000)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Customer billing workflow notes may not exceed 4,000 characters."
            });
        }

        await using var connection = await EnterpriseNotificationRepository.OpenConnectionAsync(cancellationToken);
        if (!await IsReadyAsync(connection, cancellationToken))
            return ProfileMigrationRequired();
        if (!await CanManageCustomersAsync(connection, userId.Value, cancellationToken))
        {
            return Results.Json(new
            {
                status = "access_denied",
                message = "Customer billing notification profiles are restricted to Customer Directory managers."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var customerName = await LoadCustomerNameAsync(connection, clientId, cancellationToken);
        if (customerName is null)
            return Results.NotFound(new { status = "customer_not_found", message = "Customer was not found." });

        var prior = await LoadProfileAsync(connection, clientId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            CustomerBillingNotificationProfile saved;
            await using (var command = new NpgsqlCommand("""
                INSERT INTO customer_billing_notification_profiles (
                    client_id,
                    invoice_generated_action_required_enabled,
                    workflow_notes,
                    version,
                    updated_by_user_id,
                    created_at,
                    updated_at
                )
                VALUES (
                    @client_id,
                    @enabled,
                    @workflow_notes,
                    1,
                    @actor_user_id,
                    NOW(),
                    NOW()
                )
                ON CONFLICT (client_id)
                DO UPDATE SET
                    invoice_generated_action_required_enabled = EXCLUDED.invoice_generated_action_required_enabled,
                    workflow_notes = EXCLUDED.workflow_notes,
                    version = customer_billing_notification_profiles.version + 1,
                    updated_by_user_id = EXCLUDED.updated_by_user_id,
                    updated_at = NOW()
                RETURNING
                    invoice_generated_action_required_enabled,
                    workflow_notes,
                    version,
                    updated_at;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("client_id", clientId);
                command.Parameters.AddWithValue("enabled", request.InvoiceGeneratedActionRequiredEnabled);
                command.Parameters.AddWithValue("workflow_notes", notes);
                command.Parameters.AddWithValue("actor_user_id", userId.Value);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                await reader.ReadAsync(cancellationToken);
                saved = new(
                    reader.GetBoolean(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    reader.GetFieldValue<DateTimeOffset>(3));
            }

            await using (var audit = new NpgsqlCommand("""
                INSERT INTO customer_billing_notification_profile_audit (
                    customer_billing_notification_profile_audit_id,
                    client_id,
                    action_code,
                    actor_user_id,
                    prior_state,
                    new_state,
                    occurred_at
                )
                VALUES (
                    @audit_id,
                    @client_id,
                    'PROFILE_UPDATED',
                    @actor_user_id,
                    @prior_state,
                    @new_state,
                    NOW()
                );
                """, connection, transaction))
            {
                audit.Parameters.AddWithValue("audit_id", Guid.NewGuid());
                audit.Parameters.AddWithValue("client_id", clientId);
                audit.Parameters.AddWithValue("actor_user_id", userId.Value);
                audit.Parameters.Add("prior_state", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(new
                {
                    enabled = prior.Enabled,
                    workflowNotes = prior.WorkflowNotes,
                    version = prior.Version
                });
                audit.Parameters.Add("new_state", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(new
                {
                    enabled = saved.Enabled,
                    workflowNotes = saved.WorkflowNotes,
                    version = saved.Version
                });
                await audit.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new
            {
                status = "customer_billing_notification_profile_saved",
                message = saved.Enabled
                    ? "Customer invoice workflow notifications are enabled for Email and Teams."
                    : "Customer invoice workflow notifications are disabled.",
                profile = ToResponse(clientId, customerName, saved)
            });
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    internal static async Task<CustomerInvoiceWorkflowNotificationOutcome> QueueInvoiceCreatedAsync(
        Guid projectId,
        Guid? clientId,
        Guid invoiceId,
        string invoiceNumber,
        string invoiceType,
        decimal invoiceTotal,
        Guid actorUserId,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!clientId.HasValue)
            return new(false, false, "customer_not_linked");

        try
        {
            await using var connection = await EnterpriseNotificationRepository.OpenConnectionAsync(cancellationToken);
            if (!await IsReadyAsync(connection, cancellationToken))
                return new(false, false, "customer_profile_migration_required");

            var source = await LoadNotificationSourceAsync(connection, clientId.Value, cancellationToken);
            if (source is null)
                return new(false, false, "customer_not_found");
            if (!source.Profile.Enabled)
                return new(false, false, "customer_notification_disabled");

            var occurredAt = DateTimeOffset.UtcNow;
            var correlationId = string.IsNullOrWhiteSpace(context.TraceIdentifier)
                ? $"customer-invoice-workflow-{invoiceId:N}"
                : context.TraceIdentifier;
            var payload = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["invoiceNumber"] = invoiceNumber,
                ["invoiceType"] = invoiceType,
                ["invoiceTotal"] = invoiceTotal.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                ["customerName"] = source.CustomerName,
                ["customerBillingProfileVersion"] = source.Profile.Version,
                ["status"] = "invoice_created",
                ["deepLink"] = "#invoice-billing-center",
                ["correlationId"] = correlationId
            });

            await EnterpriseNotificationRepository.InsertEventAsync(
                connection,
                PolicyCode,
                "042",
                $"customer-invoice-workflow:{invoiceId:N}",
                $"enterprise:customer-invoice-workflow:{invoiceId:N}",
                "billing_invoice",
                invoiceId,
                projectId,
                null,
                occurredAt,
                occurredAt,
                payload,
                "module_042_native",
                actorUserId,
                correlationId,
                cancellationToken);

            try
            {
                await EnterpriseNotificationOrchestrationService.RunAsync(
                    context,
                    actorUserId,
                    "event",
                    false,
                    25,
                    cancellationToken);
            }
            catch
            {
                // The durable event stays queued for Module 065 retry processing.
            }

            return new(true, true, "customer_invoice_workflow_notification_queued");
        }
        catch
        {
            // Invoice creation is already committed. A notification issue must
            // never reverse or duplicate the financial transaction.
            return new(true, false, "customer_invoice_workflow_notification_pending");
        }
    }

    private static async Task<bool> IsReadyAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                to_regclass('public.customer_billing_notification_profiles') IS NOT NULL
                AND to_regclass('public.customer_billing_notification_profile_audit') IS NOT NULL
                AND EXISTS (
                    SELECT 1
                    FROM schema_migrations
                    WHERE migration_id = '134_customer_billing_notification_profiles'
                );
            """, connection);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<string?> LoadCustomerNameAsync(
        NpgsqlConnection connection,
        Guid clientId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT client_name FROM clients WHERE client_id=@client_id;",
            connection);
        command.Parameters.AddWithValue("client_id", clientId);
        return (await command.ExecuteScalarAsync(cancellationToken))?.ToString();
    }

    private static async Task<CustomerBillingNotificationSource?> LoadNotificationSourceAsync(
        NpgsqlConnection connection,
        Guid clientId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                client.client_name,
                COALESCE(profile.invoice_generated_action_required_enabled, FALSE),
                COALESCE(profile.workflow_notes, ''),
                COALESCE(profile.version, 0),
                COALESCE(profile.updated_at, client.updated_at, client.created_at)
            FROM clients client
            LEFT JOIN customer_billing_notification_profiles profile
              ON profile.client_id = client.client_id
            WHERE client.client_id = @client_id;
            """, connection);
        command.Parameters.AddWithValue("client_id", clientId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(
            reader.GetString(0),
            new(
                reader.GetBoolean(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetFieldValue<DateTimeOffset>(4)));
    }

    private static async Task<CustomerBillingNotificationProfile> LoadProfileAsync(
        NpgsqlConnection connection,
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var source = await LoadNotificationSourceAsync(connection, clientId, cancellationToken);
        return source?.Profile ?? CustomerBillingNotificationProfile.Default;
    }

    private static async Task<bool> CanManageCustomersAsync(
        NpgsqlConnection connection,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM app_user_role_assignments assignment
                JOIN app_roles role
                  ON role.app_role_id = assignment.app_role_id
                LEFT JOIN app_role_permissions role_permission
                  ON role_permission.app_role_id = role.app_role_id
                LEFT JOIN app_permissions permission
                  ON permission.app_permission_id = role_permission.app_permission_id
                WHERE assignment.user_id = @user_id
                  AND assignment.is_active = TRUE
                  AND role.is_active = TRUE
                  AND (
                        role.role_code = ANY(@roles)
                     OR permission.permission_code = ANY(@permissions)
                  )
            );
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.Add("roles", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = ManageCustomerRoles;
        command.Parameters.Add("permissions", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = ManageCustomerPermissions;
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static object ToResponse(
        Guid clientId,
        string customerName,
        CustomerBillingNotificationProfile profile) => new
        {
            clientId,
            customerName,
            invoiceGeneratedActionRequiredEnabled = profile.Enabled,
            workflowNotes = profile.WorkflowNotes,
            version = profile.Version,
            updatedAt = profile.UpdatedAt,
            channels = new[] { "email", "teams" },
            recipientRoles = new[] { "BILLING", "FINANCE", "ACCOUNTING", "ACCOUNTING_BILLING" },
            projectContextCc = new[] { "PROJECT_MANAGER", "PROJECT_TEAM_COORDINATOR" },
            externalCustomerContactsEnabled = false
        };

    private static Guid? SessionUserId(HttpContext context) =>
        context.Items.TryGetValue("ProjectPulseSessionUserId", out var value) && value is Guid userId
            ? userId
            : null;

    private static bool IsViewAs(HttpContext context) =>
        context.Items.TryGetValue("ProjectPulseIsViewAs", out var value) && value is true;

    private static IResult SessionRequired() =>
        Results.Json(new
        {
            status = "session_required",
            message = "A valid ProjectPulse session is required."
        }, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult ProfileMigrationRequired() =>
        Results.Json(new
        {
            status = "customer_billing_notification_profile_unavailable",
            message = "Customer billing notification profiles are not initialized."
        }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private sealed record CustomerBillingNotificationSource(
        string CustomerName,
        CustomerBillingNotificationProfile Profile);

    private sealed record CustomerBillingNotificationProfile(
        bool Enabled,
        string WorkflowNotes,
        long Version,
        DateTimeOffset UpdatedAt)
    {
        internal static CustomerBillingNotificationProfile Default =>
            new(false, string.Empty, 0, DateTimeOffset.MinValue);
    }
}

public sealed record CustomerBillingNotificationProfileUpdateRequest(
    bool InvoiceGeneratedActionRequiredEnabled,
    string? WorkflowNotes);

public sealed record CustomerInvoiceWorkflowNotificationOutcome(
    bool ProfileEnabled,
    bool EventQueued,
    string DiagnosticCode);
