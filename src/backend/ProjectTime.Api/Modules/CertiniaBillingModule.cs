using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static class CertiniaBillingModule
{
    private const string SystemCode = "CERTINIA";
    private static readonly string[] ExternalIdFields =
        ["externalId", "id", "invoiceId", "recordId", "certiniaInvoiceId"];
    private static readonly string[] StatusFields =
        ["status", "invoiceStatus", "state"];
    private static readonly string[] BroadInvoiceAccessRoleCodes =
    [
        "super_administrator",
        "super_admin",
        "administrator",
        "admin",
        "project_team_coordinator",
        "accounting",
        "accounting_billing",
        "billing",
        "finance",
        "executive",
        "pmo"
    ];
    private static readonly string[] CertiniaOperatorRoleCodes =
    [
        .. BroadInvoiceAccessRoleCodes,
        "project_management_manager",
        "project_management_lead",
        "project_management_team_lead"
    ];

    public static WebApplication MapCertiniaBillingEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/billing/certinia/configuration",
            async (HttpContext context) => await GetConfigurationAsync(context));

        app.MapGet(
            "/api/billing/invoices/{invoiceId:guid}/certinia-status",
            async (Guid invoiceId, HttpContext context) =>
                await GetInvoiceStatusAsync(invoiceId, context));

        app.MapGet(
            "/api/billing/invoices/{invoiceId:guid}/document",
            async (Guid invoiceId, HttpContext context) =>
                await GetDocumentAsync(invoiceId, context));

        app.MapPost(
            "/api/billing/invoices/{invoiceId:guid}/certinia/send",
            async (Guid invoiceId, CertiniaInvoiceSendRequest request, HttpContext context) =>
                await QueueOrSendAsync(invoiceId, request, context));

        app.MapPost(
            "/api/billing/certinia/process-outbox",
            async (HttpContext context) => await ProcessOutboxEndpointAsync(context));

        app.MapPost(
            "/api/billing/certinia/sync-status",
            async (HttpContext context) => await SyncStatusEndpointAsync(context));

        app.MapPost(
            "/api/billing/certinia/nightly",
            async (HttpContext context) => await NightlyAsync(context));

        return app;
    }

    public static bool IsValidIntegrationRequest(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method)
            || !string.Equals(
                context.Request.Path.Value,
                "/api/billing/certinia/nightly",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return HasValidIntegrationToken(context);
    }

    private static bool HasValidIntegrationToken(HttpContext context)
    {
        var expected = Environment.GetEnvironmentVariable(
            "PROJECTPULSE_CERTINIA_SYNC_TOKEN") ?? string.Empty;
        var supplied = context.Request.Headers[
            "X-ProjectPulse-Integration-Token"].ToString();

        if (string.IsNullOrWhiteSpace(expected)
            || string.IsNullOrWhiteSpace(supplied))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);

        return expectedBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(
                expectedBytes,
                suppliedBytes);
    }

    private static Guid? SessionUserId(HttpContext context)
    {
        return context.Items.TryGetValue(
                "ProjectPulseSessionUserId",
                out var stored)
            && stored is Guid userId
                ? userId
                : null;
    }

    private static IResult SessionRequired()
    {
        return Results.Json(
            new
            {
                status = "session_required",
                message = "A valid ProjectPulse session is required."
            },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> GetConfigurationAsync(
        HttpContext context)
    {
        if (SessionUserId(context) is null) return SessionRequired();

        await using var connection = await OpenConnectionAsync();
        var configuration = await LoadConfigurationAsync(connection);

        return Results.Ok(new
        {
            status = "certinia_configuration_loaded",
            configuration = configuration.ToSafeResponse()
        });
    }

    private static async Task<IResult> GetInvoiceStatusAsync(
        Guid invoiceId,
        HttpContext context)
    {
        var userId = SessionUserId(context);
        if (userId is null) return SessionRequired();

        await using var connection = await OpenConnectionAsync();

        if (!await CanAccessInvoiceAsync(connection, invoiceId, userId.Value))
        {
            return Results.Json(
                new
                {
                    status = "access_denied",
                    message = "The invoice is outside the current user's billing scope."
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var invoice = await LoadInvoiceAsync(connection, invoiceId);
        if (invoice is null)
        {
            return Results.NotFound(new
            {
                status = "invoice_not_found",
                message = "The requested invoice was not found."
            });
        }

        var outbox = await LoadInvoiceOutboxAsync(connection, invoiceId);
        var events = await LoadDeliveryEventsAsync(connection, invoiceId);
        var configuration = await LoadConfigurationAsync(connection);

        return Results.Ok(new
        {
            status = "certinia_invoice_status_loaded",
            invoiceId,
            invoiceNumber = invoice.Header.InvoiceNumber,
            projectPulseStatus = invoice.Header.InvoiceStatus,
            configuration = configuration.ToSafeResponse(),
            latest = outbox.FirstOrDefault(),
            deliveries = outbox,
            events
        });
    }

    private static async Task<IResult> GetDocumentAsync(
        Guid invoiceId,
        HttpContext context)
    {
        var userId = SessionUserId(context);
        if (userId is null) return SessionRequired();

        await using var connection = await OpenConnectionAsync();

        if (!await CanAccessInvoiceAsync(connection, invoiceId, userId.Value))
        {
            return Results.Json(
                new
                {
                    status = "access_denied",
                    message = "The invoice is outside the current user's billing scope."
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var invoice = await LoadInvoiceAsync(connection, invoiceId);
        if (invoice is null)
        {
            return Results.NotFound(new
            {
                status = "invoice_not_found",
                message = "The requested invoice was not found."
            });
        }

        var format = NormalizeDocumentFormat(
            context.Request.Query["format"].ToString());
        var legacyIncludeResourceNames = ReadBoolean(
            context.Request.Query["includeResourceNames"].ToString());
        var outputOptions = new InvoiceOutputOptions(
            ReadQueryBoolean(context, "includeEngineerNames", legacyIncludeResourceNames),
            ReadQueryBoolean(context, "includeProjectManagerName", legacyIncludeResourceNames),
            ReadQueryBoolean(context, "includeProjectCoordinatorName", legacyIncludeResourceNames));
        var artifact = BuildArtifact(
            invoice,
            format,
            outputOptions);

        return Results.File(
            artifact.Bytes,
            artifact.ContentType,
            artifact.FileName,
            enableRangeProcessing: false);
    }

    private static async Task<IResult> QueueOrSendAsync(
        Guid invoiceId,
        CertiniaInvoiceSendRequest request,
        HttpContext context)
    {
        var userId = SessionUserId(context);
        if (userId is null) return SessionRequired();

        await using var connection = await OpenConnectionAsync();

        if (!await CanAccessInvoiceAsync(connection, invoiceId, userId.Value))
        {
            return Results.Json(
                new
                {
                    status = "access_denied",
                    message = "The invoice is outside the current user's billing scope."
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var invoice = await LoadInvoiceAsync(connection, invoiceId);
        if (invoice is null)
        {
            return Results.NotFound(new
            {
                status = "invoice_not_found",
                message = "The requested invoice was not found."
            });
        }

        var format = NormalizeDocumentFormat(request.DocumentFormat);
        var outputOptions = new InvoiceOutputOptions(
            request.IncludeEngineerNames ?? request.IncludeResourceNames,
            request.IncludeProjectManagerName ?? request.IncludeResourceNames,
            request.IncludeProjectCoordinatorName ?? request.IncludeResourceNames);
        var configuration = await LoadConfigurationAsync(connection);
        var queued = await QueueInvoiceAsync(
            connection,
            invoice,
            format,
            outputOptions,
            userId.Value);

        CertiniaProcessSummary? processing = null;

        if (request.TransmitNow && configuration.CanTransmit)
        {
            processing = await ProcessOutboxAsync(
                connection,
                configuration,
                invoiceId,
                1);
        }

        var current = await LoadInvoiceOutboxAsync(connection, invoiceId);

        return Results.Ok(new
        {
            status = request.TransmitNow && configuration.CanTransmit
                ? "certinia_send_processed"
                : queued.Inserted
                    ? "certinia_delivery_queued"
                    : "certinia_delivery_already_queued",
            message = request.TransmitNow && !configuration.CanTransmit
                ? "The immutable invoice artifact was queued. Certinia transmission remains disabled until the connector is configured."
                : queued.Inserted
                    ? "The immutable invoice artifact was queued for Certinia delivery."
                    : "An equivalent immutable invoice artifact is already in the integration outbox.",
            queue = queued,
            processing,
            configuration = configuration.ToSafeResponse(),
            latest = current.FirstOrDefault()
        });
    }

    private static async Task<IResult> ProcessOutboxEndpointAsync(
        HttpContext context)
    {
        var userId = SessionUserId(context);
        if (userId is null) return SessionRequired();

        await using var connection = await OpenConnectionAsync();
        if (!await CanOperateCertiniaAsync(connection, userId.Value))
        {
            return Results.Json(
                new
                {
                    status = "access_denied",
                    message = "Only Accounting, Finance, PMO, or Administrators can process the Certinia outbox."
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var configuration = await LoadConfigurationAsync(connection);

        if (!configuration.CanTransmit)
        {
            return Results.Ok(new
            {
                status = "certinia_not_configured",
                message = "Pending deliveries remain queued until Certinia is enabled and configured.",
                configuration = configuration.ToSafeResponse()
            });
        }

        var result = await ProcessOutboxAsync(
            connection,
            configuration,
            null,
            25);

        return Results.Ok(new
        {
            status = "certinia_outbox_processed",
            result
        });
    }

    private static async Task<IResult> SyncStatusEndpointAsync(
        HttpContext context)
    {
        var userId = SessionUserId(context);
        if (userId is null) return SessionRequired();

        await using var connection = await OpenConnectionAsync();
        if (!await CanOperateCertiniaAsync(connection, userId.Value))
        {
            return Results.Json(
                new
                {
                    status = "access_denied",
                    message = "Only Accounting, Finance, PMO, or Administrators can synchronize Certinia status."
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var configuration = await LoadConfigurationAsync(connection);

        if (!configuration.CanTransmit)
        {
            return Results.Ok(new
            {
                status = "certinia_not_configured",
                message = "Status synchronization remains disabled until Certinia is configured.",
                configuration = configuration.ToSafeResponse()
            });
        }

        var result = await SyncStatusesAsync(
            connection,
            configuration,
            50);

        return Results.Ok(new
        {
            status = "certinia_status_sync_complete",
            result
        });
    }

    private static async Task<IResult> NightlyAsync(HttpContext context)
    {
        if (!IsValidIntegrationRequest(context))
        {
            return Results.Json(
                new
                {
                    status = "integration_token_required",
                    message = "The Certinia nightly integration token is missing or invalid."
                },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        await using var connection = await OpenConnectionAsync();
        var configuration = await LoadConfigurationAsync(connection);
        var correlationId = $"certinia-nightly-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}";
        var runId = await BeginSyncRunAsync(connection, correlationId);

        if (!configuration.CanTransmit)
        {
            await FinishSyncRunAsync(
                connection,
                runId,
                "succeeded",
                0,
                0,
                0,
                0,
                "Certinia is intentionally not configured; no transmission or status call was performed.",
                new
                {
                    skipped = true,
                    configuration = configuration.ToSafeResponse()
                });

            return Results.Ok(new
            {
                status = "certinia_nightly_skipped_not_configured",
                correlationId,
                runId,
                configuration = configuration.ToSafeResponse(),
                transmissionPerformed = false
            });
        }

        try
        {
            var process = await ProcessOutboxAsync(
                connection,
                configuration,
                null,
                100);
            var sync = await SyncStatusesAsync(
                connection,
                configuration,
                200);
            var failed = process.Failed + sync.Failed;
            var finalStatus = failed == 0
                ? "succeeded"
                : process.Succeeded + sync.Updated > 0
                    ? "partially_succeeded"
                    : "failed";

            await FinishSyncRunAsync(
                connection,
                runId,
                finalStatus,
                process.Read + sync.Read,
                process.Succeeded,
                sync.Updated,
                failed,
                failed == 0 ? string.Empty : "One or more Certinia delivery or status operations failed.",
                new { process, sync });

            return Results.Ok(new
            {
                status = "certinia_nightly_complete",
                correlationId,
                runId,
                process,
                sync
            });
        }
        catch (Exception exception)
        {
            await FinishSyncRunAsync(
                connection,
                runId,
                "failed",
                0,
                0,
                0,
                1,
                exception.Message,
                new { exception = exception.GetType().Name });
            throw;
        }
    }

    private static async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connectionString = BuildConnectionString();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ProjectPulse database configuration is missing. Set a supported ConnectionStrings value or PTP_DB_* variables.");
        }

        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static string BuildConnectionString()
    {
        var direct = FirstNonEmpty(
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"),
            Environment.GetEnvironmentVariable("ConnectionStrings__ProjectPulse"),
            Environment.GetEnvironmentVariable("ConnectionStrings__ProjectTime"));

        if (!string.IsNullOrWhiteSpace(direct)) return direct;

        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");

        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password))
        {
            return string.Empty;
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(
                Environment.GetEnvironmentVariable("PTP_DB_PORT"),
                out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = 20
        };

        return builder.ConnectionString;
    }

    private static async Task<CertiniaConfiguration> LoadConfigurationAsync(
        NpgsqlConnection connection)
    {
        var enabled = ReadBoolean(Environment.GetEnvironmentVariable(
            "PROJECTPULSE_CERTINIA_ENABLED"));
        var baseUrl = Clean(Environment.GetEnvironmentVariable(
            "PROJECTPULSE_CERTINIA_BASE_URL"));
        var tokenUrl = Clean(Environment.GetEnvironmentVariable(
            "PROJECTPULSE_CERTINIA_TOKEN_URL"));
        var uploadPath = Clean(Environment.GetEnvironmentVariable(
            "PROJECTPULSE_CERTINIA_UPLOAD_PATH"));
        var statusPathTemplate = Clean(Environment.GetEnvironmentVariable(
            "PROJECTPULSE_CERTINIA_STATUS_PATH_TEMPLATE"));
        var scope = Clean(Environment.GetEnvironmentVariable(
            "PROJECTPULSE_CERTINIA_SCOPE"));
        var transport = Clean(Environment.GetEnvironmentVariable(
            "PROJECTPULSE_CERTINIA_TRANSPORT"));
        var defaultFormat = NormalizeDocumentFormat(
            Environment.GetEnvironmentVariable(
                "PROJECTPULSE_CERTINIA_DEFAULT_DOCUMENT_FORMAT"));
        var clientId = Clean(Environment.GetEnvironmentVariable(
            "PROJECTPULSE_CERTINIA_CLIENT_ID"));
        var clientSecret = Clean(Environment.GetEnvironmentVariable(
            "PROJECTPULSE_CERTINIA_CLIENT_SECRET"));
        var timeout = int.TryParse(
            Environment.GetEnvironmentVariable(
                "PROJECTPULSE_CERTINIA_TIMEOUT_SECONDS"),
            out var parsedTimeout)
                ? Math.Clamp(parsedTimeout, 5, 300)
                : 60;

        string connectionStatus = "not_configured";
        bool outboundEnabled = false;
        string databaseBaseUrl = string.Empty;

        await using (var command = new NpgsqlCommand("""
            SELECT
                connection_status,
                outbound_enabled,
                base_url
            FROM external_integration_connections
            WHERE system_code = @system_code
            LIMIT 1;
            """, connection))
        {
            command.Parameters.AddWithValue("system_code", SystemCode);
            await using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                connectionStatus = reader.GetString(0);
                outboundEnabled = reader.GetBoolean(1);
                databaseBaseUrl = reader.GetString(2);
            }
        }

        if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = databaseBaseUrl;
        if (string.IsNullOrWhiteSpace(transport)) transport = "oauth_client_credentials";

        var missing = new List<string>();
        if (!enabled) missing.Add("PROJECTPULSE_CERTINIA_ENABLED=true");
        if (string.IsNullOrWhiteSpace(baseUrl)) missing.Add("PROJECTPULSE_CERTINIA_BASE_URL");
        if (string.IsNullOrWhiteSpace(uploadPath)) missing.Add("PROJECTPULSE_CERTINIA_UPLOAD_PATH");
        if (string.IsNullOrWhiteSpace(statusPathTemplate)) missing.Add("PROJECTPULSE_CERTINIA_STATUS_PATH_TEMPLATE");
        if (string.IsNullOrWhiteSpace(tokenUrl)) missing.Add("PROJECTPULSE_CERTINIA_TOKEN_URL");
        if (string.IsNullOrWhiteSpace(clientId)) missing.Add("PROJECTPULSE_CERTINIA_CLIENT_ID");
        if (string.IsNullOrWhiteSpace(clientSecret)) missing.Add("PROJECTPULSE_CERTINIA_CLIENT_SECRET");
        if (!outboundEnabled) missing.Add("CERTINIA outbound_enabled database flag");
        if (connectionStatus is not ("configured" or "connected"))
        {
            missing.Add("CERTINIA connection_status configured/connected");
        }

        return new CertiniaConfiguration(
            enabled,
            baseUrl,
            tokenUrl,
            uploadPath,
            statusPathTemplate,
            scope,
            transport,
            defaultFormat,
            timeout,
            clientId,
            clientSecret,
            connectionStatus,
            outboundEnabled,
            missing);
    }

    private static async Task<bool> CanOperateCertiniaAsync(
        NpgsqlConnection connection,
        Guid userId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM app_users user_row
                JOIN app_user_role_assignments assignment
                  ON assignment.user_id = user_row.user_id
                JOIN app_roles role
                  ON role.app_role_id = assignment.app_role_id
                WHERE user_row.user_id = @user_id
                  AND user_row.is_active = TRUE
                  AND assignment.is_active = TRUE
                  AND role.is_active = TRUE
                  AND lower(role.role_code) = ANY(@role_codes)
            );
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.Add(
            "role_codes",
            NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text).Value =
                CertiniaOperatorRoleCodes;
        var result = await command.ExecuteScalarAsync();
        return result is bool allowed && allowed;
    }

    private static async Task<bool> CanAccessInvoiceAsync(
        NpgsqlConnection connection,
        Guid invoiceId,
        Guid userId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM billing_invoices invoice
                JOIN projects project
                  ON project.project_id = invoice.project_id
                JOIN app_users user_row
                  ON user_row.user_id = @user_id
                 AND user_row.is_active = TRUE
                WHERE invoice.billing_invoice_id = @invoice_id
                  AND (
                      project.project_manager_user_id = @user_id
                      OR project.project_coordinator_user_id = @user_id
                      OR EXISTS (
                          SELECT 1
                          FROM project_assignments project_assignment
                          WHERE project_assignment.project_id = project.project_id
                            AND project_assignment.user_id = @user_id
                      )
                      OR EXISTS (
                          SELECT 1
                          FROM app_user_role_assignments role_assignment
                          JOIN app_roles role
                            ON role.app_role_id = role_assignment.app_role_id
                          WHERE role_assignment.user_id = @user_id
                            AND role_assignment.is_active = TRUE
                            AND role.is_active = TRUE
                            AND lower(role.role_code) = ANY(@broad_role_codes)
                      )
                  )
            );
            """, connection);

        command.Parameters.AddWithValue("invoice_id", invoiceId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.Add(
            "broad_role_codes",
            NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text).Value =
                BroadInvoiceAccessRoleCodes;
        var result = await command.ExecuteScalarAsync();
        return result is bool allowed && allowed;
    }

    private static async Task<CertiniaInvoiceSnapshot?> LoadInvoiceAsync(
        NpgsqlConnection connection,
        Guid invoiceId)
    {
        CertiniaInvoiceHeader? header = null;

        await using (var command = new NpgsqlCommand("""
            SELECT
                billing_invoice_id,
                invoice_number,
                project_id,
                invoice_type,
                invoice_status,
                billing_period_start,
                billing_period_end,
                invoice_date,
                customer_name_snapshot,
                project_code_snapshot,
                project_name_snapshot,
                contract_type_snapshot,
                project_manager_name_snapshot,
                project_coordinator_name_snapshot,
                purchase_order_number_snapshot,
                purchase_order_amount_snapshot,
                certinia_id_snapshot,
                salesforce_id_snapshot,
                sell_quote_snapshot,
                subtotal_amount,
                adjustment_amount,
                tax_amount,
                total_amount,
                invoice_notes,
                immutable_snapshot_json::text,
                created_at,
                finalized_at
            FROM billing_invoices
            WHERE billing_invoice_id = @invoice_id;
            """, connection))
        {
            command.Parameters.AddWithValue("invoice_id", invoiceId);
            await using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                header = new CertiniaInvoiceHeader(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    ReadDateOnly(reader, 5),
                    ReadDateOnly(reader, 6),
                    ReadDateOnly(reader, 7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.GetString(10),
                    reader.GetString(11),
                    reader.GetString(12),
                    reader.GetString(13),
                    reader.GetString(14),
                    reader.IsDBNull(15) ? null : reader.GetDecimal(15),
                    reader.GetString(16),
                    reader.GetString(17),
                    reader.GetString(18),
                    reader.GetDecimal(19),
                    reader.GetDecimal(20),
                    reader.GetDecimal(21),
                    reader.GetDecimal(22),
                    reader.GetString(23),
                    reader.GetString(24),
                    reader.GetFieldValue<DateTimeOffset>(25),
                    reader.IsDBNull(26)
                        ? null
                        : reader.GetFieldValue<DateTimeOffset>(26));
            }
        }

        if (header is null) return null;

        var lines = new List<CertiniaInvoiceLine>();

        await using (var command = new NpgsqlCommand("""
            SELECT
                billing_invoice_line_id,
                line_number,
                work_date,
                resource_name_snapshot,
                resource_email_snapshot,
                task_code_snapshot,
                task_name_snapshot,
                customer_facing_description,
                time_type,
                labor_category,
                approved_hours,
                rate_code_snapshot,
                rate_description_snapshot,
                unit_rate,
                line_amount
            FROM billing_invoice_lines
            WHERE billing_invoice_id = @invoice_id
            ORDER BY line_number;
            """, connection))
        {
            command.Parameters.AddWithValue("invoice_id", invoiceId);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lines.Add(new CertiniaInvoiceLine(
                    reader.GetGuid(0),
                    reader.GetInt32(1),
                    ReadDateOnly(reader, 2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.GetDecimal(10),
                    reader.GetString(11),
                    reader.GetString(12),
                    reader.GetDecimal(13),
                    reader.GetDecimal(14)));
            }
        }

        var snapshot = new CertiniaInvoiceSnapshot(header, lines);
        var immutableText = string.IsNullOrWhiteSpace(header.ImmutableSnapshotJson)
            || header.ImmutableSnapshotJson == "{}"
                ? JsonSerializer.Serialize(new { header, lines })
                : header.ImmutableSnapshotJson;

        return snapshot with
        {
            ImmutableSnapshotSha256 = Sha256Hex(
                Encoding.UTF8.GetBytes(immutableText))
        };
    }

    private static CertiniaArtifact BuildArtifact(
        CertiniaInvoiceSnapshot invoice,
        string format,
        InvoiceOutputOptions outputOptions)
    {
        return format == "excel"
            ? BuildExcelArtifact(invoice, outputOptions)
            : BuildPdfArtifact(invoice, outputOptions);
    }

    private static BrandedInvoiceDocument ToBrandedInvoiceDocument(
        CertiniaInvoiceSnapshot invoice,
        InvoiceOutputOptions outputOptions)
    {
        var header = invoice.Header;
        var projectManager = outputOptions.IncludeProjectManagerName
            ? Fallback(header.ProjectManagerName, "Not assigned")
            : "Project Management";
        var projectCoordinator = outputOptions.IncludeProjectCoordinatorName
            ? Fallback(header.ProjectCoordinatorName, "Not assigned")
            : "Project Management";
        var personalNames = $"Personal names: engineers {(outputOptions.IncludeEngineerNames ? "included" : "hidden")}; "
            + $"project manager {(outputOptions.IncludeProjectManagerName ? "included" : "hidden")}; "
            + $"project coordinator {(outputOptions.IncludeProjectCoordinatorName ? "included" : "hidden")}.";

        return new BrandedInvoiceDocument(
            header.InvoiceNumber,
            header.InvoiceType,
            header.InvoiceStatus,
            header.InvoiceDate,
            header.BillingPeriodStart,
            header.BillingPeriodEnd,
            header.CustomerName,
            header.ProjectCode,
            header.ProjectName,
            header.ContractType,
            projectManager,
            projectCoordinator,
            header.PurchaseOrderNumber,
            header.PurchaseOrderAmount,
            header.CertiniaId,
            header.SalesforceId,
            header.SellQuote,
            header.SubtotalAmount,
            header.AdjustmentAmount,
            header.TaxAmount,
            header.TotalAmount,
            header.Notes,
            invoice.ImmutableSnapshotSha256,
            personalNames,
            invoice.Lines.Select(line => new BrandedInvoiceLine(
                line.LineNumber,
                line.WorkDate,
                CustomerResource(line, outputOptions.IncludeEngineerNames),
                line.TaskCode,
                line.TaskName,
                line.Description,
                line.TimeType,
                line.LaborCategory,
                line.ApprovedHours,
                line.RateCode,
                line.RateDescription,
                line.UnitRate,
                line.LineAmount)).ToArray());
    }

    private static CertiniaArtifact BuildPdfArtifact(
        CertiniaInvoiceSnapshot invoice,
        InvoiceOutputOptions outputOptions)
    {
        var brandedInvoice = ToBrandedInvoiceDocument(invoice, outputOptions);
        var bytes = BrandedInvoiceArtifactRenderer.BuildPdf(brandedInvoice);
        var suffix = outputOptions.IncludeAnyNames
            ? "-with-selected-names"
            : string.Empty;
        var fileName = $"US-Signal-SP-Invoice-{SafeFileName(invoice.Header.InvoiceNumber)}{suffix}.pdf";

        return new CertiniaArtifact(
            "pdf",
            "application/pdf",
            fileName,
            bytes,
            Sha256Hex(bytes));
    }

    private static CertiniaArtifact BuildExcelArtifact(
        CertiniaInvoiceSnapshot invoice,
        InvoiceOutputOptions outputOptions)
    {
        var brandedInvoice = ToBrandedInvoiceDocument(invoice, outputOptions);
        var bytes = BrandedInvoiceArtifactRenderer.BuildExcel(brandedInvoice);
        var suffix = outputOptions.IncludeAnyNames
            ? "-with-selected-names"
            : string.Empty;
        var fileName = $"US-Signal-SP-Invoice-{SafeFileName(invoice.Header.InvoiceNumber)}{suffix}.xlsx";

        return new CertiniaArtifact(
            "excel",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName,
            bytes,
            Sha256Hex(bytes));
    }

    private static byte[] BuildNativeExcelWorkbook(
        CertiniaInvoiceSnapshot invoice,
        InvoiceOutputOptions outputOptions)
    {
        using var output = new MemoryStream();

        using (var archive = new ZipArchive(
            output,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            AddXlsxEntry(archive, "[Content_Types].xml", BuildXlsxContentTypes());
            AddXlsxEntry(archive, "_rels/.rels", BuildXlsxRootRelationships());
            AddXlsxEntry(archive, "docProps/core.xml", BuildXlsxCoreProperties(invoice));
            AddXlsxEntry(archive, "docProps/app.xml", BuildXlsxAppProperties());
            AddXlsxEntry(archive, "xl/workbook.xml", BuildXlsxWorkbook(invoice));
            AddXlsxEntry(archive, "xl/_rels/workbook.xml.rels", BuildXlsxWorkbookRelationships());
            AddXlsxEntry(archive, "xl/styles.xml", BuildXlsxStyles());
            AddXlsxEntry(
                archive,
                "xl/worksheets/sheet1.xml",
                BuildXlsxSummaryWorksheet(invoice, outputOptions));
            AddXlsxEntry(
                archive,
                "xl/worksheets/sheet2.xml",
                BuildXlsxDetailWorksheet(invoice, outputOptions));
            AddXlsxEntry(
                archive,
                "xl/worksheets/_rels/sheet2.xml.rels",
                BuildXlsxDetailRelationships());
            AddXlsxEntry(
                archive,
                "xl/tables/table1.xml",
                BuildXlsxDetailTable(invoice));
        }

        return output.ToArray();
    }

    private static string BuildXlsxContentTypes() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
          <Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/tables/table1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml"/>
        </Types>
        """;

    private static string BuildXlsxRootRelationships() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
          <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>
        </Relationships>
        """;

    private static string BuildXlsxCoreProperties(CertiniaInvoiceSnapshot invoice)
    {
        var created = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:dcmitype="http://purl.org/dc/dcmitype/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <dc:title>Invoice {XlsxXml(invoice.Header.InvoiceNumber)}</dc:title>
              <dc:subject>ProjectPulse immutable invoice workbook</dc:subject>
              <dc:creator>ProjectPulse</dc:creator>
              <cp:lastModifiedBy>ProjectPulse</cp:lastModifiedBy>
              <dcterms:created xsi:type="dcterms:W3CDTF">{created}</dcterms:created>
              <dcterms:modified xsi:type="dcterms:W3CDTF">{created}</dcterms:modified>
            </cp:coreProperties>
            """;
    }

    private static string BuildXlsxAppProperties() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
          <Application>ProjectPulse</Application>
          <DocSecurity>0</DocSecurity>
          <ScaleCrop>false</ScaleCrop>
          <HeadingPairs>
            <vt:vector size="2" baseType="variant">
              <vt:variant><vt:lpstr>Worksheets</vt:lpstr></vt:variant>
              <vt:variant><vt:i4>2</vt:i4></vt:variant>
            </vt:vector>
          </HeadingPairs>
          <TitlesOfParts>
            <vt:vector size="2" baseType="lpstr">
              <vt:lpstr>Invoice Summary</vt:lpstr>
              <vt:lpstr>Invoice Detail</vt:lpstr>
            </vt:vector>
          </TitlesOfParts>
          <Company>ProjectPulse</Company>
          <AppVersion>1.0</AppVersion>
        </Properties>
        """;

    private static string BuildXlsxWorkbook(CertiniaInvoiceSnapshot invoice)
    {
        var detailEnd = Math.Max(1, invoice.Lines.Count + 1);
        var detailPrintEnd = detailEnd + 5;
        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <bookViews><workbookView activeTab="0"/></bookViews>
              <sheets>
                <sheet name="Invoice Summary" sheetId="1" r:id="rId1"/>
                <sheet name="Invoice Detail" sheetId="2" r:id="rId2"/>
              </sheets>
              <definedNames>
                <definedName name="_xlnm.Print_Area" localSheetId="0">'Invoice Summary'!$A$1:$K$22</definedName>
                <definedName name="_xlnm.Print_Area" localSheetId="1">'Invoice Detail'!$A$1:$K${detailPrintEnd}</definedName>
              </definedNames>
              <calcPr calcId="191029"/>
            </workbook>
            """;
    }

    private static string BuildXlsxWorkbookRelationships() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
          <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    private static string BuildXlsxStyles() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="3">
            <numFmt numFmtId="164" formatCode="mm/dd/yyyy"/>
            <numFmt numFmtId="165" formatCode="$#,##0.00"/>
            <numFmt numFmtId="166" formatCode="0.00"/>
          </numFmts>
          <fonts count="4">
            <font><sz val="11"/><name val="Aptos"/><family val="2"/></font>
            <font><b/><color rgb="FFFFFFFF"/><sz val="20"/><name val="Aptos Display"/><family val="2"/></font>
            <font><b/><color rgb="FFFFFFFF"/><sz val="11"/><name val="Aptos"/><family val="2"/></font>
            <font><b/><color rgb="FF17324D"/><sz val="11"/><name val="Aptos"/><family val="2"/></font>
          </fonts>
          <fills count="4">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="gray125"/></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FF0B5A7A"/><bgColor indexed="64"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFEAF3F8"/><bgColor indexed="64"/></patternFill></fill>
          </fills>
          <borders count="2">
            <border><left/><right/><top/><bottom/><diagonal/></border>
            <border>
              <left style="thin"><color rgb="FFC7D6E0"/></left>
              <right style="thin"><color rgb="FFC7D6E0"/></right>
              <top style="thin"><color rgb="FFC7D6E0"/></top>
              <bottom style="thin"><color rgb="FFC7D6E0"/></bottom>
              <diagonal/>
            </border>
          </borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="14">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            <xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf>
            <xf numFmtId="0" fontId="2" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="3" fillId="3" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="center"/></xf>
            <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1"><alignment vertical="center" wrapText="1"/></xf>
            <xf numFmtId="164" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
            <xf numFmtId="166" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
            <xf numFmtId="165" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
            <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1"><alignment vertical="top"/></xf>
            <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="3" fillId="3" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
            <xf numFmtId="165" fontId="3" fillId="3" borderId="1" xfId="0" applyNumberFormat="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
            <xf numFmtId="166" fontId="3" fillId="3" borderId="1" xfId="0" applyNumberFormat="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
            <xf numFmtId="0" fontId="3" fillId="3" borderId="0" xfId="0" applyFont="1" applyFill="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf>
          </cellXfs>
          <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
          <tableStyles count="1" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
        </styleSheet>
        """;

    private static string BuildXlsxSummaryWorksheet(
        CertiniaInvoiceSnapshot invoice,
        InvoiceOutputOptions outputOptions)
    {
        var header = invoice.Header;
        var projectManager = outputOptions.IncludeProjectManagerName
            ? Fallback(header.ProjectManagerName, "Not assigned")
            : "Project Management";
        var projectCoordinator = outputOptions.IncludeProjectCoordinatorName
            ? Fallback(header.ProjectCoordinatorName, "Not assigned")
            : "Project Management";
        var personalNames = string.Join(
            "; ",
            $"engineers {(outputOptions.IncludeEngineerNames ? "included" : "hidden")}",
            $"Project Manager {(outputOptions.IncludeProjectManagerName ? "included" : "hidden")}",
            $"Project Coordinator {(outputOptions.IncludeProjectCoordinatorName ? "included" : "hidden")}");
        var totalHours = invoice.Lines.Sum(line => line.ApprovedHours);
        var xml = new StringBuilder();

        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        xml.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        xml.Append("<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr>");
        xml.Append("<dimension ref=\"A1:K22\"/>");
        xml.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"3\" topLeftCell=\"A4\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        xml.Append("<sheetFormatPr defaultRowHeight=\"18\"/>");
        xml.Append("<cols><col min=\"1\" max=\"1\" width=\"21\" customWidth=\"1\"/><col min=\"2\" max=\"6\" width=\"17\" customWidth=\"1\"/><col min=\"7\" max=\"7\" width=\"22\" customWidth=\"1\"/><col min=\"8\" max=\"11\" width=\"17\" customWidth=\"1\"/></cols>");
        xml.Append("<sheetData>");

        xml.Append("<row r=\"1\" ht=\"30\" customHeight=\"1\">");
        AppendXlsxInlineCell(xml, "A1", $"Invoice {header.InvoiceNumber}", 1);
        xml.Append("</row>");
        xml.Append("<row r=\"3\" ht=\"22\" customHeight=\"1\">");
        AppendXlsxInlineCell(xml, "A3", "Professional Services Invoice", 13);
        xml.Append("</row>");

        AppendXlsxSummaryPair(xml, 5, "A", "Customer", "B", header.CustomerName, "G", "Invoice number", "H", header.InvoiceNumber);
        AppendXlsxSummaryPair(xml, 6, "A", "Project", "B", $"{header.ProjectCode} — {header.ProjectName}", "G", "Invoice type", "H", header.InvoiceType);
        AppendXlsxSummaryPair(xml, 7, "A", "Project Manager", "B", projectManager, "G", "Project Coordinator", "H", projectCoordinator);
        AppendXlsxSummaryPair(xml, 8, "A", "Billing period", "B", $"{FormatDate(header.BillingPeriodStart)} through {FormatDate(header.BillingPeriodEnd)}", "G", "Invoice date", "H", FormatDate(header.InvoiceDate));
        AppendXlsxSummaryPair(xml, 9, "A", "Purchase order", "B", Fallback(header.PurchaseOrderNumber, "Not configured"), "G", "SELL Quote", "H", Fallback(header.SellQuote, "Not configured"));
        AppendXlsxSummaryPair(xml, 10, "A", "Certinia ID", "B", Fallback(header.CertiniaId, "Not configured"), "G", "Salesforce ID", "H", Fallback(header.SalesforceId, "Not configured"));
        AppendXlsxSummaryPair(xml, 11, "A", "Contract type", "B", Fallback(header.ContractType, "Not configured"), "G", "Personal names", "H", personalNames);
        AppendXlsxSummaryPair(xml, 12, "A", "PO authorized amount", "B", header.PurchaseOrderAmount is decimal poAmount ? $"${poAmount:0.00}" : "Not configured", "G", "Snapshot SHA256", "H", invoice.ImmutableSnapshotSha256);

        xml.Append("<row r=\"13\">");
        AppendXlsxInlineCell(xml, "A13", "Total hours", 3);
        AppendXlsxNumberCell(xml, "B13", totalHours, 12);
        AppendXlsxInlineCell(xml, "D13", "Subtotal", 3);
        AppendXlsxNumberCell(xml, "E13", header.SubtotalAmount, 11);
        AppendXlsxInlineCell(xml, "G13", "Adjustments", 3);
        AppendXlsxNumberCell(xml, "H13", header.AdjustmentAmount, 11);
        AppendXlsxInlineCell(xml, "J13", "Tax", 3);
        AppendXlsxNumberCell(xml, "K13", header.TaxAmount, 11);
        xml.Append("</row>");

        xml.Append("<row r=\"15\" ht=\"24\" customHeight=\"1\">");
        AppendXlsxInlineCell(xml, "D15", "Invoice total", 10);
        AppendXlsxNumberCell(xml, "K15", header.TotalAmount, 11);
        xml.Append("</row>");

        xml.Append("<row r=\"17\">");
        AppendXlsxInlineCell(xml, "A17", "Invoice notes", 3);
        xml.Append("</row>");
        xml.Append("<row r=\"18\" ht=\"54\" customHeight=\"1\">");
        AppendXlsxInlineCell(xml, "A18", Fallback(header.Notes, "No invoice notes."), 4);
        xml.Append("</row>");
        xml.Append("<row r=\"22\" ht=\"28\" customHeight=\"1\">");
        AppendXlsxInlineCell(xml, "A22", "Generated from the immutable ProjectPulse invoice snapshot. Dates, hours, rates, and amounts are native Excel values.", 4);
        xml.Append("</row>");

        xml.Append("</sheetData>");
        xml.Append("<mergeCells count=\"17\">");
        foreach (var range in new[]
        {
            "A1:K2", "A3:K3",
            "B5:F5", "H5:K5", "B6:F6", "H6:K6", "B7:F7", "H7:K7",
            "B8:F8", "H8:K8", "B9:F9", "H9:K9", "B10:F10", "H10:K10",
            "D15:J15", "A18:K20", "A22:K22"
        })
        {
            xml.Append("<mergeCell ref=\"").Append(range).Append("\"/>");
        }
        xml.Append("</mergeCells>");
        xml.Append("<pageMargins left=\"0.35\" right=\"0.35\" top=\"0.5\" bottom=\"0.5\" header=\"0.2\" footer=\"0.2\"/>");
        xml.Append("<pageSetup paperSize=\"9\" orientation=\"landscape\" fitToWidth=\"1\" fitToHeight=\"1\"/>");
        xml.Append("</worksheet>");
        return xml.ToString();
    }

    private static void AppendXlsxSummaryPair(
        StringBuilder xml,
        int row,
        string leftLabelColumn,
        string leftLabel,
        string leftValueColumn,
        string leftValue,
        string rightLabelColumn,
        string rightLabel,
        string rightValueColumn,
        string rightValue)
    {
        xml.Append("<row r=\"").Append(row).Append("\">");
        AppendXlsxInlineCell(xml, $"{leftLabelColumn}{row}", leftLabel, 3);
        AppendXlsxInlineCell(xml, $"{leftValueColumn}{row}", leftValue, 4);
        AppendXlsxInlineCell(xml, $"{rightLabelColumn}{row}", rightLabel, 3);
        AppendXlsxInlineCell(xml, $"{rightValueColumn}{row}", rightValue, 4);
        xml.Append("</row>");
    }

    private static string BuildXlsxDetailWorksheet(
        CertiniaInvoiceSnapshot invoice,
        InvoiceOutputOptions outputOptions)
    {
        var dataEnd = Math.Max(1, invoice.Lines.Count + 1);
        var subtotalRow = dataEnd + 2;
        var totalRow = subtotalRow + 3;
        var xml = new StringBuilder();

        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        xml.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        xml.Append("<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr>");
        xml.Append("<dimension ref=\"A1:K").Append(totalRow).Append("\"/>");
        xml.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        xml.Append("<sheetFormatPr defaultRowHeight=\"18\"/>");
        xml.Append("<cols>");
        foreach (var column in new[]
        {
            (1, 1, 8d), (2, 2, 14d), (3, 3, 28d), (4, 4, 16d),
            (5, 5, 32d), (6, 6, 55d), (7, 7, 10d), (8, 8, 18d),
            (9, 9, 32d), (10, 10, 14d), (11, 11, 15d)
        })
        {
            xml.Append("<col min=\"").Append(column.Item1)
                .Append("\" max=\"").Append(column.Item2)
                .Append("\" width=\"").Append(column.Item3.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("\" customWidth=\"1\"/>");
        }
        xml.Append("</cols><sheetData>");

        xml.Append("<row r=\"1\" ht=\"32\" customHeight=\"1\">");
        var headings = new[]
        {
            "Line", "Work Date", "Resource", "Task Code", "Task",
            "Work Description", "Hours", "Rate Code", "Rate Description",
            "Unit Rate", "Amount"
        };
        var columns = new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K" };
        for (var index = 0; index < headings.Length; index++)
        {
            AppendXlsxInlineCell(xml, $"{columns[index]}1", headings[index], 2);
        }
        xml.Append("</row>");

        var rowNumber = 2;
        foreach (var line in invoice.Lines)
        {
            xml.Append("<row r=\"").Append(rowNumber).Append("\" ht=\"45\" customHeight=\"1\">");
            AppendXlsxNumberCell(xml, $"A{rowNumber}", line.LineNumber, 8);
            AppendXlsxDateCell(xml, $"B{rowNumber}", line.WorkDate, 5);
            AppendXlsxInlineCell(xml, $"C{rowNumber}", CustomerResource(line, outputOptions.IncludeEngineerNames), 8);
            AppendXlsxInlineCell(xml, $"D{rowNumber}", line.TaskCode, 8);
            AppendXlsxInlineCell(xml, $"E{rowNumber}", line.TaskName, 9);
            AppendXlsxInlineCell(xml, $"F{rowNumber}", line.Description, 9);
            AppendXlsxNumberCell(xml, $"G{rowNumber}", line.ApprovedHours, 6);
            AppendXlsxInlineCell(xml, $"H{rowNumber}", line.RateCode, 8);
            AppendXlsxInlineCell(xml, $"I{rowNumber}", line.RateDescription, 9);
            AppendXlsxNumberCell(xml, $"J{rowNumber}", line.UnitRate, 7);
            AppendXlsxNumberCell(xml, $"K{rowNumber}", line.LineAmount, 7);
            xml.Append("</row>");
            rowNumber++;
        }

        AppendXlsxTotalRow(xml, subtotalRow, "Subtotal", invoice.Header.SubtotalAmount);
        AppendXlsxTotalRow(xml, subtotalRow + 1, "Adjustments", invoice.Header.AdjustmentAmount);
        AppendXlsxTotalRow(xml, subtotalRow + 2, "Tax", invoice.Header.TaxAmount);
        AppendXlsxTotalRow(xml, totalRow, "Invoice total", invoice.Header.TotalAmount);

        xml.Append("</sheetData>");
        xml.Append("<pageMargins left=\"0.25\" right=\"0.25\" top=\"0.45\" bottom=\"0.45\" header=\"0.2\" footer=\"0.2\"/>");
        xml.Append("<pageSetup paperSize=\"9\" orientation=\"landscape\" fitToWidth=\"1\" fitToHeight=\"0\"/>");
        xml.Append("<tableParts count=\"1\"><tablePart r:id=\"rId1\"/></tableParts>");
        xml.Append("</worksheet>");
        return xml.ToString();
    }

    private static void AppendXlsxTotalRow(
        StringBuilder xml,
        int row,
        string label,
        decimal value)
    {
        xml.Append("<row r=\"").Append(row).Append("\">");
        AppendXlsxInlineCell(xml, $"J{row}", label, 10);
        AppendXlsxNumberCell(xml, $"K{row}", value, 11);
        xml.Append("</row>");
    }

    private static string BuildXlsxDetailRelationships() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/table" Target="../tables/table1.xml"/>
        </Relationships>
        """;

    private static string BuildXlsxDetailTable(CertiniaInvoiceSnapshot invoice)
    {
        var dataEnd = Math.Max(1, invoice.Lines.Count + 1);
        var columns = new[]
        {
            "Line", "Work Date", "Resource", "Task Code", "Task",
            "Work Description", "Hours", "Rate Code", "Rate Description",
            "Unit Rate", "Amount"
        };
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        xml.Append("<table xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" id=\"1\" name=\"InvoiceDetailTable\" displayName=\"InvoiceDetailTable\" ref=\"A1:K")
            .Append(dataEnd)
            .Append("\" totalsRowShown=\"0\">");
        xml.Append("<autoFilter ref=\"A1:K").Append(dataEnd).Append("\"/>");
        xml.Append("<tableColumns count=\"11\">");
        for (var index = 0; index < columns.Length; index++)
        {
            xml.Append("<tableColumn id=\"").Append(index + 1)
                .Append("\" name=\"").Append(XlsxXml(columns[index])).Append("\"/>");
        }
        xml.Append("</tableColumns>");
        xml.Append("<tableStyleInfo name=\"TableStyleMedium2\" showFirstColumn=\"0\" showLastColumn=\"0\" showRowStripes=\"1\" showColumnStripes=\"0\"/>");
        xml.Append("</table>");
        return xml.ToString();
    }

    private static void AddXlsxEntry(
        ZipArchive archive,
        string path,
        string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(
            entry.Open(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static void AppendXlsxInlineCell(
        StringBuilder xml,
        string reference,
        string? value,
        int style)
    {
        xml.Append("<c r=\"").Append(reference)
            .Append("\" s=\"").Append(style)
            .Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
            .Append(XlsxXml(value))
            .Append("</t></is></c>");
    }

    private static void AppendXlsxNumberCell(
        StringBuilder xml,
        string reference,
        decimal value,
        int style)
    {
        xml.Append("<c r=\"").Append(reference)
            .Append("\" s=\"").Append(style)
            .Append("\"><v>")
            .Append(value.ToString(CultureInfo.InvariantCulture))
            .Append("</v></c>");
    }

    private static void AppendXlsxDateCell(
        StringBuilder xml,
        string reference,
        DateOnly? value,
        int style)
    {
        if (value is null)
        {
            AppendXlsxInlineCell(xml, reference, "Not configured", 8);
            return;
        }

        var serial = value.Value
            .ToDateTime(TimeOnly.MinValue)
            .ToOADate()
            .ToString(CultureInfo.InvariantCulture);
        xml.Append("<c r=\"").Append(reference)
            .Append("\" s=\"").Append(style)
            .Append("\"><v>").Append(serial).Append("</v></c>");
    }

    private static string XlsxXml(string? value)
    {
        var normalized = new string((value ?? string.Empty)
            .Where(character => character is '\t' or '\n' or '\r' || character >= ' ')
            .ToArray());
        return System.Security.SecurityElement.Escape(Limit(normalized, 32767))
            ?? string.Empty;
    }

    private static byte[] BuildSimplePdf(IReadOnlyList<string> sourceLines)
    {
        const int linesPerPage = 44;
        var pages = sourceLines
            .Select(PdfAscii)
            .Select((line, index) => new { line, index })
            .GroupBy(item => item.index / linesPerPage)
            .Select(group => group.Select(item => item.line).ToList())
            .ToList();

        if (pages.Count == 0) pages.Add(["Invoice"]);

        var fontObject = 3 + pages.Count * 2;
        var objects = new List<string> { string.Empty, string.Empty };
        var kids = new List<string>();

        for (var index = 0; index < pages.Count; index++)
        {
            var pageObject = 3 + index * 2;
            var contentObject = pageObject + 1;
            kids.Add($"{pageObject} 0 R");

            var stream = new StringBuilder();
            stream.Append("BT\n/F1 9 Tf\n48 755 Td\n13 TL\n");
            foreach (var line in pages[index])
            {
                stream.Append('(')
                    .Append(PdfEscape(line))
                    .Append(") Tj\nT*\n");
            }
            stream.Append("ET\n");

            objects.Add(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {fontObject} 0 R >> >> /Contents {contentObject} 0 R >>");
            objects.Add(
                $"<< /Length {Encoding.ASCII.GetByteCount(stream.ToString())} >>\nstream\n{stream}endstream");
        }

        objects[0] = "<< /Type /Catalog /Pages 2 0 R >>";
        objects[1] = $"<< /Type /Pages /Kids [{string.Join(' ', kids)}] /Count {pages.Count} >>";
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        using var output = new MemoryStream();
        WriteAscii(output, "%PDF-1.4\n%ProjectPulse\n");
        var offsets = new List<long> { 0 };

        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(output.Position);
            WriteAscii(output, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var xref = output.Position;
        WriteAscii(output, $"xref\n0 {objects.Count + 1}\n");
        WriteAscii(output, "0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            WriteAscii(output, $"{offset:0000000000} 00000 n \n");
        }

        WriteAscii(output,
            $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    private static async Task<CertiniaQueueResult> QueueInvoiceAsync(
        NpgsqlConnection connection,
        CertiniaInvoiceSnapshot invoice,
        string format,
        InvoiceOutputOptions outputOptions,
        Guid actorUserId)
    {
        var artifact = BuildArtifact(invoice, format, outputOptions);
        var idempotencyMaterial = string.Join('|',
            invoice.Header.BillingInvoiceId,
            invoice.ImmutableSnapshotSha256,
            artifact.Format,
            outputOptions.IncludeEngineerNames ? "engineer-names-included" : "engineer-names-hidden",
            outputOptions.IncludeProjectManagerName ? "project-manager-name-included" : "project-manager-name-hidden",
            outputOptions.IncludeProjectCoordinatorName ? "project-coordinator-name-included" : "project-coordinator-name-hidden");
        var idempotencyKey = $"certinia-invoice-{Sha256Hex(Encoding.UTF8.GetBytes(idempotencyMaterial))}";
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = "projectpulse-certinia-invoice-v2",
            idempotencyKey,
            queuedAt = DateTimeOffset.UtcNow,
            immutableSnapshotSha256 = invoice.ImmutableSnapshotSha256,
            resourceNamesIncluded = outputOptions.IncludeAnyNames,
            outputPrivacy = new
            {
                engineerNamesIncluded = outputOptions.IncludeEngineerNames,
                projectManagerNameIncluded = outputOptions.IncludeProjectManagerName,
                projectCoordinatorNameIncluded = outputOptions.IncludeProjectCoordinatorName
            },
            invoice = new
            {
                billingInvoiceId = invoice.Header.BillingInvoiceId,
                invoiceNumber = invoice.Header.InvoiceNumber,
                invoiceType = invoice.Header.InvoiceType,
                invoiceStatus = invoice.Header.InvoiceStatus,
                invoiceDate = invoice.Header.InvoiceDate,
                billingPeriodStart = invoice.Header.BillingPeriodStart,
                billingPeriodEnd = invoice.Header.BillingPeriodEnd,
                customerName = invoice.Header.CustomerName,
                projectId = invoice.Header.ProjectId,
                projectCode = invoice.Header.ProjectCode,
                projectName = invoice.Header.ProjectName,
                contractType = invoice.Header.ContractType,
                projectManager = outputOptions.IncludeProjectManagerName
                    ? invoice.Header.ProjectManagerName
                    : "Project Management",
                projectCoordinator = outputOptions.IncludeProjectCoordinatorName
                    ? invoice.Header.ProjectCoordinatorName
                    : "Project Management",
                purchaseOrderNumber = invoice.Header.PurchaseOrderNumber,
                purchaseOrderAmount = invoice.Header.PurchaseOrderAmount,
                certiniaId = invoice.Header.CertiniaId,
                salesforceId = invoice.Header.SalesforceId,
                sellQuote = invoice.Header.SellQuote,
                subtotalAmount = invoice.Header.SubtotalAmount,
                adjustmentAmount = invoice.Header.AdjustmentAmount,
                taxAmount = invoice.Header.TaxAmount,
                totalAmount = invoice.Header.TotalAmount,
                notes = invoice.Header.Notes,
                lines = invoice.Lines.Select(line => new
                {
                    lineNumber = line.LineNumber,
                    workDate = line.WorkDate,
                    resource = CustomerResource(line, outputOptions.IncludeEngineerNames),
                    taskCode = line.TaskCode,
                    taskName = line.TaskName,
                    description = line.Description,
                    timeType = line.TimeType,
                    laborCategory = line.LaborCategory,
                    approvedHours = line.ApprovedHours,
                    rateCode = line.RateCode,
                    rateDescription = line.RateDescription,
                    unitRate = line.UnitRate,
                    amount = line.LineAmount
                })
            },
            document = new
            {
                format = artifact.Format,
                fileName = artifact.FileName,
                contentType = artifact.ContentType,
                sha256 = artifact.Sha256,
                contentBase64 = Convert.ToBase64String(artifact.Bytes)
            }
        });
        var includeResourceNames = outputOptions.IncludeAnyNames;

        await using var transaction = await connection.BeginTransactionAsync();
        Guid outboxId;
        string deliveryStatus;
        int attemptCount;
        var inserted = false;

        await using (var insert = new NpgsqlCommand("""
            INSERT INTO external_integration_outbox (
                system_code,
                local_entity,
                local_entity_id,
                operation_type,
                idempotency_key,
                payload_json,
                delivery_status,
                attempt_count,
                next_attempt_at,
                created_at,
                updated_at
            )
            VALUES (
                @system_code,
                'billing_invoice',
                @invoice_id,
                'create',
                @idempotency_key,
                @payload_json::jsonb,
                'pending',
                0,
                NOW(),
                NOW(),
                NOW()
            )
            ON CONFLICT (idempotency_key) DO NOTHING
            RETURNING
                external_integration_outbox_id,
                delivery_status,
                attempt_count;
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("system_code", SystemCode);
            insert.Parameters.AddWithValue("invoice_id", invoice.Header.BillingInvoiceId);
            insert.Parameters.AddWithValue("idempotency_key", idempotencyKey);
            insert.Parameters.AddWithValue("payload_json", payload);
            await using var reader = await insert.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                outboxId = reader.GetGuid(0);
                deliveryStatus = reader.GetString(1);
                attemptCount = reader.GetInt32(2);
                inserted = true;
            }
            else
            {
                outboxId = Guid.Empty;
                deliveryStatus = string.Empty;
                attemptCount = 0;
            }
        }

        if (!inserted)
        {
            await using var existing = new NpgsqlCommand("""
                SELECT
                    external_integration_outbox_id,
                    delivery_status,
                    attempt_count
                FROM external_integration_outbox
                WHERE idempotency_key = @idempotency_key;
                """, connection, transaction);
            existing.Parameters.AddWithValue("idempotency_key", idempotencyKey);
            await using var reader = await existing.ExecuteReaderAsync();
            await reader.ReadAsync();
            outboxId = reader.GetGuid(0);
            deliveryStatus = reader.GetString(1);
            attemptCount = reader.GetInt32(2);

            await AppendInvoiceEventAsync(
                connection,
                transaction,
                invoice.Header.BillingInvoiceId,
                "certinia_queue_reused",
                invoice.Header.InvoiceStatus,
                invoice.Header.InvoiceStatus,
                actorUserId,
                $"An idempotent Certinia queue request reused the existing {artifact.Format} artifact for invoice {invoice.Header.InvoiceNumber}.",
                new
                {
                    outboxId,
                    idempotencyKey,
                    documentFormat = artifact.Format,
                    documentSha256 = artifact.Sha256,
                    includeResourceNames
                });
        }
        else
        {
            await AppendInvoiceEventAsync(
                connection,
                transaction,
                invoice.Header.BillingInvoiceId,
                "certinia_queued",
                invoice.Header.InvoiceStatus,
                invoice.Header.InvoiceStatus,
                actorUserId,
                $"Invoice {invoice.Header.InvoiceNumber} queued for Certinia as {artifact.Format}; resource names {(includeResourceNames ? "included" : "hidden")}.",
                new
                {
                    outboxId,
                    idempotencyKey,
                    documentFormat = artifact.Format,
                    documentSha256 = artifact.Sha256,
                    includeResourceNames
                });
        }

        await transaction.CommitAsync();

        return new CertiniaQueueResult(
            outboxId,
            idempotencyKey,
            inserted,
            deliveryStatus,
            attemptCount,
            artifact.Format,
            artifact.FileName,
            artifact.Sha256,
            includeResourceNames);
    }

    private static async Task<CertiniaProcessSummary> ProcessOutboxAsync(
        NpgsqlConnection connection,
        CertiniaConfiguration configuration,
        Guid? invoiceId,
        int limit)
    {
        if (!configuration.CanTransmit)
        {
            return new CertiniaProcessSummary(0, 0, 0, 0);
        }

        var ids = new List<Guid>();
        await using (var select = new NpgsqlCommand("""
            SELECT external_integration_outbox_id
            FROM external_integration_outbox
            WHERE system_code = @system_code
              AND local_entity = 'billing_invoice'
              AND delivery_status IN ('pending', 'failed')
              AND attempt_count < 8
              AND (next_attempt_at IS NULL OR next_attempt_at <= NOW())
              AND (@invoice_id::uuid IS NULL OR local_entity_id = @invoice_id)
            ORDER BY created_at
            LIMIT @limit;
            """, connection))
        {
            select.Parameters.AddWithValue("system_code", SystemCode);
            select.Parameters.Add(
                "invoice_id",
                NpgsqlTypes.NpgsqlDbType.Uuid).Value =
                    invoiceId is null ? DBNull.Value : invoiceId.Value;
            select.Parameters.AddWithValue("limit", limit);
            await using var reader = await select.ExecuteReaderAsync();
            while (await reader.ReadAsync()) ids.Add(reader.GetGuid(0));
        }

        var succeeded = 0;
        var failed = 0;
        var skipped = 0;

        foreach (var outboxId in ids)
        {
            var claimed = await ClaimOutboxAsync(connection, outboxId);
            if (claimed is null)
            {
                skipped++;
                continue;
            }

            try
            {
                var response = await SendToCertiniaAsync(
                    configuration,
                    claimed.PayloadJson);
                await MarkOutboxSucceededAsync(
                    connection,
                    claimed,
                    response);
                succeeded++;
            }
            catch (Exception exception)
            {
                await MarkOutboxFailedAsync(
                    connection,
                    claimed,
                    exception.Message);
                failed++;
            }
        }

        return new CertiniaProcessSummary(
            ids.Count,
            succeeded,
            failed,
            skipped);
    }

    private static async Task<CertiniaOutboxClaim?> ClaimOutboxAsync(
        NpgsqlConnection connection,
        Guid outboxId)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE external_integration_outbox
            SET
                delivery_status = 'processing',
                attempt_count = attempt_count + 1,
                last_attempt_at = NOW(),
                updated_at = NOW(),
                last_error = ''
            WHERE external_integration_outbox_id = @outbox_id
              AND delivery_status IN ('pending', 'failed')
            RETURNING
                external_integration_outbox_id,
                local_entity_id,
                attempt_count,
                payload_json::text;
            """, connection);
        command.Parameters.AddWithValue("outbox_id", outboxId);
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync()) return null;

        return new CertiniaOutboxClaim(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetInt32(2),
            reader.GetString(3));
    }

    private static async Task<CertiniaTransportResponse> SendToCertiniaAsync(
        CertiniaConfiguration configuration,
        string payloadJson)
    {
        var accessToken = await AcquireAccessTokenAsync(configuration);
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(configuration.TimeoutSeconds)
        };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            CombineUrl(configuration.BaseUrl, configuration.UploadPath));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            "application/json"));
        request.Content = new StringContent(
            payloadJson,
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Certinia upload returned HTTP {(int)response.StatusCode}: {Limit(body, 1000)}");
        }

        var externalId = FindResponseValue(body, ExternalIdFields);
        var externalStatus = FindResponseValue(body, StatusFields);

        return new CertiniaTransportResponse(
            (int)response.StatusCode,
            body,
            externalId,
            externalStatus);
    }

    private static async Task<string> AcquireAccessTokenAsync(
        CertiniaConfiguration configuration)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(configuration.TimeoutSeconds)
        };
        var values = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = configuration.ClientId,
            ["client_secret"] = configuration.ClientSecret
        };
        if (!string.IsNullOrWhiteSpace(configuration.Scope))
        {
            values["scope"] = configuration.Scope;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            configuration.TokenUrl)
        {
            Content = new FormUrlEncodedContent(values)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            "application/json"));

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Certinia token endpoint returned HTTP {(int)response.StatusCode}: {Limit(body, 1000)}");
        }

        using var document = JsonDocument.Parse(body);
        var token = FindJsonString(
            document.RootElement,
            ["access_token", "accessToken", "token"]);

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "Certinia token response did not contain an access token.");
        }

        return token;
    }

    private static async Task MarkOutboxSucceededAsync(
        NpgsqlConnection connection,
        CertiniaOutboxClaim claim,
        CertiniaTransportResponse response)
    {
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var update = new NpgsqlCommand("""
            UPDATE external_integration_outbox
            SET
                delivery_status = 'succeeded',
                completed_at = NOW(),
                next_attempt_at = NULL,
                last_error = '',
                updated_at = NOW(),
                payload_json = jsonb_set(
                    jsonb_set(
                        jsonb_set(
                            payload_json,
                            '{certiniaExternalId}',
                            to_jsonb(CAST(@external_id AS text)),
                            TRUE
                        ),
                        '{certiniaStatus}',
                        to_jsonb(CAST(@external_status AS text)),
                        TRUE
                    ),
                    '{certiniaResponse}',
                    @response_json::jsonb,
                    TRUE
                )
            WHERE external_integration_outbox_id = @outbox_id;
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("outbox_id", claim.OutboxId);
            update.Parameters.AddWithValue("external_id", response.ExternalId);
            update.Parameters.AddWithValue("external_status", response.ExternalStatus);
            update.Parameters.AddWithValue(
                "response_json",
                NormalizeJsonObject(response.Body));
            await update.ExecuteNonQueryAsync();
        }

        if (claim.InvoiceId is Guid invoiceId)
        {
            var invoice = await LoadInvoiceHeaderForUpdateAsync(
                connection,
                transaction,
                invoiceId);

            if (invoice is not null)
            {
                var nextStatus = invoice.InvoiceStatus is "paid" or "void"
                    ? invoice.InvoiceStatus
                    : "sent";

                await UpdateInvoiceStatusAsync(
                    connection,
                    transaction,
                    invoiceId,
                    nextStatus);
                await AppendInvoiceEventAsync(
                    connection,
                    transaction,
                    invoiceId,
                    "certinia_sent",
                    invoice.InvoiceStatus,
                    nextStatus,
                    null,
                    $"Invoice {invoice.InvoiceNumber} was transmitted to Certinia.",
                    new
                    {
                        claim.OutboxId,
                        claim.AttemptCount,
                        response.HttpStatus,
                        response.ExternalId,
                        response.ExternalStatus
                    });
            }
        }

        await transaction.CommitAsync();
    }

    private static async Task MarkOutboxFailedAsync(
        NpgsqlConnection connection,
        CertiniaOutboxClaim claim,
        string error)
    {
        var retryMinutes = Math.Min(
            720,
            (int)Math.Pow(2, Math.Min(claim.AttemptCount, 8)) * 5);
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var update = new NpgsqlCommand("""
            UPDATE external_integration_outbox
            SET
                delivery_status = CASE
                    WHEN attempt_count >= 8 THEN 'dead_letter'
                    ELSE 'failed'
                END,
                next_attempt_at = CASE
                    WHEN attempt_count >= 8 THEN NULL
                    ELSE NOW() + make_interval(mins => @retry_minutes)
                END,
                last_error = @last_error,
                updated_at = NOW()
            WHERE external_integration_outbox_id = @outbox_id;
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("outbox_id", claim.OutboxId);
            update.Parameters.AddWithValue("retry_minutes", retryMinutes);
            update.Parameters.AddWithValue("last_error", Limit(error, 4000));
            await update.ExecuteNonQueryAsync();
        }

        if (claim.InvoiceId is Guid invoiceId)
        {
            var invoice = await LoadInvoiceHeaderForUpdateAsync(
                connection,
                transaction,
                invoiceId);

            if (invoice is not null)
            {
                await AppendInvoiceEventAsync(
                    connection,
                    transaction,
                    invoiceId,
                    "certinia_send_failed",
                    invoice.InvoiceStatus,
                    invoice.InvoiceStatus,
                    null,
                    $"Certinia transmission failed for invoice {invoice.InvoiceNumber}; the integration outbox will retry.",
                    new
                    {
                        claim.OutboxId,
                        claim.AttemptCount,
                        retryMinutes,
                        error = Limit(error, 1000)
                    });
            }
        }

        await transaction.CommitAsync();
    }

    private static async Task<CertiniaSyncSummary> SyncStatusesAsync(
        NpgsqlConnection connection,
        CertiniaConfiguration configuration,
        int limit)
    {
        var rows = new List<(Guid OutboxId, Guid InvoiceId, string ExternalId)>();

        await using (var command = new NpgsqlCommand("""
            SELECT
                external_integration_outbox_id,
                local_entity_id,
                COALESCE(payload_json->>'certiniaExternalId', '')
            FROM external_integration_outbox
            WHERE system_code = @system_code
              AND local_entity = 'billing_invoice'
              AND delivery_status = 'succeeded'
              AND local_entity_id IS NOT NULL
              AND COALESCE(payload_json->>'certiniaExternalId', '') <> ''
            ORDER BY completed_at DESC NULLS LAST
            LIMIT @limit;
            """, connection))
        {
            command.Parameters.AddWithValue("system_code", SystemCode);
            command.Parameters.AddWithValue("limit", limit);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2)));
            }
        }

        var updated = 0;
        var unchanged = 0;
        var failed = 0;

        foreach (var row in rows)
        {
            try
            {
                var status = await ReadCertiniaStatusAsync(
                    configuration,
                    row.ExternalId);
                var changed = await ApplyCertiniaStatusAsync(
                    connection,
                    row.OutboxId,
                    row.InvoiceId,
                    row.ExternalId,
                    status);
                if (changed) updated++; else unchanged++;
            }
            catch (Exception exception)
            {
                failed++;
                await RecordStatusFailureAsync(
                    connection,
                    row.OutboxId,
                    row.InvoiceId,
                    exception.Message);
            }
        }

        return new CertiniaSyncSummary(
            rows.Count,
            updated,
            unchanged,
            failed);
    }

    private static async Task<CertiniaStatusResponse> ReadCertiniaStatusAsync(
        CertiniaConfiguration configuration,
        string externalId)
    {
        var accessToken = await AcquireAccessTokenAsync(configuration);
        var path = configuration.StatusPathTemplate.Replace(
            "{id}",
            Uri.EscapeDataString(externalId),
            StringComparison.OrdinalIgnoreCase);
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(configuration.TimeoutSeconds)
        };
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            CombineUrl(configuration.BaseUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            "application/json"));

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Certinia status returned HTTP {(int)response.StatusCode}: {Limit(body, 1000)}");
        }

        var status = FindResponseValue(body, StatusFields);
        if (string.IsNullOrWhiteSpace(status))
        {
            throw new InvalidOperationException(
                "Certinia status response did not contain status, invoiceStatus, or state.");
        }

        return new CertiniaStatusResponse(
            status,
            body,
            (int)response.StatusCode);
    }

    private static async Task<bool> ApplyCertiniaStatusAsync(
        NpgsqlConnection connection,
        Guid outboxId,
        Guid invoiceId,
        string externalId,
        CertiniaStatusResponse response)
    {
        await using var transaction = await connection.BeginTransactionAsync();
        var invoice = await LoadInvoiceHeaderForUpdateAsync(
            connection,
            transaction,
            invoiceId);
        if (invoice is null)
        {
            await transaction.RollbackAsync();
            return false;
        }

        var mapped = MapProjectPulseInvoiceStatus(
            response.Status,
            invoice.InvoiceStatus);
        var changed = !string.Equals(
            mapped,
            invoice.InvoiceStatus,
            StringComparison.OrdinalIgnoreCase);

        await using (var updateOutbox = new NpgsqlCommand("""
            UPDATE external_integration_outbox
            SET
                payload_json = jsonb_set(
                    jsonb_set(
                        jsonb_set(
                            payload_json,
                            '{certiniaStatus}',
                            to_jsonb(CAST(@status AS text)),
                            TRUE
                        ),
                        '{certiniaStatusCheckedAt}',
                        to_jsonb(CAST(@checked_at AS text)),
                        TRUE
                    ),
                    '{certiniaStatusResponse}',
                    @response_json::jsonb,
                    TRUE
                ),
                updated_at = NOW()
            WHERE external_integration_outbox_id = @outbox_id;
            """, connection, transaction))
        {
            updateOutbox.Parameters.AddWithValue("outbox_id", outboxId);
            updateOutbox.Parameters.AddWithValue("status", response.Status);
            updateOutbox.Parameters.AddWithValue(
                "checked_at",
                DateTimeOffset.UtcNow.ToString("O"));
            updateOutbox.Parameters.AddWithValue(
                "response_json",
                NormalizeJsonObject(response.Body));
            await updateOutbox.ExecuteNonQueryAsync();
        }

        if (changed)
        {
            await UpdateInvoiceStatusAsync(
                connection,
                transaction,
                invoiceId,
                mapped);
            await AppendInvoiceEventAsync(
                connection,
                transaction,
                invoiceId,
                "certinia_status_updated",
                invoice.InvoiceStatus,
                mapped,
                null,
                $"Certinia status for invoice {invoice.InvoiceNumber} changed to {response.Status}.",
                new
                {
                    outboxId,
                    externalId,
                    certiniaStatus = response.Status,
                    mappedProjectPulseStatus = mapped,
                    response.HttpStatus
                });
        }
        else
        {
            await AppendInvoiceEventAsync(
                connection,
                transaction,
                invoiceId,
                "certinia_status_checked",
                invoice.InvoiceStatus,
                invoice.InvoiceStatus,
                null,
                $"Certinia status for invoice {invoice.InvoiceNumber} remains {response.Status}.",
                new
                {
                    outboxId,
                    externalId,
                    certiniaStatus = response.Status,
                    mappedProjectPulseStatus = mapped,
                    response.HttpStatus
                });
        }

        await transaction.CommitAsync();
        return changed;
    }

    private static async Task RecordStatusFailureAsync(
        NpgsqlConnection connection,
        Guid outboxId,
        Guid invoiceId,
        string error)
    {
        await using var transaction = await connection.BeginTransactionAsync();
        var invoice = await LoadInvoiceHeaderForUpdateAsync(
            connection,
            transaction,
            invoiceId);

        await using (var update = new NpgsqlCommand("""
            UPDATE external_integration_outbox
            SET
                last_error = @error,
                updated_at = NOW()
            WHERE external_integration_outbox_id = @outbox_id;
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("outbox_id", outboxId);
            update.Parameters.AddWithValue("error", Limit(error, 4000));
            await update.ExecuteNonQueryAsync();
        }

        if (invoice is not null)
        {
            await AppendInvoiceEventAsync(
                connection,
                transaction,
                invoiceId,
                "certinia_status_check_failed",
                invoice.InvoiceStatus,
                invoice.InvoiceStatus,
                null,
                $"Certinia status check failed for invoice {invoice.InvoiceNumber}.",
                new { outboxId, error = Limit(error, 1000) });
        }

        await transaction.CommitAsync();
    }

    private static async Task<InvoiceStatusHeader?> LoadInvoiceHeaderForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid invoiceId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT invoice_number, invoice_status
            FROM billing_invoices
            WHERE billing_invoice_id = @invoice_id
            FOR UPDATE;
            """, connection, transaction);
        command.Parameters.AddWithValue("invoice_id", invoiceId);
        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? new InvoiceStatusHeader(
                reader.GetString(0),
                reader.GetString(1))
            : null;
    }

    private static async Task UpdateInvoiceStatusAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid invoiceId,
        string status)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE billing_invoices
            SET invoice_status = @status
            WHERE billing_invoice_id = @invoice_id
              AND invoice_status <> 'void';
            """, connection, transaction);
        command.Parameters.AddWithValue("invoice_id", invoiceId);
        command.Parameters.AddWithValue("status", status);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AppendInvoiceEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid invoiceId,
        string eventType,
        string priorStatus,
        string newStatus,
        Guid? actorUserId,
        string reason,
        object eventData)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO billing_invoice_events (
                billing_invoice_id,
                event_type,
                prior_status,
                new_status,
                actor_user_id,
                event_reason,
                event_json
            )
            VALUES (
                @invoice_id,
                @event_type,
                @prior_status,
                @new_status,
                @actor_user_id,
                @event_reason,
                @event_json::jsonb
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("invoice_id", invoiceId);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("prior_status", priorStatus);
        command.Parameters.AddWithValue("new_status", newStatus);
        command.Parameters.AddWithValue(
            "actor_user_id",
            actorUserId is null ? DBNull.Value : actorUserId.Value);
        command.Parameters.AddWithValue("event_reason", reason);
        command.Parameters.AddWithValue(
            "event_json",
            JsonSerializer.Serialize(eventData));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<object>> LoadInvoiceOutboxAsync(
        NpgsqlConnection connection,
        Guid invoiceId)
    {
        var results = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT
                external_integration_outbox_id,
                idempotency_key,
                delivery_status,
                attempt_count,
                next_attempt_at,
                last_attempt_at,
                completed_at,
                last_error,
                payload_json->>'certiniaExternalId',
                payload_json->>'certiniaStatus',
                payload_json->'document'->>'format',
                payload_json->'document'->>'fileName',
                payload_json->'document'->>'sha256',
                COALESCE((payload_json->>'resourceNamesIncluded')::boolean, FALSE),
                created_at,
                updated_at
            FROM external_integration_outbox
            WHERE system_code = @system_code
              AND local_entity = 'billing_invoice'
              AND local_entity_id = @invoice_id
            ORDER BY created_at DESC;
            """, connection);
        command.Parameters.AddWithValue("system_code", SystemCode);
        command.Parameters.AddWithValue("invoice_id", invoiceId);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(new
            {
                outboxId = reader.GetGuid(0),
                idempotencyKey = reader.GetString(1),
                deliveryStatus = reader.GetString(2),
                attemptCount = reader.GetInt32(3),
                nextAttemptAt = ReadDateTimeOffset(reader, 4),
                lastAttemptAt = ReadDateTimeOffset(reader, 5),
                completedAt = ReadDateTimeOffset(reader, 6),
                lastError = reader.GetString(7),
                externalId = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                certiniaStatus = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                documentFormat = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                fileName = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                documentSha256 = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                resourceNamesIncluded = reader.GetBoolean(13),
                createdAt = reader.GetFieldValue<DateTimeOffset>(14),
                updatedAt = reader.GetFieldValue<DateTimeOffset>(15)
            });
        }

        return results;
    }

    private static async Task<IReadOnlyList<object>> LoadDeliveryEventsAsync(
        NpgsqlConnection connection,
        Guid invoiceId)
    {
        var results = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT
                billing_invoice_event_id,
                event_type,
                prior_status,
                new_status,
                event_reason,
                event_json::text,
                created_at
            FROM billing_invoice_events
            WHERE billing_invoice_id = @invoice_id
              AND event_type LIKE 'certinia_%'
            ORDER BY created_at DESC;
            """, connection);
        command.Parameters.AddWithValue("invoice_id", invoiceId);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(new
            {
                eventId = reader.GetGuid(0),
                eventType = reader.GetString(1),
                priorStatus = reader.GetString(2),
                newStatus = reader.GetString(3),
                reason = reader.GetString(4),
                eventData = JsonDocument.Parse(reader.GetString(5)).RootElement.Clone(),
                createdAt = reader.GetFieldValue<DateTimeOffset>(6)
            });
        }

        return results;
    }

    private static async Task<Guid> BeginSyncRunAsync(
        NpgsqlConnection connection,
        string correlationId)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO external_integration_sync_runs (
                system_code,
                sync_direction,
                sync_mode,
                correlation_id,
                run_status,
                started_at,
                run_metadata_json
            )
            VALUES (
                @system_code,
                'bidirectional',
                'nightly',
                @correlation_id,
                'running',
                NOW(),
                '{}'::jsonb
            )
            RETURNING external_integration_sync_run_id;
            """, connection);
        command.Parameters.AddWithValue("system_code", SystemCode);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        return (Guid)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Unable to create Certinia sync run."));
    }

    private static async Task FinishSyncRunAsync(
        NpgsqlConnection connection,
        Guid runId,
        string status,
        int read,
        int created,
        int updated,
        int failed,
        string error,
        object metadata)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE external_integration_sync_runs
            SET
                run_status = @status,
                records_read = @records_read,
                records_created = @records_created,
                records_updated = @records_updated,
                records_failed = @records_failed,
                completed_at = NOW(),
                error_summary = @error,
                run_metadata_json = @metadata::jsonb
            WHERE external_integration_sync_run_id = @run_id;
            """, connection);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("records_read", read);
        command.Parameters.AddWithValue("records_created", created);
        command.Parameters.AddWithValue("records_updated", updated);
        command.Parameters.AddWithValue("records_failed", failed);
        command.Parameters.AddWithValue("error", Limit(error, 4000));
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(metadata));
        await command.ExecuteNonQueryAsync();
    }

    private static string FindResponseValue(
        string body,
        IReadOnlyCollection<string> keys)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(body);
            return FindJsonString(document.RootElement, keys);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FindJsonString(
        JsonElement element,
        IReadOnlyCollection<string> keys)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (keys.Any(key => string.Equals(
                        key,
                        property.Name,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    return property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.ToString();
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var nested = FindJsonString(property.Value, keys);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindJsonString(item, keys);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }

        return string.Empty;
    }

    private static string MapProjectPulseInvoiceStatus(
        string certiniaStatus,
        string current)
    {
        var normalized = Clean(certiniaStatus).ToLowerInvariant();

        if (normalized is "paid" or "settled" or "closed_paid") return "paid";
        if (normalized is "sent" or "posted" or "delivered" or "open" or "approved") return "sent";
        if (normalized is "void" or "voided" or "cancelled" or "canceled") return "void";
        return current;
    }

    private static string CustomerResource(
        CertiniaInvoiceLine line,
        bool includeResourceNames)
    {
        if (includeResourceNames)
        {
            return Fallback(line.ResourceName, "Professional Services Engineer");
        }

        var combined = $"{line.LaborCategory} {line.TaskCode} {line.TaskName}".ToLowerInvariant();
        return combined.Contains("project management", StringComparison.Ordinal)
            || combined.Contains("coordination", StringComparison.Ordinal)
            || combined.Contains("project manager", StringComparison.Ordinal)
                ? "Project Management"
                : "Professional Services Engineer";
    }

    private static string NormalizeDocumentFormat(string? value)
    {
        var normalized = Clean(value).ToLowerInvariant();
        return normalized is "excel" or "xls" or "xlsx" ? "excel" : "pdf";
    }

    private static bool ReadBoolean(string? value)
    {
        return Clean(value).ToLowerInvariant() is "true" or "1" or "yes" or "y" or "on";
    }

    private static bool ReadQueryBoolean(
        HttpContext context,
        string name,
        bool fallback)
    {
        return context.Request.Query.ContainsKey(name)
            ? ReadBoolean(context.Request.Query[name].ToString())
            : fallback;
    }

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? string.Empty;
    }

    private static string Fallback(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static DateOnly? ReadDateOnly(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetFieldValue<DateOnly>(ordinal);
    }

    private static DateTimeOffset? ReadDateTimeOffset(
        NpgsqlDataReader reader,
        int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private static string FormatDate(DateOnly? value)
    {
        return value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            ?? "Not configured";
    }

    private static string Sha256Hex(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string CombineUrl(string baseUrl, string path)
    {
        return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }

    private static string NormalizeJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "{}";

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.GetRawText();
        }
        catch
        {
            return JsonSerializer.Serialize(new { raw = Limit(text, 4000) });
        }
    }

    private static string Limit(string? value, int maxLength)
    {
        var text = value ?? string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(value
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "invoice" : cleaned;
    }

    private static string PdfAscii(string? value)
    {
        var builder = new StringBuilder();
        foreach (var character in value ?? string.Empty)
        {
            builder.Append(character is >= ' ' and <= '~' ? character : ' ');
        }
        return builder.ToString();
    }

    private static string PdfEscape(string value)
    {
        return value.Replace("\\", "\\\\")
            .Replace("(", "\\(")
            .Replace(")", "\\)");
    }

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    public sealed record CertiniaInvoiceSendRequest(
        string? DocumentFormat,
        bool IncludeResourceNames,
        bool TransmitNow,
        bool? IncludeEngineerNames,
        bool? IncludeProjectManagerName,
        bool? IncludeProjectCoordinatorName);

    private sealed record InvoiceOutputOptions(
        bool IncludeEngineerNames,
        bool IncludeProjectManagerName,
        bool IncludeProjectCoordinatorName)
    {
        public bool IncludeAnyNames => IncludeEngineerNames
            || IncludeProjectManagerName
            || IncludeProjectCoordinatorName;
    }

    private sealed record CertiniaInvoiceHeader(
        Guid BillingInvoiceId,
        string InvoiceNumber,
        Guid ProjectId,
        string InvoiceType,
        string InvoiceStatus,
        DateOnly? BillingPeriodStart,
        DateOnly? BillingPeriodEnd,
        DateOnly? InvoiceDate,
        string CustomerName,
        string ProjectCode,
        string ProjectName,
        string ContractType,
        string ProjectManagerName,
        string ProjectCoordinatorName,
        string PurchaseOrderNumber,
        decimal? PurchaseOrderAmount,
        string CertiniaId,
        string SalesforceId,
        string SellQuote,
        decimal SubtotalAmount,
        decimal AdjustmentAmount,
        decimal TaxAmount,
        decimal TotalAmount,
        string Notes,
        string ImmutableSnapshotJson,
        DateTimeOffset CreatedAt,
        DateTimeOffset? FinalizedAt);

    private sealed record CertiniaInvoiceLine(
        Guid BillingInvoiceLineId,
        int LineNumber,
        DateOnly? WorkDate,
        string ResourceName,
        string ResourceEmail,
        string TaskCode,
        string TaskName,
        string Description,
        string TimeType,
        string LaborCategory,
        decimal ApprovedHours,
        string RateCode,
        string RateDescription,
        decimal UnitRate,
        decimal LineAmount);

    private sealed record CertiniaInvoiceSnapshot(
        CertiniaInvoiceHeader Header,
        IReadOnlyList<CertiniaInvoiceLine> Lines)
    {
        public string ImmutableSnapshotSha256 { get; init; } = string.Empty;
    }

    private sealed record CertiniaArtifact(
        string Format,
        string ContentType,
        string FileName,
        byte[] Bytes,
        string Sha256);

    private sealed record CertiniaQueueResult(
        Guid OutboxId,
        string IdempotencyKey,
        bool Inserted,
        string DeliveryStatus,
        int AttemptCount,
        string DocumentFormat,
        string FileName,
        string DocumentSha256,
        bool IncludeResourceNames);

    private sealed record CertiniaOutboxClaim(
        Guid OutboxId,
        Guid? InvoiceId,
        int AttemptCount,
        string PayloadJson);

    private sealed record CertiniaTransportResponse(
        int HttpStatus,
        string Body,
        string ExternalId,
        string ExternalStatus);

    private sealed record CertiniaStatusResponse(
        string Status,
        string Body,
        int HttpStatus);

    private sealed record InvoiceStatusHeader(
        string InvoiceNumber,
        string InvoiceStatus);

    private sealed record CertiniaProcessSummary(
        int Read,
        int Succeeded,
        int Failed,
        int Skipped);

    private sealed record CertiniaSyncSummary(
        int Read,
        int Updated,
        int Unchanged,
        int Failed);

    private sealed record CertiniaConfiguration(
        bool Enabled,
        string BaseUrl,
        string TokenUrl,
        string UploadPath,
        string StatusPathTemplate,
        string Scope,
        string Transport,
        string DefaultDocumentFormat,
        int TimeoutSeconds,
        string ClientId,
        string ClientSecret,
        string DatabaseConnectionStatus,
        bool DatabaseOutboundEnabled,
        IReadOnlyList<string> Missing)
    {
        public bool CanTransmit => Enabled
            && Missing.Count == 0
            && DatabaseOutboundEnabled
            && DatabaseConnectionStatus is "configured" or "connected";

        public object ToSafeResponse() => new
        {
            enabled = Enabled,
            canTransmit = CanTransmit,
            connectorStatus = DatabaseConnectionStatus,
            outboundEnabled = DatabaseOutboundEnabled,
            baseUrlConfigured = !string.IsNullOrWhiteSpace(BaseUrl),
            tokenUrlConfigured = !string.IsNullOrWhiteSpace(TokenUrl),
            uploadPathConfigured = !string.IsNullOrWhiteSpace(UploadPath),
            statusPathConfigured = !string.IsNullOrWhiteSpace(StatusPathTemplate),
            clientIdConfigured = !string.IsNullOrWhiteSpace(ClientId),
            clientSecretConfigured = !string.IsNullOrWhiteSpace(ClientSecret),
            transport = Transport,
            defaultDocumentFormat = DefaultDocumentFormat,
            timeoutSeconds = TimeoutSeconds,
            resourceNamesDefault = "hidden",
            personalNameControls = new[]
            {
                "engineer",
                "projectManager",
                "projectCoordinator"
            },
            supportedDocumentFormats = new[] { "pdf", "excel" },
            missingConfiguration = Missing
        };
    }
}
