using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 021 customer-directory synchronization through the authoritative
/// Module 026 Zendesk Sell connection. SELL organization records create or
/// update Pulse customers; locally maintained customer contacts remain
/// owned by Module 021 and are never overwritten by this synchronization.
/// </summary>
public static class CustomerDirectorySellSyncModule
{
    private const string ModuleNumber = "021";
    private const string ProviderModuleNumber = "026";
    private const string ProviderKey = "zendesk_sell";
    private const string SourceSystem = "SELL";
    private const string MigrationId = "049_module_021_sell_customer_sync";
    private const int MaximumRequestBytes = 64 * 1024;
    private const int MaximumImportRecords = 50;
    private const int MaximumPageSize = 100;
    private static readonly Uri SellBaseUri = new("https://api.getbase.com/");

    private static readonly string[] ViewRoles =
    {
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "INTEGRATION_ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR", "PROJECT_COORDINATOR", "SALES",
        "ACCOUNT_EXECUTIVE", "ACCOUNT_EXECUTIVES", "INSIDE_SALES",
        "SOLUTION_ARCHITECT", "SA", "SAA"
    };

    private static readonly string[] ManageRoles =
    {
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "INTEGRATION_ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR", "PROJECT_COORDINATOR"
    };

    public static WebApplication MapCustomerDirectorySellSyncEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/customers/sell/status",
            (Func<HttpContext, Task<IResult>>)GetStatusAsync);
        app.MapPost(
            "/api/customers/sell/preview",
            (Func<HttpContext, IHttpClientFactory, Task<IResult>>)PreviewAsync);
        app.MapPost(
            "/api/customers/sell/import",
            (Func<HttpContext, IHttpClientFactory, Task<IResult>>)ImportAsync);
        app.MapGet(
            "/api/customers/sell/runs",
            (Func<HttpContext, Task<IResult>>)GetRunsAsync);
        return app;
    }

    private static async Task<IResult> GetStatusAsync(HttpContext context)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;

        await using var connection = await OpenConnectionAsync(context);
        if (connection is null) return DependencyUnavailable();
        var schema = await ReadSchemaStatusAsync(connection, context.RequestAborted);
        if (!schema.ProviderReady) return ProviderSchemaUnavailable();
        if (!schema.SyncReady) return SyncSchemaUnavailable();

        var provider = await ReadProviderAsync(connection, context.RequestAborted);
        var linkedCustomers = await ScalarIntAsync(
            connection,
            "SELECT COUNT(*) FROM customer_directory_source_links WHERE source_system = 'SELL';",
            context.RequestAborted);
        var lastRun = await ReadLastRunAsync(connection, context.RequestAborted);

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "sell_customer_sync_status_loaded",
            providerModule = ProviderModuleNumber,
            providerKey = ProviderKey,
            sourceSystem = SourceSystem,
            authoritativeCustomerSource = true,
            localContactEnrichmentPreserved = true,
            provider = provider is null
                ? new
                {
                    configured = false,
                    name = "SELL (Zendesk Sell)",
                    authModel = string.Empty,
                    enabled = false,
                    availabilityStatus = "not_configured",
                    credentialConfigured = false,
                    baseUrl = SellBaseUri.GetLeftPart(UriPartial.Authority)
                }
                : new
                {
                    configured = true,
                    name = provider.ProviderName,
                    authModel = provider.AuthModel,
                    enabled = provider.IsEnabled,
                    availabilityStatus = provider.AvailabilityStatus,
                    credentialConfigured = provider.CredentialConfigured,
                    baseUrl = SellBaseUri.GetLeftPart(UriPartial.Authority)
                },
            linkedCustomers,
            lastRun,
            migration = MigrationId,
            secretValuesReturned = false
        });
    }

    private static async Task<IResult> PreviewAsync(HttpContext context, IHttpClientFactory httpClientFactory)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;
        if (!SameOrigin(context)) return OriginRejected();

        var body = await ReadBodyAsync<SellPreviewRequest>(context);
        if (body.Failure is not null) return body.Failure;
        var request = body.Value ?? new SellPreviewRequest(null, null, null, null);
        var page = Math.Clamp(request.Page ?? 1, 1, 100000);
        var pageSize = Math.Clamp(request.PageSize ?? MaximumPageSize, 1, MaximumPageSize);
        var search = Clean(request.Search, 200);
        var relationship = NormalizeRelationship(request.Relationship);
        if (relationship is null) return Invalid("Relationship filter must be all, customer, current_customer, past_customer, or prospect.");

        await using var connection = await OpenConnectionAsync(context);
        if (connection is null) return DependencyUnavailable();
        var schema = await ReadSchemaStatusAsync(connection, context.RequestAborted);
        if (!schema.ProviderReady) return ProviderSchemaUnavailable();
        if (!schema.SyncReady) return SyncSchemaUnavailable();

        var provider = await ReadProviderAsync(connection, context.RequestAborted);
        var readiness = ProviderReadinessFailure(provider);
        if (readiness is not null) return readiness;

        var runId = await StartRunAsync(
            connection,
            ActualUserId(context)!.Value,
            page,
            pageSize,
            search,
            context.RequestAborted);

        try
        {
            var authorizationHeader = await ResolveAuthorizationAsync(
                connection,
                provider!,
                context.RequestAborted);
            if (authorizationHeader.Failure is not null)
            {
                await FailRunAsync(connection, runId, authorizationHeader.FailureCode, authorizationHeader.FailureMessage, context.RequestAborted);
                return authorizationHeader.Failure;
            }

            var uri = new Uri(
                SellBaseUri,
                $"v2/contacts?page={page}&per_page={pageSize}&sort_by=updated_at:desc");
            var response = await SendSellAsync(
                httpClientFactory,
                uri,
                authorizationHeader.HeaderName!,
                authorizationHeader.HeaderValue!,
                context.RequestAborted);
            if (response.Failure is not null)
            {
                await FailRunAsync(connection, runId, response.FailureCode, response.FailureMessage, context.RequestAborted);
                return response.Failure;
            }

            using var document = JsonDocument.Parse(response.Body!);
            var allRecords = ParseOrganizations(document.RootElement);
            var organizations = allRecords
                .Where(item => MatchesRelationship(item, relationship))
                .Where(item => MatchesSearch(item, search))
                .ToList();
            var links = await ReadLinksAsync(connection, organizations.Select(item => item.SourceRecordId), context.RequestAborted);
            var localNames = await ReadLocalCustomersByNameAsync(connection, organizations.Select(item => item.Name), context.RequestAborted);

            var rows = organizations.Select(item =>
            {
                links.TryGetValue(item.SourceRecordId, out var link);
                localNames.TryGetValue(NormalizeName(item.Name), out var local);
                return new
                {
                    item.SourceRecordId,
                    item.Name,
                    item.CustomerStatus,
                    item.ProspectStatus,
                    item.Industry,
                    item.Website,
                    item.Phone,
                    item.Email,
                    item.AddressLine1,
                    item.City,
                    item.StateRegion,
                    item.PostalCode,
                    item.Country,
                    item.UpdatedAt,
                    linked = link is not null,
                    localClientId = link?.ClientId ?? local?.ClientId,
                    localClientName = link?.ClientName ?? local?.ClientName,
                    matchType = link is not null ? "source_link" : local is not null ? "normalized_name" : "new_customer",
                    importAction = link is not null ? "update" : local is not null ? "link_existing" : "create"
                };
            }).ToArray();

            await CompletePreviewRunAsync(
                connection,
                runId,
                allRecords.Count,
                organizations.Count,
                rows.Count(item => item.linked),
                context.RequestAborted);

            return Results.Ok(new
            {
                module = ModuleNumber,
                status = "sell_customer_preview_loaded",
                providerModule = ProviderModuleNumber,
                providerKey = ProviderKey,
                sourceSystem = SourceSystem,
                page,
                pageSize,
                search,
                relationship,
                sourceRecordsSeen = allRecords.Count,
                organizationsSeen = organizations.Count,
                linkedCount = rows.Count(item => item.linked),
                newCount = rows.Count(item => item.importAction == "create"),
                existingMatchCount = rows.Count(item => item.importAction == "link_existing"),
                customers = rows,
                runId,
                localContactEnrichmentPreserved = true,
                secretValuesReturned = false,
                message = "SELL organizations were previewed through the active Module 026 connection. Select the customers to import or refresh."
            });
        }
        catch (JsonException)
        {
            await FailRunAsync(connection, runId, "sell_response_invalid", "SELL returned an invalid contacts response.", context.RequestAborted);
            return ProviderFailure("sell_response_invalid", "SELL returned data that Pulse could not read.");
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            await FailRunAsync(connection, runId, "sell_timeout", "SELL did not respond before the connection timeout.", context.RequestAborted);
            return Results.Json(new { module = ModuleNumber, status = "sell_timeout", message = "SELL did not respond before the connection timeout." }, statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException)
        {
            await FailRunAsync(connection, runId, "sell_connection_failed", "Pulse could not reach SELL.", context.RequestAborted);
            return ProviderFailure("sell_connection_failed", "Pulse could not reach SELL.");
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "preview SELL customers");
            await FailRunAsync(connection, runId, "sell_preview_failed", "SELL customer preview could not be completed.", context.RequestAborted);
            return ProviderFailure("sell_preview_failed", "SELL customer preview could not be completed.");
        }
    }

    private static async Task<IResult> ImportAsync(HttpContext context, IHttpClientFactory httpClientFactory)
    {
        var authorization = await AuthorizeManageAsync(context);
        if (authorization is not null) return authorization;
        if (!SameOrigin(context)) return OriginRejected();

        var body = await ReadBodyAsync<SellImportRequest>(context);
        if (body.Failure is not null) return body.Failure;
        var selectedIds = (body.Value?.SourceRecordIds ?? Array.Empty<string>())
            .Select(value => Clean(value, 200))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (selectedIds.Length == 0) return Invalid("Select at least one SELL organization to import.");
        if (selectedIds.Length > MaximumImportRecords) return Invalid($"A maximum of {MaximumImportRecords} SELL organizations can be imported at once.");
        if (selectedIds.Any(value => !long.TryParse(value, out var parsed) || parsed <= 0))
            return Invalid("Every SELL source record ID must be a positive number.");

        await using var connection = await OpenConnectionAsync(context);
        if (connection is null) return DependencyUnavailable();
        var schema = await ReadSchemaStatusAsync(connection, context.RequestAborted);
        if (!schema.ProviderReady) return ProviderSchemaUnavailable();
        if (!schema.SyncReady) return SyncSchemaUnavailable();

        var provider = await ReadProviderAsync(connection, context.RequestAborted);
        var readiness = ProviderReadinessFailure(provider);
        if (readiness is not null) return readiness;

        var actor = ActualUserId(context)!.Value;
        var runId = await StartRunAsync(connection, actor, 1, selectedIds.Length, string.Empty, context.RequestAborted);
        var imported = 0;
        var updated = 0;
        var linked = 0;
        var skipped = 0;
        var failed = 0;
        var outcomes = new List<object>();

        try
        {
            var authorizationHeader = await ResolveAuthorizationAsync(connection, provider!, context.RequestAborted);
            if (authorizationHeader.Failure is not null)
            {
                await FailRunAsync(connection, runId, authorizationHeader.FailureCode, authorizationHeader.FailureMessage, context.RequestAborted);
                return authorizationHeader.Failure;
            }

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            for (var index = 0; index < selectedIds.Length; index++)
            {
                var sourceRecordId = selectedIds[index];
                var savepoint = $"sell_customer_{index}";
                await ExecuteControlAsync(connection, transaction, $"SAVEPOINT {savepoint};", context.RequestAborted);
                try
                {
                    var uri = new Uri(SellBaseUri, $"v2/contacts/{Uri.EscapeDataString(sourceRecordId)}");
                    var response = await SendSellAsync(
                        httpClientFactory,
                        uri,
                        authorizationHeader.HeaderName!,
                        authorizationHeader.HeaderValue!,
                        context.RequestAborted);
                    if (response.Failure is not null)
                    {
                        failed++;
                        outcomes.Add(new { sourceRecordId, status = "failed", resultCode = response.FailureCode });
                        await ExecuteControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", context.RequestAborted);
                        continue;
                    }

                    using var document = JsonDocument.Parse(response.Body!);
                    var organization = ParseSingleOrganization(document.RootElement);
                    if (organization is null)
                    {
                        skipped++;
                        outcomes.Add(new { sourceRecordId, status = "skipped", resultCode = "not_an_organization" });
                        await ExecuteControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", context.RequestAborted);
                        continue;
                    }

                    var outcome = await UpsertCustomerAsync(
                        connection,
                        transaction,
                        organization,
                        actor,
                        context.RequestAborted);
                    switch (outcome.Status)
                    {
                        case "imported": imported++; break;
                        case "updated": updated++; break;
                        case "linked": linked++; break;
                        default: skipped++; break;
                    }
                    outcomes.Add(new
                    {
                        sourceRecordId,
                        organization.Name,
                        outcome.ClientId,
                        outcome.ClientCode,
                        status = outcome.Status,
                        outcome.ResultCode
                    });
                    await ExecuteControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", context.RequestAborted);
                }
                catch (Exception exception) when (exception is PostgresException or JsonException or InvalidOperationException)
                {
                    await ExecuteControlAsync(connection, transaction, $"ROLLBACK TO SAVEPOINT {savepoint};", context.RequestAborted);
                    failed++;
                    outcomes.Add(new { sourceRecordId, status = "failed", resultCode = "customer_upsert_failed" });
                    await ExecuteControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", context.RequestAborted);
                }
            }

            var finalStatus = failed == 0 ? "completed" : "completed_with_failures";
            await CompleteImportRunAsync(
                connection,
                transaction,
                runId,
                selectedIds.Length,
                imported,
                updated,
                linked,
                skipped,
                failed,
                finalStatus,
                context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);

            return Results.Ok(new
            {
                module = ModuleNumber,
                status = failed == 0 ? "sell_customers_imported" : "sell_customers_imported_with_failures",
                providerModule = ProviderModuleNumber,
                providerKey = ProviderKey,
                sourceSystem = SourceSystem,
                runId,
                imported,
                updated,
                linked,
                skipped,
                failed,
                transactionCommitted = true,
                localContactEnrichmentPreserved = true,
                results = outcomes,
                message = $"SELL customer synchronization completed: {imported} created, {updated} refreshed, {linked} linked to existing customers, {skipped} skipped, and {failed} failed. Local contact details remain editable in Module 021."
            });
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            await FailRunAsync(connection, runId, "sell_timeout", "SELL did not respond before the connection timeout.", context.RequestAborted);
            return Results.Json(new { module = ModuleNumber, status = "sell_timeout", message = "SELL did not respond before the connection timeout." }, statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException)
        {
            await FailRunAsync(connection, runId, "sell_connection_failed", "Pulse could not reach SELL.", context.RequestAborted);
            return ProviderFailure("sell_connection_failed", "Pulse could not reach SELL.");
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "import SELL customers");
            await FailRunAsync(connection, runId, "sell_import_failed", "SELL customer synchronization could not be completed.", context.RequestAborted);
            return ProviderFailure("sell_import_failed", "SELL customer synchronization could not be completed.");
        }
    }

    private static async Task<IResult> GetRunsAsync(HttpContext context)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;

        await using var connection = await OpenConnectionAsync(context);
        if (connection is null) return DependencyUnavailable();
        var schema = await ReadSchemaStatusAsync(connection, context.RequestAborted);
        if (!schema.SyncReady) return SyncSchemaUnavailable();

        await using var command = new NpgsqlCommand("""
            SELECT customer_directory_sync_run_id, started_at, completed_at, status,
                   page_requested, page_size, search_text, source_records_seen,
                   organizations_seen, imported_count, updated_count, linked_count,
                   skipped_count, failed_count, error_code, message
            FROM customer_directory_sync_runs
            WHERE provider_key = @provider
            ORDER BY started_at DESC
            LIMIT 25;
            """, connection);
        command.Parameters.AddWithValue("provider", ProviderKey);
        var runs = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
        while (await reader.ReadAsync(context.RequestAborted))
        {
            runs.Add(new
            {
                runId = reader.GetGuid(0),
                startedAt = reader.GetFieldValue<DateTimeOffset>(1).ToUniversalTime(),
                completedAt = reader.IsDBNull(2) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(2).ToUniversalTime(),
                status = reader.GetString(3),
                page = reader.GetInt32(4),
                pageSize = reader.GetInt32(5),
                search = reader.GetString(6),
                sourceRecordsSeen = reader.GetInt32(7),
                organizationsSeen = reader.GetInt32(8),
                imported = reader.GetInt32(9),
                updated = reader.GetInt32(10),
                linked = reader.GetInt32(11),
                skipped = reader.GetInt32(12),
                failed = reader.GetInt32(13),
                errorCode = reader.GetString(14),
                message = reader.GetString(15)
            });
        }

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "sell_customer_sync_runs_loaded",
            providerKey = ProviderKey,
            runs,
            secretValuesReturned = false
        });
    }

    private static async Task<CustomerUpsertOutcome> UpsertCustomerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SellOrganization organization,
        Guid actor,
        CancellationToken cancellationToken)
    {
        var linkedClientId = await ReadLinkedClientIdAsync(
            connection,
            transaction,
            organization.SourceRecordId,
            cancellationToken);
        var isActive = !organization.CustomerStatus.Equals("past", StringComparison.OrdinalIgnoreCase);
        var payloadHash = SourceHash(organization);

        if (linkedClientId is not null)
        {
            await using (var update = new NpgsqlCommand("""
                UPDATE clients
                SET client_name = @name,
                    is_active = @active,
                    updated_at = NOW()
                WHERE client_id = @client_id;
                """, connection, transaction))
            {
                update.Parameters.AddWithValue("name", organization.Name);
                update.Parameters.AddWithValue("active", isActive);
                update.Parameters.AddWithValue("client_id", linkedClientId.Value);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
            await UpsertSourceLinkAsync(connection, transaction, organization, linkedClientId.Value, actor, payloadHash, cancellationToken);
            var code = await ReadClientCodeAsync(connection, transaction, linkedClientId.Value, cancellationToken);
            return new("updated", "source_link_refreshed", linkedClientId.Value, code);
        }

        var nameMatch = await ReadCustomerByNameAsync(connection, transaction, organization.Name, cancellationToken);
        if (nameMatch is not null)
        {
            await UpsertSourceLinkAsync(connection, transaction, organization, nameMatch.ClientId, actor, payloadHash, cancellationToken);
            return new("linked", "existing_customer_linked", nameMatch.ClientId, nameMatch.ClientCode);
        }

        var clientId = Guid.NewGuid();
        var clientCode = await AllocateClientCodeAsync(connection, transaction, organization.Name, organization.SourceRecordId, cancellationToken);
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO clients (client_id, client_name, client_code, is_active, created_at, updated_at)
            VALUES (@client_id, @name, @code, @active, NOW(), NOW());
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("client_id", clientId);
            insert.Parameters.AddWithValue("name", organization.Name);
            insert.Parameters.AddWithValue("code", clientCode);
            insert.Parameters.AddWithValue("active", isActive);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await UpsertSourceLinkAsync(connection, transaction, organization, clientId, actor, payloadHash, cancellationToken);
        return new("imported", "customer_created", clientId, clientCode);
    }

    private static async Task UpsertSourceLinkAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SellOrganization organization,
        Guid clientId,
        Guid actor,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO customer_directory_source_links (
                source_system, source_record_id, client_id, source_record_type,
                source_name, source_customer_status, source_prospect_status,
                source_updated_at, source_payload_hash, last_synced_at,
                created_by, updated_by, created_at, updated_at
            ) VALUES (
                @source_system, @source_record_id, @client_id, 'organization',
                @source_name, @customer_status, @prospect_status,
                @source_updated_at, @payload_hash, NOW(), @actor, @actor, NOW(), NOW()
            )
            ON CONFLICT (source_system, source_record_id) DO UPDATE
            SET client_id = EXCLUDED.client_id,
                source_name = EXCLUDED.source_name,
                source_customer_status = EXCLUDED.source_customer_status,
                source_prospect_status = EXCLUDED.source_prospect_status,
                source_updated_at = EXCLUDED.source_updated_at,
                source_payload_hash = EXCLUDED.source_payload_hash,
                last_synced_at = NOW(),
                updated_by = EXCLUDED.updated_by,
                updated_at = NOW();
            """, connection, transaction);
        command.Parameters.AddWithValue("source_system", SourceSystem);
        command.Parameters.AddWithValue("source_record_id", organization.SourceRecordId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("source_name", organization.Name);
        command.Parameters.AddWithValue("customer_status", organization.CustomerStatus);
        command.Parameters.AddWithValue("prospect_status", organization.ProspectStatus);
        command.Parameters.AddWithValue("source_updated_at", organization.UpdatedAt.HasValue ? (object)organization.UpdatedAt.Value : DBNull.Value);
        command.Parameters.AddWithValue("payload_hash", payloadHash);
        command.Parameters.AddWithValue("actor", actor);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Guid?> ReadLinkedClientIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sourceRecordId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT client_id
            FROM customer_directory_source_links
            WHERE source_system = @source_system AND source_record_id = @source_record_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("source_system", SourceSystem);
        command.Parameters.AddWithValue("source_record_id", sourceRecordId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid clientId ? clientId : null;
    }

    private static async Task<LocalCustomer?> ReadCustomerByNameAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string name,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT client_id, COALESCE(client_code, '')
            FROM clients
            WHERE lower(trim(client_name)) = lower(trim(@name))
            LIMIT 1;
            """, connection, transaction);
        command.Parameters.AddWithValue("name", name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetGuid(0), reader.GetString(1), name)
            : null;
    }

    private static async Task<string> ReadClientCodeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid clientId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT COALESCE(client_code, '') FROM clients WHERE client_id = @client_id;",
            connection,
            transaction);
        command.Parameters.AddWithValue("client_id", clientId);
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? string.Empty;
    }

    private static async Task<string> AllocateClientCodeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string name,
        string sourceRecordId,
        CancellationToken cancellationToken)
    {
        var baseCode = new string(name
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .Take(8)
            .ToArray());
        if (string.IsNullOrWhiteSpace(baseCode)) baseCode = $"SELL{sourceRecordId}"[..Math.Min(8, 4 + sourceRecordId.Length)];

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var suffix = attempt == 0 ? string.Empty : attempt.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var prefixLength = Math.Max(1, Math.Min(8 - suffix.Length, baseCode.Length));
            var candidate = (baseCode[..prefixLength] + suffix).ToUpperInvariant();
            await using var command = new NpgsqlCommand(
                "SELECT NOT EXISTS (SELECT 1 FROM clients WHERE client_code = @code);",
                connection,
                transaction);
            command.Parameters.AddWithValue("code", candidate);
            if (await command.ExecuteScalarAsync(cancellationToken) is true) return candidate;
        }
        throw new InvalidOperationException("A unique customer code could not be allocated.");
    }

    private static async Task<Dictionary<string, LocalCustomer>> ReadLinksAsync(
        NpgsqlConnection connection,
        IEnumerable<string> sourceRecordIds,
        CancellationToken cancellationToken)
    {
        var ids = sourceRecordIds.Distinct(StringComparer.Ordinal).ToArray();
        var result = new Dictionary<string, LocalCustomer>(StringComparer.Ordinal);
        if (ids.Length == 0) return result;

        await using var command = new NpgsqlCommand("""
            SELECT link.source_record_id, client.client_id, COALESCE(client.client_code, ''), client.client_name
            FROM customer_directory_source_links link
            JOIN clients client ON client.client_id = link.client_id
            WHERE link.source_system = @source_system
              AND link.source_record_id = ANY(@source_ids);
            """, connection);
        command.Parameters.AddWithValue("source_system", SourceSystem);
        command.Parameters.AddWithValue("source_ids", ids);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result[reader.GetString(0)] = new(reader.GetGuid(1), reader.GetString(2), reader.GetString(3));
        return result;
    }

    private static async Task<Dictionary<string, LocalCustomer>> ReadLocalCustomersByNameAsync(
        NpgsqlConnection connection,
        IEnumerable<string> names,
        CancellationToken cancellationToken)
    {
        var normalizedNames = names.Select(NormalizeName).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var result = new Dictionary<string, LocalCustomer>(StringComparer.Ordinal);
        if (normalizedNames.Length == 0) return result;

        await using var command = new NpgsqlCommand("""
            SELECT client_id, COALESCE(client_code, ''), client_name, lower(trim(client_name))
            FROM clients
            WHERE lower(trim(client_name)) = ANY(@names);
            """, connection);
        command.Parameters.AddWithValue("names", normalizedNames);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result[reader.GetString(3)] = new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2));
        return result;
    }

    private static List<SellOrganization> ParseOrganizations(JsonElement root)
    {
        var result = new List<SellOrganization>();
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("data", out var data)) continue;
            var organization = ParseOrganizationData(data);
            if (organization is not null) result.Add(organization);
        }
        return result;
    }

    private static SellOrganization? ParseSingleOrganization(JsonElement root)
    {
        var data = root.TryGetProperty("data", out var value) ? value : root;
        return ParseOrganizationData(data);
    }

    private static SellOrganization? ParseOrganizationData(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return null;
        if (!JsonBoolean(data, "is_organization")) return null;
        var id = JsonText(data, "id");
        var name = Clean(JsonText(data, "name"), 255);
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) return null;
        var address = data.TryGetProperty("address", out var addressValue) ? addressValue : default;
        return new(
            id,
            name,
            Clean(JsonText(data, "customer_status"), 80),
            Clean(JsonText(data, "prospect_status"), 80),
            Clean(JsonText(data, "industry"), 200),
            Clean(JsonText(data, "website"), 500),
            Clean(JsonText(data, "phone"), 100),
            Clean(JsonText(data, "email"), 320),
            Clean(JsonText(address, "line1"), 500),
            Clean(JsonText(address, "city"), 120),
            Clean(JsonText(address, "state"), 120),
            Clean(JsonText(address, "postal_code"), 40),
            Clean(JsonText(address, "country"), 120),
            JsonTimestamp(data, "updated_at"));
    }

    private static bool MatchesRelationship(SellOrganization organization, string relationship) => relationship switch
    {
        "all" => true,
        "customer" => organization.CustomerStatus is "current" or "past",
        "current_customer" => organization.CustomerStatus == "current",
        "past_customer" => organization.CustomerStatus == "past",
        "prospect" => organization.ProspectStatus is "current" or "lost",
        _ => false
    };

    private static bool MatchesSearch(SellOrganization organization, string search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        var needle = search.ToLowerInvariant();
        return new[]
        {
            organization.Name, organization.Industry, organization.Website,
            organization.Phone, organization.Email, organization.City,
            organization.StateRegion, organization.Country
        }.Any(value => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeRelationship(string? value)
    {
        var normalized = (value ?? "customer").Trim().ToLowerInvariant();
        return normalized is "all" or "customer" or "current_customer" or "past_customer" or "prospect"
            ? normalized
            : null;
    }

    private static async Task<SellProvider?> ReadProviderAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT provider_name, auth_model, base_url, api_key_header, api_key_prefix,
                   is_enabled, availability_status,
                   EXISTS (
                       SELECT 1 FROM crm_integration_credentials credential
                       WHERE credential.provider_key = provider.provider_key
                         AND credential.credential_kind = CASE
                             WHEN provider.auth_model = 'api_key' THEN 'api_key'
                             ELSE 'oauth_token'
                         END
                   ) AS credential_configured
            FROM crm_integration_providers provider
            WHERE provider.provider_key = @provider;
            """, connection);
        command.Parameters.AddWithValue("provider", ProviderKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetBoolean(5),
            reader.GetString(6),
            reader.GetBoolean(7));
    }

    private static IResult? ProviderReadinessFailure(SellProvider? provider)
    {
        if (provider is null)
            return Results.Json(new { module = ModuleNumber, status = "sell_provider_unavailable", message = "Configure SELL in Module 026 before synchronizing customers." }, statusCode: StatusCodes.Status409Conflict);
        if (!provider.IsEnabled)
            return Results.Json(new { module = ModuleNumber, status = "sell_provider_disabled", message = "Enable SELL in Module 026 before synchronizing customers." }, statusCode: StatusCodes.Status409Conflict);
        if (!provider.CredentialConfigured)
            return Results.Json(new { module = ModuleNumber, status = "sell_credential_missing", message = "Save or connect the SELL credential in Module 026 before synchronizing customers." }, statusCode: StatusCodes.Status409Conflict);
        if (!string.Equals(provider.AvailabilityStatus, "available", StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { module = ModuleNumber, status = "sell_provider_not_available", message = "Run a successful SELL connection test in Module 026 before synchronizing customers." }, statusCode: StatusCodes.Status409Conflict);
        if (!string.IsNullOrWhiteSpace(provider.BaseUrl)
            && (!Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var configured)
                || configured.Scheme != Uri.UriSchemeHttps
                || !configured.Host.Equals(SellBaseUri.Host, StringComparison.OrdinalIgnoreCase)))
            return Results.Json(new { module = ModuleNumber, status = "sell_base_url_invalid", message = "The built-in SELL connection must use https://api.getbase.com." }, statusCode: StatusCodes.Status409Conflict);
        return null;
    }

    private static async Task<AuthorizationOutcome> ResolveAuthorizationAsync(
        NpgsqlConnection connection,
        SellProvider provider,
        CancellationToken cancellationToken)
    {
        var encryptionKey = CrmErpIntegrationModule.ReadEncryptionKey();
        if (encryptionKey is null)
        {
            var failure = Results.Json(new { module = ModuleNumber, status = "secure_store_unavailable", message = "The Module 026 integration encryption key is unavailable." }, statusCode: StatusCodes.Status503ServiceUnavailable);
            return new(null, null, failure, "secure_store_unavailable", "The Module 026 integration encryption key is unavailable.");
        }
        try
        {
            if (provider.AuthModel == "api_key")
            {
                var key = await CrmErpIntegrationModule.LoadCredentialAsync(connection, ProviderKey, "api_key", encryptionKey, cancellationToken);
                if (string.IsNullOrWhiteSpace(key))
                {
                    var failure = Results.Json(new { module = ModuleNumber, status = "sell_credential_missing", message = "Save the SELL API key in Module 026 before synchronizing customers." }, statusCode: StatusCodes.Status409Conflict);
                    return new(null, null, failure, "sell_credential_missing", "The SELL API key is missing.");
                }
                var value = string.IsNullOrWhiteSpace(provider.ApiKeyPrefix) ? key : $"{provider.ApiKeyPrefix.Trim()} {key}";
                return new(provider.ApiKeyHeader, value, null, string.Empty, string.Empty);
            }

            var envelope = await CrmErpIntegrationModule.LoadCredentialAsync(connection, ProviderKey, "oauth_token", encryptionKey, cancellationToken);
            if (string.IsNullOrWhiteSpace(envelope))
            {
                var failure = Results.Json(new { module = ModuleNumber, status = "sell_oauth_required", message = "Connect SELL with OAuth in Module 026 before synchronizing customers." }, statusCode: StatusCodes.Status409Conflict);
                return new(null, null, failure, "sell_oauth_required", "The SELL OAuth token is missing.");
            }
            using var document = JsonDocument.Parse(envelope);
            var token = JsonText(document.RootElement, "accessToken");
            if (string.IsNullOrWhiteSpace(token))
            {
                var failure = Results.Json(new { module = ModuleNumber, status = "sell_oauth_invalid", message = "Reconnect SELL OAuth in Module 026 before synchronizing customers." }, statusCode: StatusCodes.Status409Conflict);
                return new(null, null, failure, "sell_oauth_invalid", "The SELL OAuth token is invalid.");
            }
            return new("Authorization", $"Bearer {token}", null, string.Empty, string.Empty);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
        }
    }

    private static async Task<SellResponseOutcome> SendSellAsync(
        IHttpClientFactory httpClientFactory,
        Uri uri,
        string headerName,
        string headerValue,
        CancellationToken cancellationToken)
    {
        if (!uri.Host.Equals(SellBaseUri.Host, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme != Uri.UriSchemeHttps
            || !await CrmErpIntegrationModule.IsSafeExternalUriAsync(uri, cancellationToken))
        {
            var failure = Results.Json(new { module = ModuleNumber, status = "sell_endpoint_rejected", message = "The SELL request endpoint is not an approved public HTTPS address." }, statusCode: StatusCodes.Status409Conflict);
            return new(null, failure, "sell_endpoint_rejected", "The SELL endpoint was rejected.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (headerName.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(headerValue);
        else
            request.Headers.TryAddWithoutValidation(headerName, headerValue);

        var client = httpClientFactory.CreateClient("Module026");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await CrmErpIntegrationModule.ReadBoundedResponseBodyAsync(response.Content, cancellationToken);
        if (body is null)
        {
            var failure = Results.Json(new { module = ModuleNumber, status = "sell_response_too_large", message = "SELL returned more data than the controlled synchronization limit." }, statusCode: StatusCodes.Status502BadGateway);
            return new(null, failure, "sell_response_too_large", "SELL returned too much data.");
        }
        if (!response.IsSuccessStatusCode)
        {
            var authenticationFailure = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
            var code = authenticationFailure ? "sell_authentication_failed" : "sell_request_failed";
            var message = authenticationFailure
                ? "SELL rejected the configured credential. Reconnect it in Module 026."
                : "SELL could not return customer records.";
            var failure = Results.Json(new { module = ModuleNumber, status = code, message, remoteStatusCode = (int)response.StatusCode }, statusCode: StatusCodes.Status502BadGateway);
            return new(null, failure, code, message);
        }
        return new(body, null, string.Empty, string.Empty);
    }

    private static async Task<SchemaStatus> ReadSchemaStatusAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                to_regclass('public.crm_integration_providers') IS NOT NULL
                AND to_regclass('public.crm_integration_credentials') IS NOT NULL,
                to_regclass('public.clients') IS NOT NULL
                AND to_regclass('public.customer_directory_source_links') IS NOT NULL
                AND to_regclass('public.customer_directory_sync_runs') IS NOT NULL;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetBoolean(0), reader.GetBoolean(1))
            : new(false, false);
    }

    private static async Task<Guid> StartRunAsync(
        NpgsqlConnection connection,
        Guid actor,
        int page,
        int pageSize,
        string search,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        await using var command = new NpgsqlCommand("""
            INSERT INTO customer_directory_sync_runs (
                customer_directory_sync_run_id, provider_key, source_system,
                requested_by, status, page_requested, page_size, search_text,
                started_at, evidence_json
            ) VALUES (
                @run_id, @provider, @source_system, @actor, 'started', @page,
                @page_size, @search, NOW(), jsonb_build_object(
                    'secretValuesRead', false,
                    'secretValuesReturned', false,
                    'localContactsOverwritten', false
                )
            );
            """, connection);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("provider", ProviderKey);
        command.Parameters.AddWithValue("source_system", SourceSystem);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("page", page);
        command.Parameters.AddWithValue("page_size", Math.Clamp(pageSize, 1, MaximumPageSize));
        command.Parameters.AddWithValue("search", search);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return runId;
    }

    private static async Task CompletePreviewRunAsync(
        NpgsqlConnection connection,
        Guid runId,
        int sourceRecords,
        int organizations,
        int linked,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE customer_directory_sync_runs
            SET status = 'previewed', completed_at = NOW(),
                source_records_seen = @source_records,
                organizations_seen = @organizations,
                linked_count = @linked,
                message = 'SELL customer preview completed.'
            WHERE customer_directory_sync_run_id = @run_id;
            """, connection);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("source_records", sourceRecords);
        command.Parameters.AddWithValue("organizations", organizations);
        command.Parameters.AddWithValue("linked", linked);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CompleteImportRunAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid runId,
        int organizations,
        int imported,
        int updated,
        int linked,
        int skipped,
        int failed,
        string status,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE customer_directory_sync_runs
            SET status = @status, completed_at = NOW(),
                source_records_seen = @organizations,
                organizations_seen = @organizations,
                imported_count = @imported,
                updated_count = @updated,
                linked_count = @linked,
                skipped_count = @skipped,
                failed_count = @failed,
                message = @message,
                evidence_json = evidence_json || jsonb_build_object(
                    'transactionCommitted', true,
                    'localContactsOverwritten', false
                )
            WHERE customer_directory_sync_run_id = @run_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("organizations", organizations);
        command.Parameters.AddWithValue("imported", imported);
        command.Parameters.AddWithValue("updated", updated);
        command.Parameters.AddWithValue("linked", linked);
        command.Parameters.AddWithValue("skipped", skipped);
        command.Parameters.AddWithValue("failed", failed);
        command.Parameters.AddWithValue("message", $"SELL customer sync completed with {failed} failure(s).");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task FailRunAsync(
        NpgsqlConnection connection,
        Guid runId,
        string errorCode,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = new NpgsqlCommand("""
                UPDATE customer_directory_sync_runs
                SET status = 'failed', completed_at = NOW(), error_code = @error_code, message = @message
                WHERE customer_directory_sync_run_id = @run_id;
                """, connection);
            command.Parameters.AddWithValue("run_id", runId);
            command.Parameters.AddWithValue("error_code", Clean(errorCode, 100));
            command.Parameters.AddWithValue("message", Clean(message, 2000));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // The original controlled error remains the response if audit persistence is unavailable.
        }
    }

    private static async Task<object?> ReadLastRunAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT customer_directory_sync_run_id, started_at, completed_at, status,
                   imported_count, updated_count, linked_count, skipped_count,
                   failed_count, error_code, message
            FROM customer_directory_sync_runs
            WHERE provider_key = @provider
            ORDER BY started_at DESC
            LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("provider", ProviderKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new
        {
            runId = reader.GetGuid(0),
            startedAt = reader.GetFieldValue<DateTimeOffset>(1).ToUniversalTime(),
            completedAt = reader.IsDBNull(2) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(2).ToUniversalTime(),
            status = reader.GetString(3),
            imported = reader.GetInt32(4),
            updated = reader.GetInt32(5),
            linked = reader.GetInt32(6),
            skipped = reader.GetInt32(7),
            failed = reader.GetInt32(8),
            errorCode = reader.GetString(9),
            message = reader.GetString(10)
        };
    }

    private static async Task<int> ScalarIntAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task ExecuteControlAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string SourceHash(SellOrganization organization)
    {
        var payload = string.Join('|', new[]
        {
            organization.SourceRecordId, organization.Name, organization.CustomerStatus,
            organization.ProspectStatus, organization.Industry, organization.Website,
            organization.Phone, organization.Email, organization.AddressLine1,
            organization.City, organization.StateRegion, organization.PostalCode,
            organization.Country, organization.UpdatedAt?.ToString("O") ?? string.Empty
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static async Task<IResult?> AuthorizeViewAsync(HttpContext context) =>
        await GovernedOperationsReadModule.AuthorizeAsync(
            context,
            ModuleNumber,
            ViewRoles,
            ["VIEW_CUSTOMERS", "MANAGE_CUSTOMERS", "VIEW_INTEGRATIONS_026", "MANAGE_INTEGRATIONS_026", "MANAGE_ALL"]);

    private static async Task<IResult?> AuthorizeManageAsync(HttpContext context)
    {
        if (IsViewAs(context))
            return Results.Json(new { module = ModuleNumber, status = "view_as_read_only", message = "Exit Administrator View-As before importing SELL customers." }, statusCode: StatusCodes.Status403Forbidden);
        return await GovernedOperationsReadModule.AuthorizeAsync(
            context,
            ModuleNumber,
            ManageRoles,
            ["MANAGE_CUSTOMERS", "MANAGE_INTEGRATIONS_026", "MANAGE_ALL"]);
    }

    private static async Task<NpgsqlConnection?> OpenConnectionAsync(HttpContext context)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        try
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(context.RequestAborted);
            return connection;
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "open customer synchronization storage");
            return null;
        }
    }

    private static string? BuildConnectionString()
    {
        foreach (var name in new[]
                 {
                     "ConnectionStrings__DefaultConnection", "ConnectionStrings__ProjectPulse",
                     "ConnectionStrings__ProjectTime", "PROJECTPULSE_CONNECTION_STRING",
                     "PROJECTTIME_DATABASE_CONNECTION"
                 })
        {
            var configured = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
        }
        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return null;
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

    private static async Task<BodyOutcome<T>> ReadBodyAsync<T>(HttpContext context)
    {
        if (context.Request.ContentLength is > MaximumRequestBytes)
            return new(default, Results.Json(new { module = ModuleNumber, status = "request_too_large", message = $"Request bodies are limited to {MaximumRequestBytes} bytes." }, statusCode: StatusCodes.Status413PayloadTooLarge));
        try
        {
            var value = await context.Request.ReadFromJsonAsync<T>(context.RequestAborted);
            return new(value, null);
        }
        catch (JsonException)
        {
            return new(default, Invalid("The JSON request body is invalid."));
        }
    }

    private static Guid? ActualUserId(HttpContext context)
    {
        foreach (var key in new[] { "ProjectPulseActualUserId", "ProjectPulseSessionUserId" })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid id) return id;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static bool IsViewAs(HttpContext context) =>
        context.Items.TryGetValue("ProjectPulseIsViewAs", out var value) && value is true;

    private static bool SameOrigin(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("Origin", out var values)) return true;
        if (!Uri.TryCreate(values.ToString(), UriKind.Absolute, out var origin)) return false;
        return string.Equals(origin.Host, context.Request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && origin.Port == (context.Request.Host.Port ?? (context.Request.IsHttps ? 443 : 80))
            && string.Equals(origin.Scheme, context.Request.Scheme, StringComparison.OrdinalIgnoreCase);
    }

    private static string JsonText(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value)) return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            _ => string.Empty
        };
    }

    private static bool JsonBoolean(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? JsonTimestamp(JsonElement element, string property) =>
        DateTimeOffset.TryParse(JsonText(element, property), out var parsed) ? parsed.ToUniversalTime() : null;

    private static string NormalizeName(string value) => value.Trim().ToLowerInvariant();

    private static string Clean(string? value, int maximum)
    {
        var cleaned = value?.Trim() ?? string.Empty;
        return cleaned.Length > maximum ? cleaned[..maximum] : cleaned;
    }

    private static IResult Invalid(string message) => Results.BadRequest(new { module = ModuleNumber, status = "invalid_request", message });
    private static IResult OriginRejected() => Results.Json(new { module = ModuleNumber, status = "origin_rejected", message = "The request origin is not allowed." }, statusCode: StatusCodes.Status403Forbidden);
    private static IResult DependencyUnavailable() => Results.Json(new { module = ModuleNumber, status = "customer_sync_storage_unavailable", message = "Customer synchronization storage is temporarily unavailable." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    private static IResult ProviderSchemaUnavailable() => Results.Json(new { module = ModuleNumber, status = "integration_schema_unavailable", migration = "034_module_026_crm_erp_integrations", message = "Module 026 integration storage is not installed." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    private static IResult SyncSchemaUnavailable() => Results.Json(new { module = ModuleNumber, status = "customer_sync_schema_unavailable", migration = MigrationId, message = "The Module 021 SELL customer synchronization migration has not been applied." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    private static IResult ProviderFailure(string status, string message) => Results.Json(new { module = ModuleNumber, status, message }, statusCode: StatusCodes.Status502BadGateway);

    private static void LogFailure(HttpContext context, Exception exception, string operation)
    {
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CustomerDirectorySellSyncModule")
            .LogWarning("Modules 021/026 could not {Operation} ({ExceptionType}).", operation, exception.GetType().Name);
    }

    private sealed record SellPreviewRequest(string? Search, string? Relationship, int? Page, int? PageSize);
    private sealed record SellImportRequest(string[]? SourceRecordIds);
    private sealed record BodyOutcome<T>(T? Value, IResult? Failure);
    private sealed record SchemaStatus(bool ProviderReady, bool SyncReady);
    private sealed record SellProvider(
        string ProviderName,
        string AuthModel,
        string BaseUrl,
        string ApiKeyHeader,
        string ApiKeyPrefix,
        bool IsEnabled,
        string AvailabilityStatus,
        bool CredentialConfigured);
    private sealed record AuthorizationOutcome(
        string? HeaderName,
        string? HeaderValue,
        IResult? Failure,
        string FailureCode,
        string FailureMessage);
    private sealed record SellResponseOutcome(
        string? Body,
        IResult? Failure,
        string FailureCode,
        string FailureMessage);
    private sealed record SellOrganization(
        string SourceRecordId,
        string Name,
        string CustomerStatus,
        string ProspectStatus,
        string Industry,
        string Website,
        string Phone,
        string Email,
        string AddressLine1,
        string City,
        string StateRegion,
        string PostalCode,
        string Country,
        DateTimeOffset? UpdatedAt);
    private sealed record LocalCustomer(Guid ClientId, string ClientCode, string ClientName);
    private sealed record CustomerUpsertOutcome(string Status, string ResultCode, Guid ClientId, string ClientCode);
}
