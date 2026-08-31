using System.Text;
using System.Text.RegularExpressions;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Converts every citation-backed SOW work package into a complete five-phase
/// FlowHive execution chain. The language model identifies the governed scope;
/// this builder deterministically guarantees Plan, Design, Implement, Validate,
/// and Release coverage, detailed execution fields, citation continuity, and
/// schedule-safe predecessor logic.
/// </summary>
public static class ProjectFlowHiveDetailedPlanBuilder
{
    public const string DetailContract = "flowhive-five-phase-detailed-work-package-v1-20260818";

    private static readonly PhaseProfile[] Phases =
    [
        new("1", "Plan", 0.15m,
            "Establish the complete delivery boundary, inventory every source-backed item that will be used, confirm prerequisites and responsibilities, and make unresolved facts visible before design begins."),
        new("2", "Design", 0.20m,
            "Translate the approved scope into a traceable technical and operational design, implementation sequence, validation method, rollback approach, and measurable completion criteria."),
        new("3", "Implement", 0.40m,
            "Perform the authorized work in controlled stages using the approved products, platforms, tools, access, inputs, and procedures while retaining objective evidence for every material action."),
        new("4", "Validate", 0.20m,
            "Verify the implemented result against the cited scope, technical requirements, acceptance criteria, dependencies, security and operational expectations, and record all passed, failed, and repeated checks."),
        new("5", "Release", 0.05m,
            "Complete documentation, knowledge transfer, support ownership, operational handoff, acceptance evidence, outstanding-action ownership, and governed closeout for the work package.")
    ];

    public static ProjectFlowHivePlanRequest Build(
        ProjectFlowHivePlanRequest source,
        PulseAiPrivateFlowHivePlan? privatePlan)
    {
        if (privatePlan is null)
            throw new ArgumentNullException(nameof(privatePlan));

        var workPackages = CanonicalWorkPackages(privatePlan.Tasks)
            .Take(80)
            .ToArray();
        if (workPackages.Length == 0)
            throw new InvalidOperationException("A citation-backed FlowHive work package is required.");

        var generated = new List<ProjectFlowHivePlanTaskInput>(5 + (workPackages.Length * 5));
        var generatedWbs = new Dictionary<(string PackageKey, string Phase), string>();

        foreach (var phase in Phases)
        {
            generated.Add(new ProjectFlowHivePlanTaskInput(
                ClientTaskId: Guid.NewGuid(),
                CanonicalTaskId: null,
                WbsNumber: phase.Wbs,
                ParentWbsNumber: null,
                Name: phase.Name,
                Description: $"{phase.Name} phase summary. Dates, duration, progress, and effort roll up from the detailed citation-backed work packages below.",
                DurationWorkingDays: 0,
                IsMilestone: false,
                ConstraintType: "ASAP",
                ConstraintDate: null,
                PercentComplete: 0m,
                RemainingEffortHours: 0m,
                Status: "not_started",
                IsSummary: true,
                Phase: phase.Name,
                Priority: "summary",
                Comments: $"Generated under {DetailContract}."));

            for (var index = 0; index < workPackages.Length; index++)
            {
                var package = workPackages[index];
                var wbs = $"{phase.Wbs}.{index + 1}";
                generatedWbs[(package.Key, phase.Name)] = wbs;
                generated.Add(BuildPhaseTask(package, phase, index + 1, wbs));
            }
        }

        var dependencies = BuildDependencies(workPackages, generatedWbs);
        var milestones = BuildMilestones(workPackages, generatedWbs, privatePlan.Milestones);
        var assignments = BuildRoleAssignments(generated);
        var notes = BuildPlanNotes(privatePlan, workPackages.Length);

        return source with
        {
            PlanName = Limit(source.PlanName, 240, $"{source.ProjectCode} Celar AI governed plan"),
            RevisionLabel = $"Celar AI detailed Planner review {DateTimeOffset.UtcNow:yyyyMMdd-HHmm}",
            ProjectStartDate = source.ProjectStartDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            Tasks = generated,
            Dependencies = dependencies,
            Assignments = assignments,
            Milestones = milestones,
            Notes = Limit(notes, 12_000, string.Empty),
            CelarAiCitationIds = privatePlan.CitationIds
                .Distinct()
                .OrderBy(value => value)
                .ToArray()
        };
    }

