using System.Text.Json;

namespace ProjectTime.Api.Modules;

public sealed record Module025SowGsdCreateRequest(
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerEntryMode,
    string? CommercialModel,
    string? CustomerProgram,
    Guid? AccountExecutiveUserId,
    Guid? ResaleUserId,
    string? ServiceOverview);

public sealed record Module025SowGsdPhaseSaveRequest(
    string? PhaseCode,
    decimal? FinalHours,
    string? Objective,
    IReadOnlyList<string>? DetailedActivities,
    IReadOnlyList<string>? TechnicalTasks,
    IReadOnlyList<string>? Deliverables,
    IReadOnlyList<string>? CustomerResponsibilities,
    IReadOnlyList<string>? UsSignalResponsibilities,
    IReadOnlyList<string>? Prerequisites,
    IReadOnlyList<string>? Dependencies,
    IReadOnlyList<string>? Assumptions,
    IReadOnlyList<string>? OpenQuestions,
    IReadOnlyList<string>? AcceptanceCriteria,
    IReadOnlyList<string>? ValidationSteps,
    IReadOnlyList<string>? Risks,
    string? LoeRationale);

public sealed record Module025SowGsdSaveRequest(
    int ExpectedRevision,
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerEntryMode,
    string? CommercialModel,
    string? CustomerProgram,
    Guid? AccountExecutiveUserId,
    Guid? ResaleUserId,
    string? ServiceOverview,
    IReadOnlyList<Module025SowGsdPhaseSaveRequest>? Phases);

internal sealed record Module025AccessContext(
    Guid ActualUserId,
    Guid EffectiveUserId,
    string DisplayName,
    string Email,
    string DepartmentName,
    string TeamName,
    IReadOnlySet<string> Roles,
    bool IsViewAs,
    bool IsAdministrator,
    bool IsSolutionArchitect,
    bool IsProtectedTestUatRoleFixture,
    bool IsManager,
    IReadOnlySet<Guid> VisibleSolutionArchitectIds)
{
    internal bool CanCreate => !IsViewAs && (IsSolutionArchitect || IsAdministrator);
    internal bool CanWriteOwned(Guid ownerUserId) =>
        !IsViewAs && (IsAdministrator || (IsSolutionArchitect && ownerUserId == EffectiveUserId));
    internal bool CanViewOwned(Guid ownerUserId) =>
        IsAdministrator || ownerUserId == EffectiveUserId || VisibleSolutionArchitectIds.Contains(ownerUserId);
}

internal sealed record Module025EngagementRow(
    Guid EngagementId,
    string EngagementNumber,
    Guid OwnerUserId,
    string OwnerDisplayName,
    string OwnerDepartmentName,
    string OwnerTeamName,
    Guid? CustomerId,
    string CustomerName,
    string CustomerEntryMode,
    string CommercialModel,
    string CustomerProgram,
    string GsdTemplateKey,
    Guid? AccountExecutiveUserId,
    string AccountExecutiveName,
    Guid? ResaleUserId,
    string ResaleName,
    string ServiceOverview,
    JsonElement SowSections,
    JsonElement AiMetadata,
    string Status,
    bool IsActive,
    int Revision,
    DateTimeOffset? LastGeneratedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<Module025PhaseRow> Phases);

internal sealed record Module025PhaseRow(
    string PhaseCode,
    int SortOrder,
    decimal SuggestedHours,
    decimal FinalHours,
    string Objective,
    IReadOnlyList<string> DetailedActivities,
    IReadOnlyList<string> TechnicalTasks,
    IReadOnlyList<string> Deliverables,
    IReadOnlyList<string> CustomerResponsibilities,
    IReadOnlyList<string> UsSignalResponsibilities,
    IReadOnlyList<string> Prerequisites,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> OpenQuestions,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<string> ValidationSteps,
    IReadOnlyList<string> Risks,
    string LoeRationale,
    IReadOnlyList<int> SourceCitationIds,
    bool AiGenerated,
    DateTimeOffset UpdatedAt);

internal sealed record Module025DocumentModel(
    Module025EngagementRow Engagement,
    IReadOnlyList<Module025PhaseRow> Phases,
    decimal SuggestedHours,
    decimal FinalHours);
