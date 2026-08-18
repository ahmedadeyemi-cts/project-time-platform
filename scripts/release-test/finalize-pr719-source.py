from pathlib import Path


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
        raw = source[start:end]
        lines = [
            line[10:] if line.startswith("          ") else line
            for line in raw.splitlines()
        ]
        blocks.append("\n".join(lines) + "\n")
        offset = end + len(end_marker)
    return blocks


def omit_call(block: str, start_marker: str, next_marker: str) -> str:
    start = block.find(start_marker)
    if start < 0:
        raise SystemExit(f"repair block omission marker was not found: {start_marker[:120]!r}")
    end = block.find(next_marker, start)
    if end < 0:
        raise SystemExit(f"repair block continuation marker was not found: {next_marker[:120]!r}")
    return block[:start] + block[end + 2:]


def execute(block: str, label: str) -> None:
    namespace = {"__name__": "__main__", "__file__": label}
    exec(compile(block, label, "exec"), namespace, namespace)


base_publisher = ".github/workflows/publish-pr719-module-directory-owner-001a.yml"
policy_publisher = ".github/workflows/publish-pr719-finalize-pr.yml"

base_blocks = python_blocks(base_publisher)
if len(base_blocks) != 1:
    raise SystemExit(f"{base_publisher}: expected one Python repair block, found {len(base_blocks)}")
base_block = omit_call(
    base_blocks[0],
    'replace_once(\n    portal,\n    """        <div className="modules-directory-empty">',
    '\n\ntable = '
)
execute(base_block, f"{base_publisher}#repair")

policy_blocks = python_blocks(policy_publisher)
if len(policy_blocks) != 2:
    raise SystemExit(f"{policy_publisher}: expected two Python blocks, found {len(policy_blocks)}")
execute(policy_blocks[1], f"{policy_publisher}#developer-owner-policy")

Path("scripts/release-test/finalize-pr719-source.py").unlink(missing_ok=True)
