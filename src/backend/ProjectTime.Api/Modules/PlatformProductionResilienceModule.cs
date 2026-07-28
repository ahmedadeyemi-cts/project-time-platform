using System.Text.Json;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Modules 014, 015, and 017 consume the provider-neutral platform snapshot
/// established by Group 2A. The endpoints remain read-only and deliberately
/// represent missing target-state, recovery, replica, approval, and ownership
/// evidence as not_recorded instead of inferring production readiness.
/// </summary>
public static partial class PlatformOperationsModule
{
    private const string ProductionResilienceContractVersion = "2026-07-28.1";

    public static IEndpointRouteBuilder MapPlatformProductionResilienceEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/system/backup-dr/production-planning",
            (Func<HttpContext, Task<IResult>>)GetProductionPlanningAsync);
        endpoints.MapGet(
            "/api/system/restore-validation/recovery-continuity",
            (Func<HttpContext, Task<IResult>>)GetRecoveryContinuityAsync);
        endpoints.MapGet(
            "/api/system/replication-sync/redundancy-failover",
            (Func<HttpContext, Task<IResult>>)GetRedundancyFailoverAsync);
        endpoints.MapGet(
            "/api/system/backup-dr/resilience-report",
            (Func<HttpContext, Task<IResult>>)GetResilienceReportAsync);
        endpoints.MapGet(
            "/api/system/backup-dr/resilience-report/export",
            (Func<HttpContext, Task<IResult>>)ExportResilienceReportAsync);

        return endpoints;
    }

    private static async Task<IResult> GetProductionPlanningAsync(HttpContext context)
    {
        var authorization = await AuthorizeAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;
        var snapshot = await BuildSnapshotAsync(context, connection);
        return Results.Ok(BuildProductionPlanningContract(context, snapshot));
    }

    private static async Task<IResult> GetRecoveryContinuityAsync(HttpContext context)
    {
        var authorization = await AuthorizeAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;
        var snapshot = await BuildSnapshotAsync(context, connection);
        return Results.Ok(BuildRecoveryContinuityContract(context, snapshot));
    }

    private static async Task<IResult> GetRedundancyFailoverAsync(HttpContext context)
    {
        var authorization = await AuthorizeAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;
        var snapshot = await BuildSnapshotAsync(context, connection);
        return Results.Ok(BuildRedundancyFailoverContract(context, snapshot));
    }

    private static async Task<IResult> GetResilienceReportAsync(HttpContext context)
    {
        var authorization = await AuthorizeAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;
        var snapshot = await BuildSnapshotAsync(context, connection);
        return Results.Ok(BuildResilienceReport(context, snapshot));
    }

    private static async Task<IResult> ExportResilienceReportAsync(HttpContext context)
    {
        var authorization = await AuthorizeAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;
        var snapshot = await BuildSnapshotAsync(context, connection);
        var report = BuildResilienceReport(context, snapshot);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            report,
            new JsonSerializerOptions { WriteIndented = true });

        return Results.File(
            payload,
            "application/json",
            $"projectpulse-production-resilience-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
    }

    private static object BuildProductionPlanningContract(
        HttpContext context,
        PlatformSnapshot snapshot)
    {
        var platform = BuildPlatformComparison(snapshot);
        var owners = BuildOwners();
        var blockers = BuildProductionPlanningBlockers(snapshot, platform.Target, owners);
        var evidence = BuildEvidenceAndApprovalHistory("014");

        return new
        {
            module = "014",
            moduleName = "Environment & Production Readiness Planning",
            responsibility = "environment_and_production_readiness_planning",
            status = blockers.Length == 0 ? "ready_for_review" : "planning_required",
            contractVersion = ProductionResilienceContractVersion,
            generatedAt = DateTimeOffset.UtcNow,
            access = AccessContract(context),
            platform,
            singleInstance = BuildSingleInstanceView(snapshot),
            productionPlanning = new
            {
                designStatus = TargetDesignStatus(platform.Target),
                targetEnvironment = platform.Target.Environment,
                targetProvider = platform.Target.Provider,
                targetRegion = platform.Target.Region,
                targetWorkloadKind = platform.Target.WorkloadKind,
                targetReplicaCount = platform.Target.ReplicaCount,
                databaseTopology = Recorded(
                    "PROJECTPULSE_TARGET_DATABASE_TOPOLOGY",
                    "PROJECTPULSE_PRODUCTION_DATABASE_TOPOLOGY"),
                storageTopology = Recorded(
                    "PROJECTPULSE_TARGET_STORAGE_TOPOLOGY",
                    "PROJECTPULSE_PRODUCTION_STORAGE_TOPOLOGY"),
                networkBoundary = Recorded(
                    "PROJECTPULSE_TARGET_NETWORK_BOUNDARY",
                    "PROJECTPULSE_PRODUCTION_NETWORK_BOUNDARY"),
                observabilityDesign = Recorded(
                    "PROJECTPULSE_TARGET_OBSERVABILITY_DESIGN",
                    "PROJECTPULSE_PRODUCTION_OBSERVABILITY_DESIGN"),
                releaseApprovalModel = Recorded(
                    "PROJECTPULSE_PRODUCTION_RELEASE_APPROVAL_MODEL"),
                rollbackDesign = Recorded(
                    "PROJECTPULSE_PRODUCTION_ROLLBACK_DESIGN"),
                notes = Recorded(
                    "PROJECTPULSE_PRODUCTION_DESIGN_NOTES")
            },
            readiness = new
            {
                blockerCount = blockers.Length,
                blockers,
                responsibleOwners = owners
            },
            evidence,
            reporting = ReportingContract(),
            security = SecurityContract()
        };
    }

    private static object BuildRecoveryContinuityContract(
        HttpContext context,
        PlatformSnapshot snapshot)
    {
        var platform = BuildPlatformComparison(snapshot);
        var owners = BuildOwners();
        var recovery = BuildRecoveryView(snapshot);
        var blockers = BuildRecoveryBlockers(snapshot, recovery, owners);
        var evidence = BuildEvidenceAndApprovalHistory("015");

        return new
        {
            module = "015",
            moduleName = "Backup, Recovery, Restoration & Continuity",
            responsibility = "backup_recovery_restoration_and_continuity",
            status = blockers.Length == 0 ? "continuity_ready_for_review" : "continuity_evidence_required",
            contractVersion = ProductionResilienceContractVersion,
            generatedAt = DateTimeOffset.UtcNow,
            access = AccessContract(context),
            platform,
            singleInstance = BuildSingleInstanceView(snapshot),
            recoveryContinuity = recovery,
            readiness = new
            {
                blockerCount = blockers.Length,
                blockers,
                responsibleOwners = owners
            },
            evidence,
            reporting = ReportingContract(),
            security = SecurityContract()
        };
    }

    private static object BuildRedundancyFailoverContract(
        HttpContext context,
        PlatformSnapshot snapshot)
    {
        var platform = BuildPlatformComparison(snapshot);
        var owners = BuildOwners();
        var redundancy = BuildRedundancyView(snapshot);
        var blockers = BuildRedundancyBlockers(snapshot, redundancy, owners);
        var evidence = BuildEvidenceAndApprovalHistory("017");

        return new
        {
            module = "017",
            moduleName = "Availability, Regions, Replicas, Redundancy & Failover",
            responsibility = "availability_regions_replicas_redundancy_and_failover",
            status = blockers.Length == 0 ? "failover_ready_for_review" : "failover_prerequisites_required",
            contractVersion = ProductionResilienceContractVersion,
            generatedAt = DateTimeOffset.UtcNow,
            access = AccessContract(context),
            platform,
            singleInstance = BuildSingleInstanceView(snapshot),
            redundancyFailover = redundancy,
            readiness = new
            {
                blockerCount = blockers.Length,
                blockers,
                responsibleOwners = owners
            },
            evidence,
            reporting = ReportingContract(),
            security = SecurityContract()
        };
    }

    private static object BuildResilienceReport(
        HttpContext context,
        PlatformSnapshot snapshot) => new
    {
        report = "projectpulse_production_resilience",
        modules = new[] { "014", "015", "017" },
        status = "production_resilience_report_loaded",
        contractVersion = ProductionResilienceContractVersion,
        platformAdapterContract = ContractVersion,
        generatedAt = DateTimeOffset.UtcNow,
        access = AccessContract(context),
        productionPlanning = BuildProductionPlanningContract(context, snapshot),
        recoveryContinuity = BuildRecoveryContinuityContract(context, snapshot),
        redundancyFailover = BuildRedundancyFailoverContract(context, snapshot),
        providerSpecificDetails = snapshot.ProviderSpecificDetails,
        reporting = ReportingContract(),
        security = SecurityContract()
    };

    private static PlatformComparison BuildPlatformComparison(PlatformSnapshot snapshot)
    {
        var targetReplicaCount = ReadPositiveInt(
            "PROJECTPULSE_TARGET_REPLICA_COUNT",
            "PROJECTPULSE_PRODUCTION_REPLICA_COUNT");
        var targetEnvironment = Recorded(
            "PROJECTPULSE_TARGET_ENVIRONMENT",
            "PROJECTPULSE_PRODUCTION_ENVIRONMENT");
        if (targetEnvironment == "not_recorded") targetEnvironment = "production";

        var current = new PlatformSide(
            snapshot.Platform.Environment,
            snapshot.Platform.Provider,
            snapshot.Platform.DisplayName,
            snapshot.Platform.Adapter,
            snapshot.Platform.AdapterStatus,
            snapshot.Platform.Region,
            snapshot.Platform.WorkloadKind,
            snapshot.Platform.Instance,
            snapshot.Replicas.Length,
            snapshot.Runtime.Deployment,
            snapshot.Runtime.ReleaseSha,
            "observed_from_active_group_2a_adapter");

        var target = new PlatformSide(
            targetEnvironment,
            Recorded(
                "PROJECTPULSE_TARGET_PLATFORM_PROVIDER",
                "PROJECTPULSE_PRODUCTION_PROVIDER"),
            Recorded(
                "PROJECTPULSE_TARGET_PLATFORM_NAME",
                "PROJECTPULSE_PRODUCTION_PLATFORM_NAME"),
            Recorded(
                "PROJECTPULSE_TARGET_PLATFORM_ADAPTER",
                "PROJECTPULSE_PRODUCTION_PLATFORM_ADAPTER"),
            "planned",
            Recorded(
                "PROJECTPULSE_TARGET_REGION",
                "PROJECTPULSE_PRODUCTION_REGION"),
            Recorded(
                "PROJECTPULSE_TARGET_WORKLOAD_KIND",
                "PROJECTPULSE_PRODUCTION_WORKLOAD_KIND"),
            Recorded(
                "PROJECTPULSE_TARGET_INSTANCE_NAME",
                "PROJECTPULSE_PRODUCTION_INSTANCE_NAME"),
            targetReplicaCount,
            Recorded(
                "PROJECTPULSE_TARGET_DEPLOYMENT_NAME",
                "PROJECTPULSE_PRODUCTION_DEPLOYMENT_NAME"),
            Recorded(
                "PROJECTPULSE_TARGET_RELEASE_SHA",
                "PROJECTPULSE_PRODUCTION_RELEASE_SHA"),
            "operator_recorded_target_design");

        var comparison = new[]
        {
            new EnvironmentComparisonRow(
                current.Environment,
                "current_runtime",
                current.Provider,
                current.Region,
                current.WorkloadKind,
                current.ReplicaCount,
                "observed",
                "Group 2A adapter and runtime metadata"),
            new EnvironmentComparisonRow(
                "test",
                "controlled_test",
                Recorded("PROJECTPULSE_TEST_PLATFORM_PROVIDER"),
                Recorded("PROJECTPULSE_TEST_REGION"),
                Recorded("PROJECTPULSE_TEST_WORKLOAD_KIND"),
                ReadPositiveInt("PROJECTPULSE_TEST_REPLICA_COUNT"),
                IsAnyRecorded(
                    Recorded("PROJECTPULSE_TEST_PLATFORM_PROVIDER"),
                    Recorded("PROJECTPULSE_TEST_REGION"))
                    ? "recorded"
                    : "not_recorded",
                "Optional provider-neutral test design contract"),
            new EnvironmentComparisonRow(
                target.Environment,
                "production_target",
                target.Provider,
                target.Region,
                target.WorkloadKind,
                target.ReplicaCount,
                TargetDesignStatus(target),
                "Operator-recorded target design; no provider value is inferred")
        };

        return new PlatformComparison(current, target, comparison);
    }

    private static object BuildSingleInstanceView(PlatformSnapshot snapshot)
    {
        var observedReplicaCount = snapshot.Replicas.Length;
        var singleInstance = observedReplicaCount <= 1;

        return new
        {
            observedReplicaCount,
            singleInstance,
            status = singleInstance ? "single_instance_observed" : "multiple_instances_observed",
            limitations = singleInstance
                ? new[]
                {
                    "The active adapter reports one runtime instance.",
                    "An application-instance failure may interrupt service until the platform replaces or restores the workload.",
                    "A provider replica count, database replica, storage replication, secondary region, and tested failover path are not inferred from one process observation."
                }
                : new[]
                {
                    "Multiple runtime instances are observed, but health probes, traffic distribution, state sharing, database failover, and storage replication still require evidence."
                },
            source = "group_2a_provider_neutral_replica_inventory"
        };
    }

    private static RecoveryView BuildRecoveryView(PlatformSnapshot snapshot)
    {
        var rpoMinutes = ReadPositiveInt(
            "PROJECTPULSE_RPO_MINUTES",
            "PROJECTPULSE_RECOVERY_POINT_OBJECTIVE_MINUTES");
        var rtoMinutes = ReadPositiveInt(
            "PROJECTPULSE_RTO_MINUTES",
            "PROJECTPULSE_RECOVERY_TIME_OBJECTIVE_MINUTES");
        var lastSuccessfulBackupAt = ReadDate(
            "PROJECTPULSE_LAST_SUCCESSFUL_BACKUP_AT",
            "PROJECTPULSE_BACKUP_LAST_SUCCESS_AT");
        var lastSuccessfulRecoveryTestAt = ReadDate(
            "PROJECTPULSE_LAST_SUCCESSFUL_RECOVERY_TEST_AT",
            "PROJECTPULSE_LAST_RESTORE_VALIDATION_AT");
        var storageStatus = snapshot.Dependencies.Storage.Status;

        return new RecoveryView(
            rpoMinutes,
            rtoMinutes,
            lastSuccessfulBackupAt,
            lastSuccessfulRecoveryTestAt,
            Recorded(
                "PROJECTPULSE_BACKUP_POLICY_NAME",
                "PROJECTPULSE_RECOVERY_POLICY_NAME"),
            Recorded(
                "PROJECTPULSE_BACKUP_FREQUENCY",
                "PROJECTPULSE_BACKUP_SCHEDULE"),
            Recorded(
                "PROJECTPULSE_BACKUP_RETENTION",
                "PROJECTPULSE_BACKUP_RETENTION_POLICY"),
            Recorded(
                "PROJECTPULSE_BACKUP_LOCATION",
                "PROJECTPULSE_BACKUP_TARGET"),
            storageStatus,
            snapshot.Dependencies.Storage.Message,
            Recorded(
                "PROJECTPULSE_RECOVERY_RUNBOOK",
                "PROJECTPULSE_RESTORE_RUNBOOK_REFERENCE"),
            Recorded(
                "PROJECTPULSE_CONTINUITY_COMMUNICATION_PLAN"),
            lastSuccessfulBackupAt.HasValue
                ? "last_success_recorded"
                : storageStatus is "healthy" or "configured"
                    ? "storage_configuration_observed_backup_success_not_recorded"
                    : "not_recorded",
            lastSuccessfulRecoveryTestAt.HasValue
                ? "last_success_recorded"
                : "not_recorded");
    }

    private static RedundancyView BuildRedundancyView(PlatformSnapshot snapshot)
    {
        var regions = snapshot.Replicas
            .Select(item => item.Region)
            .Append(snapshot.Platform.Region)
            .Where(item => !string.IsNullOrWhiteSpace(item)
                && !string.Equals(item, "not_reported", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item)
            .ToArray();
        var targetRegion = Recorded(
            "PROJECTPULSE_TARGET_REGION",
            "PROJECTPULSE_PRODUCTION_REGION",
            "PROJECTPULSE_FAILOVER_REGION");
        var databaseReplicaStatus = Recorded(
            "PROJECTPULSE_DATABASE_REPLICA_STATUS",
            "PROJECTPULSE_DATABASE_HIGH_AVAILABILITY_STATUS");
        var storageReplicationStatus = Recorded(
            "PROJECTPULSE_STORAGE_REPLICATION_STATUS",
            "PROJECTPULSE_ARTIFACT_REPLICATION_STATUS");
        var failoverMode = Recorded(
            "PROJECTPULSE_FAILOVER_MODE",
            "PROJECTPULSE_PRODUCTION_FAILOVER_MODE");
        var failoverTestedAt = ReadDate(
            "PROJECTPULSE_LAST_FAILOVER_TEST_AT");

        var prerequisites = new[]
        {
            new ResiliencePrerequisite(
                "verified_backup",
                "Verified recent backup",
                ReadDate("PROJECTPULSE_LAST_SUCCESSFUL_BACKUP_AT", "PROJECTPULSE_BACKUP_LAST_SUCCESS_AT").HasValue
                    ? "evidence_recorded"
                    : "evidence_required",
                "Module 015 backup evidence"),
            new ResiliencePrerequisite(
                "successful_recovery_test",
                "Successful recovery test",
                ReadDate("PROJECTPULSE_LAST_SUCCESSFUL_RECOVERY_TEST_AT", "PROJECTPULSE_LAST_RESTORE_VALIDATION_AT").HasValue
                    ? "evidence_recorded"
                    : "evidence_required",
                "Module 015 recovery validation evidence"),
            new ResiliencePrerequisite(
                "database_replica",
                "Database replica or managed high availability",
                IsPositiveStatus(databaseReplicaStatus) ? "evidence_recorded" : "evidence_required",
                "Provider database adapter or operator-recorded evidence"),
            new ResiliencePrerequisite(
                "storage_replication",
                "Storage replication",
                IsPositiveStatus(storageReplicationStatus) ? "evidence_recorded" : "evidence_required",
                "Provider storage adapter or operator-recorded evidence"),
            new ResiliencePrerequisite(
                "secondary_region",
                "Secondary region or approved same-region strategy",
                regions.Length > 1 || targetRegion != "not_recorded"
                    ? "design_recorded"
                    : "design_required",
                "Group 2A region inventory and target design"),
            new ResiliencePrerequisite(
                "failover_runbook",
                "Approved failover runbook and accountable owner",
                Recorded("PROJECTPULSE_FAILOVER_RUNBOOK") != "not_recorded"
                    && Owner("failover") != "not_assigned"
                    ? "evidence_recorded"
                    : "evidence_required",
                "Operator-recorded governance contract")
        };

        return new RedundancyView(
            snapshot.Replicas,
            snapshot.Replicas.Length,
            regions,
            targetRegion,
            databaseReplicaStatus,
            storageReplicationStatus,
            snapshot.Dependencies.Storage.Status,
            snapshot.Dependencies.Storage.Message,
            failoverMode,
            Recorded("PROJECTPULSE_FAILOVER_RUNBOOK"),
            failoverTestedAt,
            prerequisites,
            prerequisites.Count(item => item.Status.EndsWith("required", StringComparison.OrdinalIgnoreCase)));
    }

    private static ResilienceBlocker[] BuildProductionPlanningBlockers(
        PlatformSnapshot snapshot,
        PlatformSide target,
        OwnerAssignment[] owners)
    {
        var blockers = new List<ResilienceBlocker>();
        if (target.Provider == "not_recorded")
            blockers.Add(Blocker("TARGET_PROVIDER_NOT_RECORDED", "high", "target_platform", "Record the approved production platform/provider.", OwnerFor(owners, "production")));
        if (target.Region == "not_recorded")
            blockers.Add(Blocker("TARGET_REGION_NOT_RECORDED", "high", "target_platform", "Record the approved production region or regional strategy.", OwnerFor(owners, "production")));
        if (!target.ReplicaCount.HasValue)
            blockers.Add(Blocker("TARGET_REPLICA_COUNT_NOT_RECORDED", "high", "availability", "Record the intended production application replica count.", OwnerFor(owners, "production")));
        if (target.WorkloadKind == "not_recorded")
            blockers.Add(Blocker("TARGET_WORKLOAD_NOT_RECORDED", "medium", "target_platform", "Record the intended production workload kind.", OwnerFor(owners, "production")));
        if (Recorded("PROJECTPULSE_TARGET_DATABASE_TOPOLOGY", "PROJECTPULSE_PRODUCTION_DATABASE_TOPOLOGY") == "not_recorded")
            blockers.Add(Blocker("DATABASE_TOPOLOGY_NOT_RECORDED", "high", "database", "Record the intended production database topology and availability model.", OwnerFor(owners, "production")));
        if (Recorded("PROJECTPULSE_TARGET_STORAGE_TOPOLOGY", "PROJECTPULSE_PRODUCTION_STORAGE_TOPOLOGY") == "not_recorded")
            blockers.Add(Blocker("STORAGE_TOPOLOGY_NOT_RECORDED", "high", "storage", "Record the intended production artifact and backup storage topology.", OwnerFor(owners, "production")));
        if (Recorded("PROJECTPULSE_TARGET_NETWORK_BOUNDARY", "PROJECTPULSE_PRODUCTION_NETWORK_BOUNDARY") == "not_recorded")
            blockers.Add(Blocker("NETWORK_BOUNDARY_NOT_RECORDED", "medium", "network", "Record the production network and trust boundary design.", OwnerFor(owners, "production")));
        if (Recorded("PROJECTPULSE_TARGET_OBSERVABILITY_DESIGN", "PROJECTPULSE_PRODUCTION_OBSERVABILITY_DESIGN") == "not_recorded")
            blockers.Add(Blocker("OBSERVABILITY_DESIGN_NOT_RECORDED", "medium", "observability", "Record production monitoring, alerting, and evidence ownership.", OwnerFor(owners, "production")));
        if (Recorded("PROJECTPULSE_PRODUCTION_RELEASE_APPROVAL_MODEL") == "not_recorded")
            blockers.Add(Blocker("RELEASE_APPROVAL_MODEL_NOT_RECORDED", "medium", "governance", "Record the production release approval model.", OwnerFor(owners, "approval")));
        if (Recorded("PROJECTPULSE_PRODUCTION_ROLLBACK_DESIGN") == "not_recorded")
            blockers.Add(Blocker("ROLLBACK_DESIGN_NOT_RECORDED", "high", "recovery", "Record the production rollback design and verification boundary.", OwnerFor(owners, "production")));
        if (snapshot.Replicas.Length <= 1)
            blockers.Add(Blocker("CURRENT_SINGLE_INSTANCE", "high", "current_platform", "The active adapter reports one runtime instance; document replacement and failover design.", OwnerFor(owners, "production")));
        if (snapshot.Platform.Region == "not_reported")
            blockers.Add(Blocker("CURRENT_REGION_NOT_REPORTED", "medium", "current_platform", "Configure the platform adapter to report the current region.", OwnerFor(owners, "production")));
        if (OwnerFor(owners, "production") == "not_assigned")
            blockers.Add(Blocker("PRODUCTION_OWNER_NOT_ASSIGNED", "high", "governance", "Assign an accountable production-readiness owner.", "platform_administration"));
        return blockers.ToArray();
    }

    private static ResilienceBlocker[] BuildRecoveryBlockers(
        PlatformSnapshot snapshot,
        RecoveryView recovery,
        OwnerAssignment[] owners)
    {
        var blockers = new List<ResilienceBlocker>();
        if (!recovery.RecoveryPointObjectiveMinutes.HasValue)
            blockers.Add(Blocker("RPO_NOT_RECORDED", "high", "recovery_objectives", "Record the approved recovery point objective in minutes.", OwnerFor(owners, "recovery")));
        if (!recovery.RecoveryTimeObjectiveMinutes.HasValue)
            blockers.Add(Blocker("RTO_NOT_RECORDED", "high", "recovery_objectives", "Record the approved recovery time objective in minutes.", OwnerFor(owners, "recovery")));
        if (!recovery.LastSuccessfulBackupAt.HasValue)
            blockers.Add(Blocker("LAST_BACKUP_NOT_RECORDED", "high", "backup", "Publish evidence of the last successful backup.", OwnerFor(owners, "recovery")));
        if (!recovery.LastSuccessfulRecoveryTestAt.HasValue)
            blockers.Add(Blocker("LAST_RECOVERY_TEST_NOT_RECORDED", "high", "restore_validation", "Publish evidence of the last successful recovery test.", OwnerFor(owners, "recovery")));
        if (snapshot.Dependencies.Storage.Status is "failed" or "not_configured")
            blockers.Add(Blocker("BACKUP_STORAGE_NOT_READY", "high", "storage", snapshot.Dependencies.Storage.Message, OwnerFor(owners, "recovery")));
        if (recovery.PolicyName == "not_recorded")
            blockers.Add(Blocker("BACKUP_POLICY_NOT_RECORDED", "medium", "backup", "Record the governed backup and recovery policy.", OwnerFor(owners, "recovery")));
        if (recovery.BackupFrequency == "not_recorded")
            blockers.Add(Blocker("BACKUP_FREQUENCY_NOT_RECORDED", "medium", "backup", "Record the backup schedule or provider-managed frequency.", OwnerFor(owners, "recovery")));
        if (recovery.RetentionPolicy == "not_recorded")
            blockers.Add(Blocker("BACKUP_RETENTION_NOT_RECORDED", "medium", "retention", "Record the backup retention policy and evidence boundary.", OwnerFor(owners, "recovery")));
        if (recovery.BackupLocation == "not_recorded")
            blockers.Add(Blocker("BACKUP_LOCATION_NOT_RECORDED", "medium", "storage", "Record the provider-neutral backup location or target classification.", OwnerFor(owners, "recovery")));
        if (recovery.RecoveryRunbook == "not_recorded")
            blockers.Add(Blocker("RECOVERY_RUNBOOK_NOT_RECORDED", "medium", "runbook", "Record the governed restore and continuity runbook reference.", OwnerFor(owners, "recovery")));
        if (recovery.ContinuityCommunicationPlan == "not_recorded")
            blockers.Add(Blocker("CONTINUITY_COMMUNICATION_PLAN_NOT_RECORDED", "medium", "continuity", "Record the continuity communication and escalation plan.", OwnerFor(owners, "recovery")));
        if (OwnerFor(owners, "recovery") == "not_assigned")
            blockers.Add(Blocker("RECOVERY_OWNER_NOT_ASSIGNED", "high", "governance", "Assign an accountable backup and recovery owner.", "platform_administration"));
        return blockers.ToArray();
    }

    private static ResilienceBlocker[] BuildRedundancyBlockers(
        PlatformSnapshot snapshot,
        RedundancyView redundancy,
        OwnerAssignment[] owners)
    {
        var blockers = new List<ResilienceBlocker>();
        if (snapshot.Replicas.Length <= 1)
            blockers.Add(Blocker("APPLICATION_REDUNDANCY_NOT_OBSERVED", "high", "application_replicas", "The active adapter reports one runtime replica.", OwnerFor(owners, "failover")));
        if (!IsPositiveStatus(redundancy.DatabaseReplicaStatus))
            blockers.Add(Blocker("DATABASE_REPLICA_NOT_RECORDED", "high", "database", "Record database replica or managed high-availability evidence.", OwnerFor(owners, "failover")));
        if (!IsPositiveStatus(redundancy.StorageReplicationStatus))
            blockers.Add(Blocker("STORAGE_REPLICATION_NOT_RECORDED", "high", "storage", "Record storage replication evidence separately from storage availability.", OwnerFor(owners, "failover")));
        if (redundancy.ObservedRegions.Length <= 1 && redundancy.TargetRegion == "not_recorded")
            blockers.Add(Blocker("SECONDARY_REGION_NOT_RECORDED", "medium", "regional_coverage", "Record a secondary region or approved same-region failover strategy.", OwnerFor(owners, "failover")));
        if (redundancy.FailoverMode == "not_recorded")
            blockers.Add(Blocker("FAILOVER_MODE_NOT_RECORDED", "medium", "failover", "Record the intended failover mode and decision authority.", OwnerFor(owners, "failover")));
        if (redundancy.FailoverRunbook == "not_recorded")
            blockers.Add(Blocker("FAILOVER_RUNBOOK_NOT_RECORDED", "high", "runbook", "Record the governed failover runbook reference.", OwnerFor(owners, "failover")));
        if (!redundancy.LastFailoverTestAt.HasValue)
            blockers.Add(Blocker("FAILOVER_TEST_NOT_RECORDED", "high", "validation", "Publish evidence of the last successful failover exercise.", OwnerFor(owners, "failover")));
        if (OwnerFor(owners, "failover") == "not_assigned")
            blockers.Add(Blocker("FAILOVER_OWNER_NOT_ASSIGNED", "high", "governance", "Assign an accountable availability and failover owner.", "platform_administration"));
        return blockers.ToArray();
    }

    private static OwnerAssignment[] BuildOwners() =>
    [
        new("production", "Environment and production readiness", Owner("production"), Owner("production") == "not_assigned" ? "not_assigned" : "assigned"),
        new("recovery", "Backup, recovery, restoration, and continuity", Owner("recovery"), Owner("recovery") == "not_assigned" ? "not_assigned" : "assigned"),
        new("failover", "Availability, redundancy, and failover", Owner("failover"), Owner("failover") == "not_assigned" ? "not_assigned" : "assigned"),
        new("approval", "Production resilience approval", Owner("approval"), Owner("approval") == "not_assigned" ? "not_assigned" : "assigned")
    ];

    private static object BuildEvidenceAndApprovalHistory(string moduleCode)
    {
        var observations = Evidence
            .Where(item => string.Equals(item.ModuleCode, moduleCode, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.ObservedAt)
            .Take(25)
            .Select(item => new
            {
                item.EvidenceId,
                item.ObservedAt,
                item.CorrelationId,
                item.EventType,
                item.Status,
                item.Method,
                item.Path,
                item.StatusCode,
                item.DurationMs,
                item.ErrorCode,
                item.Message,
                item.ReleaseSha
            })
            .ToArray();

        var approvedAt = ReadDate(
            $"PROJECTPULSE_MODULE_{moduleCode}_APPROVED_AT",
            "PROJECTPULSE_PRODUCTION_RESILIENCE_APPROVED_AT");
        var approvedBy = Recorded(
            $"PROJECTPULSE_MODULE_{moduleCode}_APPROVED_BY",
            "PROJECTPULSE_PRODUCTION_RESILIENCE_APPROVED_BY");
        var approvalReference = Recorded(
            $"PROJECTPULSE_MODULE_{moduleCode}_APPROVAL_REFERENCE",
            "PROJECTPULSE_PRODUCTION_RESILIENCE_APPROVAL_REFERENCE");
        var approvals = approvedAt.HasValue || approvedBy != "not_recorded" || approvalReference != "not_recorded"
            ? new[]
            {
                new
                {
                    approvedAt,
                    approvedBy,
                    reference = approvalReference,
                    status = approvedAt.HasValue && approvedBy != "not_recorded"
                        ? "recorded"
                        : "partial_evidence"
                }
            }
            : Array.Empty<object>();

        return new
        {
            observationCount = observations.Length,
            latestObservations = observations,
            approvalStatus = approvals.Length == 0
                ? "not_recorded"
                : approvedAt.HasValue && approvedBy != "not_recorded"
                    ? "recorded"
                    : "partial_evidence",
            approvalHistory = approvals,
            evidenceSource = "bounded_group_2a_runtime_telemetry_and_operator_recorded_governance",
            secretsIncluded = false
        };
    }

    private static object ReportingContract() => new
    {
        authoritativeSource = "group_2a_provider_neutral_platform_abstraction",
        moduleApis = new[]
        {
            "/api/system/backup-dr/production-planning",
            "/api/system/restore-validation/recovery-continuity",
            "/api/system/replication-sync/redundancy-failover"
        },
        sharedSourceApis = new[]
        {
            "/api/platform-operations/overview",
            "/api/platform-operations/architecture",
            "/api/platform-operations/evidence"
        },
        operationalSourceApis = new[]
        {
            "/api/system/backup-dr/status",
            "/api/system/restore-validation/status",
            "/api/system/replication-sync/status"
        },
        consolidatedReportApi = "/api/system/backup-dr/resilience-report",
        exportApi = "/api/system/backup-dr/resilience-report/export",
        futureAdapterRule = "A future provider adapter supplies the same platform, replica, region, dependency, and evidence fields without rebuilding Modules 014, 015, or 017.",
        missingEvidenceRule = "Unknown provider, target, recovery, ownership, approval, or failover values remain not_recorded and become readiness blockers."
    };

    private static string TargetDesignStatus(PlatformSide target) =>
        target.Provider != "not_recorded"
        && target.Region != "not_recorded"
        && target.ReplicaCount.HasValue
            ? "recorded"
            : "not_recorded";

    private static bool IsAnyRecorded(params string[] values) =>
        values.Any(value => !string.Equals(value, "not_recorded", StringComparison.OrdinalIgnoreCase));

    private static bool IsPositiveStatus(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.StartsWith("not_", StringComparison.Ordinal)
            || normalized.Contains("failed", StringComparison.Ordinal)
            || normalized.Contains("unavailable", StringComparison.Ordinal)
            || normalized.Contains("required", StringComparison.Ordinal)
            || normalized.Contains("unknown", StringComparison.Ordinal))
        {
            return false;
        }

        return normalized.Contains("healthy", StringComparison.Ordinal)
            || normalized.Contains("active", StringComparison.Ordinal)
            || normalized.Contains("ready", StringComparison.Ordinal)
            || normalized.Contains("configured", StringComparison.Ordinal)
            || normalized.Contains("enabled", StringComparison.Ordinal)
            || normalized.Contains("replicat", StringComparison.Ordinal)
            || normalized.Contains("available", StringComparison.Ordinal);
    }

    private static string Recorded(params string[] names)
    {
        var value = FirstEnvironment(names);
        return string.IsNullOrWhiteSpace(value) ? "not_recorded" : Limit(value, 240);
    }

    private static int? ReadPositiveInt(params string[] names)
    {
        var value = FirstEnvironment(names);
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;
    }

    private static DateTimeOffset? ReadDate(params string[] names)
    {
        var value = FirstEnvironment(names);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string Owner(string area)
    {
        var value = area switch
        {
            "production" => Recorded("PROJECTPULSE_PRODUCTION_READINESS_OWNER", "PROJECTPULSE_PLATFORM_OWNER"),
            "recovery" => Recorded("PROJECTPULSE_RECOVERY_OWNER", "PROJECTPULSE_BACKUP_OWNER"),
            "failover" => Recorded("PROJECTPULSE_FAILOVER_OWNER", "PROJECTPULSE_AVAILABILITY_OWNER"),
            "approval" => Recorded("PROJECTPULSE_PRODUCTION_RESILIENCE_APPROVER", "PROJECTPULSE_PRODUCTION_APPROVER"),
            _ => "not_assigned"
        };

        return value == "not_recorded" ? "not_assigned" : value;
    }

    private static string OwnerFor(OwnerAssignment[] owners, string area) =>
        owners.FirstOrDefault(item => string.Equals(item.Area, area, StringComparison.OrdinalIgnoreCase))?.Owner
        ?? "not_assigned";

    private static ResilienceBlocker Blocker(
        string code,
        string severity,
        string area,
        string message,
        string owner) => new(code, severity, area, message, owner);

    private sealed record PlatformComparison(
        PlatformSide Current,
        PlatformSide Target,
        EnvironmentComparisonRow[] EnvironmentComparison);

    private sealed record PlatformSide(
        string Environment,
        string Provider,
        string DisplayName,
        string Adapter,
        string AdapterStatus,
        string Region,
        string WorkloadKind,
        string Instance,
        int? ReplicaCount,
        string Deployment,
        string ReleaseSha,
        string EvidenceSource);

    private sealed record EnvironmentComparisonRow(
        string Environment,
        string Purpose,
        string Provider,
        string Region,
        string WorkloadKind,
        int? ReplicaCount,
        string Status,
        string EvidenceSource);

    private sealed record RecoveryView(
        int? RecoveryPointObjectiveMinutes,
        int? RecoveryTimeObjectiveMinutes,
        DateTimeOffset? LastSuccessfulBackupAt,
        DateTimeOffset? LastSuccessfulRecoveryTestAt,
        string PolicyName,
        string BackupFrequency,
        string RetentionPolicy,
        string BackupLocation,
        string StorageStatus,
        string StorageEvidence,
        string RecoveryRunbook,
        string ContinuityCommunicationPlan,
        string BackupEvidenceStatus,
        string RecoveryTestEvidenceStatus);

    private sealed record RedundancyView(
        ReplicaEntry[] Replicas,
        int ObservedReplicaCount,
        string[] ObservedRegions,
        string TargetRegion,
        string DatabaseReplicaStatus,
        string StorageReplicationStatus,
        string StorageAvailabilityStatus,
        string StorageAvailabilityEvidence,
        string FailoverMode,
        string FailoverRunbook,
        DateTimeOffset? LastFailoverTestAt,
        ResiliencePrerequisite[] FailoverPrerequisites,
        int MissingPrerequisiteCount);

    private sealed record ResiliencePrerequisite(
        string Code,
        string Name,
        string Status,
        string EvidenceSource);

    private sealed record ResilienceBlocker(
        string Code,
        string Severity,
        string Area,
        string Message,
        string Owner);

    private sealed record OwnerAssignment(
        string Area,
        string Responsibility,
        string Owner,
        string Status);
}
