#!/usr/bin/env bash
set -Eeuo pipefail

SOURCE_ROOT="${SOURCE_ROOT:-$(git rev-parse --show-toplevel 2>/dev/null || true)}"
BASE_DIR="${BASE_DIR:-$HOME/project-health-dashboard-source-checkpoint}"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az07c-diff-aware-secret-review-$STAMP.log"

mkdir -p "$LOG_DIR"

if [ -z "$SOURCE_ROOT" ]; then
    echo "ERROR: Current directory is not inside a Git repository."
    return 1 2>/dev/null || true
fi

python3 - "$SOURCE_ROOT" <<'PY' | tee "$LOG"
import re
import subprocess
import sys
from pathlib import Path

root = Path(sys.argv[1]).resolve()
expected_head = "5a221da29cdfc1134e5d603175b311ff97658b67"


def git(*args, check=True, text=True):
    result = subprocess.run(
        ["git", "-C", str(root), *args],
        text=text,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    if check and result.returncode != 0:
        stderr = result.stderr.strip() if text else result.stderr.decode(errors="replace").strip()
        raise RuntimeError(stderr or f"git {' '.join(args)} failed")
    return result


def section(title):
    print()
    print("=" * 60)
    print(title)
    print("=" * 60)


patterns = {
    "PRIVATE_KEY_HEADER": re.compile(
        r"-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----"
    ),
    "HARDCODED_CREDENTIAL_STRING": re.compile(
        r'''(?ix)
        \b(password|passwd|pwd|secret|client[_-]?secret|api[_-]?key|
           access[_-]?key|token|connection[_-]?string)\b
        \s*[:=]\s*
        ["'][^"'\r\n]{6,}["']
        '''
    ),
    "URI_EMBEDDED_CREDENTIAL": re.compile(
        r"(?i)\b[a-z][a-z0-9+.-]*://[^\s/:]+:[^\s/@]+@"
    ),
    "AZURE_SAS_SIGNATURE": re.compile(
        r"(?i)(?:\?|&|\b)sig=[A-Za-z0-9%+/=_-]{16,}"
    ),
    "AWS_ACCESS_KEY": re.compile(r"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b"),
    "GITHUB_TOKEN": re.compile(r"\bgh[opurs]_[A-Za-z0-9_]{20,}\b"),
    "JWT_LIKE_TOKEN": re.compile(
        r"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b"
    ),
}


def scan_lines(lines):
    findings = []
    for line_number, line in lines:
        for finding_type, pattern in patterns.items():
            if pattern.search(line):
                findings.append((finding_type, line_number))
    return findings


def utf8_lines(data):
    if b"\x00" in data:
        return None
    try:
        text = data.decode("utf-8")
    except UnicodeDecodeError:
        return None
    return list(enumerate(text.splitlines(), start=1))


def added_lines_from_diff(path):
    result = git("diff", "--unified=0", "--no-color", "--", path)
    added = []
    new_line = None
    for raw in result.stdout.splitlines():
        if raw.startswith("@@"):
            match = re.search(r"\+(\d+)(?:,(\d+))?", raw)
            new_line = int(match.group(1)) if match else None
            continue
        if raw.startswith("+++") or raw.startswith("---"):
            continue
        if new_line is None:
            continue
        if raw.startswith("+"):
            added.append((new_line, raw[1:]))
            new_line += 1
        elif raw.startswith("-"):
            continue
        else:
            new_line += 1
    return added


section("AZ-07C - Diff-Aware Secret Review")
print(f"TIME={subprocess.check_output(['date', '-u', '-Is'], text=True).strip()}")
print(f"SOURCE_REPOSITORY_ROOT={root}")
print("READ_ONLY_SOURCE_REVIEW=true")
print("PATCH_CONTENT_PRINTED=false")
print("SECRET_VALUES_PRINTED=false")
print("SOURCE_FILES_MODIFIED=false")
print("GIT_STAGE_COMMIT_CHECKOUT_FETCH_PERFORMED=false")
print("APPLICATION_BUILD_STARTED=false")
print("AZURE_IMAGE_BUILD_STARTED=false")

head = git("rev-parse", "HEAD").stdout.strip()
print(f"SOURCE_HEAD={head}")
print(f"EXPECTED_HEAD={expected_head}")
print(f"SOURCE_HEAD_MATCH={'yes' if head == expected_head else 'no'}")

tracked = sorted(
    set(
        line.strip()
        for line in git("diff", "--name-only").stdout.splitlines()
        if line.strip()
    )
)
untracked = sorted(
    set(
        line.strip()
        for line in git("ls-files", "--others", "--exclude-standard").stdout.splitlines()
        if line.strip()
    )
)

print(f"TRACKED_CHANGED_FILE_COUNT={len(tracked)}")
print(f"UNTRACKED_FILE_COUNT={len(untracked)}")

baseline_total = 0
worktree_total = 0
added_total = 0
untracked_total = 0
binary_count = 0

section("Tracked-file diff-aware findings")
for path in tracked:
    full = root / path
    worktree_data = full.read_bytes() if full.is_file() else b""
    worktree_lines = utf8_lines(worktree_data)

    head_result = git("show", f"HEAD:{path}", check=False, text=False)
    baseline_lines = utf8_lines(head_result.stdout) if head_result.returncode == 0 else None

    if worktree_lines is None:
        binary_count += 1
        print(f"TRACKED_FILE={path}")
        print("  CONTENT_TYPE=binary-or-non-utf8")
        print("  DIFF_AWARE_SECRET_SCAN=not-scanned")
        continue

    baseline_findings = scan_lines(baseline_lines or [])
    worktree_findings = scan_lines(worktree_lines)
    added_lines = added_lines_from_diff(path)
    added_findings = scan_lines(added_lines)

    baseline_total += len(baseline_findings)
    worktree_total += len(worktree_findings)
    added_total += len(added_findings)

    print(f"TRACKED_FILE={path}")
    print("  CONTENT_TYPE=utf-8-text")
    print(f"  BASELINE_SECRET_FINDING_COUNT={len(baseline_findings)}")
    print(f"  WORKTREE_SECRET_FINDING_COUNT={len(worktree_findings)}")
    print(f"  ADDED_LINE_COUNT={len(added_lines)}")
    print(f"  ADDED_LINE_SECRET_FINDING_COUNT={len(added_findings)}")
    if added_findings:
        locations = ",".join(
            f"{finding_type}@L{line_number}"
            for finding_type, line_number in added_findings
        )
        print(f"  ADDED_LINE_SECRET_FINDING_LOCATIONS={locations}")
    else:
        print("  ADDED_LINE_SECRET_FINDING_LOCATIONS=none")

section("Untracked-file findings")
for path in untracked:
    full = root / path
    data = full.read_bytes() if full.is_file() else b""
    lines = utf8_lines(data)
    print(f"UNTRACKED_FILE={path}")
    if lines is None:
        binary_count += 1
        print("  CONTENT_TYPE=binary-or-non-utf8")
        print("  UNTRACKED_SECRET_FINDING_COUNT=not-scanned")
        continue

    findings = scan_lines(lines)
    untracked_total += len(findings)
    print("  CONTENT_TYPE=utf-8-text")
    print(f"  LINE_COUNT={len(lines)}")
    print(f"  UNTRACKED_SECRET_FINDING_COUNT={len(findings)}")
    if findings:
        locations = ",".join(
            f"{finding_type}@L{line_number}"
            for finding_type, line_number in findings
        )
        print(f"  UNTRACKED_SECRET_FINDING_LOCATIONS={locations}")
    else:
        print("  UNTRACKED_SECRET_FINDING_LOCATIONS=none")

section("Summary")
print(f"BASELINE_SECRET_FINDINGS_TOTAL={baseline_total}")
print(f"WORKTREE_SECRET_FINDINGS_TOTAL={worktree_total}")
print(f"ADDED_LINE_SECRET_FINDINGS_TOTAL={added_total}")
print(f"UNTRACKED_SECRET_FINDINGS_TOTAL={untracked_total}")
print(f"BINARY_OR_NON_UTF8_FILE_COUNT={binary_count}")
print("SECRET_VALUES_DISCLOSED=false")

if added_total > 0 or untracked_total > 0:
    result = "CHANGED_CONTENT_SECRET_REVIEW_REQUIRED"
else:
    result = "NO_SECRET_PATTERNS_IN_CHANGED_TEXT"

print(f"DIFF_AWARE_SECRET_REVIEW_RESULT={result}")
print("SOURCE_COMMIT_ALLOWED=false")
print("SOURCE_IMAGE_BUILD_ALLOWED=false")
print("NEXT_ACTION=MANUAL_FUNCTIONAL_DIFF_REVIEW_AND_GENERATED_FILE_CLEANUP_PLAN")

print()
print("*" * 60)
print("DIFF-AWARE SECRET REVIEW COMPLETE")
print("*" * 60)
PY

echo
echo "Review log: $LOG"
