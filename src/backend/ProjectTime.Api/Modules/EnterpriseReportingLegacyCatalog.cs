namespace ProjectTime.Api.Modules;

/// <summary>
/// Familiar ChangePoint-era report identities retained in the new Analytics
/// Center. These definitions reuse the current server-scoped report engine,
/// branded exports, immutable run history, and Module 065 scheduling boundary.
/// </summary>
internal static partial class EnterpriseReportingCatalog
{
    private static readonly EnterpriseReportDefinition[] LegacyAndExtendedReports =
        LegacyExecutiveAndFinancialReports
            .Concat(LegacyPeopleAndTimeReports)
            .Concat(LegacyDeliveryReports)
            .Concat(LegacyOperationsReports)
            .ToArray();

    private static EnterpriseReportDefinition Alias(
        string code,
        string name,
        string category,
        string description,
        string[] modules,
        string[] requiredSources,
        string[] optionalSources,
        string[] audience,
        EnterpriseReportFilterDefinition[] filters,
        EnterpriseReportColumnDefinition[] columns) =>
        Report(code, name, category, description, modules, requiredSources, optionalSources,
            audience, "Rows and filters are limited by the server-authorized role, project, person, and financial scope.", filters, columns);
}
