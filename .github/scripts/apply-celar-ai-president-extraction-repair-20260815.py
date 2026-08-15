#!/usr/bin/env python3
from __future__ import annotations

import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[2]
SERVICE_PATH = ROOT / "src/backend/ProjectTime.Api/Ai/CelarAiAuthoritativePublicFactService.cs"
TEST_PATH = ROOT / "tests/CelarAiAuthoritativePublicFactTests/Program.cs"
TEMP_WORKFLOW = ROOT / ".github/workflows/temporary-celar-ai-president-extraction-repair-20260815.yml"
TEMP_SCRIPT = ROOT / ".github/scripts/apply-celar-ai-president-extraction-repair-20260815.py"
BRANCH = "fix/celar-ai-president-identity-extraction-20260815"


def run(*args: str) -> None:
    subprocess.run(list(args), cwd=ROOT, check=True)


def replace_once(source: str, old: str, new: str, label: str) -> str:
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one match, found {count}.")
    return source.replace(old, new, 1)


head = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
expected = os.environ.get("GITHUB_SHA", "")
if not expected or head != expected:
    raise SystemExit(f"Checkout mismatch: expected {expected or '<missing>'}, got {head}.")

service = SERVICE_PATH.read_text(encoding="utf-8")
tests = TEST_PATH.read_text(encoding="utf-8")

service = replace_once(
    service,
    '''    private static readonly Regex Whitespace = new(
        @"\\s+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PresidentName = new(
''',
    '''    private static readonly Regex Whitespace = new(
        @"\\s+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex UnitedStatesPresidentIdentity = new(
        @"(?<!Vice )\\bPresident\\s+(?<name>(?:[A-Z][A-Za-z'’.-]*)(?:\\s+(?:[A-Z][A-Za-z'’.-]*)){1,4})\\s+(?:\\d{1,2}(?:st|nd|rd|th)(?:\\s*&\\s*\\d{1,2}(?:st|nd|rd|th))?\\s+)?President\\s+of\\s+the\\s+United\\s+States\\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PresidentName = new(
''',
    "United States President identity regex insertion",
)

service = replace_once(
    service,
    "        var names = ExtractNames(PresidentName, page.Text).ToArray();",
    '''        var contextualNames = ExtractNames(
            UnitedStatesPresidentIdentity,
            page.Text).ToArray();
        var names = contextualNames.Length > 0
            ? contextualNames
            : ExtractNames(PresidentName, page.Text).ToArray();''',
    "President identity extraction selection",
)

tests = replace_once(
    tests,
    '    ["https://www.whitehouse.gov/administration/"] = "<html><h1>President Donald J. Trump</h1><p>President Trump</p><h2>Vice President JD Vance</h2></html>",',
    '    ["https://www.whitehouse.gov/administration/"] = "<html><body><h1>The Administration</h1><h2>President Donald J. Trump</h2><p>45th &amp; 47th President of the United States</p><nav>President Office President Trump About</nav><h2>Vice President JD Vance</h2><p>Vice President of the United States</p><h2>The Cabinet</h2><p>President Trump’s Team Established in Article II, Section 2 of the Constitution, the Cabinet advises the President on any subject.</p></body></html>",',
    "realistic White House regression fixture",
)

tests = replace_once(
    tests,
    '''Require(wrongProvider.Answer.DirectConclusion.Contains("Donald", StringComparison.OrdinalIgnoreCase),
    "official retrieval-time answer overrides wrong-provider text");
Require(wrongProvider.Sources.All(source => source.SourceType == "authoritative_public_web"),''',
    '''Require(wrongProvider.Answer.DirectConclusion.Contains("Donald", StringComparison.OrdinalIgnoreCase),
    "official retrieval-time answer overrides wrong-provider text");
Require(wrongProvider.Answer.Conflicts.Count == 0,
    "White House navigation and Cabinet language do not create false President conflicts");
Require(wrongProvider.Sources.All(source => source.SourceType == "authoritative_public_web"),''',
    "false-conflict regression assertion",
)

tests = replace_once(
    tests,
    '''Console.WriteLine("CELAR_AI_STALE_PRESIDENT_TEST=PASS");
Console.WriteLine("CELAR_AI_US_SIGNAL_CEO_TEST=PASS");''',
    '''Console.WriteLine("CELAR_AI_STALE_PRESIDENT_TEST=PASS");
Console.WriteLine("CELAR_AI_WHITE_HOUSE_NOISE_EXTRACTION_TEST=PASS");
Console.WriteLine("CELAR_AI_US_SIGNAL_CEO_TEST=PASS");''',
    "President extraction regression result",
)

SERVICE_PATH.write_text(service, encoding="utf-8")
TEST_PATH.write_text(tests, encoding="utf-8")

run(
    "dotnet",
    "run",
    "--project",
    "tests/CelarAiAuthoritativePublicFactTests/CelarAiAuthoritativePublicFactTests.csproj",
    "--configuration",
    "Release",
)
run(
    "dotnet",
    "build",
    "src/backend/ProjectTime.Api/ProjectTime.Api.csproj",
    "--configuration",
    "Release",
)
run("bash", "tests/test-projectpulse-api-startup.sh")
run("node", "tests/validate-systemwide-enterprise-reliability.mjs")
run("node", "tests/validate-systemwide-image-build-controller.mjs")
run("node", "tests/validate-utilization-role-scoping.mjs")
run("git", "diff", "--check")

actual = subprocess.check_output(["git", "diff", "--name-only"], cwd=ROOT, text=True).splitlines()
expected_files = [
    "src/backend/ProjectTime.Api/Ai/CelarAiAuthoritativePublicFactService.cs",
    "tests/CelarAiAuthoritativePublicFactTests/Program.cs",
]
if sorted(actual) != sorted(expected_files):
    raise SystemExit(f"Unexpected modified files: {actual}; expected {expected_files}.")

run("git", "config", "user.name", "github-actions[bot]")
run("git", "config", "user.email", "41898282+github-actions[bot]@users.noreply.github.com")
run("git", "rm", "--", str(TEMP_WORKFLOW.relative_to(ROOT)), str(TEMP_SCRIPT.relative_to(ROOT)))
run("git", "add", "--", str(SERVICE_PATH.relative_to(ROOT)), str(TEST_PATH.relative_to(ROOT)))
run("git", "diff", "--cached", "--check")
run("git", "commit", "-m", "Scope President extraction to official identity evidence")
run("git", "push", "origin", f"HEAD:refs/heads/{BRANCH}")

print("CELAR_AI_PRESIDENT_IDENTITY_EXTRACTION_REPAIR=PASS")
print("FINAL_PR_FILES=2")
print("PRODUCTION_MUTATION=NONE")
