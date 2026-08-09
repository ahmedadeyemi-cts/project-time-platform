using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;

namespace ProjectTime.Api.Modules;

public static partial class PlatformOperationsModule
{
    private static async Task<IResult> GetArchitectureAsync(HttpContext context)
    {
        var authorization = await AuthorizeAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;
        await using var connection = authorization.Connection!;

        var snapshot = await BuildSnapshotAsync(context, connection);
        var apis = BuildApiInventory(context);
        var architecture = BuildArchitecture(snapshot, apis);

        return Results.Ok(new
        {
            module = "068",
            status = "provider_neutral_architecture_loaded",
            contractVersion = ContractVersion,
            generatedAt = DateTimeOffset.UtcNow,
            access = AccessContract(context),
            platform = snapshot.Platform,
            runtime = snapshot.Runtime,
            layers = architecture.Layers,
            nodes = architecture.Nodes,
            connections = architecture.Connections,
            trustBoundaries = architecture.TrustBoundaries,
            legend = architecture.Legend,
            externalDataFlows = architecture.ExternalDataFlows,
            moduleApiRelationships = architecture.ModuleApiRelationships,
            regions = architecture.Regions,
            redundancy = architecture.Redundancy,
            apiAppendix = apis,
            export = new
            {
                html = "/api/platform-operations/architecture/export",
                branded = true,
                footer = "Created by Ahmed Adeyemi"
            },
            security = SecurityContract()
        });
    }

    private static async Task<IResult> ExportArchitectureAsync(HttpContext context)
    {
        var authorization = await AuthorizeAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;
        await using var connection = authorization.Connection!;

        var snapshot = await BuildSnapshotAsync(context, connection);
        var apis = BuildApiInventory(context);
        var architecture = BuildArchitecture(snapshot, apis);
        var logo = EmbeddedLogoDataUrl();
        var html = ArchitectureHtml(snapshot, architecture, apis, logo);

        return Results.File(
            Encoding.UTF8.GetBytes(html),
            "text/html; charset=utf-8",
            $"projectpulse-system-architecture-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.html");
    }

