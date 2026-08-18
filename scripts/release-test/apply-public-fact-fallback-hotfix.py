from pathlib import Path


def read(path: str) -> str:
    return Path(path).read_text()


def write(path: str, source: str) -> None:
    Path(path).write_text(source)


def replace_once(path: str, old: str, new: str) -> None:
    source = read(path)
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected one replacement target, found {count}: {old[:180]!r}")
    write(path, source.replace(old, new, 1))


service = "src/backend/ProjectTime.Api/Ai/CelarAiAuthoritativePublicFactService.cs"
replace_once(
    service,
    '''    private static readonly Uri UsSignalLeadership =
        new("https://ussignal.com/why-us-signal/leadership/", UriKind.Absolute);
''',
    '''    private static readonly Uri UsSignalLeadership =
        new("https://ussignal.com/why-us-signal/leadership/", UriKind.Absolute);
    private static readonly Uri UsSignalTechElite2026 =
        new("https://ussignal.com/press-releases/crn-tech-elite-250-2026/", UriKind.Absolute);
    private static readonly Uri UsSignalSolutionProvider2026 =
        new("https://ussignal.com/press-releases/crn-solution-provider-500-2026/", UriKind.Absolute);
'''
)
replace_once(
    service,
    '''    private static readonly Regex ChiefExecutiveName = new(
        @"(?<name>(?:[A-Z][A-Za-z'’.-]+|[A-Z]\\.)(?:\\s+(?:[A-Z][A-Za-z'’.-]+|[A-Z]\\.)){1,4})\\s*,?\\s+(?i:Chief\\s+Executive\\s+Officer)\\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
''',
    '''    private static readonly Regex ChiefExecutiveName = new(
        @"(?<name>(?:[A-Z][A-Za-z'’.-]+|[A-Z]\\.)(?:\\s+(?:[A-Z][A-Za-z'’.-]+|[A-Z]\\.)){1,4})\\s*,?\\s+(?i:(?:Chief\\s+Executive\\s+Officer|CEO))\\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
'''
)
replace_once(
    service,
    '''    private async Task<PulseAiSystemQuestionResult> VerifyUsSignalChiefExecutiveAsync(
        PulseAiSystemQuestionResult result,
        CancellationToken cancellationToken)
    {
        var page = await RetrieveAsync(
            UsSignalLeadership,
            "us_signal_leadership",
            "US Signal — Leadership Team",
            cancellationToken);
        if (!page.Succeeded) return FailClosed(result, page.DiagnosticCode);

        var names = ExtractNames(ChiefExecutiveName, page.Text).ToArray();
        var chiefExecutive = ResolveCanonicalPersonName(names);
        if (chiefExecutive is null && names.Length == 0) return FailClosed(result, "official_source_claim_not_found");
        if (chiefExecutive is null)
            return Conflict(result, [page], $"The official source returned conflicting US Signal chief executive names: {string.Join(", ", names)}.");

        var conclusion = $"The Chief Executive Officer of US Signal is {chiefExecutive}. [1]";
        return Verified(result, conclusion, [page]);
    }
''',
    '''    private async Task<PulseAiSystemQuestionResult> VerifyUsSignalChiefExecutiveAsync(
        PulseAiSystemQuestionResult result,
        CancellationToken cancellationToken)
    {
        var profiles = new[]
        {
            (Uri: UsSignalLeadership, SourceCode: "us_signal_leadership", SourceName: "US Signal — Leadership Team"),
            (Uri: UsSignalTechElite2026, SourceCode: "us_signal_tech_elite_2026", SourceName: "US Signal — CRN Tech Elite 250 for 2026"),
            (Uri: UsSignalSolutionProvider2026, SourceCode: "us_signal_solution_provider_2026", SourceName: "US Signal — CRN Solution Provider 500 for 2026")
        };
        var diagnostics = new List<string>();

        foreach (var profile in profiles)
        {
            try
            {
                var page = await RetrieveAsync(
                    profile.Uri,
                    profile.SourceCode,
                    profile.SourceName,
                    cancellationToken);
                if (!page.Succeeded)
                {
                    diagnostics.Add($"{profile.SourceCode}:{page.DiagnosticCode}");
                    continue;
                }

                var names = ExtractNames(ChiefExecutiveName, page.Text).ToArray();
                var chiefExecutive = ResolveCanonicalPersonName(names);
                if (chiefExecutive is null && names.Length == 0)
                {
                    diagnostics.Add($"{profile.SourceCode}:official_source_claim_not_found");
                    continue;
                }
                if (chiefExecutive is null)
                    return Conflict(result, [page], $"The official source returned conflicting US Signal chief executive names: {string.Join(", ", names)}.");

                var conclusion = $"The Chief Executive Officer of US Signal is {chiefExecutive}. [1]";
                return Verified(result, conclusion, [page]);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                diagnostics.Add($"{profile.SourceCode}:authoritative_public_retrieval_timeout");
            }
            catch (Exception exception)
            {
                var diagnostic = Diagnostic(exception);
                diagnostics.Add($"{profile.SourceCode}:{diagnostic}");
                _logger.LogWarning(
                    exception,
                    "US Signal authoritative public source failed; trying the next official source. Source={SourceCode} Diagnostic={Diagnostic}",
                    profile.SourceCode,
                    diagnostic);
            }
        }

        var combinedDiagnostic = diagnostics.Count == 0
            ? "official_us_signal_sources_unavailable"
            : $"official_us_signal_sources_unavailable:{string.Join("|", diagnostics)}";
        return FailClosed(result, combinedDiagnostic);
    }
'''
)

