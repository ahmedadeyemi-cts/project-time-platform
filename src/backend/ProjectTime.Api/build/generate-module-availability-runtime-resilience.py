#!/usr/bin/env python3
"""Generate the compiler copy of ModuleAvailabilityModule with resilient reads.

The canonical source remains readable. This transformation is intentionally
anchor-checked and fails the build when the reviewed source structure changes.
Only request-time availability enforcement is degraded to the documented
"missing override means enabled" policy; availability management endpoints
continue to report dependency failures and never claim a write succeeded.
"""

from __future__ import annotations

import argparse
from pathlib import Path


def replace_once(source: str, old: str, new: str, label: str) -> str:
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one source anchor, found {count}")
    return source.replace(old, new, 1)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    input_path = Path(args.input)
    output_path = Path(args.output)
    source = input_path.read_text(encoding="utf-8")

    source = replace_once(
        source,
        "    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(10);",
        "    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(60);",
        "availability cache lifetime",
    )

    source = replace_once(
        source,
        """    private static readonly ConcurrentDictionary<string, AvailabilityCacheEntry> AvailabilityCache =
        new(StringComparer.OrdinalIgnoreCase);
""",
        """    private static readonly ConcurrentDictionary<string, AvailabilityCacheEntry> AvailabilityCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> DegradedAvailabilityWarnings =
        new(StringComparer.OrdinalIgnoreCase);
""",
        "degraded warning registry",
    )

    source = replace_once(
        source,
        """            var connectionString = BuildConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                await WriteUnavailableAsync(context, moduleNumber);
                return;
            }
""",
        """            var connectionString = BuildConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                await ContinueWithDegradedAvailabilityAsync(
                    context,
                    next,
                    moduleNumber,
                    "configuration_missing");
                return;
            }
""",
        "middleware configuration fallback",
    )

    source = replace_once(
        source,
        """            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                await WriteMigrationPendingAsync(context, moduleNumber);
            }
            catch (Exception exception)
            {
                LogFailure(context, exception, moduleNumber, "enforce module availability");
                await WriteUnavailableAsync(context, moduleNumber);
            }
""",
        """            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                LogFailure(context, exception, moduleNumber, "verify module availability because Migration 042 is pending");
                await ContinueWithDegradedAvailabilityAsync(
                    context,
                    next,
                    moduleNumber,
                    "migration_042_pending");
            }
            catch (Exception exception)
            {
                LogFailure(context, exception, moduleNumber, "enforce module availability");
                await ContinueWithDegradedAvailabilityAsync(
                    context,
                    next,
                    moduleNumber,
                    "availability_store_unreachable");
            }
""",
        "middleware exception fallback",
    )

    helper_anchor = "    private static async Task WriteUnavailableAsync(HttpContext context, string moduleNumber)\n"
    helper = """    private static async Task ContinueWithDegradedAvailabilityAsync(
        HttpContext context,
        Func<Task> next,
        string moduleNumber,
        string reason)
    {
        // Availability overrides are optional governance metadata. A transient
        // read failure must not take Module 011, Module 066, or another enabled
        // application capability offline. Preserve any known disabled state in
        // memory; otherwise follow the documented default-Enabled policy while
        // endpoint-local authentication, authorization, and record scope remain
        // fully enforced.
        if (AvailabilityCache.TryGetValue(moduleNumber, out var cached)
            && !cached.IsEnabled)
        {
            var permanentFullControl = context.Items.TryGetValue(
                    "ProjectPulsePermanentFullControl",
                    out var permanent)
                && permanent is true
                && !ProjectPulseActualSessionAuthority.IsViewAs(context);
            if (!permanentFullControl)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    module = moduleNumber,
                    status = "module_disabled",
                    message = "This module is disabled and is available only to the Super Administrator role."
                });
                return;
            }
        }

        var warningKey = $"{moduleNumber}:{reason}";
        if (DegradedAvailabilityWarnings.TryAdd(warningKey, 0))
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ModuleAvailabilityModule")
                .LogWarning(
                    "Module {ModuleNumber} availability verification is degraded ({Reason}); the documented default-enabled state is being used while endpoint authorization remains active.",
                    moduleNumber,
                    reason);
        }

        context.Items["ProjectPulseModuleAvailabilityDegraded"] = true;
        context.Response.Headers["X-ProjectPulse-Module-Availability"] =
            $"degraded-default-enabled; reason={reason}";
        await next();
    }

"""
    source = replace_once(
        source,
        helper_anchor,
        helper + helper_anchor,
        "degraded enforcement helper",
    )

    start_token = "    private static string? BuildConnectionString()\n"
    end_token = "\n    private sealed record ModuleDefinition"
    start = source.find(start_token)
    end = source.find(end_token, start)
    if start < 0 or end < 0:
        raise SystemExit("connection string resolver anchors were not found")

    connection_resolver = """    private static string? BuildConnectionString()
    {
        // The protected runtime's canonical PTP database variables are the same
        // variables used by FlowHive persistence. Prefer that coherent set over
        // legacy connection-string aliases that can remain populated with a
        // retired endpoint after an environment migration.
        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (!string.IsNullOrWhiteSpace(host)
            && !string.IsNullOrWhiteSpace(database)
            && !string.IsNullOrWhiteSpace(username)
            && !string.IsNullOrWhiteSpace(password))
        {
            return new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
                Database = database,
                Username = username,
                Password = password,
                IncludeErrorDetail = false,
                Pooling = true,
                MinPoolSize = 0,
                MaxPoolSize = 30,
                Timeout = 5,
                CommandTimeout = 5,
                KeepAlive = 30
            }.ConnectionString;
        }

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

        return null;
    }
"""
    source = source[:start] + connection_resolver + source[end:]

    required_markers = (
        "degraded-default-enabled",
        "ProjectPulseModuleAvailabilityDegraded",
        "availability_store_unreachable",
        "MaxPoolSize = 30",
        "Timeout = 5",
        "await next();",
    )
    for marker in required_markers:
        if marker not in source:
            raise SystemExit(f"generated module availability source is missing marker: {marker}")

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(source, encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
