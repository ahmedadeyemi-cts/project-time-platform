using System.Globalization;
using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Explicit public-entity policy for isolated public fact questions. An entity
/// name is eligible only when it is approved here or in the deployment-owned
/// allowlist and the question contains no enterprise-record context.
/// </summary>
public static class CelarAiPublicEntityRegistry
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly string[] DefaultApprovedEntities =
    [
        "US Signal",
        "OneNeck IT Solutions",
        "Microsoft",
        "OpenAI",
        "Anthropic",
        "Oracle",
        "Amazon Web Services",
        "Google Cloud",
        "Cisco",
        "Broadcom",
        "VMware",
        "Five9",
        "Salesforce"
    ];

    private static readonly Lazy<IReadOnlyList<string>> DefaultApprovedCountries =
        new(BuildApprovedCountries);

    private static IReadOnlyList<string> BuildApprovedCountries()
    {
        var countries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "United States", "United States of America", "USA", "U.S.", "US",
            "United Kingdom", "UK", "U.K.",
            "South Korea", "North Korea", "Russia", "Czechia", "Czech Republic",
            "Ivory Coast", "Côte d'Ivoire", "Vatican City", "Palestine", "Taiwan"
        };

        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var country = new RegionInfo(culture.Name).EnglishName.Trim();
                if (country.Length is >= 2 and <= 160) countries.Add(country);
            }
            catch (ArgumentException)
            {
                // Some synthetic or incomplete cultures do not expose a region.
            }
        }

        return countries
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static readonly Regex PublicFactCue = new(
        @"\b(?:who\s+(?:is|are|was|were)\s+(?:the\s+)?(?:current\s+)?(?:ceo|chief\s+executive\s+officer|president|vice\s+president|prime\s+minister|premier|chancellor|king|queen|monarch|emir|sultan|head\s+of\s+state|head\s+of\s+government|founder|owner|chair(?:man|woman|person)?)\s+(?:of|for)|where\s+(?:is|are)\s+(?:the\s+)?(?:headquarters|headquarter|hq|main\s+office)\s+(?:of|for)|when\s+was\s+.+\s+founded|what\s+(?:is|are|does|do)\s+.+\s+(?:do|make|provide|sell|offer)|what\s+(?:company\s+)?(?:owns|acquired)\s+|what\s+is\s+the\s+(?:website|headquarters|parent\s+company|ownership|industry)\s+(?:of|for))\b",
        Options,
        RegexTimeout);

    private static readonly Regex EnterpriseContextCue = new(
        @"\b(?:pulse|celar|module|work\s+register|flowhive|project\s+forge|project|our|my|assigned|customer|client|employee|engineer|manager|timesheet|time\s+entry|invoice|billing|expense|contract|statement\s+of\s+work|sow|global\s+solution\s+design|gsd|iqs|task|ticket|case|account\s+id|user\s+id|private|confidential|proprietary|internal\s+system|internal\s+record)\b",
        Options,
        RegexTimeout);

    public static bool IsGovernedPublicQuestion(string? question) =>
        TryGetApprovedEntity(question, out _);

    public static bool TryGetApprovedEntity(string? question, out string entity)
    {
        entity = string.Empty;
        var value = question?.Trim() ?? string.Empty;
        if (value.Length is < 4 or > 800
            || value.Any(character => char.IsControl(character) && character is not '\r' and not '\n')
            || EnterpriseContextCue.IsMatch(value)
            || !PublicFactCue.IsMatch(value))
        {
            return false;
        }

        entity = ApprovedEntities()
            .OrderByDescending(candidate => candidate.Length)
            .FirstOrDefault(candidate => Regex.IsMatch(
                value,
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(candidate)}(?![\p{{L}}\p{{N}}])",
                Options,
                RegexTimeout)) ?? string.Empty;
        return entity.Length > 0;
    }

    public static IReadOnlyList<string> ApprovedEntities()
    {
        var configured = (Environment.GetEnvironmentVariable(
                "PROJECTPULSE_CELAR_AI_PUBLIC_ENTITY_ALLOWLIST") ?? string.Empty)
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => value.Length is >= 2 and <= 160)
            .Take(200);
        return DefaultApprovedEntities
            .Concat(DefaultApprovedCountries.Value)
            .Concat(configured)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