public_fact_tests = "tests/CelarAiAuthoritativePublicFactTests/Program.cs"
replace_once(
    public_fact_tests,
    '''    ["https://ussignal.com/why-us-signal/leadership/"] = "<html><h2>Dan Watts, Chief Executive Officer</h2></html>"
''',
    '''    ["https://ussignal.com/why-us-signal/leadership/"] = "<html><h2>Dan Watts, Chief Executive Officer</h2></html>",
    ["https://ussignal.com/press-releases/crn-tech-elite-250-2026/"] = "<html><p>Daniel Watts, CEO of US Signal</p></html>",
    ["https://ussignal.com/press-releases/crn-solution-provider-500-2026/"] = "<html><p>Daniel Watts, Chief Executive Officer at US Signal</p></html>"
'''
)
replace_once(
    public_fact_tests,
    '''Require(wrongUsSignalProvider.Sources.Count == 1
    && wrongUsSignalProvider.Sources[0].Path.Contains("ussignal.com/why-us-signal/leadership", StringComparison.OrdinalIgnoreCase),
    "US Signal CEO answer cites the official leadership page");

var previousEnabled = Environment.GetEnvironmentVariable("PROJECTPULSE_CELAR_AI_CURRENT_PUBLIC_FACTS_ENABLED");
''',
    '''Require(wrongUsSignalProvider.Sources.Count == 1
    && wrongUsSignalProvider.Sources[0].Path.Contains("ussignal.com/why-us-signal/leadership", StringComparison.OrdinalIgnoreCase),
    "US Signal CEO answer cites the official leadership page");

var usSignalFallbackFactory = new StubHttpClientFactory(uri =>
{
    if (uri.AbsolutePath.Contains("/why-us-signal/leadership/", StringComparison.OrdinalIgnoreCase))
        return Response(HttpStatusCode.GatewayTimeout, "leadership source timed out");
    if (uri.AbsolutePath.Contains("/press-releases/crn-tech-elite-250-2026/", StringComparison.OrdinalIgnoreCase))
        return Response(HttpStatusCode.OK, "<html><p>Daniel Watts, CEO of US Signal</p></html>");
    return Response(HttpStatusCode.NotFound, "not found");
});
var usSignalFallbackService = new CelarAiAuthoritativePublicFactService(
    usSignalFallbackFactory,
    NullLogger<CelarAiAuthoritativePublicFactService>.Instance);
var usSignalFallback = await usSignalFallbackService.VerifyAsync(
    Result("completed", "general_knowledge", "A provider did not establish the current CEO."),
    usSignalPlan,
    "Who is the CEO of US Signal?",
    CancellationToken.None);
Require(usSignalFallback.Answer.DirectConclusion.Contains("Daniel Watts", StringComparison.OrdinalIgnoreCase),
    "a failed leadership request falls back to a second official US Signal source");
Require(usSignalFallback.Sources.Count == 1
    && usSignalFallback.Sources[0].Path.Contains("crn-tech-elite-250-2026", StringComparison.OrdinalIgnoreCase),
    "the fallback answer cites the successful official US Signal press release");
Require(usSignalFallbackFactory.Requests.Count == 2,
    "the US Signal fallback stops after the first successful official source");

var previousEnabled = Environment.GetEnvironmentVariable("PROJECTPULSE_CELAR_AI_CURRENT_PUBLIC_FACTS_ENABLED");
'''
)
replace_once(
    public_fact_tests,
    '''Console.WriteLine("CELAR_AI_US_SIGNAL_CEO_TEST=PASS");
''',
    '''Console.WriteLine("CELAR_AI_US_SIGNAL_CEO_TEST=PASS");
Console.WriteLine("CELAR_AI_US_SIGNAL_CEO_FALLBACK_TEST=PASS");
'''
)

Path("scripts/release-test/apply-public-fact-fallback-hotfix.py").unlink(missing_ok=True)