    private static ProjectFlowHivePlanTaskInput BuildPhaseTask(
        CanonicalWorkPackage package,
        PhaseProfile phase,
        int packageNumber,
        string wbs)
    {
        var duration = AllocateDays(package.EstimatedDurationDays)[phase.Name];
        var effort = AllocateHours(package.EstimatedDurationDays, package.EstimatedHours)[phase.Name];
        var citationText = package.CitationIds.Count == 0
            ? "No citation identifier was supplied."
            : $"Private evidence citations: {string.Join(", ", package.CitationIds.Select(id => $"[{id}]"))}.";
        var requiredRoles = package.RequiredRoles.Count == 0
            ? "Project Manager and accountable Engineering reviewer"
            : string.Join(", ", package.RequiredRoles);

        return new ProjectFlowHivePlanTaskInput(
            ClientTaskId: Guid.NewGuid(),
            CanonicalTaskId: null,
            WbsNumber: wbs,
            ParentWbsNumber: phase.Wbs,
            Name: Limit($"{phase.Name} — {package.Name}", 300, $"{phase.Name} work package {packageNumber}"),
            Description: Limit(
                $"{phase.Purpose} Source-backed work package: {package.Description} {citationText}",
                4_000,
                $"Complete the {phase.Name.ToLowerInvariant()} work for this cited SOW work package and retain objective evidence."),
            DurationWorkingDays: duration,
            IsMilestone: false,
            ConstraintType: "ASAP",
            ConstraintDate: null,
            PercentComplete: 0m,
            RemainingEffortHours: effort,
            Status: "not_started",
            IsSummary: false,
            Phase: phase.Name,
            DetailedSteps: PhaseSteps(phase.Name, package),
            Inputs: PhaseInputs(phase.Name, package, requiredRoles),
            Outputs: PhaseOutputs(phase.Name, package),
            AcceptanceCriteria: PhaseAcceptanceCriteria(phase.Name, package),
            ValidationSteps: PhaseValidationSteps(phase.Name, package),
            CustomerResponsibilities: PhaseCustomerResponsibilities(phase.Name, package),
            UsSignalResponsibilities: PhaseUsSignalResponsibilities(phase.Name, package),
            Prerequisites: PhasePrerequisites(phase.Name, package),
            Risks: PhaseRisks(phase.Name, package),
            OpenQuestions: PhaseOpenQuestions(phase.Name, package)
                .Concat(TechnicalGapQuestions(package))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Priority: package.Priority,
            CitationIds: package.CitationIds,
            Comments: $"Source work package {packageNumber}; deterministic {phase.Name} expansion under {DetailContract}.",
            Notes: Limit(
                $"Required roles: {requiredRoles}. Duration and effort remain planning estimates until PM and Engineering review. "
                + $"Source WBS references: {string.Join(", ", package.SourceWbs.DefaultIfEmpty("not supplied"))}. "
                + $"Source assumption flag: {(package.IsAssumption ? "yes" : "no")}. {citationText}",
                4_000,
                string.Empty),
            Products: Combine(24, 1_000, package.Products, TechnicalInventory(package, "product", "appliance", "solution")),
            Platforms: Combine(24, 1_000, package.Platforms, TechnicalInventory(package, "platform", "cloud", "operating system", "hypervisor")),
            Manufacturers: Combine(24, 1_000, package.Manufacturers, TechnicalInventory(package, "manufacturer", "vendor", "cisco", "microsoft", "nutanix", "vmware", "dell", "hpe")),
            Models: Combine(24, 1_000, package.Models, TechnicalInventory(package, "model", "sku", "part number")),
            SoftwareVersions: Combine(24, 1_000, package.SoftwareVersions, TechnicalInventory(package, "software", "version", "release", "edition")),
            FirmwareVersions: Combine(24, 1_000, package.FirmwareVersions, TechnicalInventory(package, "firmware", "bios")),
            LicensingRequirements: Combine(24, 1_000, package.LicensingRequirements, TechnicalInventory(package, "license", "licensing", "subscription", "entitlement")),
            Quantities: Combine(24, 1_000, package.Quantities, TechnicalInventory(package, "quantity", "count", "total", "each", "units", "devices", "servers")),
            Tools: Combine(24, 1_000, package.Tools, TechnicalInventory(package, "tool", "utility", "console", "portal", "cli")),
            Systems: Combine(24, 1_000, package.Systems, TechnicalInventory(package, "system", "server", "cluster", "tenant", "application", "database")),
            Interfaces: Combine(24, 1_000, package.Interfaces, TechnicalInventory(package, "interface", "api", "protocol", "port", "endpoint")),
            IntegrationPoints: Combine(24, 1_000, package.IntegrationPoints, TechnicalInventory(package, "integrat", "connect", "federat", "synchron")),
            AccessRequirements: Combine(24, 1_000, package.AccessRequirements, TechnicalInventory(package, "access", "permission", "credential", "account", "role")),
            RollbackSteps: Combine(24, 1_000, package.RollbackSteps, TechnicalInventory(package, "rollback", "backout", "restore", "revert", "backup")),
            Assumptions: Combine(
                24,
                1_000,
                package.Assumptions,
                package.IsAssumption ? new[] { package.Description } : [],
                TechnicalInventory(package, "assum", "subject to", "dependent on")),
            RequiredRoles: package.RequiredRoles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static IReadOnlyList<ProjectFlowHivePlanMilestoneInput> BuildMilestones(
        IReadOnlyList<CanonicalWorkPackage> workPackages,
        IReadOnlyDictionary<(string PackageKey, string Phase), string> generatedWbs,
        IReadOnlyList<PulseAiPrivateFlowHiveMilestone> sourceMilestones)
    {
        var milestones = workPackages.Select(package =>
            new ProjectFlowHivePlanMilestoneInput(
                Guid.NewGuid(),
                Limit($"{package.Name} accepted and released", 300, "Work package accepted and released"),
                Limit(
                    $"Complete the cited release, handoff, and acceptance gate for {package.Description}",
                    2_000,
                    "Complete the cited release and acceptance gate."),
                generatedWbs[(package.Key, "Release")],
                null,
                package.AcceptanceCriteria
                    .Concat(package.ValidationSteps)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(20)
                    .DefaultIfEmpty("Retain approved acceptance and release evidence for the cited work package.")
                    .ToArray(),
                package.CitationIds,
                package.IsAssumption)).ToList();

        foreach (var milestone in sourceMilestones.Take(100))
        {
            var citations = milestone.CitationIds.Distinct().OrderBy(value => value).ToArray();
            if (citations.Length == 0) continue;
            var related = workPackages.FirstOrDefault(package => package.CitationIds.Intersect(citations).Any());
            var predecessor = related is null
                ? generatedWbs[(workPackages[^1].Key, "Release")]
                : generatedWbs[(related.Key, "Release")];
            milestones.Add(new ProjectFlowHivePlanMilestoneInput(
                Guid.NewGuid(),
                Limit(milestone.Name, 300, "Cited project milestone"),
                Limit(milestone.Description, 2_000, "Complete the cited project milestone."),
                predecessor,
                null,
                milestone.AcceptanceEvidence
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(20)
                    .DefaultIfEmpty("Retain objective acceptance evidence for the cited milestone.")
                    .ToArray(),
                citations,
                milestone.IsAssumption));
        }

        return milestones
            .GroupBy(item => $"{item.PredecessorWbs}|{item.Name}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(250)
            .ToArray();
    }

    private static IReadOnlyList<ProjectFlowHivePlanAssignmentInput> BuildRoleAssignments(
        IReadOnlyList<ProjectFlowHivePlanTaskInput> tasks)
    {
        return tasks
            .Where(task => !task.IsSummary && !task.IsMilestone)
            .Select(task => new
            {
                Task = task,
                Role = (task.RequiredRoles ?? [])
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                    ?? "Required delivery role — PM review required"
            })
            .Select(item => new ProjectFlowHivePlanAssignmentInput(
                item.Task.WbsNumber,
                null,
                item.Role,
                100m,
                Math.Max(0.25m, item.Task.RemainingEffortHours)))
            .Take(5_000)
            .ToArray();
    }

    private static IReadOnlyList<ProjectFlowHiveDependencyInput> BuildDependencies(
        IReadOnlyList<CanonicalWorkPackage> workPackages,
        IReadOnlyDictionary<(string PackageKey, string Phase), string> generatedWbs)
    {
        var dependencies = new List<ProjectFlowHiveDependencyInput>();
        var sourceWbsToPackage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in workPackages)
        {
            foreach (var sourceWbs in package.SourceWbs)
            {
                if (!string.IsNullOrWhiteSpace(sourceWbs))
                    sourceWbsToPackage[sourceWbs.Trim()] = package.Key;
            }
        }

        foreach (var package in workPackages)
        {
            for (var phaseIndex = 1; phaseIndex < Phases.Length; phaseIndex++)
            {
                dependencies.Add(new ProjectFlowHiveDependencyInput(
                    generatedWbs[(package.Key, Phases[phaseIndex - 1].Name)],
                    generatedWbs[(package.Key, Phases[phaseIndex].Name)],
                    "FS",
                    0));
            }

            foreach (var predecessorSourceWbs in package.Predecessors)
            {
                if (!sourceWbsToPackage.TryGetValue(predecessorSourceWbs, out var predecessorPackageKey)
                    || predecessorPackageKey.Equals(package.Key, StringComparison.OrdinalIgnoreCase))
                    continue;

                dependencies.Add(new ProjectFlowHiveDependencyInput(
                    generatedWbs[(predecessorPackageKey, "Release")],
                    generatedWbs[(package.Key, "Plan")],
                    "FS",
                    0));
            }
        }

        return dependencies
            .GroupBy(item => $"{item.PredecessorWbs}|{item.SuccessorWbs}|{item.Type}|{item.LagWorkingDays}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(4_000)
            .ToArray();
    }

    private static IReadOnlyList<CanonicalWorkPackage> CanonicalWorkPackages(
        IReadOnlyList<PulseAiPrivateFlowHiveTask> sourceTasks)
    {
        var packages = new Dictionary<string, CanonicalWorkPackage>(StringComparer.OrdinalIgnoreCase);
        var order = 0;

        foreach (var task in sourceTasks.Take(450))
        {
            var citations = task.CitationIds
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
            if (citations.Length == 0) continue;

            var name = CanonicalName(task.Name);
            var key = WorkPackageKey(name, citations, task.Wbs, order);

            if (!packages.TryGetValue(key, out var package))
            {
                package = new CanonicalWorkPackage(
                    Key: key,
                    Order: order++,
                    Name: Limit(name, 240, $"Cited SOW work package {order}"),
                    Description: Limit(task.Description, 3_000, "Complete the cited SOW scope outcome."),
                    EstimatedDurationDays: Math.Max(1m, task.EstimatedDurationDays),
                    EstimatedHours: task.EstimatedHours,
                    IsAssumption: task.IsAssumption,
                    Priority: PlanningPriority(task.Priority));
                packages[key] = package;
            }

            package.Merge(task, citations);
        }

        return packages.Values
            .OrderBy(package => package.Order)
            .ToArray();
    }

    private static string CanonicalName(string? value)
    {
        var clean = Limit(value, 300, "Cited SOW work package");
        clean = Regex.Replace(
            clean,
            @"^(?:\d+(?:\.\d+)*\s*[-.:]?\s*)?(?:plan|design|implement|implementation|validate|validation|release)(?:\s+(?:phase|task|work\s*package))?\s*[-:–—]\s*",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return clean.Length == 0 ? "Cited SOW work package" : clean;
    }

    private static string WorkPackageKey(
        string name,
        IReadOnlyList<int> citationIds,
        string? sourceWbs,
        int order)
    {
        var normalizedName = Regex.Replace(
                name.ToLowerInvariant(),
                @"[^a-z0-9]+",
                " ",
                RegexOptions.CultureInvariant)
            .Trim();
        var citationKey = citationIds.Count == 0
            ? "no-citation"
            : string.Join("-", citationIds);
        var fallback = string.IsNullOrWhiteSpace(sourceWbs) ? order.ToString() : sourceWbs.Trim();
        return $"{citationKey}|{(normalizedName.Length == 0 ? fallback : normalizedName)}";
    }

    private static IReadOnlyDictionary<string, int> AllocateDays(decimal estimatedDurationDays)
    {
        var total = Math.Max(5, (int)Math.Ceiling(Math.Max(1m, estimatedDurationDays)));
        var allocated = Phases.ToDictionary(phase => phase.Name, _ => 1, StringComparer.OrdinalIgnoreCase);
        var remaining = total - Phases.Length;
        if (remaining <= 0) return allocated;

        var provisional = Phases.Select(phase => new
        {
            phase.Name,
            Whole = (int)Math.Floor(remaining * phase.Weight),
            Fraction = (remaining * phase.Weight) - Math.Floor(remaining * phase.Weight)
        }).ToArray();
        foreach (var item in provisional) allocated[item.Name] += item.Whole;

        var undistributed = total - allocated.Values.Sum();
        foreach (var item in provisional.OrderByDescending(item => item.Fraction).ThenBy(item => Array.FindIndex(Phases, phase => phase.Name == item.Name)).Take(undistributed))
            allocated[item.Name]++;
        return allocated;
    }

    private static IReadOnlyDictionary<string, decimal> AllocateHours(
        decimal estimatedDurationDays,
        decimal? estimatedHours)
    {
        var days = AllocateDays(estimatedDurationDays);
        var total = Math.Max(days.Values.Sum() * 8m, estimatedHours ?? 0m);
        var allocated = Phases.ToDictionary(
            phase => phase.Name,
            phase => Math.Round(total * phase.Weight, 2, MidpointRounding.AwayFromZero),
            StringComparer.OrdinalIgnoreCase);
        var difference = total - allocated.Values.Sum();
        allocated["Implement"] = Math.Max(0.25m, allocated["Implement"] + difference);
        return allocated;
    }


    private static IReadOnlyList<string> TechnicalInventory(
        CanonicalWorkPackage package,
        params string[] terms)
    {
        var source = new[] { package.Name, package.Description }
            .Concat(package.DetailedSteps)
            .Concat(package.Inputs)
            .Concat(package.Outputs)
            .Concat(package.AcceptanceCriteria)
            .Concat(package.ValidationSteps)
            .Concat(package.CustomerResponsibilities)
            .Concat(package.UsSignalResponsibilities)
            .Concat(package.Prerequisites)
            .Concat(package.Risks)
            .Concat(package.OpenQuestions)
            .Where(value => !string.IsNullOrWhiteSpace(value));
        return source
            .Where(value => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Select(value => Limit(value, 1_000, string.Empty))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToArray();
    }

    private static IReadOnlyList<string> TechnicalGapQuestions(CanonicalWorkPackage package)
    {
        var questions = new List<string>();
        void Require(string question, params string[] terms)
        {
            if (TechnicalInventory(package, terms).Count == 0) questions.Add(question);
        }
        if (package.Products.Count + package.Platforms.Count + package.Manufacturers.Count + package.Models.Count == 0)
            Require("Confirm every product, platform, manufacturer, and model required for this work package; the SOW evidence did not state all of them.", "product", "platform", "manufacturer", "vendor", "model");
        if (package.SoftwareVersions.Count + package.FirmwareVersions.Count == 0)
            Require("Confirm applicable software and firmware versions and whether upgrades or compatibility constraints apply.", "software", "version", "firmware", "bios", "release");
        if (package.LicensingRequirements.Count + package.Quantities.Count == 0)
            Require("Confirm licensing, subscription, entitlement, and quantity requirements before implementation.", "license", "licensing", "subscription", "entitlement", "quantity", "count");
        if (package.Tools.Count + package.Systems.Count + package.Interfaces.Count + package.IntegrationPoints.Count + package.AccessRequirements.Count == 0)
            Require("Confirm the approved tools, systems, interfaces, integration points, and access path needed to perform and validate the work.", "tool", "system", "interface", "api", "integrat", "access", "permission");
        if (package.RollbackSteps.Count == 0)
            Require("Confirm the reviewed rollback or backout procedure and the objective trigger for invoking it.", "rollback", "backout", "restore", "revert");
        return questions;
    }

    private static IReadOnlyList<string> PhaseSteps(string phase, CanonicalWorkPackage package)
    {
        var sourceSteps = package.DetailedSteps
            .Select(step => $"Execute the source-backed work instruction without expanding scope: {step}")
            .Take(12)
            .ToArray();
        var values = phase switch
        {
            "Plan" => new[]
            {
                $"Review citations {CitationLabel(package)} and restate the exact in-scope outcome, deliverables, exclusions, quantities, locations, responsibilities, dependencies, and acceptance language for {package.Name}.",
                "Create a complete work-package inventory of every source-backed product, platform, service, model, software or firmware version, license, site, environment, interface, data source, tool, access method, credential dependency, and accountable role that will be used; record every unsupported item as an open question rather than assuming it.",
                "Confirm customer and US Signal stakeholders, decision owners, technical owners, approvers, support owners, communication cadence, escalation paths, and required review gates.",
                "Verify prerequisites including approved source documents, current-state information, access, maintenance or change windows, backups, licensing, network reachability, dependencies, customer inputs, security requirements, and rollback readiness.",
                "Develop the bounded delivery sequence, dependency logic, planned evidence, schedule assumptions, risks, issue triggers, and change-control path for this work package.",
                "Record unresolved facts with owners and due dates, then obtain PM and Engineering review that the package is ready to proceed to design without unsupported scope or commitments."
            },
            "Design" => new[]
            {
                $"Translate the cited outcome for {package.Name} into explicit functional, technical, security, operational, support, and acceptance requirements with one-to-one traceability to the source evidence.",
                "Document the current state and target state for every source-backed product, platform, component, version, site, environment, interface, dependency, data flow, account or access boundary, and management tool involved in the work package.",
                "Define the proposed configuration, architecture, implementation sequence, integration behavior, naming, addressing, capacity, availability, security, monitoring, logging, backup, recovery, and support requirements that are supported by the evidence.",
                "Create detailed implementation instructions, pre-checks, checkpoints, stop conditions, rollback triggers, rollback steps, communication actions, evidence requirements, and post-change verification steps.",
                "Map measurable validation cases and acceptance criteria to each design requirement, component, interface, dependency, and expected output.",
                "Conduct PM and Engineering design review, resolve or assign every exception, and preserve the approved design decision record before implementation begins."
            },
            "Implement" => Combine(20, 1_200,
                new[]
                {
                    $"Confirm the approved design, citations {CitationLabel(package)}, exact product and version inventory, access, licenses, tools, backups, dependencies, change window, communications, monitoring, and rollback capability before changing {package.Name}.",
                    "Perform the authorized configuration, deployment, migration, integration, installation, upgrade, remediation, documentation, or other scoped action in controlled stages and in the approved sequence.",
                    "For each stage, identify the actor, input, command or configuration action, affected component, expected result, validation checkpoint, captured evidence, and condition required before advancing.",
                    "Capture before-and-after state, configuration records, output files, logs, screenshots or command evidence as appropriate, timestamps, decisions, deviations, failed actions, and rollback or recovery activity without recording secrets in the plan.",
                    "Stop and escalate when scope, access, licensing, dependency, safety, security, rollback, acceptance, or evidence requirements are not satisfied; do not silently substitute a product, method, location, or commitment.",
                    "Update the as-built record and implementation status, then confirm readiness for formal validation with the accountable PM and Engineering reviewer."
                },
                sourceSteps),
            "Validate" => new[]
            {
                $"Review the implemented state, cited scope, approved design, acceptance criteria, and required evidence for {package.Name} before executing validation.",
                "Execute component, configuration, connectivity, integration, functional, security, performance, resiliency, monitoring, logging, backup or recovery, operational, and regression checks that apply to every source-backed item used by the work package.",
                "Record each test identifier, prerequisite, input, procedure, expected result, actual result, pass or fail status, evidence location, tester, timestamp, and affected requirement or acceptance criterion.",
                "For every failed or incomplete check, record the defect or exception, impact, accountable owner, approved corrective action, scope effect, schedule effect, and retest requirement.",
                "Apply only authorized corrections, repeat affected checks and regression tests, and retain before-and-after evidence without concealing residual risk or deferred work.",
                "Prepare the acceptance evidence package and obtain PM, Engineering, and required customer or operational review before release."
            },
            _ => new[]
            {
                $"Reconcile the final implemented and validated state of {package.Name} with the cited scope, approved design, acceptance evidence, open actions, and change records.",
                "Finalize the as-built inventory for every product, platform, version, license, site, interface, dependency, management tool, support contact, monitoring item, configuration artifact, and operating procedure used by the work package.",
                "Complete role-appropriate knowledge transfer, demonstrations, runbooks, support instructions, monitoring and escalation procedures, access ownership, backup and recovery information, and known limitation documentation.",
                "Confirm operational ownership, support readiness, documentation location, acceptance status, unresolved action owners and due dates, warranty or support boundaries, and customer communication requirements.",
                "Obtain the required handoff and acceptance evidence, archive governed artifacts, record lessons learned, and ensure no customer delivery or completion claim exceeds the approved evidence.",
                "Close the work package only after PM and Engineering confirm deliverable status, exceptions, follow-up ownership, retention requirements, and the separately governed baseline or customer-delivery decision."
            }
        };
        return Combine(20, 1_200, values);
    }

    private static IReadOnlyList<string> PhaseInputs(string phase, CanonicalWorkPackage package, string requiredRoles) =>
        Combine(30, 1_200,
            new[]
            {
                $"Approved citations {CitationLabel(package)} and the exact SOW scope, deliverables, exclusions, responsibilities, dependencies, and acceptance requirements for this work package.",
                "Source-backed inventory of products, platforms, services, models, versions, licenses, sites, environments, interfaces, data, tools, access dependencies, quantities, and configuration or design artifacts used by this work package.",
                $"Required delivery roles: {requiredRoles}.",
                $"Approved {phase.ToLowerInvariant()} prerequisites, decisions, change controls, communications, evidence standards, and review criteria."
            },
            package.Inputs);

    private static IReadOnlyList<string> PhaseOutputs(string phase, CanonicalWorkPackage package)
    {
        var phaseOutputs = phase switch
        {
            "Plan" => new[]
            {
                "Approved work-package scope and usage inventory covering all source-backed products, platforms, versions, licenses, sites, interfaces, tools, access, data, dependencies, roles, quantities, assumptions, and unresolved questions.",
                "Delivery sequence, dependency map, responsibility matrix, communication and escalation plan, risk and issue register entries, evidence plan, and readiness decision."
            },
            "Design" => new[]
            {
                "Traceable current-state and target-state design with component, interface, dependency, configuration, security, operational, monitoring, support, validation, and rollback details.",
                "Reviewed implementation procedure, test and acceptance matrix, rollback plan, decision record, and assigned design exceptions."
            },
            "Implement" => new[]
            {
                "Completed source-authorized implementation output with before-and-after evidence, configuration records, logs or other objective artifacts, deviations, and outstanding actions.",
                "Updated as-built state and implementation-readiness record for formal validation."
            },
            "Validate" => new[]
            {
                "Complete validation and regression evidence mapped to requirements, source citations, components, interfaces, acceptance criteria, failures, corrections, retests, and residual risks.",
                "PM and Engineering-reviewed acceptance evidence package and release-readiness decision."
            },
            _ => new[]
            {
                "Final as-built inventory, operating documentation, runbooks, knowledge-transfer evidence, monitoring and support ownership, escalation information, known limitations, and open-action register.",
                "Handoff, acceptance, archival, lessons-learned, and governed closeout evidence for separate baseline or customer-delivery review."
            }
        };
        return Combine(30, 1_200, phaseOutputs, package.Outputs);
    }

    private static IReadOnlyList<string> PhaseAcceptanceCriteria(string phase, CanonicalWorkPackage package)
    {
        var criteria = new[]
        {
            $"Every {phase.ToLowerInvariant()} output is traceable to citations {CitationLabel(package)} and contains no unsupported product, quantity, location, technical fact, date, completion claim, or customer commitment.",
            "Every required input, product, version, license, tool, access dependency, interface, role, responsibility, prerequisite, output, validation result, exception, and open question is explicitly recorded or assigned for resolution.",
            "The accountable Project Manager and Engineering reviewer confirm that the task output is complete enough to advance and that all exceptions and residual risks have an owner and disposition."
        };
        return Combine(30, 1_200, criteria, package.AcceptanceCriteria);
    }

    private static IReadOnlyList<string> PhaseValidationSteps(string phase, CanonicalWorkPackage package)
    {
        var steps = new[]
        {
            $"Compare the completed {phase.ToLowerInvariant()} output against the cited scope, approved prerequisites, source-backed usage inventory, responsibilities, deliverables, and measurable acceptance criteria.",
            "Verify that objective evidence exists for every material action, decision, configuration, component, interface, dependency, test, exception, and review gate represented as complete.",
            "Confirm that failed, missing, stale, conflicting, or unsupported information is identified without being converted into a successful result or implicit assumption."
        };
        return Combine(30, 1_200, steps, package.ValidationSteps);
    }

    private static IReadOnlyList<string> PhaseCustomerResponsibilities(string phase, CanonicalWorkPackage package) =>
        Combine(25, 1_200,
            new[]
            {
                $"Provide the source-backed information, decisions, products or services under customer control, licenses, site or environment access, network or system access, data, maintenance windows, contacts, reviews, and acceptance participation required for the {phase.ToLowerInvariant()} task.",
                "Confirm the accuracy and availability of customer-owned prerequisites and promptly identify restrictions, conflicts, unavailable dependencies, or changes that affect scope, schedule, risk, or acceptance.",
                "Review assigned outputs and exceptions within the agreed cadence without sharing credentials or secrets in FlowHive task text."
            },
            package.CustomerResponsibilities);

    private static IReadOnlyList<string> PhaseUsSignalResponsibilities(string phase, CanonicalWorkPackage package) =>
        Combine(25, 1_200,
            new[]
            {
                $"Perform only the authorized {phase.ToLowerInvariant()} activity, use the approved products, platforms, tools, access, procedures, and evidence controls, and remain inside the cited scope boundary.",
                "Protect credentials and sensitive information, preserve objective evidence, document assumptions and deviations, and stop or escalate when a prerequisite, dependency, security control, rollback condition, or acceptance requirement is not satisfied.",
                "Maintain traceability among the SOW work package, design, implementation actions, validation evidence, release artifacts, risks, decisions, and review approvals."
            },
            package.UsSignalResponsibilities);

    private static IReadOnlyList<string> PhasePrerequisites(string phase, CanonicalWorkPackage package) =>
        Combine(30, 1_200,
            new[]
            {
                $"The governing citations {CitationLabel(package)} remain current, authorized, applicable, and unrevoked for this {phase.ToLowerInvariant()} task.",
                "Required products, versions, licensing, sites, environments, interfaces, data, tools, accounts or access, backups, maintenance controls, dependencies, approvals, contacts, communications, monitoring, and rollback capability are confirmed before execution.",
                "Every unresolved prerequisite or decision has an accountable owner and a disposition that permits the task to proceed without guessing."
            },
            package.Prerequisites);

    private static IReadOnlyList<string> PhaseRisks(string phase, CanonicalWorkPackage package) =>
        Combine(30, 1_200,
            new[]
            {
                $"Incomplete or inaccurate product, version, quantity, license, location, interface, dependency, access, data, tool, responsibility, or acceptance information can make the {phase.ToLowerInvariant()} output unusable or expand scope unintentionally.",
                "Missing prerequisites, approvals, backups, rollback capability, maintenance windows, customer inputs, or objective evidence can delay delivery and must be escalated rather than assumed.",
                "A generated plan can omit environment-specific technical nuance; PM and Engineering review remains mandatory before scheduling, assignment, baseline approval, or customer delivery."
            },
            package.Risks);

    private static IReadOnlyList<string> PhaseOpenQuestions(string phase, CanonicalWorkPackage package) =>
        Combine(30, 1_200,
            new[]
            {
                "Which product names, models, software or firmware versions, quantities, licenses, environments, sites, interfaces, tools, accounts, data sources, access methods, dependencies, or responsible roles required for this work package are not explicitly established by the cited evidence?",
                $"Which customer decisions, technical choices, prerequisites, change windows, validation measures, acceptance approvers, support owners, or release conditions must be confirmed before the {phase.ToLowerInvariant()} task can be treated as executable?",
                "Which assumptions, conflicts, exclusions, options, stale values, or missing documents require governed clarification or change control?"
            },
            package.OpenQuestions);

    private static string BuildPlanNotes(PulseAiPrivateFlowHivePlan privatePlan, int workPackageCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Detail contract: {DetailContract}");
        builder.AppendLine($"Objective: {privatePlan.Objective}");
        builder.AppendLine($"Canonical source work packages: {workPackageCount}");
        builder.AppendLine($"Generated executable tasks: {workPackageCount * Phases.Length} plus {Phases.Length} phase summaries.");
        builder.AppendLine("Each source-backed work package is expanded deterministically through Plan, Design, Implement, Validate, and Release. Each phase task retains the source citations and includes ordered steps, all known items being used, roles, inputs, outputs, prerequisites, responsibilities, validation, acceptance criteria, risks, open questions, duration, effort, and dependency logic.");
        builder.AppendLine($"Private evidence citations: {string.Join(", ", privatePlan.CitationIds.Select(id => $"[{id}]"))}");
        builder.AppendLine($"Assumptions: {string.Join(" | ", privatePlan.Assumptions)}");
        builder.AppendLine($"Risks: {string.Join(" | ", privatePlan.Risks)}");
        builder.AppendLine($"Out of scope: {string.Join(" | ", privatePlan.OutOfScopeItems)}");
        builder.AppendLine($"Open questions: {string.Join(" | ", privatePlan.OpenQuestions)}");
        return builder.ToString();
    }

    private static string CitationLabel(CanonicalWorkPackage package) =>
        package.CitationIds.Count == 0
            ? "not supplied"
            : string.Join(", ", package.CitationIds.Select(id => $"[{id}]"));

    private static string PlanningPriority(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "low" => "low",
        "high" => "high",
        "critical" => "critical",
        _ => "normal"
    };

    private static string HigherPriority(string left, string right)
    {
        static int Rank(string value) => value switch
        {
            "critical" => 4,
            "high" => 3,
            "normal" => 2,
            "low" => 1,
            _ => 0
        };
        return Rank(right) > Rank(left) ? right : left;
    }

    private static string[] Combine(
        int maximumItems,
        int maximumLength,
        params IEnumerable<string>?[] groups) =>
        groups
            .Where(group => group is not null)
            .SelectMany(group => group!)
            .Select(value => Limit(value, maximumLength, string.Empty))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maximumItems)
            .ToArray();

    private static string Limit(string? value, int maximumLength, string fallback)
    {
        var clean = value?.Trim() ?? string.Empty;
        if (clean.Length == 0) return fallback;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }

    private sealed record PhaseProfile(string Wbs, string Name, decimal Weight, string Purpose);

    private sealed class CanonicalWorkPackage
    {
        public CanonicalWorkPackage(
            string Key,
            int Order,
            string Name,
            string Description,
            decimal EstimatedDurationDays,
            decimal? EstimatedHours,
            bool IsAssumption,
            string Priority)
        {
            this.Key = Key;
            this.Order = Order;
            this.Name = Name;
            this.Description = Description;
            this.EstimatedDurationDays = EstimatedDurationDays;
            this.EstimatedHours = EstimatedHours;
            this.IsAssumption = IsAssumption;
            this.Priority = Priority;
        }

        public string Key { get; }
        public int Order { get; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public decimal EstimatedDurationDays { get; private set; }
        public decimal? EstimatedHours { get; private set; }
        public bool IsAssumption { get; private set; }
        public string Priority { get; private set; }
        public HashSet<string> SourceWbs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> RequiredRoles { get; } = [];
        public List<string> Predecessors { get; } = [];
        public List<int> CitationIds { get; } = [];
        public List<string> DetailedSteps { get; } = [];
        public List<string> Inputs { get; } = [];
        public List<string> Outputs { get; } = [];
        public List<string> AcceptanceCriteria { get; } = [];
        public List<string> ValidationSteps { get; } = [];
        public List<string> CustomerResponsibilities { get; } = [];
        public List<string> UsSignalResponsibilities { get; } = [];
        public List<string> Prerequisites { get; } = [];
        public List<string> Risks { get; } = [];
        public List<string> OpenQuestions { get; } = [];
        public List<string> Products { get; } = [];
        public List<string> Platforms { get; } = [];
        public List<string> Manufacturers { get; } = [];
        public List<string> Models { get; } = [];
        public List<string> SoftwareVersions { get; } = [];
        public List<string> FirmwareVersions { get; } = [];
        public List<string> LicensingRequirements { get; } = [];
        public List<string> Quantities { get; } = [];
        public List<string> Tools { get; } = [];
        public List<string> Systems { get; } = [];
        public List<string> Interfaces { get; } = [];
        public List<string> IntegrationPoints { get; } = [];
        public List<string> AccessRequirements { get; } = [];
        public List<string> RollbackSteps { get; } = [];
        public List<string> Assumptions { get; } = [];

        public void Merge(PulseAiPrivateFlowHiveTask task, IReadOnlyList<int> citations)
        {
            if (!string.IsNullOrWhiteSpace(task.Wbs)) SourceWbs.Add(task.Wbs.Trim());
            AddDistinct(RequiredRoles, task.RequiredRoles);
            AddDistinct(Predecessors, task.Predecessors);
            foreach (var citation in citations)
                if (!CitationIds.Contains(citation)) CitationIds.Add(citation);
            CitationIds.Sort();
            AddDistinct(DetailedSteps, task.DetailedSteps);
            AddDistinct(Inputs, task.Inputs);
            AddDistinct(Outputs, task.Outputs);
            AddDistinct(AcceptanceCriteria, task.AcceptanceCriteria);
            AddDistinct(ValidationSteps, task.ValidationSteps);
            AddDistinct(CustomerResponsibilities, task.CustomerResponsibilities);
            AddDistinct(UsSignalResponsibilities, task.UsSignalResponsibilities);
            AddDistinct(Prerequisites, task.Prerequisites);
            AddDistinct(Risks, task.Risks);
            AddDistinct(OpenQuestions, task.OpenQuestions);
            AddDistinct(Products, task.Products);
            AddDistinct(Platforms, task.Platforms);
            AddDistinct(Manufacturers, task.Manufacturers);
            AddDistinct(Models, task.Models);
            AddDistinct(SoftwareVersions, task.SoftwareVersions);
            AddDistinct(FirmwareVersions, task.FirmwareVersions);
            AddDistinct(LicensingRequirements, task.LicensingRequirements);
            AddDistinct(Quantities, task.Quantities);
            AddDistinct(Tools, task.Tools);
            AddDistinct(Systems, task.Systems);
            AddDistinct(Interfaces, task.Interfaces);
            AddDistinct(IntegrationPoints, task.IntegrationPoints);
            AddDistinct(AccessRequirements, task.AccessRequirements);
            AddDistinct(RollbackSteps, task.RollbackSteps);
            AddDistinct(Assumptions, task.Assumptions);

            var candidateName = CanonicalName(task.Name);
            if (candidateName.Length > Name.Length) Name = Limit(candidateName, 240, Name);
            var candidateDescription = Limit(task.Description, 3_000, string.Empty);
            if (candidateDescription.Length > Description.Length) Description = candidateDescription;
            EstimatedDurationDays = Math.Max(EstimatedDurationDays, Math.Max(1m, task.EstimatedDurationDays));
            EstimatedHours = Math.Max(EstimatedHours ?? 0m, task.EstimatedHours ?? 0m);
            IsAssumption |= task.IsAssumption;
            Priority = HigherPriority(Priority, PlanningPriority(task.Priority));
        }

        private static void AddDistinct(List<string> target, IEnumerable<string>? values)
        {
            foreach (var value in values ?? [])
            {
                var clean = Limit(value, 2_000, string.Empty);
                if (clean.Length > 0 && !target.Contains(clean, StringComparer.OrdinalIgnoreCase))
                    target.Add(clean);
            }
        }
    }
}
