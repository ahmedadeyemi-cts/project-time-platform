from pathlib import Path

path = Path("src/backend/ProjectTime.Api/Ai/CelarAiAuthoritativePublicFactService.cs")
text = path.read_text()
old = '''    private static readonly Regex PresidentName = new(
        @"(?<!Vice )\\bPresident\\s+(?<name>(?:[A-Z][A-Za-z'’.-]+|[A-Z]\\.)(?:\\s+(?:[A-Z][A-Za-z'’.-]+|[A-Z]\\.)){0,4})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
'''
new = '''    private static readonly Regex PresidentName = new(
        @"(?<!Vice )\\bPresident\\s+(?<name>(?:[A-Z][A-Za-z'’.-]*)(?:\\s+(?!(?:President|Vice|Administration|White|House|The|First|United|States|His|Her|Majesty|Royal|Court)\\b)(?:[A-Z][A-Za-z'’.-]*)){0,4})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
'''
count = text.count(old)
if count != 1:
    raise SystemExit(f"President-name conflict anchor: expected one match, found {count}.")
path.write_text(text.replace(old, new, 1))
