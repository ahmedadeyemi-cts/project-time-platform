using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 021 customer-source authority shared by customer-facing modules.
/// SELL remains the backward-compatible default, while authorized operators may
/// select another Module 026 CRM/ERP provider or locally managed customers.
/// </summary>
public static class CustomerSourceAuthorityModule
{
    internal const string ModuleNumber = "021";
    internal const string ProviderModuleNumber = "026";
    internal const string SellProviderKey = "zendesk_sell";
    internal const string MigrationId = "098_customer_directory_source_authority";
    private const int MaximumRequestBytes = 64 * 1024;
    private const int MaximumPageSize = 100;
    private const int MaximumImportRecords = 50;

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

    public static WebApplication MapCustomerSourceAuthorityEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/customers/source",
            (Func<HttpContext, Task<IResult>>)GetSourceAsync);
        app.MapPut(
            "/api/customers/source",
            (Func<HttpContext, Task<IResult>>)UpdateSourceAsync);
        app.MapPost(
            "/api/customers/source/preview",
            (Func<HttpContext, IHttpClientFactory, Task<IResult>>)PreviewAsync);
        app.MapPost(
            "/api/customers/source/import",
            (Func<HttpContext, IHttpClientFactory, Task<IResult>>)ImportAsync);
        app.MapGet(
            "/api/customers/source/runs",
            (Func<HttpContext, Task<IResult>>)GetRunsAsync);
        return app;
    }

    private static async Task<IResult> GetSourceAsync(HttpContext context)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;

        await using var connection = await OpenConnectionAsync(context);
        if (connection is null) return DependencyUnavailable();

        if (!await ProviderSchemaReadyAsync(connection, context.RequestAborted))
            return ProviderSchemaUnavailable();

        var source = await LoadAuthorityAsync(connection, null, context.RequestAborted);
        var providers = await ListProviderChoicesAsync(connection, context.RequestAborted);
        var canManage = await AuthorizeManageAsync(context) is null;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "customer_source_loaded",
            providerModule = ProviderModuleNumber,
            migration = MigrationId,
            migrationApplied = source.MigrationApplied,
            canManage,
            source = ToPublicSource(source),
            providers,
            message = source.IsManual
                ? "Manual customer management is authoritative. External CRM and SELL associations are not required."
                : source.IsSell
                    ? "SELL is the authoritative customer source through Module 026."
                    : $"{source.ProviderName} is the authoritative customer source through Module 026."
        });
    }

    private static async Task<IResult> UpdateSourceAsync(HttpContext context)
    {
        var authorization = await AuthorizeManageAsync(context);
        if (authorization is not null) return authorization;
        if (!SameOrigin(context)) return OriginRejected();

        var body = await ReadBodyAsync<CustomerSourceUpdateRequest>(context);
        if (body.Failure is not null) return body.Failure;

        var mode = NormalizeMode(body.Value?.Mode);
        if (mode is null)
            return Invalid("Customer source mode must be sell, crm, or manual.");

        await using var connection = await OpenConnectionAsync(context);
        if (connection is null) return DependencyUnavailable();
        if (!await AuthoritySchemaReadyAsync(connection, context.RequestAborted))
            return AuthoritySchemaUnavailable();

        string? providerKey = null;
        ProviderRecord? selectedProvider = null;
        if (mode == "sell")
        {
            providerKey = SellProviderKey;
            selectedProvider = await LoadProviderRecordAsync(connection, providerKey, null, context.RequestAborted);
            if (selectedProvider is null)
                return Invalid("The built-in SELL provider is not registered in Module 026.");
        }
        else if (mode == "crm")
        {
            providerKey = Clean(body.Value?.ProviderKey, 200).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(providerKey))
                return Invalid("Select a Module 026 CRM/ERP provider before enabling CRM customer sourcing.");
            if (providerKey == SellProviderKey)
                return Invalid("Choose SELL mode when Zendesk Sell is the selected provider.");

            selectedProvider = await LoadProviderRecordAsync(connection, providerKey, null, context.RequestAborted);
            if (selectedProvider is null)
                return Invalid("The selected Module 026 provider was not found.");
            if (!IsEligibleCustomerProvider(selectedProvider))
                return Invalid("The selected Module 026 provider is not a CRM/ERP customer source.");
        }

        var actor = ActualUserId(context);
        if (actor is null) return Results.Unauthorized();

        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        var previous = await LoadAuthorityAsync(connection, transaction, context.RequestAborted);

        await using (var command = new NpgsqlCommand("""
            INSERT INTO customer_directory_source_authority (
                customer_source_authority_id, source_mode, provider_key, updated_by, updated_at
            ) VALUES (1, @mode, @provider_key, @actor, NOW())
            ON CONFLICT (customer_source_authority_id) DO UPDATE
            SET source_mode = EXCLUDED.source_mode,
                provider_key = EXCLUDED.provider_key,
                updated_by = EXCLUDED.updated_by,
                updated_at = NOW();
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("mode", mode);
            command.Parameters.AddWithValue("provider_key", providerKey is null ? DBNull.Value : providerKey);
            command.Parameters.AddWithValue("actor", actor.Value);
            await command.ExecuteNonQueryAsync(context.RequestAborted);
        }

        await using (var history = new NpgsqlCommand("""
            INSERT INTO customer_directory_source_authority_history (
                previous_source_mode, previous_provider_key,
                next_source_mode, next_provider_key, changed_by, changed_at
            ) VALUES (
                @previous_mode, @previous_provider,
                @next_mode, @next_provider, @actor, NOW()
            );
            """, connection, transaction))
        {
            history.Parameters.AddWithValue("previous_mode", previous.Mode);
            history.Parameters.AddWithValue("previous_provider", previous.ProviderKey is null ? DBNull.Value : previous.ProviderKey);
            history.Parameters.AddWithValue("next_mode", mode);
            history.Parameters.AddWithValue("next_provider", providerKey is null ? DBNull.Value : providerKey);
            history.Parameters.AddWithValue("actor", actor.Value);
            await history.ExecuteNonQueryAsync(context.RequestAborted);
        }

        await transaction.CommitAsync(context.RequestAborted);
        var updated = await LoadAuthorityAsync(connection, null, context.RequestAborted);

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "customer_source_updated",
            providerModule = ProviderModuleNumber,
            source = ToPublicSource(updated),
            previousSource = ToPublicSource(previous),
            stateChanged = previous.Mode != updated.Mode
                           || !string.Equals(previous.ProviderKey, updated.ProviderKey, StringComparison.OrdinalIgnoreCase),
            message = updated.IsManual
                ? "Manual customer management is now authoritative. SELL association is not required by downstream customer workflows."
                : updated.IsSell
                    ? "SELL is now the authoritative customer source."
                    : $"{updated.ProviderName} is now the authoritative Module 026 customer source."
        });
    }

    private static async Task<IResult> PreviewAsync(HttpContext context, IHttpClientFactory httpClientFactory)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;
        if (!SameOrigin(context)) return OriginRejected();

        var body = await ReadBodyAsync<CustomerSourcePreviewRequest>(context);
        if (body.Failure is not null) return body.Failure;
        var request = body.Value ?? new CustomerSourcePreviewRequest(null, null, null);
        var page = Math.Clamp(request.Page ?? 1, 1, 100000);
        var pageSize = Math.Clamp(request.PageSize ?? MaximumPageSize, 1, MaximumPageSize);
        var search = Clean(request.Search, 200);

        await using var connection = await OpenConnectionAsync(context);
        if (connection is null) return DependencyUnavailable();
        if (!await AuthoritySchemaReadyAsync(connection, context.RequestAborted))
            return AuthoritySchemaUnavailable();

        var source = await LoadAuthorityAsync(connection, null, context.RequestAborted);
        if (source.IsManual)
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "manual_customer_source_active",
                message = "Manual customer management is active. Add customers directly in Module 021 instead of previewing an external CRM."
            }, statusCode: StatusCodes.Status409Conflict);
        if (source.IsSell)
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "sell_native_sync_active",
                useNativeSellEndpoints = true,
                message = "SELL uses the existing governed Module 021 synchronization controls."
            }, statusCode: StatusCodes.Status409Conflict);
        if (!source.ProviderReady)
            return SourceNotReady(source);

        var provider = await LoadProviderRecordAsync(connection, source.ProviderKey!, null, context.RequestAborted);
        if (provider is null) return SourceNotReady(source);
        var mapping = ParseCustomerImportMapping(provider);
        if (!mapping.PreviewConfigured)
            return MappingMissing(source, "Configure customerListUrl, itemsPath, idPath, and namePath in the selected Module 026 provider import mapping.");

        var actor = ActualUserId(context);
        if (actor is null) return Results.Unauthorized();
        var runId = await StartRunAsync(
            connection,
            provider.ProviderKey,
            source.SourceSystem,
            actor.Value,
            page,
            pageSize,
            search,
            context.RequestAborted);

        try
        {
            var authentication = await ResolveAuthorizationAsync(connection, provider, context.RequestAborted);
            if (authentication.Failure is not null)
            {
                await FailRunAsync(connection, runId, authentication.FailureCode, authentication.FailureMessage, context.RequestAborted);
                return authentication.Failure;
            }

            var uri = BuildProviderUri(provider, mapping.CustomerListUrl, page, pageSize, search, null);
            if (uri is null)
            {
                await FailRunAsync(connection, runId, "customer_source_endpoint_invalid", "The customer list endpoint is invalid.", context.RequestAborted);
                return InvalidProviderEndpoint(source);
            }

            var remote = await SendProviderAsync(httpClientFactory, provider, uri, authentication, context.RequestAborted);
            if (remote.Failure is not null)
            {
                await FailRunAsync(connection, runId, remote.FailureCode, remote.FailureMessage, context.RequestAborted);
                return remote.Failure;
            }

            using var document = JsonDocument.Parse(remote.Body!);
            var customers = ParseCustomerList(document.RootElement, mapping)
                .Where(item => MatchesSearch(item, search))
                .Take(MaximumPageSize)
                .ToList();

            var links = await ReadLinksAsync(connection, source.SourceSystem, customers.Select(item => item.SourceRecordId), context.RequestAborted);
            var localNames = await ReadLocalCustomersByNameAsync(connection, customers.Select(item => item.Name), context.RequestAborted);
            var rows = customers.Select(item =>
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

            await CompletePreviewRunAsync(connection, runId, rows.Length, rows.Count(item => item.linked), source.ProviderName, context.RequestAborted);

            return Results.Ok(new
            {
                module = ModuleNumber,
                status = "crm_customer_preview_loaded",
                providerModule = ProviderModuleNumber,
                providerKey = source.ProviderKey,
                providerName = source.ProviderName,
                sourceSystem = source.SourceSystem,
                page,
                pageSize,
                search,
                customers = rows,
                sourceRecordsSeen = rows.Length,
                linkedCount = rows.Count(item => item.linked),
                newCount = rows.Count(item => item.importAction == "create"),
                existingMatchCount = rows.Count(item => item.importAction == "link_existing"),
                runId,
                localContactEnrichmentPreserved = true,
                secretValuesReturned = false,
                message = $"{source.ProviderName} customers were previewed through the selected Module 026 connection."
            });
        }
        catch (JsonException)
        {
            await FailRunAsync(connection, runId, "customer_source_response_invalid", "The selected CRM returned an invalid customer response.", context.RequestAborted);
            return ProviderFailure("customer_source_response_invalid", "The selected CRM returned data that ProjectPulse could not read.");
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            await FailRunAsync(connection, runId, "customer_source_timeout", "The selected CRM did not respond before the timeout.", context.RequestAborted);
            return Results.Json(new { module = ModuleNumber, status = "customer_source_timeout", message = "The selected CRM did not respond before the timeout." }, statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException)
        {
            await FailRunAsync(connection, runId, "customer_source_connection_failed", "ProjectPulse could not reach the selected CRM.", context.RequestAborted);
            return ProviderFailure("customer_source_connection_failed", "ProjectPulse could not reach the selected CRM.");
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "preview CRM customers");
            await FailRunAsync(connection, runId, "customer_source_preview_failed", "CRM customer preview could not be completed.", context.RequestAborted);
            return ProviderFailure("customer_source_preview_failed", "CRM customer preview could not be completed.");
        }
    }

    private static async Task<IResult> ImportAsync(HttpContext context, IHttpClientFactory httpClientFactory)
    {
        var authorization = await AuthorizeManageAsync(context);
        if (authorization is not null) return authorization;
        if (!SameOrigin(context)) return OriginRejected();

        var body = await ReadBodyAsync<CustomerSourceImportRequest>(context);
        if (body.Failure is not null) return body.Failure;
        var selectedIds = (body.Value?.SourceRecordIds ?? Array.Empty<string>())
            .Select(value => Clean(value, 200))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (selectedIds.Length == 0) return Invalid("Select at least one CRM customer to import.");
        if (selectedIds.Length > MaximumImportRecords) return Invalid($"A maximum of {MaximumImportRecords} customers can be imported at once.");

        await using var connection = await OpenConnectionAsync(context);
        if (connection is null) return DependencyUnavailable();
        if (!await AuthoritySchemaReadyAsync(connection, context.RequestAborted))
            return AuthoritySchemaUnavailable();

        var source = await LoadAuthorityAsync(connection, null, context.RequestAborted);
        if (source.IsManual)
            return Results.Json(new { module = ModuleNumber, status = "manual_customer_source_active", message = "Manual customer management is active. Add customers directly in Module 021." }, statusCode: StatusCodes.Status409Conflict);
        if (source.IsSell)
            return Results.Json(new { module = ModuleNumber, status = "sell_native_sync_active", useNativeSellEndpoints = true, message = "Use the existing governed SELL import controls for the SELL source." }, statusCode: StatusCodes.Status409Conflict);
        if (!source.ProviderReady) return SourceNotReady(source);

        var provider = await LoadProviderRecordAsync(connection, source.ProviderKey!, null, context.RequestAborted);
        if (provider is null) return SourceNotReady(source);
        var mapping = ParseCustomerImportMapping(provider);
        if (!mapping.ImportConfigured)
            return MappingMissing(source, "Configure customerRecordUrlTemplate (or recordLookupUrlTemplate), idPath, and namePath in Module 026 before importing customers.");

        var actor = ActualUserId(context);
        if (actor is null) return Results.Unauthorized();
        var runId = await StartRunAsync(connection, provider.ProviderKey, source.SourceSystem, actor.Value, 1, selectedIds.Length, string.Empty, context.RequestAborted);

        var authentication = await ResolveAuthorizationAsync(connection, provider, context.RequestAborted);
        if (authentication.Failure is not null)
        {
            await FailRunAsync(connection, runId, authentication.FailureCode, authentication.FailureMessage, context.RequestAborted);
            return authentication.Failure;
        }

        var imported = 0;
        var updated = 0;
        var linked = 0;
        var skipped = 0;
        var failed = 0;
        var outcomes = new List<object>();

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            for (var index = 0; index < selectedIds.Length; index++)
            {
                var sourceRecordId = selectedIds[index];
                var savepoint = $"crm_customer_{index}";
                await ExecuteControlAsync(connection, transaction, $"SAVEPOINT {savepoint};", context.RequestAborted);
                try
                {
                    var uri = BuildProviderUri(provider, mapping.CustomerRecordUrlTemplate, 1, 1, string.Empty, sourceRecordId);
                    if (uri is null)
                    {
                        failed++;
                        outcomes.Add(new { sourceRecordId, status = "failed", resultCode = "customer_source_endpoint_invalid" });
                        await ExecuteControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", context.RequestAborted);
                        continue;
                    }

                    var remote = await SendProviderAsync(httpClientFactory, provider, uri, authentication, context.RequestAborted);
                    if (remote.Failure is not null)
                    {
                        failed++;
                        outcomes.Add(new { sourceRecordId, status = "failed", resultCode = remote.FailureCode });
                        await ExecuteControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", context.RequestAborted);
                        continue;
                    }

                    using var document = JsonDocument.Parse(remote.Body!);
                    var recordRoot = ResolvePath(document.RootElement, mapping.RecordPath) ?? document.RootElement;
                    var customer = MapCustomer(recordRoot, mapping);
                    if (customer is null)
                    {
                        skipped++;
                        outcomes.Add(new { sourceRecordId, status = "skipped", resultCode = "customer_record_not_mapped" });
                        await ExecuteControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", context.RequestAborted);
                        continue;
                    }

                    if (!string.Equals(customer.SourceRecordId, sourceRecordId, StringComparison.Ordinal))
                        customer = customer with { SourceRecordId = sourceRecordId };

                    var outcome = await UpsertCustomerAsync(connection, transaction, source.SourceSystem, customer, actor.Value, context.RequestAborted);
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
                        customer.Name,
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
                source.ProviderName,
                context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);

            return Results.Ok(new
            {
                module = ModuleNumber,
                status = failed == 0 ? "crm_customers_imported" : "crm_customers_imported_with_failures",
                providerModule = ProviderModuleNumber,
                providerKey = source.ProviderKey,
                providerName = source.ProviderName,
                sourceSystem = source.SourceSystem,
                runId,
                imported,
                updated,
                linked,
                skipped,
                failed,
                transactionCommitted = true,
                localContactEnrichmentPreserved = true,
                results = outcomes,
                message = $"{source.ProviderName} customer synchronization completed: {imported} created, {updated} refreshed, {linked} linked, {skipped} skipped, and {failed} failed."
            });
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            await FailRunAsync(connection, runId, "customer_source_timeout", "The selected CRM did not respond before the timeout.", context.RequestAborted);
            return Results.Json(new { module = ModuleNumber, status = "customer_source_timeout", message = "The selected CRM did not respond before the timeout." }, statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException)
        {
            await FailRunAsync(connection, runId, "customer_source_connection_failed", "ProjectPulse could not reach the selected CRM.", context.RequestAborted);
            return ProviderFailure("customer_source_connection_failed", "ProjectPulse could not reach the selected CRM.");
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "import CRM customers");
            await FailRunAsync(connection, runId, "customer_source_import_failed", "CRM customer synchronization could not be completed.", context.RequestAborted);
            return ProviderFailure("customer_source_import_failed", "CRM customer synchronization could not be completed.");
        }
    }

    private static async Task<IResult> GetRunsAsync(HttpContext context)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;

        await using var connection = await OpenConnectionAsync(context);
        if (connection is null) return DependencyUnavailable();
        var source = await LoadAuthorityAsync(connection, null, context.RequestAborted);
        if (source.IsManual || string.IsNullOrWhiteSpace(source.ProviderKey))
            return Results.Ok(new { module = ModuleNumber, status = "customer_source_runs_loaded", source = ToPublicSource(source), runs = Array.Empty<object>() });

        await using var command = new NpgsqlCommand("""
            SELECT customer_directory_sync_run_id, started_at, completed_at, status,
                   imported_count, updated_count, linked_count, skipped_count,
                   failed_count, error_code, message, source_system
            FROM customer_directory_sync_runs
            WHERE provider_key = @provider
            ORDER BY started_at DESC
            LIMIT 25;
            """, connection);
        command.Parameters.AddWithValue("provider", source.ProviderKey);
        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
        var runs = new List<object>();
        while (await reader.ReadAsync(context.RequestAborted))
        {
            runs.Add(new
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
                message = reader.GetString(10),
                sourceSystem = reader.GetString(11)
            });
        }

        return Results.Ok(new { module = ModuleNumber, status = "customer_source_runs_loaded", source = ToPublicSource(source), runs });
    }

    internal static async Task<CustomerSourceAuthorityState> LoadAuthorityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var migrationApplied = await TableExistsAsync(connection, "customer_directory_source_authority", transaction, cancellationToken);
        var mode = "sell";
        string? providerKey = SellProviderKey;
        DateTimeOffset? updatedAt = null;

        if (migrationApplied)
        {
            await using var authority = new NpgsqlCommand("""
                SELECT source_mode, provider_key, updated_at
                FROM customer_directory_source_authority
                WHERE customer_source_authority_id = 1;
                """, connection, transaction);
            await using var reader = await authority.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                mode = reader.GetString(0).Trim().ToLowerInvariant();
                providerKey = reader.IsDBNull(1) ? null : reader.GetString(1);
                updatedAt = reader.GetFieldValue<DateTimeOffset>(2).ToUniversalTime();
            }
        }

        if (mode == "manual")
        {
            return new CustomerSourceAuthorityState(
                "manual", null, "Manual customer directory", "manual",
                true, "manual", true, string.Empty, string.Empty, string.Empty,
                "{}", updatedAt, migrationApplied, null);
        }

        providerKey ??= SellProviderKey;
        var provider = await LoadProviderRecordAsync(connection, providerKey, transaction, cancellationToken);
        var lastSync = await ReadLastSuccessfulCustomerSyncAsync(connection, providerKey, transaction, cancellationToken);
        if (provider is null)
        {
            return new CustomerSourceAuthorityState(
                mode, providerKey, providerKey, string.Empty,
                false, "not_configured", false, string.Empty, string.Empty,
                string.Empty, "{}", updatedAt, migrationApplied, lastSync);
        }

        return new CustomerSourceAuthorityState(
            mode,
            provider.ProviderKey,
            provider.ProviderName,
            provider.ProviderType,
            provider.IsEnabled,
            provider.AvailabilityStatus,
            provider.CredentialConfigured,
            provider.AuthModel,
            provider.BaseUrl,
            provider.RecordLookupUrlTemplate,
            provider.ImportMappingJson,
            updatedAt,
            migrationApplied,
            lastSync);
    }

    private static object ToPublicSource(CustomerSourceAuthorityState source) => new
    {
        mode = source.Mode,
        providerKey = source.ProviderKey,
        providerName = source.ProviderName,
        providerType = source.ProviderType,
        providerEnabled = source.ProviderEnabled,
        availabilityStatus = source.AvailabilityStatus,
        credentialConfigured = source.CredentialConfigured,
        providerReady = source.ProviderReady,
        sourceSystem = source.SourceSystem,
        requiresSellAssociation = source.RequiresSellAssociation,
        manualCustomerEntryEnabled = source.IsManual,
        customerPreviewConfigured = source.IsSell || ParseCustomerImportMapping(source).PreviewConfigured,
        customerImportConfigured = source.IsSell || ParseCustomerImportMapping(source).ImportConfigured,
        lastSuccessfulCustomerSyncAt = source.LastSuccessfulCustomerSyncAt,
        updatedAt = source.UpdatedAt
    };

    private static async Task<IReadOnlyList<object>> ListProviderChoicesAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT provider_key, provider_name, provider_type, auth_model,
                   base_url, api_key_header, api_key_prefix,
                   COALESCE(record_lookup_url_template, ''),
                   COALESCE(import_mapping_json::text, '{}'),
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
            ORDER BY CASE provider_key
                WHEN 'zendesk_sell' THEN 0
                WHEN 'salesforce' THEN 1
                WHEN 'certinia' THEN 2
                WHEN 'servicenow' THEN 3
                ELSE 4
            END, provider_name;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<object>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var provider = ReadProviderRecord(reader);
            var mapping = ParseCustomerImportMapping(provider);
            rows.Add(new
            {
                providerKey = provider.ProviderKey,
                providerName = provider.ProviderName,
                providerType = provider.ProviderType,
                providerEnabled = provider.IsEnabled,
                availabilityStatus = provider.AvailabilityStatus,
                credentialConfigured = provider.CredentialConfigured,
                providerReady = provider.IsEnabled
                                && provider.CredentialConfigured
                                && provider.AvailabilityStatus.Equals("available", StringComparison.OrdinalIgnoreCase),
                eligibleCustomerSource = IsEligibleCustomerProvider(provider),
                customerPreviewConfigured = provider.ProviderKey == SellProviderKey || mapping.PreviewConfigured,
                customerImportConfigured = provider.ProviderKey == SellProviderKey || mapping.ImportConfigured
            });
        }
        return rows;
    }

    private static bool IsEligibleCustomerProvider(ProviderRecord provider)
    {
        if (provider.ProviderKey == SellProviderKey) return true;
        var type = provider.ProviderType.ToLowerInvariant();
        return type.Contains("crm", StringComparison.Ordinal)
               || type.Contains("erp", StringComparison.Ordinal)
               || type.Contains("psa", StringComparison.Ordinal)
               || type.Contains("itsm", StringComparison.Ordinal);
    }

    private static CustomerImportMapping ParseCustomerImportMapping(CustomerSourceAuthorityState source)
    {
        var provider = new ProviderRecord(
            source.ProviderKey ?? string.Empty,
            source.ProviderName,
            source.ProviderType,
            source.AuthModel,
            source.BaseUrl,
            "Authorization",
            "Bearer",
            source.RecordLookupUrlTemplate,
            source.ImportMappingJson,
            source.ProviderEnabled,
            source.AvailabilityStatus,
            source.CredentialConfigured);
        return ParseCustomerImportMapping(provider);
    }

    private static CustomerImportMapping ParseCustomerImportMapping(ProviderRecord provider)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(provider.ImportMappingJson) ? "{}" : provider.ImportMappingJson);
            var root = document.RootElement;
            var listUrl = MappingText(root, "customerListUrl", "customer_list_url");
            var recordUrl = MappingText(root, "customerRecordUrlTemplate", "customer_record_url_template");
            if (string.IsNullOrWhiteSpace(recordUrl)) recordUrl = provider.RecordLookupUrlTemplate;
            return new CustomerImportMapping(
                listUrl,
                recordUrl,
                MappingText(root, "itemsPath", "items_path"),
                MappingText(root, "recordPath", "record_path"),
                MappingText(root, "idPath", "id_path"),
                MappingText(root, "namePath", "name_path"),
                MappingText(root, "customerStatusPath", "customer_status_path"),
                MappingText(root, "prospectStatusPath", "prospect_status_path"),
                MappingText(root, "industryPath", "industry_path"),
                MappingText(root, "websitePath", "website_path"),
                MappingText(root, "phonePath", "phone_path"),
                MappingText(root, "emailPath", "email_path"),
                MappingText(root, "addressLine1Path", "address_line1_path"),
                MappingText(root, "cityPath", "city_path"),
                MappingText(root, "statePath", "state_path"),
                MappingText(root, "postalCodePath", "postal_code_path"),
                MappingText(root, "countryPath", "country_path"),
                MappingText(root, "updatedAtPath", "updated_at_path"),
                MappingText(root, "activePath", "active_path"));
        }
        catch (JsonException)
        {
            return CustomerImportMapping.Empty;
        }
    }

    private static string MappingText(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.String) return value.GetString()?.Trim() ?? string.Empty;
        }
        return string.Empty;
    }

    private static async Task<ProviderRecord?> LoadProviderRecordAsync(
        NpgsqlConnection connection,
        string providerKey,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT provider_key, provider_name, provider_type, auth_model,
                   base_url, api_key_header, api_key_prefix,
                   COALESCE(record_lookup_url_template, ''),
                   COALESCE(import_mapping_json::text, '{}'),
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
            """, connection, transaction);
        command.Parameters.AddWithValue("provider", providerKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProviderRecord(reader) : null;
    }

    private static ProviderRecord ReadProviderRecord(NpgsqlDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
        reader.GetString(8), reader.GetBoolean(9), reader.GetString(10), reader.GetBoolean(11));

    private static async Task<DateTimeOffset?> ReadLastSuccessfulCustomerSyncAsync(
        NpgsqlConnection connection,
        string providerKey,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, "customer_directory_sync_runs", transaction, cancellationToken)) return null;
        await using var command = new NpgsqlCommand("""
            SELECT completed_at
            FROM customer_directory_sync_runs
            WHERE provider_key = @provider
              AND status IN ('completed', 'completed_with_failures')
              AND completed_at IS NOT NULL
            ORDER BY completed_at DESC
            LIMIT 1;
            """, connection, transaction);
        command.Parameters.AddWithValue("provider", providerKey);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is DateTimeOffset timestamp ? timestamp.ToUniversalTime() : null;
    }

    private static async Task<AuthorizationOutcome> ResolveAuthorizationAsync(
        NpgsqlConnection connection,
        ProviderRecord provider,
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
                var key = await CrmErpIntegrationModule.LoadCredentialAsync(connection, provider.ProviderKey, "api_key", encryptionKey, cancellationToken);
                if (string.IsNullOrWhiteSpace(key))
                {
                    var failure = Results.Json(new { module = ModuleNumber, status = "customer_source_credential_missing", message = $"Save the {provider.ProviderName} API key in Module 026 before synchronizing customers." }, statusCode: StatusCodes.Status409Conflict);
                    return new(null, null, failure, "customer_source_credential_missing", "The selected CRM API key is missing.");
                }
                var value = string.IsNullOrWhiteSpace(provider.ApiKeyPrefix) ? key : $"{provider.ApiKeyPrefix.Trim()} {key}";
                return new(provider.ApiKeyHeader, value, null, string.Empty, string.Empty);
            }

            var envelope = await CrmErpIntegrationModule.LoadCredentialAsync(connection, provider.ProviderKey, "oauth_token", encryptionKey, cancellationToken);
            if (string.IsNullOrWhiteSpace(envelope))
            {
                var failure = Results.Json(new { module = ModuleNumber, status = "customer_source_oauth_required", message = $"Connect {provider.ProviderName} with OAuth in Module 026 before synchronizing customers." }, statusCode: StatusCodes.Status409Conflict);
                return new(null, null, failure, "customer_source_oauth_required", "The selected CRM OAuth token is missing.");
            }
            using var document = JsonDocument.Parse(envelope);
            var token = JsonText(document.RootElement, "accessToken");
            if (string.IsNullOrWhiteSpace(token)) token = JsonText(document.RootElement, "access_token");
            if (string.IsNullOrWhiteSpace(token))
            {
                var failure = Results.Json(new { module = ModuleNumber, status = "customer_source_oauth_invalid", message = $"Reconnect {provider.ProviderName} OAuth in Module 026 before synchronizing customers." }, statusCode: StatusCodes.Status409Conflict);
                return new(null, null, failure, "customer_source_oauth_invalid", "The selected CRM OAuth token is invalid.");
            }
            return new("Authorization", $"Bearer {token}", null, string.Empty, string.Empty);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
        }
    }

    private static Uri? BuildProviderUri(
        ProviderRecord provider,
        string template,
        int page,
        int pageSize,
        string search,
        string? sourceRecordId)
    {
        if (string.IsNullOrWhiteSpace(template)) return null;
        var value = template
            .Replace("{page}", page.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{pageSize}", pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{search}", Uri.EscapeDataString(search), StringComparison.Ordinal)
            .Replace("{id}", Uri.EscapeDataString(sourceRecordId ?? string.Empty), StringComparison.Ordinal);

        Uri? uri = null;
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
        {
            uri = absolute;
        }
        else if (Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            uri = new Uri(baseUri, value);
        }

        if (uri is null || uri.Scheme != Uri.UriSchemeHttps) return null;
        if (Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var configured)
            && !configured.Host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase)) return null;
        return uri;
    }

    private static async Task<RemoteResponseOutcome> SendProviderAsync(
        IHttpClientFactory httpClientFactory,
        ProviderRecord provider,
        Uri uri,
        AuthorizationOutcome authentication,
        CancellationToken cancellationToken)
    {
        if (!await CrmErpIntegrationModule.IsSafeExternalUriAsync(uri, cancellationToken))
        {
            var failure = Results.Json(new { module = ModuleNumber, status = "customer_source_endpoint_rejected", message = "The selected CRM endpoint is not an approved public HTTPS address." }, statusCode: StatusCodes.Status409Conflict);
            return new(null, failure, "customer_source_endpoint_rejected", "The selected CRM endpoint was rejected.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (authentication.HeaderName!.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(authentication.HeaderValue!);
        else
            request.Headers.TryAddWithoutValidation(authentication.HeaderName!, authentication.HeaderValue!);

        var client = httpClientFactory.CreateClient("Module026");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await CrmErpIntegrationModule.ReadBoundedResponseBodyAsync(response.Content, cancellationToken);
        if (body is null)
        {
            var failure = Results.Json(new { module = ModuleNumber, status = "customer_source_response_too_large", message = "The selected CRM returned more data than the controlled synchronization limit." }, statusCode: StatusCodes.Status502BadGateway);
            return new(null, failure, "customer_source_response_too_large", "The selected CRM returned too much data.");
        }
        if (!response.IsSuccessStatusCode)
        {
            var authenticationFailure = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
            var code = authenticationFailure ? "customer_source_authentication_failed" : "customer_source_request_failed";
            var message = authenticationFailure
                ? $"{provider.ProviderName} rejected the configured credential. Reconnect it in Module 026."
                : $"{provider.ProviderName} could not return customer records.";
            var failure = Results.Json(new { module = ModuleNumber, status = code, message, remoteStatusCode = (int)response.StatusCode }, statusCode: StatusCodes.Status502BadGateway);
            return new(null, failure, code, message);
        }
        return new(body, null, string.Empty, string.Empty);
    }

    private static List<GenericCustomer> ParseCustomerList(JsonElement root, CustomerImportMapping mapping)
    {
        var items = ResolvePath(root, mapping.ItemsPath);
        if (items is null || items.Value.ValueKind != JsonValueKind.Array)
            throw new JsonException("The configured customer items path did not resolve to an array.");

        var result = new List<GenericCustomer>();
        foreach (var item in items.Value.EnumerateArray())
        {
            var customer = MapCustomer(item, mapping);
            if (customer is not null) result.Add(customer);
        }
        return result;
    }

    private static GenericCustomer? MapCustomer(JsonElement root, CustomerImportMapping mapping)
    {
        var id = Clean(JsonTextAt(root, mapping.IdPath), 200);
        var name = Clean(JsonTextAt(root, mapping.NamePath), 255);
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) return null;
        return new GenericCustomer(
            id,
            name,
            Clean(JsonTextAt(root, mapping.CustomerStatusPath), 80),
            Clean(JsonTextAt(root, mapping.ProspectStatusPath), 80),
            Clean(JsonTextAt(root, mapping.IndustryPath), 200),
            Clean(JsonTextAt(root, mapping.WebsitePath), 500),
            Clean(JsonTextAt(root, mapping.PhonePath), 100),
            Clean(JsonTextAt(root, mapping.EmailPath), 320),
            Clean(JsonTextAt(root, mapping.AddressLine1Path), 500),
            Clean(JsonTextAt(root, mapping.CityPath), 120),
            Clean(JsonTextAt(root, mapping.StatePath), 120),
            Clean(JsonTextAt(root, mapping.PostalCodePath), 40),
            Clean(JsonTextAt(root, mapping.CountryPath), 120),
            JsonTimestampAt(root, mapping.UpdatedAtPath),
            JsonActiveAt(root, mapping.ActivePath));
    }

    private static JsonElement? ResolvePath(JsonElement root, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "$") return root;
        var current = root;
        foreach (var rawSegment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var segment = rawSegment == "$" ? string.Empty : rawSegment;
            if (string.IsNullOrWhiteSpace(segment)) continue;
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next)) return null;
            current = next;
        }
        return current;
    }

    private static string JsonTextAt(JsonElement root, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var value = ResolvePath(root, path);
        if (value is null) return string.Empty;
        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => value.Value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static DateTimeOffset? JsonTimestampAt(JsonElement root, string path) =>
        DateTimeOffset.TryParse(JsonTextAt(root, path), out var parsed) ? parsed.ToUniversalTime() : null;

    private static bool JsonActiveAt(JsonElement root, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        var value = ResolvePath(root, path);
        if (value is null) return true;
        if (value.Value.ValueKind == JsonValueKind.True) return true;
        if (value.Value.ValueKind == JsonValueKind.False) return false;
        var text = JsonTextAt(root, path);
        return !new[] { "false", "inactive", "disabled", "0", "closed" }.Contains(text, StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchesSearch(GenericCustomer customer, string search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        return new[]
        {
            customer.Name, customer.Industry, customer.Website, customer.Phone,
            customer.Email, customer.City, customer.StateRegion, customer.Country
        }.Any(value => value.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<CustomerUpsertOutcome> UpsertCustomerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sourceSystem,
        GenericCustomer customer,
        Guid actor,
        CancellationToken cancellationToken)
    {
        var linkedClientId = await ReadLinkedClientIdAsync(connection, transaction, sourceSystem, customer.SourceRecordId, cancellationToken);
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
                update.Parameters.AddWithValue("name", customer.Name);
                update.Parameters.AddWithValue("active", customer.IsActive);
                update.Parameters.AddWithValue("client_id", linkedClientId.Value);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
            await UpsertSourceLinkAsync(connection, transaction, sourceSystem, customer, linkedClientId.Value, actor, cancellationToken);
            var code = await ReadClientCodeAsync(connection, transaction, linkedClientId.Value, cancellationToken);
            return new("updated", "source_link_refreshed", linkedClientId.Value, code);
        }

        var nameMatch = await ReadCustomerByNameAsync(connection, transaction, customer.Name, cancellationToken);
        if (nameMatch is not null)
        {
            await UpsertSourceLinkAsync(connection, transaction, sourceSystem, customer, nameMatch.ClientId, actor, cancellationToken);
            return new("linked", "existing_customer_linked", nameMatch.ClientId, nameMatch.ClientCode);
        }

        var clientId = Guid.NewGuid();
        var clientCode = await AllocateClientCodeAsync(connection, transaction, customer.Name, customer.SourceRecordId, cancellationToken);
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO clients (client_id, client_name, client_code, is_active, created_at, updated_at)
            VALUES (@client_id, @name, @code, @active, NOW(), NOW());
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("client_id", clientId);
            insert.Parameters.AddWithValue("name", customer.Name);
            insert.Parameters.AddWithValue("code", clientCode);
            insert.Parameters.AddWithValue("active", customer.IsActive);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await UpsertSourceLinkAsync(connection, transaction, sourceSystem, customer, clientId, actor, cancellationToken);
        return new("imported", "customer_created", clientId, clientCode);
    }

    private static async Task UpsertSourceLinkAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sourceSystem,
        GenericCustomer customer,
        Guid clientId,
        Guid actor,
        CancellationToken cancellationToken)
    {
        await using (var removeObsolete = new NpgsqlCommand("""
            DELETE FROM customer_directory_source_links
            WHERE source_system = @source_system
              AND client_id = @client_id
              AND source_record_id <> @source_record_id;
            """, connection, transaction))
        {
            removeObsolete.Parameters.AddWithValue("source_system", sourceSystem);
            removeObsolete.Parameters.AddWithValue("client_id", clientId);
            removeObsolete.Parameters.AddWithValue("source_record_id", customer.SourceRecordId);
            await removeObsolete.ExecuteNonQueryAsync(cancellationToken);
        }

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
        command.Parameters.AddWithValue("source_system", sourceSystem);
        command.Parameters.AddWithValue("source_record_id", customer.SourceRecordId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("source_name", customer.Name);
        command.Parameters.AddWithValue("customer_status", customer.CustomerStatus);
        command.Parameters.AddWithValue("prospect_status", customer.ProspectStatus);
        command.Parameters.AddWithValue("source_updated_at", customer.UpdatedAt.HasValue ? (object)customer.UpdatedAt.Value : DBNull.Value);
        command.Parameters.AddWithValue("payload_hash", SourceHash(customer));
        command.Parameters.AddWithValue("actor", actor);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Guid?> ReadLinkedClientIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sourceSystem,
        string sourceRecordId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT client_id
            FROM customer_directory_source_links
            WHERE source_system = @source_system AND source_record_id = @source_record_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("source_system", sourceSystem);
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
            ? new LocalCustomer(reader.GetGuid(0), reader.GetString(1), name)
            : null;
    }

    private static async Task<string> ReadClientCodeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid clientId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT COALESCE(client_code, '') FROM clients WHERE client_id = @client_id;", connection, transaction);
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
        var baseCode = new string(name.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).Take(8).ToArray());
        if (string.IsNullOrWhiteSpace(baseCode))
        {
            var fallback = new string(sourceRecordId.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).Take(6).ToArray());
            baseCode = string.IsNullOrWhiteSpace(fallback) ? "CUSTOMER" : $"CRM{fallback}";
        }
        baseCode = baseCode[..Math.Min(8, baseCode.Length)];

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var suffix = attempt == 0 ? string.Empty : attempt.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var prefixLength = Math.Max(1, Math.Min(8 - suffix.Length, baseCode.Length));
            var candidate = (baseCode[..prefixLength] + suffix).ToUpperInvariant();
            await using var command = new NpgsqlCommand("SELECT NOT EXISTS (SELECT 1 FROM clients WHERE client_code = @code);", connection, transaction);
            command.Parameters.AddWithValue("code", candidate);
            if (await command.ExecuteScalarAsync(cancellationToken) is true) return candidate;
        }
        throw new InvalidOperationException("A unique customer code could not be allocated.");
    }

    private static async Task<Dictionary<string, LocalCustomer>> ReadLinksAsync(
        NpgsqlConnection connection,
        string sourceSystem,
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
        command.Parameters.AddWithValue("source_system", sourceSystem);
        command.Parameters.AddWithValue("source_ids", ids);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result[reader.GetString(0)] = new LocalCustomer(reader.GetGuid(1), reader.GetString(2), reader.GetString(3));
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
            result[reader.GetString(3)] = new LocalCustomer(reader.GetGuid(0), reader.GetString(1), reader.GetString(2));
        return result;
    }

    private static async Task<Guid> StartRunAsync(
        NpgsqlConnection connection,
        string providerKey,
        string sourceSystem,
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
                    'localContactsOverwritten', false,
                    'authorityMode', 'crm'
                )
            );
            """, connection);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("provider", providerKey);
        command.Parameters.AddWithValue("source_system", sourceSystem);
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
        int records,
        int linked,
        string providerName,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE customer_directory_sync_runs
            SET status = 'previewed', completed_at = NOW(),
                source_records_seen = @records,
                organizations_seen = @records,
                linked_count = @linked,
                message = @message
            WHERE customer_directory_sync_run_id = @run_id;
            """, connection);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("records", records);
        command.Parameters.AddWithValue("linked", linked);
        command.Parameters.AddWithValue("message", $"{providerName} customer preview completed.");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CompleteImportRunAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid runId,
        int records,
        int imported,
        int updated,
        int linked,
        int skipped,
        int failed,
        string status,
        string providerName,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE customer_directory_sync_runs
            SET status = @status, completed_at = NOW(),
                source_records_seen = @records,
                organizations_seen = @records,
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
        command.Parameters.AddWithValue("records", records);
        command.Parameters.AddWithValue("imported", imported);
        command.Parameters.AddWithValue("updated", updated);
        command.Parameters.AddWithValue("linked", linked);
        command.Parameters.AddWithValue("skipped", skipped);
        command.Parameters.AddWithValue("failed", failed);
        command.Parameters.AddWithValue("message", $"{providerName} customer sync completed with {failed} failure(s).");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task FailRunAsync(NpgsqlConnection connection, Guid runId, string errorCode, string message, CancellationToken cancellationToken)
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
            // Preserve the original controlled failure response if run evidence cannot be updated.
        }
    }

    private static async Task<bool> ProviderSchemaReadyAsync(NpgsqlConnection connection, CancellationToken cancellationToken) =>
        await TableExistsAsync(connection, "crm_integration_providers", null, cancellationToken)
        && await TableExistsAsync(connection, "crm_integration_credentials", null, cancellationToken);

    private static async Task<bool> AuthoritySchemaReadyAsync(NpgsqlConnection connection, CancellationToken cancellationToken) =>
        await ProviderSchemaReadyAsync(connection, cancellationToken)
        && await TableExistsAsync(connection, "customer_directory_source_authority", null, cancellationToken)
        && await TableExistsAsync(connection, "customer_directory_source_authority_history", null, cancellationToken)
        && await TableExistsAsync(connection, "customer_directory_source_links", null, cancellationToken)
        && await TableExistsAsync(connection, "customer_directory_sync_runs", null, cancellationToken);

    private static async Task<bool> TableExistsAsync(
        NpgsqlConnection connection,
        string tableName,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT to_regclass(@table_name) IS NOT NULL;", connection, transaction);
        command.Parameters.AddWithValue("table_name", $"public.{tableName}");
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task ExecuteControlAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string SourceHash(GenericCustomer customer)
    {
        var payload = string.Join('|', new[]
        {
            customer.SourceRecordId, customer.Name, customer.CustomerStatus,
            customer.ProspectStatus, customer.Industry, customer.Website,
            customer.Phone, customer.Email, customer.AddressLine1,
            customer.City, customer.StateRegion, customer.PostalCode,
            customer.Country, customer.UpdatedAt?.ToString("O") ?? string.Empty,
            customer.IsActive.ToString(System.Globalization.CultureInfo.InvariantCulture)
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
            return Results.Json(new { module = ModuleNumber, status = "view_as_read_only", message = "Exit Administrator View-As before changing the customer source." }, statusCode: StatusCodes.Status403Forbidden);
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
            LogFailure(context, exception, "open customer source authority storage");
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

    private static string NormalizeName(string value) => value.Trim().ToLowerInvariant();
    private static string? NormalizeMode(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "sell" or "crm" or "manual" ? normalized : null;
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

    private static string Clean(string? value, int maximum)
    {
        var cleaned = value?.Trim() ?? string.Empty;
        return cleaned.Length > maximum ? cleaned[..maximum] : cleaned;
    }

    private static IResult Invalid(string message) => Results.BadRequest(new { module = ModuleNumber, status = "invalid_request", message });
    private static IResult OriginRejected() => Results.Json(new { module = ModuleNumber, status = "origin_rejected", message = "The request origin is not allowed." }, statusCode: StatusCodes.Status403Forbidden);
    private static IResult DependencyUnavailable() => Results.Json(new { module = ModuleNumber, status = "customer_source_storage_unavailable", message = "Customer source storage is temporarily unavailable." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    private static IResult ProviderSchemaUnavailable() => Results.Json(new { module = ModuleNumber, status = "integration_schema_unavailable", migration = "034_module_026_crm_erp_integrations", message = "Module 026 integration storage is not installed." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    private static IResult AuthoritySchemaUnavailable() => Results.Json(new { module = ModuleNumber, status = "customer_source_authority_schema_unavailable", migration = MigrationId, message = "Apply migration 098 before changing or synchronizing the configurable customer source." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    private static IResult ProviderFailure(string status, string message) => Results.Json(new { module = ModuleNumber, status, message }, statusCode: StatusCodes.Status502BadGateway);
    private static IResult InvalidProviderEndpoint(CustomerSourceAuthorityState source) => Results.Json(new { module = ModuleNumber, status = "customer_source_endpoint_invalid", providerKey = source.ProviderKey, message = "The selected Module 026 customer endpoint is invalid or does not match the configured provider base URL." }, statusCode: StatusCodes.Status409Conflict);
    private static IResult MappingMissing(CustomerSourceAuthorityState source, string message) => Results.Json(new { module = ModuleNumber, status = "customer_import_mapping_missing", providerKey = source.ProviderKey, providerName = source.ProviderName, message }, statusCode: StatusCodes.Status409Conflict);
    private static IResult SourceNotReady(CustomerSourceAuthorityState source) => Results.Json(new { module = ModuleNumber, status = "customer_source_not_ready", providerKey = source.ProviderKey, providerName = source.ProviderName, source.ProviderEnabled, source.AvailabilityStatus, source.CredentialConfigured, message = $"Make the {source.ProviderName} connection available in Module 026 before synchronizing customers." }, statusCode: StatusCodes.Status409Conflict);

    private static void LogFailure(HttpContext context, Exception exception, string operation)
    {
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CustomerSourceAuthorityModule")
            .LogWarning("Modules 021/026 could not {Operation} ({ExceptionType}).", operation, exception.GetType().Name);
    }

    internal sealed record CustomerSourceAuthorityState(
        string Mode,
        string? ProviderKey,
        string ProviderName,
        string ProviderType,
        bool ProviderEnabled,
        string AvailabilityStatus,
        bool CredentialConfigured,
        string AuthModel,
        string BaseUrl,
        string RecordLookupUrlTemplate,
        string ImportMappingJson,
        DateTimeOffset? UpdatedAt,
        bool MigrationApplied,
        DateTimeOffset? LastSuccessfulCustomerSyncAt)
    {
        internal bool IsManual => Mode == "manual";
        internal bool IsSell => Mode == "sell";
        internal bool ProviderReady => IsManual || (ProviderEnabled && CredentialConfigured && AvailabilityStatus.Equals("available", StringComparison.OrdinalIgnoreCase));
        internal bool RequiresSellAssociation => IsSell;
        internal string SourceSystem => IsManual ? "MANUAL" : IsSell ? "SELL" : $"CRM:{ProviderKey}";
    }

    private sealed record ProviderRecord(
        string ProviderKey,
        string ProviderName,
        string ProviderType,
        string AuthModel,
        string BaseUrl,
        string ApiKeyHeader,
        string ApiKeyPrefix,
        string RecordLookupUrlTemplate,
        string ImportMappingJson,
        bool IsEnabled,
        string AvailabilityStatus,
        bool CredentialConfigured);

    private sealed record CustomerImportMapping(
        string CustomerListUrl,
        string CustomerRecordUrlTemplate,
        string ItemsPath,
        string RecordPath,
        string IdPath,
        string NamePath,
        string CustomerStatusPath,
        string ProspectStatusPath,
        string IndustryPath,
        string WebsitePath,
        string PhonePath,
        string EmailPath,
        string AddressLine1Path,
        string CityPath,
        string StatePath,
        string PostalCodePath,
        string CountryPath,
        string UpdatedAtPath,
        string ActivePath)
    {
        internal static readonly CustomerImportMapping Empty = new(
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        internal bool PreviewConfigured => !string.IsNullOrWhiteSpace(CustomerListUrl)
                                           && !string.IsNullOrWhiteSpace(ItemsPath)
                                           && !string.IsNullOrWhiteSpace(IdPath)
                                           && !string.IsNullOrWhiteSpace(NamePath);
        internal bool ImportConfigured => !string.IsNullOrWhiteSpace(CustomerRecordUrlTemplate)
                                          && !string.IsNullOrWhiteSpace(IdPath)
                                          && !string.IsNullOrWhiteSpace(NamePath);
    }

    private sealed record GenericCustomer(
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
        DateTimeOffset? UpdatedAt,
        bool IsActive);

    private sealed record CustomerUpsertOutcome(string Status, string ResultCode, Guid ClientId, string ClientCode);
    private sealed record LocalCustomer(Guid ClientId, string ClientCode, string ClientName);
    private sealed record CustomerSourceUpdateRequest(string? Mode, string? ProviderKey);
    private sealed record CustomerSourcePreviewRequest(string? Search, int? Page, int? PageSize);
    private sealed record CustomerSourceImportRequest(string[]? SourceRecordIds);
    private sealed record BodyOutcome<T>(T? Value, IResult? Failure);
    private sealed record AuthorizationOutcome(string? HeaderName, string? HeaderValue, IResult? Failure, string FailureCode, string FailureMessage);
    private sealed record RemoteResponseOutcome(string? Body, IResult? Failure, string FailureCode, string FailureMessage);
}
