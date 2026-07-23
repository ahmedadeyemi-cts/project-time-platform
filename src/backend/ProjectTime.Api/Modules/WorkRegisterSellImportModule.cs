using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static class WorkRegisterSellImportModule
{
    private const string ProviderKey = "zendesk_sell";

    public static WebApplication MapWorkRegisterSellImportEndpoints(this WebApplication app)
    {
        app.MapPost(
            "/api/work-register/intake/packages/sell/import",
            (Func<WorkRegisterSellImportRequest, HttpContext, IHttpClientFactory, Task<IResult>>)ImportAsync);
        return app;
    }

    private static async Task<IResult> ImportAsync(
        WorkRegisterSellImportRequest request,
        HttpContext context,
        IHttpClientFactory httpClientFactory)
    {
        if (!SameOrigin(context)) return Results.Json(new { status = "origin_rejected" }, statusCode: 403);
        var actorUserId = ActualUserId(context);
        if (actorUserId is null) return Results.Json(new { status = "session_required" }, statusCode: 401);
        if (string.IsNullOrWhiteSpace(request.SellRecordId) || request.SellRecordId.Trim().Length > 200)
            return Invalid("A SELL record ID of 200 characters or fewer is required.");
        if (request.CustomerId == Guid.Empty) return Invalid("Select the ProjectPulse customer for this SELL record.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return Invalid("An intake reason is required for audit history.");

        DateOnly? sowSignedDate = null;
        if (!string.IsNullOrWhiteSpace(request.SowSignedDate))
        {
            if (!DateOnly.TryParseExact(
                    request.SowSignedDate.Trim(),
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var parsedSowSignedDate))
            {
                return Invalid("SOW signed date must use YYYY-MM-DD.");
            }
            sowSignedDate = parsedSowSignedDate;
        }

        DateOnly? estimatedEndDate = null;
        if (!string.IsNullOrWhiteSpace(request.EstimatedEndDate))
        {
            if (!DateOnly.TryParseExact(
                    request.EstimatedEndDate.Trim(),
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var parsedEstimatedEndDate))
            {
                return Invalid("Estimated end date must use YYYY-MM-DD.");
            }
            if (parsedEstimatedEndDate < DateOnly.FromDateTime(DateTime.UtcNow))
            {
                return Invalid("Estimated end date cannot be before the project creation date.");
            }
            estimatedEndDate = parsedEstimatedEndDate;
        }

        await using var connection = await OpenAsync(context.RequestAborted);
        if (!await WorkRegisterAuthorization.HasCreateAuthorityAsync(
                connection, context, cancellationToken: context.RequestAborted))
        {
            return Results.Json(new
            {
                status = "access_denied",
                message = "Only a Project Team Coordinator, Administrator, or Super Administrator can import SELL work into the Work Register."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var provider = await ReadSellProviderAsync(connection, context.RequestAborted);
        if (provider is null) return Results.Json(new
        {
            status = "sell_provider_unavailable",
            message = "Configure SELL in Module 026 before importing a Work Register record."
        }, statusCode: StatusCodes.Status409Conflict);
        if (!provider.IsEnabled) return Results.Json(new
        {
            status = "sell_provider_disabled",
            message = "Enable SELL in Module 026 before importing."
        }, statusCode: StatusCodes.Status409Conflict);
        if (!string.Equals(provider.AvailabilityStatus, "available", StringComparison.OrdinalIgnoreCase))
            return Results.Json(new
            {
                status = "sell_provider_not_available",
                message = "Run a successful SELL availability test in Module 026 before importing."
            }, statusCode: StatusCodes.Status409Conflict);
        if (!provider.RecordLookupUrlTemplate.Contains("{recordId}", StringComparison.Ordinal))
            return Invalid("SELL record lookup is not configured in Module 026.");

        var lookupValue = provider.RecordLookupUrlTemplate.Replace(
            "{recordId}",
            Uri.EscapeDataString(request.SellRecordId.Trim()),
            StringComparison.Ordinal);
        if (!Uri.TryCreate(lookupValue, UriKind.Absolute, out var lookupUri)
            || !await CrmErpIntegrationModule.IsSafeExternalUriAsync(lookupUri, context.RequestAborted))
        {
            return Invalid("The configured SELL lookup URL is not an approved public HTTPS address.");
        }

        var encryptionKey = CrmErpIntegrationModule.ReadEncryptionKey();
        if (encryptionKey is null) return Results.Json(new
        {
            status = "secure_store_unavailable",
            message = "The Module 026 integration encryption key is unavailable."
        }, statusCode: StatusCodes.Status503ServiceUnavailable);

        try
        {
            using var outbound = new HttpRequestMessage(HttpMethod.Get, lookupUri);
            if (provider.AuthModel == "api_key")
            {
                var apiKey = await CrmErpIntegrationModule.LoadCredentialAsync(
                    connection, ProviderKey, "api_key", encryptionKey, context.RequestAborted);
                if (string.IsNullOrWhiteSpace(apiKey)) return Invalid("Save the SELL API key in Module 026 before importing.");
                var value = string.IsNullOrWhiteSpace(provider.ApiKeyPrefix)
                    ? apiKey
                    : $"{provider.ApiKeyPrefix.Trim()} {apiKey}";
                outbound.Headers.TryAddWithoutValidation(provider.ApiKeyHeader, value);
            }
            else
            {
                var envelope = await CrmErpIntegrationModule.LoadCredentialAsync(
                    connection, ProviderKey, "oauth_token", encryptionKey, context.RequestAborted);
                if (string.IsNullOrWhiteSpace(envelope)) return Invalid("Connect SELL with OAuth in Module 026 before importing.");
                using var tokenDocument = JsonDocument.Parse(envelope);
                var token = Text(tokenDocument.RootElement, "accessToken");
                if (string.IsNullOrWhiteSpace(token)) return Invalid("Reconnect SELL OAuth in Module 026 before importing.");
                outbound.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var client = httpClientFactory.CreateClient("Module026");
            using var response = await client.SendAsync(
                outbound,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted);
            var responseBody = await CrmErpIntegrationModule.ReadBoundedResponseBodyAsync(
                response.Content,
                context.RequestAborted);
            if (responseBody is null) return Results.Json(new
            {
                status = "sell_response_too_large",
                message = "SELL returned more data than the controlled import limit."
            }, statusCode: StatusCodes.Status502BadGateway);
            if (!response.IsSuccessStatusCode) return Results.Json(new
            {
                status = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? "sell_authentication_failed"
                    : "sell_record_unavailable",
                message = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? "SELL rejected the configured credential. Reconnect it in Module 026."
                    : "SELL could not return the requested record.",
                remoteStatusCode = (int)response.StatusCode
            }, statusCode: StatusCodes.Status502BadGateway);

            using var source = JsonDocument.Parse(responseBody);
            using var mappingDocument = JsonDocument.Parse(provider.ImportMappingJson);
            var mapping = mappingDocument.RootElement;
            var projectName = PathText(source.RootElement, Mapping(mapping, "projectNamePath"));
            var quoteNumber = PathText(source.RootElement, Mapping(mapping, "quoteNumberPath"));
            var sellCustomerName = PathText(source.RootElement, Mapping(mapping, "customerNamePath"));
            var contractedAmount = PathDecimal(source.RootElement, Mapping(mapping, "contractedAmountPath"));
            var rateElement = Path(source.RootElement, Mapping(mapping, "rateLinesPath"));
            if (string.IsNullOrWhiteSpace(projectName)) return Invalid("The SELL mapping did not return a project name. Update the field mapping in Module 026.");
            if (rateElement is null || rateElement.Value.ValueKind != JsonValueKind.Array)
                return Invalid("The SELL mapping did not return Pricing / Rate Review rows. Update the field mapping in Module 026.");

            var rates = new List<object>();
            foreach (var rate in rateElement.Value.EnumerateArray())
            {
                var amount = PathDecimal(rate, Mapping(mapping, "unitRatePath"));
                if (amount is null || amount < 0) continue;
                rates.Add(new
                {
                    include = true,
                    source = "SELL",
                    sourceLocked = true,
                    sku = PathText(rate, Mapping(mapping, "rateCodePath")),
                    description = PathText(rate, Mapping(mapping, "descriptionPath")),
                    rate = amount.Value,
                    unitRate = amount.Value,
                    hours = 0m,
                    laborCategory = PathText(rate, Mapping(mapping, "laborCategoryPath")),
                    timeType = PathText(rate, Mapping(mapping, "timeTypePath")),
                    unitType = PathText(rate, Mapping(mapping, "unitTypePath")),
                    billable = PathBool(rate, Mapping(mapping, "billablePath")) ?? true
                });
            }
            if (rates.Count == 0) return Invalid("SELL returned no usable Actual Rate rows for Pricing / Rate Review.");

            var customerName = await CustomerNameAsync(connection, request.CustomerId, context.RequestAborted);
            if (customerName is null) return Invalid("The selected ProjectPulse customer was not found.");
            var contractType = CanonicalContractType(request.ContractType);
            var extracted = new
            {
                sourceMode = "sell_import",
                sourceSystem = "SELL",
                sourceRecordId = request.SellRecordId.Trim(),
                sourceFieldsLocked = new[] { "projectName", "rates" },
                projectName,
                customerId = request.CustomerId,
                customerName,
                sellCustomerName,
                sellQuoteNumber = quoteNumber,
                requestedWorkType = Clean(request.RequestedWorkType, "Project"),
                contractType,
                sowSignedDate,
                estimatedEndDate,
                projectListPrice = contractedAmount,
                rates,
                tasks = Array.Empty<object>(),
                phaseTotals = Array.Empty<object>(),
                parserNotes = new[]
                {
                    "Project name and Actual Rate / Pricing / Rate Review rows were imported directly from SELL and are source-locked.",
                    "Task and assignment planning remains a ProjectPulse review step before final creation."
                }
            };
            var extractedJson = JsonSerializer.Serialize(extracted);
            var intakePackageId = Guid.NewGuid();

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO work_register_intake_packages (
                    work_register_intake_package_id,intake_status,requested_work_type,contract_type,
                    sell_quote_number,customer_id,source_mode,customer_hint,project_name_hint,notes,
                    extraction_status,extracted_json,review_status,created_by_user_id,updated_at)
                VALUES (
                    @id,'uploaded',@work_type,@contract_type,@quote,@customer_id,'sell_import',
                    @customer_name,@project_name,@notes,'completed',CAST(@extracted AS jsonb),
                    'needs_review',@actor,NOW());
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("id", intakePackageId);
                insert.Parameters.AddWithValue("work_type", Clean(request.RequestedWorkType, "Project"));
                insert.Parameters.AddWithValue("contract_type", contractType);
                insert.Parameters.AddWithValue("quote", quoteNumber);
                insert.Parameters.AddWithValue("customer_id", request.CustomerId);
                insert.Parameters.AddWithValue("customer_name", customerName);
                insert.Parameters.AddWithValue("project_name", projectName);
                insert.Parameters.AddWithValue("notes", Clean(request.Notes));
                insert.Parameters.AddWithValue("extracted", extractedJson);
                insert.Parameters.AddWithValue("actor", actorUserId.Value);
                await insert.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await using (var history = new NpgsqlCommand("""
                INSERT INTO work_register_intake_history (
                    work_register_intake_history_id,work_register_intake_package_id,action,summary,
                    changed_by_user_id,payload_json)
                VALUES (
                    gen_random_uuid(),@id,'sell_import_created',@reason,@actor,
                    jsonb_build_object(
                        'sourceSystem','SELL','sourceRecordId',@record_id,'projectName',@project_name,
                        'sellQuoteNumber',@quote,'rateCount',@rate_count,'sourceFieldsLocked',
                        jsonb_build_array('projectName','rates')));
                """, connection, transaction))
            {
                history.Parameters.AddWithValue("id", intakePackageId);
                history.Parameters.AddWithValue("reason", request.Reason.Trim());
                history.Parameters.AddWithValue("actor", actorUserId.Value);
                history.Parameters.AddWithValue("record_id", request.SellRecordId.Trim());
                history.Parameters.AddWithValue("project_name", projectName);
                history.Parameters.AddWithValue("quote", quoteNumber);
                history.Parameters.AddWithValue("rate_count", rates.Count);
                await history.ExecuteNonQueryAsync(context.RequestAborted);
            }
            await transaction.CommitAsync(context.RequestAborted);

            return Results.Ok(new
            {
                status = "sell_intake_imported",
                intakePackageId,
                requestedWorkType = Clean(request.RequestedWorkType, "Project"),
                contractType,
                customerId = request.CustomerId,
                customerHint = customerName,
                projectNameHint = projectName,
                sellQuoteNumber = quoteNumber,
                sourceMode = "sell_import",
                extractionStatus = "completed",
                reviewStatus = "needs_review",
                rateCount = rates.Count,
                message = "SELL project name and Actual Rate / Pricing / Rate Review data were imported. Review assignments, then create the Work Register record."
            });
        }
        catch (JsonException)
        {
            return Invalid("SELL returned data that does not match the configured import mapping.");
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            return Results.Json(new { status = "sell_timeout", message = "SELL did not respond before the connection timeout." }, statusCode: 504);
        }
        catch (HttpRequestException)
        {
            return Results.Json(new { status = "sell_connection_failed", message = "ProjectPulse could not reach SELL." }, statusCode: 502);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
        }
    }

    private static async Task<SellProvider?> ReadSellProviderAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT auth_model,record_lookup_url_template,import_mapping_json::text,
                   api_key_header,api_key_prefix,is_enabled,availability_status
            FROM crm_integration_providers
            WHERE provider_key=@provider;
            """, connection);
        command.Parameters.AddWithValue("provider", ProviderKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new SellProvider(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetBoolean(5), reader.GetString(6))
            : null;
    }

    private static async Task<string?> CustomerNameAsync(NpgsqlConnection connection, Guid customerId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT client_name FROM clients WHERE client_id=@id AND COALESCE(is_active,TRUE)=TRUE;",
            connection);
        command.Parameters.AddWithValue("id", customerId);
        return (await command.ExecuteScalarAsync(cancellationToken))?.ToString();
    }

    private static JsonElement? Path(JsonElement root, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var current = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current)) return null;
        }
        return current;
    }

    private static string PathText(JsonElement root, string path)
    {
        var value = Path(root, path);
        return value?.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => value.Value.ToString(),
            _ => string.Empty
        };
    }

    private static decimal? PathDecimal(JsonElement root, string path)
    {
        var value = Path(root, path);
        if (value is null) return null;
        if (value.Value.ValueKind == JsonValueKind.Number && value.Value.TryGetDecimal(out var number)) return number;
        if (value.Value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.Value.GetString()?.Replace("$", "").Replace(",", ""), out number)) return number;
        return null;
    }

    private static bool? PathBool(JsonElement root, string path)
    {
        var value = Path(root, path);
        if (value is null) return null;
        if (value.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.Value.GetBoolean();
        return bool.TryParse(value.Value.ToString(), out var parsed) ? parsed : null;
    }

    private static string Mapping(JsonElement mapping, string name) => Text(mapping, name);
    private static string Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static bool SameOrigin(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("Origin", out var values)) return true;
        if (!Uri.TryCreate(values.ToString(), UriKind.Absolute, out var origin)) return false;
        return string.Equals(origin.Host, context.Request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && origin.Port == (context.Request.Host.Port ?? (context.Request.IsHttps ? 443 : 80))
            && string.Equals(origin.Scheme, context.Request.Scheme, StringComparison.OrdinalIgnoreCase);
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

    private static async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var config = InvoiceBillingDatabaseConfig.FromEnvironment();
        if (config.Missing.Count > 0) throw new InvalidOperationException("ProjectPulse database configuration is missing.");
        var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static IResult Invalid(string message) => Results.BadRequest(new { status = "validation_failed", message });
    private static string Clean(string? value, string fallback = "") => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string CanonicalContractType(string? value)
    {
        var original = Clean(value, "Fixed Price");
        var normalized = System.Text.RegularExpressions.Regex.Replace(
            original.ToLowerInvariant(),
            "[^a-z0-9]+",
            string.Empty);

        return normalized switch
        {
            "tm" or "timeandmaterial" or "timeandmaterials" => "Time and Material",
            "fp" or "fixedprice" => "Fixed Price",
            _ => original
        };
    }

    private sealed record SellProvider(
        string AuthModel,
        string RecordLookupUrlTemplate,
        string ImportMappingJson,
        string ApiKeyHeader,
        string ApiKeyPrefix,
        bool IsEnabled,
        string AvailabilityStatus);
}

public sealed record WorkRegisterSellImportRequest(
    string? SellRecordId,
    Guid CustomerId,
    string? RequestedWorkType,
    string? ContractType,
    string? SowSignedDate,
    string? EstimatedEndDate,
    string? Notes,
    string? Reason);