    private static ArchitectureContract BuildArchitecture(
        PlatformSnapshot snapshot,
        List<ApiInventoryItem> apis)
    {
        var nodes = new List<ArchitectureNode>
        {
            new(
                "browser",
                "Browser and Pulse web application",
                "experience",
                "client",
                "Permission-aware React application used by authenticated users."),
            new(
                "web",
                "Web delivery service",
                "delivery",
                "web",
                "Serves static assets and forwards protected API requests."),
            new(
                "api",
                "Pulse API",
                "application",
                "api",
                "Hosts module routes, authorization, workflows, diagnostics, and provider adapters."),
            new(
                "database",
                "Pulse database",
                "data",
                "database",
                "Canonical business records, security policy, workflow state, and audit evidence."),
            new(
                "storage",
                "Governed document and artifact storage",
                "data",
                "storage",
                "Project documents, approved exports, backup evidence, and branded outputs."),
            new(
                "platform",
                snapshot.Platform.DisplayName,
                "platform",
                "provider",
                $"{snapshot.Platform.Adapter} adapter for {snapshot.Platform.WorkloadKind}."),
            new(
                "github",
                "GitHub source and deployment controls",
                "delivery",
                "delivery",
                "Source review, validation workflows, immutable release controls, and rollback evidence.")
        };

        foreach (var integration in snapshot.Integrations)
        {
            nodes.Add(new ArchitectureNode(
                $"integration-{SafeId(integration.Key)}",
                integration.Name,
                "integration",
                "external_integration",
                $"{integration.Owner} · {integration.Status}"));
        }

        var connections = new List<ArchitectureConnection>
        {
            new(
                "browser",
                "web",
                "HTTPS",
                "Application assets and navigation",
                "public_then_authenticated"),
            new(
                "browser",
                "api",
                "HTTPS/JSON",
                "Authorized module requests",
                "authenticated"),
            new(
                "api",
                "database",
                "PostgreSQL",
                "Business data and authorization evidence",
                "restricted"),
            new(
                "api",
                "storage",
                "Provider-neutral storage adapter",
                "Documents and governed artifacts",
                "restricted"),
            new(
                "platform",
                "web",
                "Compute/network",
                "Web workload hosting",
                "operational"),
            new(
                "platform",
                "api",
                "Compute/network",
                "API workload hosting",
                "operational"),
            new(
                "github",
                "platform",
                "OCI/release adapter",
                "Validated deployment promotion and rollback",
                "controlled")
        };

        foreach (var integration in snapshot.Integrations)
        {
            connections.Add(new ArchitectureConnection(
                "api",
                $"integration-{SafeId(integration.Key)}",
                "Approved provider contract",
                string.Join(", ", integration.Capabilities),
                "restricted"));
        }

        var moduleRelationships = apis
            .GroupBy(api => new
            {
                api.ModuleCode,
                api.ModuleName
            })
            .OrderBy(group => group.Key.ModuleCode)
            .Select(group => new ModuleApiRelationship(
                group.Key.ModuleCode,
                group.Key.ModuleName,
                group.Count(),
                group.Select(api => new ApiRelationship(
                    api.ApiId,
                    api.Method,
                    api.Path,
                    api.Purpose)).ToArray()))
            .ToArray();

        return new ArchitectureContract(
            [
                new("experience", "Experience", 1),
                new("delivery", "Delivery", 2),
                new("application", "Application services", 3),
                new("data", "Data and evidence", 4),
                new("integration", "External integrations", 5),
                new("platform", "Hosting platform", 6)
            ],
            nodes.ToArray(),
            connections.ToArray(),
            [
                new(
                    "browser",
                    "Browser trust boundary",
                    "No provider secrets or privileged View-As authority are returned."),
                new(
                    "api",
                    "API authorization boundary",
                    "Protected endpoints require a valid session and server authorization."),
                new(
                    "data",
                    "Data boundary",
                    "Business data and evidence remain behind backend authorization."),
                new(
                    "provider",
                    "Provider boundary",
                    "Provider-specific details remain behind a generic adapter contract.")
            ],
            [
                new("healthy", "Verified by a current safe check"),
                new("configured", "Configuration is present; no live probe was executed"),
                new("not_configured", "The adapter or probe has not been configured"),
                new("not_observed", "No runtime request evidence has been recorded")
            ],
            snapshot.Integrations
                .Select(integration => new ExternalDataFlow(
                    integration.Name,
                    "Pulse API",
                    string.Join(", ", integration.Capabilities),
                    integration.Status,
                    integration.Owner))
                .ToArray(),
            moduleRelationships,
            [
                new RegionEntry(
                    snapshot.Platform.Region,
                    snapshot.Platform.Provider,
                    snapshot.Platform.Environment)
            ],
            new RedundancyContract(
                snapshot.Replicas.Length,
                snapshot.Replicas.Length > 1
                    ? "multiple_instances_observed"
                    : "single_instance_or_not_reported",
                snapshot.Replicas,
                "Redundancy is reported only from current adapter evidence. No future topology is represented as active."));
    }

