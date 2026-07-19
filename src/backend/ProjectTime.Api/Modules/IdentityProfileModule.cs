using System.Net.Http.Headers;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static class IdentityProfileModule
{
    private static readonly HashSet<string> MicrosoftDomains =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "onenecklab.com",
            "ussignal.com"
        };

    private static readonly HashSet<string> LocalDomains =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "ussignal.local",
            "ussignal.cloud"
        };

    public static WebApplication MapIdentityProfileEndpoints(
        this WebApplication app)
    {
        app.MapGet(
            "/api/identity/profile",
            async Task<IResult> (HttpContext context) =>
            {
                var effectiveUserId = SessionUserId(context);

                if (effectiveUserId is null)
                {
                    return Results.Json(
                        new
                        {
                            module = "062",
                            status = "session_required",
                            message =
                                "A ProjectPulse session is required."
                        },
                        statusCode: 401);
                }

                await using var connection =
                    new NpgsqlConnection(ConnectionString());

                await connection.OpenAsync();

                var local = await LoadLocalProfile(
                    connection,
                    effectiveUserId.Value);

                if (local is null)
                {
                    return Results.Json(
                        new
                        {
                            module = "062",
                            status = "identity_profile_not_found",
                            message =
                                "The authenticated ProjectPulse profile "
                                + "could not be resolved."
                        },
                        statusCode: 404);
                }

                var domain = DomainOf(local.Email);
                var isMicrosoftIdentity =
                    MicrosoftDomains.Contains(domain);

                var authenticationProvider =
                    AuthenticationProvider(
                        domain,
                        local.SourceProvider);

                var directoryProvider =
                    isMicrosoftIdentity
                        ? domain.Equals(
                            "onenecklab.com",
                            StringComparison.OrdinalIgnoreCase)
                            ? "microsoft_graph_test"
                            : "microsoft_graph_production"
                        : "projectpulse_local";

                var displayName = local.DisplayName;
                var email = local.Email;
                var jobTitle = local.JobTitle;
                var department = local.Department;
                var team = local.Team;
                var entraObjectId = local.EntraObjectId;
                var profilePhotoDataUrl =
                    local.ProfilePhotoDataUrl;

                var identitySource = "projectpulse_local";
                var graphStatus =
                    isMicrosoftIdentity
                        ? "graph_not_configured"
                        : "local_identity";

                var presence = new PresenceProfile(
                    Availability: "presenceUnknown",
                    Activity: "presenceUnknown",
                    Supported: false,
                    Status:
                        isMicrosoftIdentity
                            ? "graph_not_configured"
                            : "local_identity",
                    RetrievedAt: DateTimeOffset.UtcNow);

                if (isMicrosoftIdentity)
                {
                    var credentials =
                        GraphCredentials.ForDomain(domain);

                    if (credentials.IsConfigured)
                    {
                        try
                        {
                            var token =
                                await GetGraphToken(credentials);

                            var target =
                                !string.IsNullOrWhiteSpace(
                                    entraObjectId)
                                    ? entraObjectId
                                    : email;

                            var graphProfile =
                                await GetGraphProfile(
                                    token,
                                    target);

                            if (graphProfile is not null)
                            {
                                displayName =
                                    First(
                                        graphProfile.DisplayName,
                                        displayName);

                                email =
                                    First(
                                        graphProfile.Mail,
                                        graphProfile.UserPrincipalName,
                                        email);

                                jobTitle =
                                    First(
                                        graphProfile.JobTitle,
                                        jobTitle);

                                department =
                                    First(
                                        graphProfile.Department,
                                        department);

                                entraObjectId =
                                    First(
                                        graphProfile.Id,
                                        entraObjectId);

                                identitySource =
                                    "microsoft_graph";

                                graphStatus =
                                    "graph_profile_loaded";
                            }
                            else
                            {
                                graphStatus =
                                    "graph_profile_not_found";
                            }

                            var presenceTarget =
                                First(
                                    entraObjectId,
                                    target);

                            presence =
                                await GetGraphPresence(
                                    token,
                                    presenceTarget);

                            if (string.IsNullOrWhiteSpace(
                                    profilePhotoDataUrl)
                                || IsPhotoStale(
                                    local.ProfilePhotoUpdatedAt))
                            {
                                var photo =
                                    await GetGraphPhoto(
                                        token,
                                        presenceTarget);

                                if (!string.IsNullOrWhiteSpace(photo))
                                {
                                    profilePhotoDataUrl = photo;

                                    await CacheProfilePhoto(
                                        connection,
                                        local.UserId,
                                        photo);
                                }
                            }
                        }
                        catch
                        {
                            graphStatus =
                                "graph_temporarily_unavailable";

                            presence = presence with
                            {
                                Supported = true,
                                Status =
                                    "graph_temporarily_unavailable",
                                RetrievedAt =
                                    DateTimeOffset.UtcNow
                            };
                        }
                    }
                }

                var profile = new
                {
                    userId = local.UserId,
                    effectiveUserId = local.UserId,
                    entraObjectId,
                    email,
                    displayName,
                    jobTitle,
                    department,
                    team,
                    role = local.Role,
                    profilePhotoDataUrl,
                    domain,
                    identitySource,
                    authenticationProvider,
                    directoryProvider,
                    sourceProvider = local.SourceProvider,
                    isMicrosoftIdentity,
                    isLocalIdentity =
                        LocalDomains.Contains(domain)
                        || !isMicrosoftIdentity,
                    graphConfigured =
                        isMicrosoftIdentity
                        && GraphCredentials
                            .ForDomain(domain)
                            .IsConfigured,
                    graphStatus,
                    profileRetrievedAt =
                        DateTimeOffset.UtcNow,
                    presence = new
                    {
                        availability =
                            presence.Availability,
                        activity =
                            presence.Activity,
                        supported =
                            presence.Supported,
                        status =
                            presence.Status,
                        retrievedAt =
                            presence.RetrievedAt
                    }
                };

                return Results.Ok(new
                {
                    module = "062",
                    status = "identity_profile_loaded",
                    profile
                });
            });

        return app;
    }

    private static async Task<LocalProfile?> LoadLocalProfile(
        NpgsqlConnection connection,
        Guid userId)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT
                u.user_id,
                COALESCE(
                    NULLIF(to_jsonb(u)->>'email', ''),
                    ''
                ),
                COALESCE(
                    NULLIF(to_jsonb(u)->>'display_name', ''),
                    NULLIF(to_jsonb(u)->>'email', ''),
                    'ProjectPulse user'
                ),
                NULLIF(
                    to_jsonb(u)->>'entra_object_id',
                    ''
                ),
                COALESCE(
                    NULLIF(to_jsonb(u)->>'source_provider', ''),
                    'LOCAL_APP'
                ),
                COALESCE(
                    NULLIF(to_jsonb(u)->>'job_title', ''),
                    ''
                ),
                COALESCE(
                    NULLIF(to_jsonb(u)->>'department_name', ''),
                    NULLIF(to_jsonb(u)->>'department', ''),
                    ''
                ),
                COALESCE(
                    NULLIF(to_jsonb(u)->>'team_name', ''),
                    NULLIF(to_jsonb(u)->>'department_name', ''),
                    NULLIF(to_jsonb(u)->>'department', ''),
                    ''
                ),
                COALESCE(
                    NULLIF(
                        to_jsonb(u)->>'profile_photo_data_url',
                        ''
                    ),
                    ''
                ),
                NULLIF(
                    to_jsonb(u)->>'profile_photo_updated_at',
                    ''
                ),
                COALESCE(
                    NULLIF(to_jsonb(u)->>'role_name', ''),
                    ''
                )
            FROM app_users u
            WHERE u.user_id = @user_id
              AND COALESCE(u.is_active, TRUE) = TRUE
            LIMIT 1;
            """,
            connection);

        command.Parameters.AddWithValue(
            "user_id",
            userId);

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        DateTimeOffset? profilePhotoUpdatedAt = null;

        if (!reader.IsDBNull(9)
            && DateTimeOffset.TryParse(
                reader.GetString(9),
                out var parsedPhotoUpdatedAt))
        {
            profilePhotoUpdatedAt =
                parsedPhotoUpdatedAt;
        }

        return new LocalProfile(
            UserId: reader.GetGuid(0),
            Email: reader.GetString(1),
            DisplayName: reader.GetString(2),
            EntraObjectId:
                reader.IsDBNull(3)
                    ? null
                    : reader.GetString(3),
            SourceProvider: reader.GetString(4),
            JobTitle: reader.GetString(5),
            Department: reader.GetString(6),
            Team: reader.GetString(7),
            ProfilePhotoDataUrl: reader.GetString(8),
            ProfilePhotoUpdatedAt:
                profilePhotoUpdatedAt,
            Role: reader.GetString(10));
    }

    private static async Task<string> GetGraphToken(
        GraphCredentials credentials)
    {
        using var client = new HttpClient();

        using var content =
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["client_id"] =
                        credentials.ClientId,
                    ["client_secret"] =
                        credentials.ClientSecret,
                    ["scope"] =
                        "https://graph.microsoft.com/.default",
                    ["grant_type"] =
                        "client_credentials"
                });

        using var response =
            await client.PostAsync(
                "https://login.microsoftonline.com/"
                + Uri.EscapeDataString(
                    credentials.TenantId)
                + "/oauth2/v2.0/token",
                content);

        response.EnsureSuccessStatusCode();

        var raw =
            await response.Content.ReadAsStringAsync();

        using var document =
            JsonDocument.Parse(raw);

        return document.RootElement
            .GetProperty("access_token")
            .GetString()
            ?? throw new InvalidOperationException(
                "Microsoft Graph token was missing.");
    }

    private static async Task<GraphProfile?> GetGraphProfile(
        string token,
        string target)
    {
        using var client = GraphClient(token);

        var endpoint =
            "https://graph.microsoft.com/v1.0/users/"
            + Uri.EscapeDataString(target)
            + "?$select=id,displayName,mail,"
            + "userPrincipalName,jobTitle,department";

        using var response =
            await client.GetAsync(endpoint);

        if (response.StatusCode
            == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var raw =
            await response.Content.ReadAsStringAsync();

        using var document =
            JsonDocument.Parse(raw);

        var root = document.RootElement;

        return new GraphProfile(
            Id: String(root, "id"),
            DisplayName:
                String(root, "displayName"),
            Mail: String(root, "mail"),
            UserPrincipalName:
                String(root, "userPrincipalName"),
            JobTitle:
                String(root, "jobTitle"),
            Department:
                String(root, "department"));
    }

    private static async Task<PresenceProfile>
        GetGraphPresence(
            string token,
            string target)
    {
        using var client = GraphClient(token);

        using var response =
            await client.GetAsync(
                "https://graph.microsoft.com/v1.0/users/"
                + Uri.EscapeDataString(target)
                + "/presence");

        if (!response.IsSuccessStatusCode)
        {
            return new PresenceProfile(
                Availability: "presenceUnknown",
                Activity: "presenceUnknown",
                Supported: true,
                Status:
                    "graph_presence_http_"
                    + (int)response.StatusCode,
                RetrievedAt:
                    DateTimeOffset.UtcNow);
        }

        var raw =
            await response.Content.ReadAsStringAsync();

        using var document =
            JsonDocument.Parse(raw);

        return new PresenceProfile(
            Availability:
                First(
                    String(
                        document.RootElement,
                        "availability"),
                    "presenceUnknown"),
            Activity:
                First(
                    String(
                        document.RootElement,
                        "activity"),
                    "presenceUnknown"),
            Supported: true,
            Status: "graph_presence_loaded",
            RetrievedAt:
                DateTimeOffset.UtcNow);
    }

    private static async Task<string?> GetGraphPhoto(
        string token,
        string target)
    {
        using var client = GraphClient(token);

        using var response =
            await client.GetAsync(
                "https://graph.microsoft.com/v1.0/users/"
                + Uri.EscapeDataString(target)
                + "/photos/96x96/$value");

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var bytes =
            await response.Content.ReadAsByteArrayAsync();

        if (bytes.Length == 0
            || bytes.Length > 1_500_000)
        {
            return null;
        }

        var mediaType =
            response.Content.Headers.ContentType
                ?.MediaType;

        if (string.IsNullOrWhiteSpace(mediaType)
            || !mediaType.StartsWith(
                "image/",
                StringComparison.OrdinalIgnoreCase))
        {
            mediaType = "image/jpeg";
        }

        return
            $"data:{mediaType};base64,"
            + Convert.ToBase64String(bytes);
    }

    private static async Task CacheProfilePhoto(
        NpgsqlConnection connection,
        Guid userId,
        string photo)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE app_users
            SET profile_photo_data_url = @photo,
                profile_photo_updated_at = NOW()
            WHERE user_id = @user_id;
            """,
            connection);

        command.Parameters.AddWithValue(
            "photo",
            photo);

        command.Parameters.AddWithValue(
            "user_id",
            userId);

        await command.ExecuteNonQueryAsync();
    }

    private static HttpClient GraphClient(
        string token)
    {
        var client = new HttpClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token);

        return client;
    }

    private static bool IsPhotoStale(
        DateTimeOffset? updatedAt) =>
        updatedAt is null
        || updatedAt
            < DateTimeOffset.UtcNow.AddDays(-7);

    private static Guid? SessionUserId(
        HttpContext context)
    {
        foreach (var key in new[]
        {
            "ProjectPulseEffectiveUserId",
            "ProjectPulseSessionUserId",
            "ProjectPulseActualUserId"
        })
        {
            if (!context.Items.TryGetValue(
                    key,
                    out var value))
            {
                continue;
            }

            if (value is Guid guid)
            {
                return guid;
            }

            if (Guid.TryParse(
                    value?.ToString(),
                    out var parsed))
            {
                return parsed;
            }
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
            var value =
                Environment.GetEnvironmentVariable(name);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        throw new InvalidOperationException(
            "ProjectPulse database connection "
            + "is not configured.");
    }

    private static string DomainOf(
        string email)
    {
        var separator =
            email.LastIndexOf('@');

        if (separator < 0
            || separator == email.Length - 1)
        {
            return string.Empty;
        }

        return email[(separator + 1)..]
            .Trim()
            .ToLowerInvariant();
    }

    private static string AuthenticationProvider(
        string domain,
        string sourceProvider)
    {
        if (domain.Equals(
                "onenecklab.com",
                StringComparison.OrdinalIgnoreCase))
        {
            return "microsoft_entra_test";
        }

        if (domain.Equals(
                "ussignal.com",
                StringComparison.OrdinalIgnoreCase))
        {
            return "microsoft_entra_production";
        }

        if (LocalDomains.Contains(domain))
        {
            return "projectpulse_local";
        }

        return sourceProvider
            .StartsWith(
                "ENTRA",
                StringComparison.OrdinalIgnoreCase)
            ? "microsoft_entra"
            : "projectpulse_local";
    }

    private static string First(
        params string?[] values) =>
        values.FirstOrDefault(
            value =>
                !string.IsNullOrWhiteSpace(value))
        ?.Trim()
        ?? string.Empty;

    private static string? String(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var property)
            || property.ValueKind
                == JsonValueKind.Null)
        {
            return null;
        }

        return property.GetString();
    }

    private sealed record LocalProfile(
        Guid UserId,
        string Email,
        string DisplayName,
        string? EntraObjectId,
        string SourceProvider,
        string JobTitle,
        string Department,
        string Team,
        string ProfilePhotoDataUrl,
        DateTimeOffset? ProfilePhotoUpdatedAt,
        string Role);

    private sealed record GraphProfile(
        string? Id,
        string? DisplayName,
        string? Mail,
        string? UserPrincipalName,
        string? JobTitle,
        string? Department);

    private sealed record PresenceProfile(
        string Availability,
        string Activity,
        bool Supported,
        string Status,
        DateTimeOffset RetrievedAt);

    private sealed record GraphCredentials(
        string TenantId,
        string ClientId,
        string ClientSecret)
    {
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(TenantId)
            && !string.IsNullOrWhiteSpace(ClientId)
            && !string.IsNullOrWhiteSpace(ClientSecret);

        public static GraphCredentials ForDomain(
            string domain)
        {
            var mode = FirstEnvironment(
                    "PROJECTPULSE_ENTRA_MODE")
                .Trim()
                .ToLowerInvariant();

            var allowGenericTest =
                string.IsNullOrWhiteSpace(mode)
                || mode is "development"
                    or "dev"
                    or "test"
                    or "onenecklab";

            var allowGenericProduction =
                mode is "production"
                    or "prod"
                    or "ussignal";

            if (domain.Equals(
                    "onenecklab.com",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new GraphCredentials(
                    ExplicitOrGeneric(
                        "PROJECTPULSE_ENTRA_TEST_TENANT_ID",
                        "PROJECTPULSE_ENTRA_TENANT_ID",
                        allowGenericTest),
                    ExplicitOrGeneric(
                        "PROJECTPULSE_ENTRA_TEST_CLIENT_ID",
                        "PROJECTPULSE_ENTRA_CLIENT_ID",
                        allowGenericTest),
                    ExplicitOrGeneric(
                        "PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET",
                        "PROJECTPULSE_ENTRA_CLIENT_SECRET",
                        allowGenericTest));
            }

            if (domain.Equals(
                    "ussignal.com",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new GraphCredentials(
                    ExplicitOrGeneric(
                        "PROJECTPULSE_ENTRA_PRODUCTION_TENANT_ID",
                        "PROJECTPULSE_ENTRA_TENANT_ID",
                        allowGenericProduction),
                    ExplicitOrGeneric(
                        "PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_ID",
                        "PROJECTPULSE_ENTRA_CLIENT_ID",
                        allowGenericProduction),
                    ExplicitOrGeneric(
                        "PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_SECRET",
                        "PROJECTPULSE_ENTRA_CLIENT_SECRET",
                        allowGenericProduction));
            }

            return new GraphCredentials(
                string.Empty,
                string.Empty,
                string.Empty);
        }

        private static string ExplicitOrGeneric(
            string explicitName,
            string genericName,
            bool allowGeneric)
        {
            var explicitValue =
                FirstEnvironment(explicitName);

            if (!string.IsNullOrWhiteSpace(
                    explicitValue))
            {
                return explicitValue;
            }

            return allowGeneric
                ? FirstEnvironment(genericName)
                : string.Empty;
        }

        private static string FirstEnvironment(
            params string[] names)
        {
            foreach (var name in names)
            {
                var value =
                    Environment.GetEnvironmentVariable(name);

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }
    }
}
