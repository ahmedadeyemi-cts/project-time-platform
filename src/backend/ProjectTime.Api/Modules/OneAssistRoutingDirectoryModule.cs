using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 072 provides the US Signal OneAssist routing PIN directory. Routing
/// PINs are intentionally visible identifiers, not authentication secrets.
/// Everyone can read them; only the confirmed manager, administrator, and PTC
/// roles can edit them.
/// </summary>
public static class OneAssistRoutingDirectoryModule
{
    private const string ModuleNumber = "072";
    private const string ContractVersion = "2026-07-19.1";
    private const string ImplementationBaseline =
        "2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4";
    private const string ManagePermission = "MANAGE_ONEASSIST_ROUTING_DIRECTORY";

    public static WebApplication MapOneAssistRoutingDirectoryEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/oneassist/capabilities",
            (Func<HttpContext, Task<IResult>>)GetCapabilitiesAsync);
        app.MapGet(
            "/api/oneassist/routes",
            (Func<HttpContext, Task<IResult>>)GetRoutesAsync);
        app.MapPut(
            "/api/oneassist/routes",
            (Func<HttpContext, Task<IResult>>)SaveRoutesAsync);
        app.MapPost(
            "/api/oneassist/import/preview",
            (Func<HttpContext, Task<IResult>>)PreviewImportAsync);
        app.MapGet(
            "/api/public/v1/oneassist/routes",
            (Func<HttpContext, Task<IResult>>)GetPublicRoutesAsync);
        app.MapGet(
            "/api/public/v1/oneassist/resolve",
            (Func<string?, HttpContext, Task<IResult>>)ResolvePublicPinAsync);

        return app;
    }

    private static async Task<IResult> GetCapabilitiesAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, requireManage: false);
        if (access.Failure is not null) return access.Failure;

        return Results.Ok(new
        {
            module = ModuleNumber,
            moduleName = "OneAssist Routing PIN Directory",
            status = "capabilities_loaded",
            contractVersion = ContractVersion,
            implementationBaseline = ImplementationBaseline,
            access = AccessResponse(access.Context!, context),
            dataClassification = new
            {
                pinClassification = "public_routing_identifier",
                masked = false,
                visibleToAllAuthenticatedUsers = true,
                publicApiEnabled = true,
                authenticationCredential = false
            },
            authorization = new
            {
                view = "everyone",
                manage = new[]
                {
                    "MANAGER",
                    "ADMINISTRATOR",
                    "SUPER_ADMINISTRATOR",
                    "PROJECT_TEAM_COORDINATOR"
                },
                permission = ManagePermission,
                serverEnforced = true,
                viewAsTransfersAuthority = false
            },
            validation = new
            {
                exactDigits = 5,
                unique = true,
                nameRequired = true,
                stableCustomerId = true
            },
            import = new
            {
                csv = true,
                xlsx = true,
                previewBeforeApply = true,
                automaticallyPersists = false
            },
            publicApi = new[]
            {
                "/api/public/v1/oneassist/routes",
                "/api/public/v1/oneassist/resolve?pin=12345"
            },
            persistence = PersistenceStatus()
        });
    }

    private static async Task<IResult> GetRoutesAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, requireManage: false);
        if (access.Failure is not null) return access.Failure;
        var source = await ReadUpstreamAsync(context);
        if (source.Failure is not null) return source.Failure;
        var routes = NormalizeRoutes(source.Payload);
        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "routes_loaded",
            access = AccessResponse(access.Context!, context),
            canManage = access.Context!.CanManage,
            pinVisibility = "visible_unmasked",
            count = routes.Count,
            routes
        });
    }

    private static async Task<IResult> SaveRoutesAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, requireManage: true);
        if (access.Failure is not null) return access.Failure;
        var request = await ReadJsonBodyAsync(context);
        if (request.Failure is not null) return request.Failure;
        var routesNode = request.Payload?["routes"] ?? request.Payload?["customers"] ?? request.Payload;
        if (routesNode is not JsonArray routes)
        {
            return Results.BadRequest(new
            {
                module = ModuleNumber,
                status = "invalid_routes",
                message = "A routes array is required."
            });
        }
        var normalized = NormalizeAndValidateRoutes(routes, out var validationFailure);
        if (validationFailure is not null) return validationFailure;

        var upstreamBody = new JsonObject { ["customers"] = normalized!.DeepClone() };
        var upstream = await SendUpstreamAsync(
            HttpMethod.Post,
            "/api/admin/ps-customers/save",
            upstreamBody,
            includeAdminHeaders: true,
            context);
        if (upstream.Failure is not null) return upstream.Failure;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "routes_saved",
            count = normalized.Count,
            savedAt = DateTimeOffset.UtcNow,
            savedBy = access.Context!.ActualUserId,
            persistence = upstream.Payload,
            auditRequired = true
        });
    }

    private static async Task<IResult> PreviewImportAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, requireManage: true);
        if (access.Failure is not null) return access.Failure;
        if (!context.Request.HasFormContentType)
        {
            return Results.BadRequest(new
            {
                module = ModuleNumber,
                status = "tabular_file_required",
                message = "Upload one CSV or XLSX file as multipart form data."
            });
        }

        var form = await context.Request.ReadFormAsync();
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new { module = ModuleNumber, status = "empty_import_file" });
        }
        if (file.Length > 5 * 1024 * 1024)
        {
            return Results.BadRequest(new
            {
                module = ModuleNumber,
                status = "import_file_too_large",
                maximumBytes = 5 * 1024 * 1024
            });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var rows = extension switch
            {
                ".csv" => await ReadCsvRowsAsync(stream),
                ".xlsx" => ReadXlsxRows(stream),
                _ => throw new InvalidDataException("Only CSV and XLSX files are supported.")
            };
            var preview = BuildImportPreview(rows);
            return Results.Ok(new
            {
                module = ModuleNumber,
                status = "import_preview_loaded",
                sourceFileName = Path.GetFileName(file.FileName),
                sourceType = extension.TrimStart('.'),
                persistencePerformed = false,
                validCount = preview.Valid.Count,
                warningCount = preview.Warnings.Count,
                routes = preview.Valid,
                warnings = preview.Warnings
            });
        }
        catch (InvalidDataException exception)
        {
            return Results.BadRequest(new
            {
                module = ModuleNumber,
                status = "invalid_import_file",
                message = exception.Message
            });
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "preview a OneAssist import");
            return Results.Problem(
                title: "OneAssist import unavailable",
                detail: "The tabular file could not be previewed.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> GetPublicRoutesAsync(HttpContext context)
    {
        SetPublicHeaders(context);
        var source = await ReadUpstreamAsync(context);
        if (source.Failure is not null) return source.Failure;
        var routes = NormalizeRoutes(source.Payload);
        return Results.Ok(new
        {
            module = ModuleNumber,
            service = "US Signal OneAssist Routing",
            status = "routes_loaded",
            generatedAt = DateTimeOffset.UtcNow,
            pinClassification = "public_routing_identifier",
            count = routes.Count,
            routes
        });
    }

    private static async Task<IResult> ResolvePublicPinAsync(string? pin, HttpContext context)
    {
        SetPublicHeaders(context);
        var normalizedPin = NormalizePin(pin);
        if (normalizedPin is null)
        {
            return Results.BadRequest(new
            {
                module = ModuleNumber,
                service = "US Signal OneAssist Routing",
                status = "invalid_pin",
                message = "PIN must contain exactly five digits."
            });
        }

        var source = await ReadUpstreamAsync(context);
        if (source.Failure is not null) return source.Failure;
        var routes = NormalizeRoutes(source.Payload);
        var match = routes
            .Select(node => node as JsonObject)
            .FirstOrDefault(route => route?["pin"]?.GetValue<string>() == normalizedPin);
        if (match is null)
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                service = "US Signal OneAssist Routing",
                status = "route_not_found",
                match = false,
                pin = normalizedPin
            }, statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Ok(new
        {
            module = ModuleNumber,
            service = "US Signal OneAssist Routing",
            status = "route_resolved",
            match = true,
            route = match
        });
    }

    private static JsonArray NormalizeRoutes(JsonNode? payload)
    {
        /* MODULE_072_JSON_ARRAY_ROUTE_NORMALIZATION */
        var source = payload is JsonObject objectPayload
            ? objectPayload["routes"] ?? objectPayload["customers"] ?? objectPayload
            : payload;
        var routes = source as JsonArray ?? new JsonArray();
        var normalized = new JsonArray();
        foreach (var node in routes)
        {
            if (node is not JsonObject route) continue;
            var name = NodeText(route["name"])?.Trim();
            var pin = NormalizePin(NodeText(route["pin"]));
            if (string.IsNullOrWhiteSpace(name) || pin is null) continue;
            normalized.Add(new JsonObject
            {
                ["id"] = NodeText(route["id"])?.Trim() ?? string.Empty,
                ["name"] = name,
                ["pin"] = pin
            });
        }
        return normalized;
    }

    private static JsonArray? NormalizeAndValidateRoutes(JsonArray routes, out IResult? failure)
    {
        var normalized = new JsonArray();
        var pins = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < routes.Count; index++)
        {
            if (routes[index] is not JsonObject route)
            {
                failure = InvalidRoute(index, "Each route must be an object.");
                return null;
            }
            var name = NodeText(route["name"])?.Trim();
            var pin = NormalizePin(NodeText(route["pin"]));
            var id = NodeText(route["id"])?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                failure = InvalidRoute(index, "Customer name is required.");
                return null;
            }
            if (pin is null)
            {
                failure = InvalidRoute(index, "PIN must contain exactly five digits.");
                return null;
            }
            if (!pins.Add(pin))
            {
                failure = InvalidRoute(index, "Every routing PIN must be unique.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(id)) id = Guid.NewGuid().ToString();
            if (!ids.Add(id))
            {
                failure = InvalidRoute(index, "Every customer identifier must be unique.");
                return null;
            }
            normalized.Add(new JsonObject { ["id"] = id, ["name"] = name, ["pin"] = pin });
        }
        failure = null;
        return normalized;
    }

    private static IResult InvalidRoute(int index, string message) => Results.BadRequest(new
    {
        module = ModuleNumber,
        status = "invalid_route",
        row = index + 1,
        message
    });

    private static string? NormalizePin(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed is { Length: 5 } && trimmed.All(char.IsAsciiDigit) ? trimmed : null;
    }

    private static async Task<List<Dictionary<string, string>>> ReadCsvRowsAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = await reader.ReadToEndAsync();
        var table = ParseCsv(text);
        return MapTable(table);
    }

    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (character == '"') quoted = false;
                else field.Append(character);
                continue;
            }
            if (character == '"') quoted = true;
            else if (character == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                row.Add(field.ToString());
                field.Clear();
                if (row.Any(value => !string.IsNullOrWhiteSpace(value))) rows.Add(row);
                row = new List<string>();
            }
            else field.Append(character);
        }
        row.Add(field.ToString());
        if (row.Any(value => !string.IsNullOrWhiteSpace(value))) rows.Add(row);
        if (quoted) throw new InvalidDataException("CSV contains an unterminated quoted field.");
        return rows;
    }

    private static List<Dictionary<string, string>> ReadXlsxRows(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var sharedStrings = new List<string>();
        var sharedEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sharedEntry is not null)
        {
            using var sharedStream = sharedEntry.Open();
            var sharedDocument = XDocument.Load(sharedStream);
            sharedStrings.AddRange(sharedDocument.Descendants()
                .Where(element => element.Name.LocalName == "si")
                .Select(element => string.Concat(element.Descendants()
                    .Where(value => value.Name.LocalName == "t")
                    .Select(value => value.Value))));
        }

        var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? archive.Entries.FirstOrDefault(entry =>
                entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        if (sheetEntry is null) throw new InvalidDataException("XLSX does not contain a worksheet.");
        using var sheetStream = sheetEntry.Open();
        var sheetDocument = XDocument.Load(sheetStream);
        var table = new List<List<string>>();
        foreach (var rowElement in sheetDocument.Descendants().Where(element => element.Name.LocalName == "row"))
        {
            var values = new SortedDictionary<int, string>();
            foreach (var cell in rowElement.Elements().Where(element => element.Name.LocalName == "c"))
            {
                var reference = cell.Attribute("r")?.Value ?? string.Empty;
                var column = ColumnIndex(reference);
                var type = cell.Attribute("t")?.Value;
                var raw = cell.Descendants().FirstOrDefault(element => element.Name.LocalName == "v")?.Value
                    ?? string.Concat(cell.Descendants().Where(element => element.Name.LocalName == "t").Select(element => element.Value));
                if (type == "s" && int.TryParse(raw, out var sharedIndex)
                    && sharedIndex >= 0 && sharedIndex < sharedStrings.Count) raw = sharedStrings[sharedIndex];
                values[column] = raw;
            }
            if (values.Count == 0) continue;
            var row = Enumerable.Repeat(string.Empty, values.Keys.Max() + 1).ToList();
            foreach (var (index, value) in values) row[index] = value;
            table.Add(row);
        }
        return MapTable(table);
    }

    private static int ColumnIndex(string reference)
    {
        var index = 0;
        foreach (var character in reference.TakeWhile(char.IsLetter))
        {
            index = index * 26 + (char.ToUpperInvariant(character) - 'A' + 1);
        }
        return Math.Max(index - 1, 0);
    }

    private static List<Dictionary<string, string>> MapTable(List<List<string>> table)
    {
        if (table.Count == 0) return [];
        var headers = table[0].Select(NormalizeHeader).ToArray();
        var rows = new List<Dictionary<string, string>>();
        foreach (var source in table.Skip(1))
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(headers[index])) continue;
                row[headers[index]] = index < source.Count ? source[index].Trim() : string.Empty;
            }
            if (row.Values.Any(value => !string.IsNullOrWhiteSpace(value))) rows.Add(row);
        }
        return rows;
    }

    private static string NormalizeHeader(string value) =>
        new(value.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray());

    private static ImportPreview BuildImportPreview(List<Dictionary<string, string>> rows)
    {
        var valid = new List<object>();
        var warnings = new List<object>();
        var pins = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            row.TryGetValue("name", out var name);
            if (string.IsNullOrWhiteSpace(name)) row.TryGetValue("customer_name", out name);
            row.TryGetValue("pin", out var pinText);
            row.TryGetValue("id", out var id);
            if (string.IsNullOrWhiteSpace(id)) row.TryGetValue("customer_id", out id);
            var pin = NormalizePin(pinText);
            if (string.IsNullOrWhiteSpace(name) || pin is null)
            {
                warnings.Add(new { row = index + 2, code = "invalid_name_or_pin" });
                continue;
            }
            if (!pins.Add(pin))
            {
                warnings.Add(new { row = index + 2, code = "duplicate_pin_in_file", pin });
                continue;
            }
            valid.Add(new { id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id.Trim(), name = name.Trim(), pin });
        }
        return new(valid, warnings);
    }

    private static async Task<AccessOutcome> ResolveAccessAsync(HttpContext context, bool requireManage)
    {
        var effectiveUserId = SessionUserId(context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId");
        var actualUserId = SessionUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        if (effectiveUserId is null || actualUserId is null)
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
                SELECT upper(COALESCE(r.role_code, ''))
                FROM app_user_role_assignments ura
                JOIN app_roles r
                  ON r.app_role_id = ura.app_role_id
                 AND r.is_active = TRUE
                WHERE ura.user_id = @user_id
                  AND ura.is_active = TRUE;
                """, connection);
            command.Parameters.AddWithValue("user_id", actualUserId.Value);
            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) roles.Add(reader.GetString(0));
            var canManage = roles.Overlaps(new[]
            {
                "MANAGER",
                "ADMINISTRATOR",
                "SUPER_ADMINISTRATOR",
                "PROJECT_TEAM_COORDINATOR"
            });
            if (requireManage && !canManage)
            {
                return new(null, Results.Json(new
                {
                    module = ModuleNumber,
                    status = "oneassist_manage_permission_required",
                    permission = ManagePermission,
                    message = "Only Super Administrators, Administrators, Managers, and Project Team Coordinators can edit OneAssist routing PINs."
                }, statusCode: StatusCodes.Status403Forbidden));
            }
            return new(new AccessContext(actualUserId.Value, effectiveUserId.Value, roles, canManage), null);
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "authorize OneAssist access");
            return new(null, AuthorizationUnavailable());
        }
    }

    private static object AccessResponse(AccessContext access, HttpContext context) => new
    {
        actualUserId = access.ActualUserId,
        effectiveUserId = access.EffectiveUserId,
        roles = access.Roles.OrderBy(value => value),
        canView = true,
        canManage = access.CanManage,
        managePermission = ManagePermission,
        isViewAs = IsViewAs(context),
        authoritySource = "actual ProjectPulse session"
    };


    private static async Task<UpstreamOutcome> ReadUpstreamAsync(HttpContext context)
    {
        var outcome = await Module071072NativePersistence.ReadOneAssistRoutesAsync(context);
        return new(outcome.Payload, outcome.Failure);
    }

    private static async Task<UpstreamOutcome> SendUpstreamAsync(
        HttpMethod method,
        string path,
        JsonNode? payload,
        bool includeAdminHeaders,
        HttpContext context)
    {
        _ = method;
        _ = includeAdminHeaders;
        if (path != "/api/admin/ps-customers/save" || payload is null)
        {
            return new(null, Results.NotFound(new { module = ModuleNumber, status = "native_persistence_operation_not_supported", path }));
        }

        var actorUserId = SessionUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        if (actorUserId is null)
        {
            return new(null, Results.Json(new { module = ModuleNumber, status = "session_required" }, statusCode: StatusCodes.Status401Unauthorized));
        }
        if (IsViewAs(context))
        {
            return new(null, Results.Json(new
            {
                module = ModuleNumber,
                status = "actual_session_required",
                message = "Exit Administrator View-As preview before saving Module 072 changes."
            }, statusCode: StatusCodes.Status403Forbidden));
        }

        var outcome = await Module071072NativePersistence.SaveOneAssistRoutesAsync(payload, actorUserId.Value, context);
        return new(outcome.Payload, outcome.Failure);
    }

    private static string? NodeText(JsonNode? node)
    {
        if (node is null) return null;
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text;
        if (node is JsonValue number && number.TryGetValue<long>(out var integer))
        {
            return integer.ToString(CultureInfo.InvariantCulture);
        }
        return null;
    }

    private static async Task<JsonOutcome> ReadJsonBodyAsync(HttpContext context)
    {
        try
        {
            var payload = await JsonNode.ParseAsync(context.Request.Body);
            return payload is null
                ? new(null, Results.BadRequest(new { module = ModuleNumber, status = "json_body_required" }))
                : new(payload, null);
        }
        catch (JsonException)
        {
            return new(null, Results.BadRequest(new
            {
                module = ModuleNumber,
                status = "invalid_json",
                message = "A valid JSON request body is required."
            }));
        }
    }


    private static object PersistenceStatus() => new
    {
        mode = "projectpulse_postgresql",
        configured = !string.IsNullOrWhiteSpace(BuildConnectionString()),
        databaseSchemaIntroduced = true,
        migration = "031_modules_071_072_native_persistence.sql",
        externalCompatibilityDependency = false,
        activation = "migration 031 and current API deployment"
    };

    private static void SetPublicHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "public, max-age=60";
        context.Response.Headers.AccessControlAllowOrigin = "*";
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    }

    private static Guid? SessionUserId(HttpContext context, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid userId) return userId;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static bool IsViewAs(HttpContext context) =>
        context.Items.TryGetValue("ProjectPulseIsViewAs", out var value)
        && value is bool isViewAs
        && isViewAs;

    private static IResult AuthorizationUnavailable() => Results.Json(new
    {
        module = ModuleNumber,
        status = "authorization_dependency_unavailable",
        message = "OneAssist authorization is temporarily unavailable."
    }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult DependencyUnavailable(string message) => Results.Json(new
    {
        module = ModuleNumber,
        status = "dependency_unavailable",
        message
    }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static void LogFailure(HttpContext context, Exception exception, string operation)
    {
        context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("OneAssistRoutingDirectoryModule")
            .LogWarning(exception, "Module 072 could not {Operation}.", operation);
    }

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

    private sealed record AccessOutcome(AccessContext? Context, IResult? Failure);
    private sealed record AccessContext(
        Guid ActualUserId,
        Guid EffectiveUserId,
        IReadOnlySet<string> Roles,
        bool CanManage);
    private sealed record UpstreamOutcome(JsonNode? Payload, IResult? Failure);
    private sealed record JsonOutcome(JsonNode? Payload, IResult? Failure);
    private sealed record ImportPreview(List<object> Valid, List<object> Warnings);
}
