from pathlib import Path


YAML_BLOCK_INDENT = "          "
TRIPLE_QUOTES = ('"""', "'''")


def normalize_python_heredoc(raw: str, path: str) -> str:
    lines: list[str] = []
    active_triple: str | None = None

    for raw_line in raw.splitlines():
        if active_triple is None:
            line = raw_line[len(YAML_BLOCK_INDENT):] if raw_line.startswith(YAML_BLOCK_INDENT) else raw_line
            lines.append(line)
            for delimiter in TRIPLE_QUOTES:
                if line.count(delimiter) % 2 == 1:
                    active_triple = delimiter
                    break
            continue

        stripped = raw_line.lstrip()
        if stripped.startswith(active_triple):
            line = raw_line[len(YAML_BLOCK_INDENT):] if raw_line.startswith(YAML_BLOCK_INDENT) else raw_line
            lines.append(line)
            if line.count(active_triple) % 2 == 1:
                active_triple = None
            continue

        # Leading whitespace inside a triple-quoted replacement is source text,
        # not workflow indentation. Preserve it exactly.
        lines.append(raw_line)
        if raw_line.count(active_triple) % 2 == 1:
            active_triple = None

    if active_triple is not None:
        raise SystemExit(f"{path}: unterminated triple-quoted string in Python heredoc")
    return "\n".join(lines) + "\n"


def preserve_triple_string_line_continuations(block: str, path: str) -> str:
    lines: list[str] = []
    active_triple: str | None = None

    for source_line in block.splitlines():
        line = source_line
        if active_triple is None:
            opened: str | None = None
            for delimiter in TRIPLE_QUOTES:
                if line.count(delimiter) % 2 == 1:
                    opened = delimiter
                    break
            if opened is not None and line.endswith("\\") and not line.endswith("\\\\"):
                line += "\\"
            lines.append(line)
            active_triple = opened
            continue

        # A single trailing backslash inside a Python triple-quoted literal
        # suppresses the source newline. Double it so shell continuations in the
        # intended replacement text remain a literal backslash plus newline.
        if line.endswith("\\") and not line.endswith("\\\\"):
            line += "\\"
        lines.append(line)
        if line.count(active_triple) % 2 == 1:
            active_triple = None

    if active_triple is not None:
        raise SystemExit(f"{path}: unterminated triple-quoted string while preserving line continuations")
    return "\n".join(lines) + "\n"


def python_blocks(path: str) -> list[str]:
    source = Path(path).read_text()
    start_marker = "          python3 - <<'PY'\n"
    end_marker = "\n          PY\n"
    blocks: list[str] = []
    offset = 0

    while True:
        start = source.find(start_marker, offset)
        if start < 0:
            break
        start += len(start_marker)
        end = source.find(end_marker, start)
        if end < 0:
            raise SystemExit(f"{path}: unterminated Python heredoc")
        normalized = normalize_python_heredoc(source[start:end], path)
        blocks.append(preserve_triple_string_line_continuations(normalized, path))
        offset = end + len(end_marker)

    return blocks


def execute(block: str, label: str) -> None:
    namespace = {"__name__": "__main__", "__file__": label}
    exec(compile(block, label, "exec"), namespace, namespace)


def repair_generated_validator_escapes() -> None:
    path = Path('src/frontend/project-time-web/scripts/validate-module-loading-assignment-propagation.mjs')
    source = path.read_text()
    replacements = [
        (
            "'if (!tableMode) return undefined;\n    void loadOwnership();'",
            "'if (!tableMode) return undefined;\\n    void loadOwnership();'",
        ),
        (
            "'WHERE app_user.is_active = TRUE\n                    ORDER BY display_name, preferred_email'",
            "'WHERE app_user.is_active = TRUE\\n                    ORDER BY display_name, preferred_email'",
        ),
    ]
    for malformed, corrected in replacements:
        count = source.count(malformed)
        if count != 1:
            raise SystemExit(
                f'{path}: expected one generated multiline validator literal, found {count}: {malformed!r}'
            )
        source = source.replace(malformed, corrected, 1)
    path.write_text(source)


base_publisher = ".github/workflows/publish-pr719-module-directory-owner-001a.yml"
policy_publisher = ".github/workflows/publish-pr719-finalize-pr.yml"

base_blocks = python_blocks(base_publisher)
if len(base_blocks) != 1:
    raise SystemExit(f"{base_publisher}: expected one Python repair block, found {len(base_blocks)}")
execute(base_blocks[0], f"{base_publisher}#repair")

policy_blocks = python_blocks(policy_publisher)
if len(policy_blocks) != 2:
    raise SystemExit(f"{policy_publisher}: expected two Python blocks, found {len(policy_blocks)}")
execute(policy_blocks[1], f"{policy_publisher}#developer-owner-policy")
repair_generated_validator_escapes()

Path("scripts/release-test/finalize-pr719-source.py").unlink(missing_ok=True)
