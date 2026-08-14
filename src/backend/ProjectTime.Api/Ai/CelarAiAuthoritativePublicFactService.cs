using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Retrieval-time verification for changing public facts. The service accepts
/// only the already-classified public question and uses a closed, server-owned
/// official-source catalog. It never receives or forwards Pulse records,
/// private documents, attachments, tool responses, identities, financial data,
/// or provider prompts. Model output is treated as draft language, never as
/// evidence.
/// </summary>
public sealed class CelarAiAuthoritativePublicFactService
{
    public const string ContractVersion = "celar-ai-authoritative-public-facts-v1-20260813";
    public const string ClientName = "CelarAiAuthoritativePublicFacts";
    public const int MaximumResponseBytes = 1_000_000;

    private static readonly Uri WhiteHouseAdministration =
        new("https://www.whitehouse.gov/administration/", UriKind.Absolute);
    private static readonly Uri JordanGoverningSystem =
        new("https://rhc.jo/en/jordans-governing-system", UriKind.Absolute);
    private static readonly Uri JordanKing =
        new("https://rhc.jo/en/king-abdullah", UriKind.Absolute);
    private static readonly Uri UsSignalLeadership =
        new("https://ussignal.com/why-us-signal/leadership/", UriKind.Absolute);

    private static readonly HashSet<string> ApprovedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "www.whitehouse.gov",
        "whitehouse.gov",
        "rhc.jo",
        "www.rhc.jo",
        "ussignal.com",
        "www.ussignal.com"
    };

    private static readonly Regex ScriptOrStyle = new(
        @"<(script|style)[^>]*>.*?</\1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex HtmlTag = new(
        @"<[^>]+>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(
        @"\s+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PresidentName = new(
        @"(?<!Vice )\bPresident\s+(?<name>(?:[A-Z][A-Za-z'’.-]*)(?:\s+(?!(?:President|Vice|Administration|White|House|The|First|United|States|His|Her|Majesty|Royal|Court)\b)(?:[A-Z][A-Za-z'’.-]*)){0,4})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex KingName = new(
        @"\bKing\s+(?<name>(?:[A-Z][A-Za-z'’.-]+|II|III|IV)(?:\s+(?:[A-Z][A-Za-z'’.-]+|II|III|IV)){0,4})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ChiefExecutiveName = new(
        @"(?<name>(?:[A-Z][A-Za-z'’.-]+|[A-Z]\.)(?:\s+(?:[A-Z][A-Za-z'’.-]+|[A-Z]\.)){1,4})\s*,?\s+(?i:Chief\s+Executive\s+Officer)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> NameStops = new(StringComparer.OrdinalIgnoreCase)
    {
        "President", "Vice", "Administration", "White", "House", "The", "First",
        "United", "States", "His", "Her", "Majesty", "Royal", "Court"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CelarAiAuthoritativePublicFactService> _logger;

    public CelarAiAuthoritativePublicFactService(
        IHttpClientFactory httpClientFactory,
        ILogger<CelarAiAuthoritativePublicFactService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<PulseAiSystemQuestionResult> VerifyAsync(
        PulseAiSystemQuestionResult result,
        CelarAiUniversalAnswerPlan plan,
        string question,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(question);
        if (LooksLikeInternalJordan(normalized))
            return FailClosed(result, "public_fact_profile_rejected_internal_subject");

        // A named current-officeholder question is inherently time-sensitive even
        // when an upstream planner under-classifies a prompt that omits the word
        // "current". The closed profile catalog remains the authority boundary.
        var recognizedCurrentPublicProfile =
            IsUnitedStatesPresidentQuestion(normalized)
            || IsJordanPresidentQuestion(normalized)
            || IsUsSignalChiefExecutiveQuestion(normalized);
        if (plan.QuestionClass != CelarAiAnswerQuestionClass.PublicCurrent
            && !recognizedCurrentPublicProfile)
            return result;
        if (!Enabled()) return FailClosed(result, "current_public_connector_disabled");

        try
        {
            if (IsUnitedStatesPresidentQuestion(normalized))
                return await VerifyUnitedStatesPresidentAsync(result, cancellationToken);
            if (IsJordanPresidentQuestion(normalized))
                return await VerifyJordanHeadOfStateAsync(result, cancellationToken);
            if (IsUsSignalChiefExecutiveQuestion(normalized))
                return await VerifyUsSignalChiefExecutiveAsync(result, cancellationToken);
            return FailClosed(result, "authoritative_public_profile_unavailable");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FailClosed(result, "authoritative_public_retrieval_timeout");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Authoritative public-fact retrieval failed. Contract={ContractVersion} Diagnostic={Diagnostic}",
                ContractVersion,
                Diagnostic(exception));
            return FailClosed(result, Diagnostic(exception));
        }
    }

    private async Task<PulseAiSystemQuestionResult> VerifyUnitedStatesPresidentAsync(
        PulseAiSystemQuestionResult result,
        CancellationToken cancellationToken)
    {
        var page = await RetrieveAsync(
            WhiteHouseAdministration,
            "white_house_administration",
            "The White House — Administration",
            cancellationToken);
        if (!page.Succeeded) return FailClosed(result, page.DiagnosticCode);

        var names = ExtractNames(PresidentName, page.Text).ToArray();
        var president = ResolveCanonicalPersonName(names);
        if (president is null && names.Length == 0) return FailClosed(result, "official_source_claim_not_found");
        if (president is null)
            return Conflict(result, [page], $"The official source returned conflicting president names: {string.Join(", ", names)}.");

        var conclusion = $"The current President of the United States is {president}. [1]";
        return Verified(result, conclusion, [page]);
    }

    private async Task<PulseAiSystemQuestionResult> VerifyJordanHeadOfStateAsync(
        PulseAiSystemQuestionResult result,
        CancellationToken cancellationToken)
    {
        var governing = await RetrieveAsync(
            JordanGoverningSystem,
            "jordan_governing_system",
            "The Royal Hashemite Court — Jordan's Governing System",
            cancellationToken);
        var king = await RetrieveAsync(
            JordanKing,
            "jordan_current_monarch",
            "The Royal Hashemite Court — King Abdullah",
            cancellationToken);
        if (!governing.Succeeded) return FailClosed(result, governing.DiagnosticCode);
        if (!king.Succeeded) return FailClosed(result, king.DiagnosticCode);

        var governingVerified = governing.Text.Contains("hereditary monarchy", StringComparison.OrdinalIgnoreCase)
            && governing.Text.Contains("king is the head of state", StringComparison.OrdinalIgnoreCase);
        if (!governingVerified) return FailClosed(result, "official_source_governing_system_not_verified");

        var names = ExtractNames(KingName, king.Text).ToArray();
        var monarch = ResolveCanonicalPersonName(names);
        if (monarch is null && names.Length == 0) return FailClosed(result, "official_source_monarch_not_found");
        if (monarch is null)
            return Conflict(result, [governing, king], $"The official sources returned conflicting monarch names: {string.Join(", ", names)}.");

        var conclusion = $"Jordan does not have a president. Its constitutional system is a hereditary monarchy, the King is the head of state, and the current monarch is King {monarch}. [1] [2]";
        return Verified(result, conclusion, [governing, king]);
    }


    private async Task<PulseAiSystemQuestionResult> VerifyUsSignalChiefExecutiveAsync(
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

    private async Task<RetrievedSource> RetrieveAsync(
        Uri uri,
        string sourceCode,
        string sourceName,
        CancellationToken cancellationToken)
    {
        if (!IsApprovedUri(uri))
            return RetrievedSource.Failed(uri, sourceCode, sourceName, "public_source_not_allowlisted");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain", 0.8));
        request.Headers.UserAgent.ParseAdd("Pulse-CelarAI-Authoritative-Facts/1.0");

        var client = _httpClientFactory.CreateClient(ClientName);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        var finalUri = response.RequestMessage?.RequestUri ?? uri;
        if (!IsApprovedUri(finalUri))
            return RetrievedSource.Failed(finalUri, sourceCode, sourceName, "public_source_redirect_not_allowlisted");
        if (!response.IsSuccessStatusCode)
            return RetrievedSource.Failed(finalUri, sourceCode, sourceName, $"public_source_http_{(int)response.StatusCode}");

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (mediaType.Length > 0
            && !mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase)
            && !mediaType.Equals("text/plain", StringComparison.OrdinalIgnoreCase))
        {
            return RetrievedSource.Failed(finalUri, sourceCode, sourceName, "public_source_content_type_rejected");
        }

        var body = await ReadLimitedAsync(response.Content, timeout.Token);
        var text = HtmlToText(body);
        if (text.Length == 0)
            return RetrievedSource.Failed(finalUri, sourceCode, sourceName, "public_source_empty");
        return new RetrievedSource(
            finalUri,
            sourceCode,
            sourceName,
            true,
            "verified",
            text,
            DateTimeOffset.UtcNow);
    }

    private static async Task<string> ReadLimitedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[16_384];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (memory.Length + read > MaximumResponseBytes)
                throw new InvalidOperationException("Authoritative public source exceeded the response-size limit.");
            memory.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(memory.ToArray());
    }

    private static PulseAiSystemQuestionResult Verified(
        PulseAiSystemQuestionResult result,
        string conclusion,
        IReadOnlyList<RetrievedSource> retrieved)
    {
        var sources = retrieved.Select((source, index) => SourceEvidence(source, index + 1)).ToArray();
        var citations = Enumerable.Range(1, sources.Length).ToArray();
        var dataAsOf = retrieved.Max(source => source.ObservedAt);
        var evidence = retrieved.Select((source, index) =>
            $"Source {index + 1}: {source.SourceName}; retrieved {source.ObservedAt:O}; official HTTPS source {source.Uri}.").ToArray();
        var answer = result.Answer with
        {
            DirectConclusion = conclusion,
            ExecutiveSummary = conclusion,
            CurrentState = [conclusion],
            DetailedAnalysis = [conclusion],
            SourceEvidence = evidence,
            KnownUnknownAndStaleValues = [],
            Assumptions = [],
            Conflicts = [],
            Limitations = [],
            RecommendedActions = [],
            CitationIds = citations,
            Confidence = 1m,
            ConfidenceExplanation = "The material claim was extracted from retrieval-time, allowlisted official sources and every material sentence carries an inline citation.",
            DataAsOf = dataAsOf
        };
        return result with
        {
            Status = "completed",
            Answer = answer,
            Sources = sources,
            Warnings = AppendDistinct(
                result.Warnings,
                "Provider execution was used only for language assistance and was not counted as evidence.",
                $"Authoritative public verification passed under {ContractVersion}.")
        };
    }

    private static PulseAiSystemQuestionResult Conflict(
        PulseAiSystemQuestionResult result,
        IReadOnlyList<RetrievedSource> retrieved,
        string conflict)
    {
        var sources = retrieved.Where(source => source.Succeeded)
            .Select((source, index) => SourceEvidence(source, index + 1))
            .ToArray();
        var citations = Enumerable.Range(1, sources.Length).ToArray();
        var citationText = citations.Length == 0
            ? string.Empty
            : " " + string.Join(' ', citations.Select(id => $"[{id}]"));
        var conclusion = $"Celar AI found conflicting authoritative public evidence and will not select an answer until the conflict is resolved.{citationText}";
        return result with
        {
            Status = "partial",
            Sources = sources,
            Answer = result.Answer with
            {
                DirectConclusion = conclusion,
                ExecutiveSummary = conclusion,
                CurrentState = [conclusion],
                DetailedAnalysis = [conflict + citationText],
                Conflicts = [conflict],
                CitationIds = citations,
                Confidence = 0m,
                ConfidenceExplanation = "Conflicting authoritative evidence blocks answer promotion.",
                DataAsOf = retrieved.Count > 0 ? retrieved.Max(source => source.ObservedAt) : DateTimeOffset.UtcNow
            },
            Warnings = AppendDistinct(result.Warnings, "Authoritative source conflict requires resolution; model memory was not used.")
        };
    }

    private static PulseAiSystemQuestionResult FailClosed(
        PulseAiSystemQuestionResult result,
        string diagnosticCode)
    {
        const string conclusion = "Celar AI could not verify this current public fact from an authoritative retrieval-time source, so it will not answer from model memory.";
        return result with
        {
            Status = "partial",
            Sources = [],
            Answer = result.Answer with
            {
                DirectConclusion = conclusion,
                ExecutiveSummary = conclusion,
                CurrentState = [],
                DetailedAnalysis = [],
                SourceEvidence = [],
                KnownUnknownAndStaleValues = [diagnosticCode],
                Assumptions = [],
                Conflicts = [],
                Limitations = ["No provider response was promoted as factual evidence."],
                CitationIds = [],
                Confidence = 0m,
                ConfidenceExplanation = "The required official current-source evidence was unavailable, incomplete, or disabled.",
                DataAsOf = DateTimeOffset.UtcNow
            },
            Warnings = AppendDistinct(result.Warnings, $"Authoritative public verification failed closed: {diagnosticCode}.")
        };
    }

    private static PulseAiSystemSourceEvidence SourceEvidence(
        RetrievedSource source,
        int sourceId) =>
        new(
            SourceId: sourceId,
            SourceType: "authoritative_public_web",
            SourceCode: source.SourceCode,
            SourceName: source.SourceName,
            ModuleCode: "011",
            Method: "GET",
            Path: source.Uri.ToString(),
            Status: "succeeded",
            StatusCode: 200,
            ObservedAt: source.ObservedAt,
            Freshness: "live_retrieved_current",
            EvidenceScope: "Official public fact only; no Pulse, customer, employee, project, financial, document, attachment, tool, or private model context was transmitted");

    private static IEnumerable<string> ExtractNames(Regex pattern, string text) =>
        pattern.Matches(text)
            .Select(match => NormalizeName(match.Groups["name"].Value))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string? ResolveCanonicalPersonName(IReadOnlyList<string> names)
    {
        if (names.Count == 0) return null;
        var candidates = names
            .Select(name => new
            {
                Display = name,
                Tokens = NameTokens(name)
            })
            .Where(candidate => candidate.Tokens.Length > 0)
            .OrderByDescending(candidate => candidate.Tokens.Length)
            .ThenByDescending(candidate => candidate.Display.Length)
            .ToArray();
        if (candidates.Length == 0) return null;

        var canonical = candidates[0];
        foreach (var candidate in candidates.Skip(1))
        {
            if (!IsOrderedSubset(candidate.Tokens, canonical.Tokens)) return null;
        }
        return canonical.Display;
    }

    private static string[] NameTokens(string value) =>
        Whitespace.Split(value.Trim())
            .Select(token => token.Trim(' ', ',', '.', ':', ';', '-', '—', '(', ')').ToLowerInvariant())
            .Where(token => token.Length > 0)
            .ToArray();

    private static bool IsOrderedSubset(IReadOnlyList<string> candidate, IReadOnlyList<string> canonical)
    {
        var offset = 0;
        foreach (var token in candidate)
        {
            while (offset < canonical.Count && !canonical[offset].Equals(token, StringComparison.OrdinalIgnoreCase))
                offset++;
            if (offset >= canonical.Count) return false;
            offset++;
        }
        return true;
    }

    private static string NormalizeName(string value)
    {
        var tokens = Whitespace.Split(value.Trim());
        var accepted = new List<string>();
        foreach (var token in tokens)
        {
            var clean = token.Trim(' ', ',', '.', ':', ';', '-', '—', '(', ')');
            if (clean.Length == 0) continue;
            if (NameStops.Contains(clean)) break;
            accepted.Add(clean);
            if (accepted.Count == 5) break;
        }
        return accepted.Count >= 1 ? string.Join(' ', accepted) : string.Empty;
    }

    private static string HtmlToText(string html)
    {
        var withoutScripts = ScriptOrStyle.Replace(html ?? string.Empty, " ");
        var withoutTags = HtmlTag.Replace(withoutScripts, " ");
        return Whitespace.Replace(WebUtility.HtmlDecode(withoutTags), " ").Trim();
    }

    private static bool IsApprovedUri(Uri uri) =>
        uri.IsAbsoluteUri
        && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && ApprovedHosts.Contains(uri.Host)
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo);

    private static bool IsUnitedStatesPresidentQuestion(string value) =>
        value.Contains("president", StringComparison.Ordinal)
        && (value.Contains("united states", StringComparison.Ordinal)
            || value.Contains("u.s. president", StringComparison.Ordinal)
            || value.Contains("us president", StringComparison.Ordinal)
            || value.Contains("american president", StringComparison.Ordinal));

    private static bool IsJordanPresidentQuestion(string value) =>
        value.Contains("president", StringComparison.Ordinal)
        && value.Contains("jordan", StringComparison.Ordinal);

    private static bool IsUsSignalChiefExecutiveQuestion(string value) =>
        (value.Contains("ceo", StringComparison.Ordinal)
            || value.Contains("chief executive", StringComparison.Ordinal))
        && (value.Contains("us signal", StringComparison.Ordinal)
            || value.Contains("u.s. signal", StringComparison.Ordinal));

    private static bool LooksLikeInternalJordan(string value) =>
        value.Contains("project jordan", StringComparison.Ordinal)
        || value.Contains("customer jordan", StringComparison.Ordinal)
        || value.Contains("jordan project", StringComparison.Ordinal);

    private static bool Enabled() =>
        !bool.TryParse(
            Environment.GetEnvironmentVariable("PROJECTPULSE_CELAR_AI_CURRENT_PUBLIC_FACTS_ENABLED"),
            out var enabled)
        || enabled;

    private static string Normalize(string value) =>
        Whitespace.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), " ");

    private static IReadOnlyList<string> AppendDistinct(
        IReadOnlyList<string> existing,
        params string[] additions) =>
        existing.Concat(additions)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string Diagnostic(Exception exception) => exception switch
    {
        TaskCanceledException => "authoritative_public_retrieval_timeout",
        HttpRequestException => "authoritative_public_transport_failure",
        InvalidOperationException => "authoritative_public_policy_failure",
        _ => "authoritative_public_verification_failure"
    };

    private sealed record RetrievedSource(
        Uri Uri,
        string SourceCode,
        string SourceName,
        bool Succeeded,
        string DiagnosticCode,
        string Text,
        DateTimeOffset ObservedAt)
    {
        public static RetrievedSource Failed(
            Uri uri,
            string sourceCode,
            string sourceName,
            string diagnosticCode) =>
            new(uri, sourceCode, sourceName, false, diagnosticCode, string.Empty, DateTimeOffset.UtcNow);
    }
}
