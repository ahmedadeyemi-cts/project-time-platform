using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 074 provides a validated, unsaved OEM/vendor directory draft while
/// persistence remains separately governed.
/// </summary>
public static class OemVendorDirectoryModule
{
    private const string ModuleNumber = "074";
    private const string ContractVersion = "2026-07-19.1";
    private const string ImplementationBaseline = "2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4";
    private static readonly HashSet<string> ManageRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "SOLUTION_ARCHITECT", "PROJECT_TEAM_COORDINATOR"
    };
    private static readonly HashSet<string> Statuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "preferred", "limited", "inactive", "under_review"
    };

    public static WebApplication MapOemVendorDirectoryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/oem-vendor-directory/capabilities", (Func<HttpContext, Task<IResult>>)GetCapabilitiesAsync);
        app.MapGet("/api/oem-vendor-directory/directory", (Func<HttpContext, Task<IResult>>)GetDirectoryAsync);
        app.MapGet("/api/oem-vendor-directory/reference", (Func<HttpContext, Task<IResult>>)GetReferenceAsync);
        app.MapPost("/api/oem-vendor-directory/validate", (Func<HttpContext, Task<IResult>>)ValidateDraftAsync);
        return app;
    }

    private static async Task<IResult> GetCapabilitiesAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, false);
        if (access.Failure is not null) return access.Failure;
        return Results.Ok(new
        {
            module = ModuleNumber,
            moduleName = "OEM & Vendor Directory",
            status = "capabilities_loaded",
            contractVersion = ContractVersion,
            implementationBaseline = ImplementationBaseline,
            access = AccessResponse(access.Context!, context),
            fields = new[] { "vendorName", "oemCategory", "contacts", "supportLinks", "certifications", "products", "status" },
            persistence = new { mode = "validated_unsaved_draft", enabled = false, databaseAuthorizationRequired = true }
        });
    }

    private static async Task<IResult> GetDirectoryAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, false);
        if (access.Failure is not null) return access.Failure;
        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "directory_source_not_configured",
            vendors = Array.Empty<object>(),
            count = 0,
            persistencePerformed = false,
            message = "No canonical vendor persistence source is authorized. Build and export a validated draft without changing external state."
        });
    }

    private static async Task<IResult> GetReferenceAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, false);
        if (access.Failure is not null) return access.Failure;
        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "reference_loaded",
            statuses = Statuses.OrderBy(value => value),
            suggestedCategories = new[]
            {
                "Cloud", "Collaboration", "Data Center", "Networking", "Security", "Storage", "Services", "Other"
            },
            supportLinkSchemes = new[] { "https" }
        });
    }

    private static async Task<IResult> ValidateDraftAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, true);
        if (access.Failure is not null) return access.Failure;
        JsonNode? payload;
        try { payload = await JsonNode.ParseAsync(context.Request.Body); }
        catch (JsonException) { return Results.BadRequest(new { module = ModuleNumber, status = "invalid_json" }); }
        var vendors = payload?["vendors"] as JsonArray ?? payload as JsonArray;
        if (vendors is null) return Results.BadRequest(new { module = ModuleNumber, status = "vendors_required" });
        if (vendors.Count > 500) return Results.BadRequest(new { module = ModuleNumber, status = "vendor_limit_exceeded", maximum = 500 });

        var normalized = new JsonArray();
        var errors = new List<object>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < vendors.Count; index++)
        {
            if (vendors[index] is not JsonObject vendor) { errors.Add(Error(index, "object_required")); continue; }
            var name = Text(vendor, "vendorName");
            var category = Text(vendor, "oemCategory");
            var status = Text(vendor, "status")?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(name)) { errors.Add(Error(index, "vendor_name_required")); continue; }
            if (!names.Add(name)) { errors.Add(Error(index, "duplicate_vendor_name")); continue; }
            if (string.IsNullOrWhiteSpace(category)) { errors.Add(Error(index, "oem_category_required")); continue; }
            if (status is null || !Statuses.Contains(status)) { errors.Add(Error(index, "invalid_status")); continue; }
            var websiteValue = Text(vendor, "website");
            var website = HttpsOrNull(websiteValue);
            if (!string.IsNullOrWhiteSpace(websiteValue) && website is null)
            {
                errors.Add(Error(index, "website_must_use_https"));
                continue;
            }

            var contacts = NormalizeContacts(vendor["contacts"] as JsonArray, index, errors);
            var links = NormalizeLinks(vendor["supportLinks"] as JsonArray, index, errors);
            var certifications = NormalizeNamedItems(vendor["certifications"] as JsonArray, "certificationName");
            var products = NormalizeNamedItems(vendor["products"] as JsonArray, "productName");
            normalized.Add(new JsonObject
            {
                ["id"] = Text(vendor, "id") ?? Guid.NewGuid().ToString(),
                ["vendorName"] = name,
                ["oemCategory"] = category,
                ["status"] = status,
                ["website"] = website,
                ["contacts"] = contacts,
                ["supportLinks"] = links,
                ["certifications"] = certifications,
                ["products"] = products,
                ["notes"] = Text(vendor, "notes") ?? string.Empty
            });
        }

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = errors.Count == 0 ? "draft_valid" : "draft_has_errors",
            valid = errors.Count == 0,
            validCount = normalized.Count,
            errorCount = errors.Count,
            vendors = normalized,
            errors,
            persistencePerformed = false
        });
    }

    private static JsonArray NormalizeContacts(JsonArray? source, int vendorIndex, List<object> errors)
    {
        var contacts = new JsonArray();
        if (source is null) return contacts;
        foreach (var (node, index) in source.Select((value, index) => (value, index)))
        {
            if (node is not JsonObject contact) { errors.Add(Error(vendorIndex, "contact_object_required", index)); continue; }
            var name = Text(contact, "name"); var role = Text(contact, "role"); var email = Text(contact, "email");
            if (string.IsNullOrWhiteSpace(name)) { errors.Add(Error(vendorIndex, "contact_name_required", index)); continue; }
            if (!string.IsNullOrWhiteSpace(email) && !ValidEmail(email)) { errors.Add(Error(vendorIndex, "invalid_contact_email", index)); continue; }
            contacts.Add(new JsonObject { ["name"] = name, ["role"] = role ?? string.Empty, ["email"] = email ?? string.Empty, ["phone"] = Text(contact, "phone") ?? string.Empty });
        }
        return contacts;
    }

    private static JsonArray NormalizeLinks(JsonArray? source, int vendorIndex, List<object> errors)
    {
        var links = new JsonArray();
        if (source is null) return links;
        foreach (var (node, index) in source.Select((value, index) => (value, index)))
        {
            if (node is not JsonObject link) { errors.Add(Error(vendorIndex, "support_link_object_required", index)); continue; }
            var label = Text(link, "label"); var url = HttpsOrNull(Text(link, "url"));
            if (string.IsNullOrWhiteSpace(label) || url is null) { errors.Add(Error(vendorIndex, "support_link_requires_label_and_https_url", index)); continue; }
            links.Add(new JsonObject { ["label"] = label, ["url"] = url });
        }
        return links;
    }

    private static JsonArray NormalizeNamedItems(JsonArray? source, string key)
    {
        var items = new JsonArray(); if (source is null) return items;
        foreach (var node in source)
        {
            if (node is JsonObject item && !string.IsNullOrWhiteSpace(Text(item, key))) items.Add(item.DeepClone());
        }
        return items;
    }

    private static object Error(int vendorIndex, string code, int? itemIndex = null) => new { vendorRow = vendorIndex + 1, itemRow = itemIndex is null ? null : itemIndex + 1, code };
    private static string? Text(JsonObject value, string key) =>
        value[key] is JsonValue node && node.TryGetValue<string>(out var result)
            ? result.Trim()
            : null;
    private static string? HttpsOrNull(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? uri.AbsoluteUri : null;
    private static bool ValidEmail(string value) { try { return string.Equals(new MailAddress(value).Address, value, StringComparison.OrdinalIgnoreCase); } catch { return false; } }

    private static async Task<AccessOutcome> ResolveAccessAsync(HttpContext context, bool requireManage)
    {
        var actual=SessionUserId(context,"ProjectPulseActualUserId","ProjectPulseSessionUserId");var effective=SessionUserId(context,"ProjectPulseEffectiveUserId","ProjectPulseSessionUserId");
        if(actual is null||effective is null)return new(null,Results.Json(new{module=ModuleNumber,status="session_required"},statusCode:401));
        var connectionString=BuildConnectionString();if(string.IsNullOrWhiteSpace(connectionString))return new(null,DependencyUnavailable());
        try{await using var connection=new NpgsqlConnection(connectionString);await connection.OpenAsync();await using var command=new NpgsqlCommand("""
            SELECT upper(r.role_code) FROM app_user_role_assignments ura
            JOIN app_roles r ON r.app_role_id=ura.app_role_id AND r.is_active=TRUE
            WHERE ura.user_id=@user_id AND ura.is_active=TRUE;
            """,connection);command.Parameters.AddWithValue("user_id",actual.Value);var roles=new HashSet<string>(StringComparer.OrdinalIgnoreCase);await using var reader=await command.ExecuteReaderAsync();while(await reader.ReadAsync())roles.Add(reader.GetString(0));var canManage=roles.Overlaps(ManageRoles);if(requireManage&&!canManage)return new(null,Results.Json(new{module=ModuleNumber,status="vendor_directory_manage_permission_required",message="Administrators, Solution Architects, or Project Team Coordinators are required."},statusCode:403));return new(new(actual.Value,effective.Value,roles,canManage),null);}catch(Exception exception){context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("OemVendorDirectoryModule").LogWarning(exception,"Module 074 authorization failed.");return new(null,DependencyUnavailable());}
    }
    private static object AccessResponse(AccessContext access,HttpContext context)=>new{actualUserId=access.ActualUserId,effectiveUserId=access.EffectiveUserId,roles=access.Roles.OrderBy(value=>value),canView=true,canManage=access.CanManage,manageRoles=ManageRoles.OrderBy(value=>value),isViewAs=IsViewAs(context),authoritySource="actual ProjectPulse session"};
    private static Guid? SessionUserId(HttpContext context,params string[] keys){foreach(var key in keys){if(!context.Items.TryGetValue(key,out var value))continue;if(value is Guid id)return id;if(Guid.TryParse(value?.ToString(),out var parsed))return parsed;}return null;}private static bool IsViewAs(HttpContext context)=>context.Items.TryGetValue("ProjectPulseIsViewAs",out var value)&&value is bool flag&&flag;
    private static IResult DependencyUnavailable()=>Results.Json(new{module=ModuleNumber,status="dependency_unavailable",message="Vendor-directory authorization is temporarily unavailable."},statusCode:503);
    private static string? BuildConnectionString(){foreach(var name in new[]{"ConnectionStrings__DefaultConnection","ConnectionStrings__ProjectPulse","ConnectionStrings__ProjectTime","PROJECTPULSE_CONNECTION_STRING","PROJECTTIME_DATABASE_CONNECTION"}){var value=Environment.GetEnvironmentVariable(name);if(!string.IsNullOrWhiteSpace(value))return value;}var host=Environment.GetEnvironmentVariable("PTP_DB_HOST");var database=Environment.GetEnvironmentVariable("PTP_DB_NAME");var username=Environment.GetEnvironmentVariable("PTP_DB_USER");var password=Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");if(new[]{host,database,username,password}.Any(string.IsNullOrWhiteSpace))return null;return new NpgsqlConnectionStringBuilder{Host=host,Port=int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"),out var port)?port:5432,Database=database,Username=username,Password=password,IncludeErrorDetail=false,Pooling=true,MaxPoolSize=5}.ConnectionString;}
    private sealed record AccessOutcome(AccessContext? Context,IResult? Failure);private sealed record AccessContext(Guid ActualUserId,Guid EffectiveUserId,IReadOnlySet<string> Roles,bool CanManage);
}