    private static string EmbeddedLogoDataUrl()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(
                "ProjectTime.Api.Assets.Branding.USSNavyStacked.png");
            if (stream is null) return string.Empty;

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return $"data:image/png;base64,{Convert.ToBase64String(memory.ToArray())}";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ArchitectureHtml(
        PlatformSnapshot snapshot,
        ArchitectureContract architecture,
        List<ApiInventoryItem> apis,
        string logo)
    {
        static string H(string? value) =>
            HtmlEncoder.Default.Encode(value ?? string.Empty);

        var generated = DateTimeOffset.UtcNow;
        var nodeRows = string.Join(
            string.Empty,
            architecture.Nodes.Select(node =>
                $"<tr><td>{H(node.Layer)}</td><td>{H(node.Name)}</td><td>{H(node.Kind)}</td><td>{H(node.Description)}</td></tr>"));
        var connectionRows = string.Join(
            string.Empty,
            architecture.Connections.Select(connection =>
                $"<tr><td>{H(connection.From)}</td><td>{H(connection.To)}</td><td>{H(connection.Protocol)}</td><td>{H(connection.Data)}</td><td>{H(connection.Classification)}</td></tr>"));
        var apiRows = string.Join(
            string.Empty,
            apis.Select(api =>
                $"<tr><td>{H(api.ModuleCode)}</td><td>{H(api.Method)}</td><td><code>{H(api.Path)}</code></td><td>{H(api.Purpose)}</td><td>{H(api.AuthenticationRequirement)}</td><td>{H(api.PermissionRequirement)}</td><td>{H(api.CurrentStatus)}</td></tr>"));
        var legendRows = string.Join(
            string.Empty,
            architecture.Legend.Select(item =>
                $"<span><strong>{H(item.Code)}</strong> — {H(item.Description)}</span>"));
        var logoHtml = string.IsNullOrWhiteSpace(logo)
            ? "<div class=\"logo-fallback\">US SIGNAL</div>"
            : $"<img class=\"logo\" src=\"{logo}\" alt=\"US Signal\" />";

        return $$"""
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8" />
<title>Pulse System Architecture</title>
<style>
@page { size: landscape; margin: 0.45in; }
* { box-sizing: border-box; }
body { font-family: Arial, sans-serif; color: #17233d; margin: 0; padding-bottom: 28px; }
header { display:flex; justify-content:space-between; gap:24px; border-bottom:4px solid #0b5f9e; padding-bottom:16px; }
.logo { width:150px; height:auto; object-fit:contain; }
.logo-fallback { font-weight:900; color:#0b5f9e; }
h1 { margin:0 0 6px; font-size:28px; }
h2 { margin:24px 0 8px; border-bottom:1px solid #b9cbe0; padding-bottom:5px; }
.meta { display:grid; grid-template-columns:repeat(4, minmax(0, 1fr)); gap:10px; margin:16px 0; }
.meta div { border:1px solid #ccd9e7; padding:9px; border-radius:8px; }
.meta span { display:block; font-size:10px; text-transform:uppercase; color:#52627b; }
table { width:100%; border-collapse:collapse; font-size:10px; margin-bottom:18px; }
th, td { border:1px solid #cbd7e4; padding:6px; vertical-align:top; text-align:left; }
th { background:#eaf3fb; }
code { overflow-wrap:anywhere; }
.legend { display:flex; gap:12px; flex-wrap:wrap; }
.legend span { border:1px solid #cbd7e4; padding:6px 9px; border-radius:999px; }
footer { position:fixed; bottom:0; left:0; right:0; border-top:1px solid #9fb3c8; padding-top:5px; display:flex; justify-content:space-between; font-size:9px; background:#fff; }
.page-break { page-break-before:always; }
</style>
</head>
<body>
<header>
<div>{{logoHtml}}</div>
<div>
<h1>Pulse System Architecture &amp; API Inventory</h1>
<div>Provider-neutral runtime architecture and governed operational evidence</div>
</div>
</header>
<section class="meta">
<div><span>Provider</span><strong>{{H(snapshot.Platform.DisplayName)}}</strong></div>
<div><span>Environment</span><strong>{{H(snapshot.Platform.Environment)}}</strong></div>
<div><span>Region</span><strong>{{H(snapshot.Platform.Region)}}</strong></div>
<div><span>Release SHA</span><strong>{{H(snapshot.Runtime.ReleaseSha)}}</strong></div>
<div><span>Adapter</span><strong>{{H(snapshot.Platform.Adapter)}}</strong></div>
<div><span>Workload</span><strong>{{H(snapshot.Platform.WorkloadKind)}}</strong></div>
<div><span>Generated</span><strong>{{H(generated.ToString("u"))}}</strong></div>
<div><span>API endpoints</span><strong>{{apis.Count}}</strong></div>
</section>
<h2>Architecture legend</h2>
<div class="legend">{{legendRows}}</div>
<h2>System components</h2>
<table><thead><tr><th>Layer</th><th>Component</th><th>Type</th><th>Purpose</th></tr></thead><tbody>{{nodeRows}}</tbody></table>
<h2>External and internal data flows</h2>
<table><thead><tr><th>From</th><th>To</th><th>Protocol</th><th>Data / purpose</th><th>Classification</th></tr></thead><tbody>{{connectionRows}}</tbody></table>
<div class="page-break"></div>
<h2>API appendix</h2>
<table><thead><tr><th>Module</th><th>Method</th><th>Path</th><th>Purpose</th><th>Authentication</th><th>Permission</th><th>Status</th></tr></thead><tbody>{{apiRows}}</tbody></table>
<footer><span>Pulse · {{H(snapshot.Platform.Environment)}} · {{H(snapshot.Runtime.ReleaseSha)}}</span><span>Created by Ahmed Adeyemi</span></footer>
</body>
</html>
""";
    }
}
