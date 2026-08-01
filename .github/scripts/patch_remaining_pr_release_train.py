#!/usr/bin/env python3
"""Fail-closed runtime patch for the encoded remaining-PR controller."""
from __future__ import annotations

import argparse
import ast
import json
import os
import subprocess
import tempfile
from pathlib import Path

MARKER = "PROJECTPULSE_RELEASE_TRAIN_GENERATED_SOURCE_CONVERGENCE_V1"
TARGET = ("git", "diff", "--exit-code")
HELPER_NAME = "_projectpulse_validate_generated_source_convergence"

HELPER_SOURCE = r'''
# __MARKER__
def _projectpulse_validate_generated_source_convergence(cwd=None):
    import os
    import subprocess
    from pathlib import Path

    marker = "__MARKER__"
    start = Path(cwd).resolve() if cwd is not None else Path.cwd().resolve()
    repo = start
    while repo != repo.parent and not (repo / ".git").exists():
        repo = repo.parent
    if not (repo / ".git").exists():
        raise RuntimeError(f"Unable to locate the release-train Git worktree from {start}.")

    frontend = repo / "src/frontend/project-time-web"
    project = repo / "src/backend/ProjectTime.Api/ProjectTime.Api.csproj"
    if not frontend.is_dir() or not project.is_file():
        raise RuntimeError("Release-train convergence paths are missing.")

    def run(command, *, workdir=repo, capture=False):
        result = subprocess.run(
            list(command), cwd=str(workdir), text=True,
            stdout=subprocess.PIPE if capture else None,
            stderr=subprocess.PIPE if capture else None,
            check=False,
        )
        if result.returncode:
            details = "\n".join(
                value.strip() for value in (result.stdout or "", result.stderr or "")
                if value and value.strip()
            )
            suffix = f"\n{details}" if details else ""
            raise RuntimeError(
                f"Command failed ({result.returncode}): {' '.join(command)}{suffix}"
            )
        return result.stdout or ""

    def names(*arguments):
        return sorted({
            line.strip() for line in run(("git", *arguments), capture=True).splitlines()
            if line.strip()
        })

    staged = names("diff", "--cached", "--name-only")
    if staged:
        raise RuntimeError("Pre-existing staged paths: " + ", ".join(staged))

    generated = sorted(set(
        names("diff", "--name-only")
        + names("ls-files", "--others", "--exclude-standard")
    ))

    def allowed(path):
        path = path.replace("\\", "/")
        return (
            path.startswith("src/frontend/project-time-web/src/")
            or path.startswith("src/frontend/project-time-web/container-context/")
            or (
                path.startswith("src/backend/ProjectTime.Api/")
                and path.endswith(".g.cs")
            )
        )

    unexpected = [path for path in generated if not allowed(path)]
    if unexpected:
        raise RuntimeError(
            "Production validation changed source outside the reviewed deterministic "
            "generation boundary: " + ", ".join(unexpected)
        )
    if not generated:
        print(f"{marker}=CLEAN_NO_GENERATION")
        return

    has_backend = any(
        path.startswith("src/backend/ProjectTime.Api/") and path.endswith(".g.cs")
        for path in generated
    )
    run(("git", "diff", "--check"))
    run(("git", "add", "--", *generated))

    failure = None
    try:
        run(("npm", "run", "prebuild"), workdir=frontend)
        if has_backend:
            run((
                "dotnet", "build", str(project), "--configuration", "Release", "--no-restore"
            ))
        run(("git", "diff", "--check"))
        run(("git", "diff", "--cached", "--check"), capture=True)
        run(("git", "diff", "--exit-code"))
        extra = names("ls-files", "--others", "--exclude-standard")
        if extra:
            raise RuntimeError("Second pass created untracked source: " + ", ".join(extra))
        second = names("diff", "--cached", "--name-only")
        if second != generated:
            raise RuntimeError(f"Generated path set changed: first={generated}; second={second}")
        print(f"{marker}=PASSED paths={len(generated)} backend={'yes' if has_backend else 'no'}")
    except BaseException as error:
        failure = error
    finally:
        cleanup = []
        for command in (
            ("git", "reset", "--hard", "HEAD"),
            (
                "git", "clean", "-fd", "--",
                "src/backend/ProjectTime.Api",
                "src/frontend/project-time-web/src",
                "src/frontend/project-time-web/container-context",
            ),
        ):
            try:
                run(command)
            except BaseException as error:
                cleanup.append(str(error))
        try:
            status = run(("git", "status", "--porcelain=v1"), capture=True).strip()
            if status:
                cleanup.append("Candidate tree was not restored:\n" + status)
        except BaseException as error:
            cleanup.append(str(error))
        if cleanup:
            problem = RuntimeError("; ".join(cleanup))
            if failure is not None:
                raise problem from failure
            raise problem
    if failure is not None:
        raise failure
'''.replace("__MARKER__", MARKER).strip() + "\n"


class ContractError(RuntimeError):
    pass


def literal_command(node: ast.AST):
    if isinstance(node, (ast.List, ast.Tuple)):
        values = []
        for item in node.elts:
            if not isinstance(item, ast.Constant) or not isinstance(item.value, str):
                return None
            values.append(item.value)
        return tuple(values)
    if isinstance(node, ast.Constant) and isinstance(node.value, str):
        return tuple(node.value.split())
    return None


def patch_source(source: str):
    if MARKER in source:
        raise ContractError("Controller is already patched.")
    tree = ast.parse(source)
    matches = [
        node for node in ast.walk(tree)
        if isinstance(node, ast.Call) and node.args and literal_command(node.args[0]) == TARGET
    ]
    if len(matches) != 1:
        raise ContractError(f"Expected one literal git diff --exit-code call; found {len(matches)}.")
    call = matches[0]
    if len(call.args) != 1:
        raise ContractError("Target call gained positional arguments.")
    unknown = [keyword.arg for keyword in call.keywords if keyword.arg != "cwd"]
    if unknown:
        raise ContractError("Target call gained unsupported keywords: " + ", ".join(map(str, unknown)))
    cwd = [keyword for keyword in call.keywords if keyword.arg == "cwd"]
    if len(cwd) > 1:
        raise ContractError("Target call has duplicate cwd keywords.")
    cwd_text = ast.get_source_segment(source, cwd[0].value) if cwd else None
    if cwd and not cwd_text:
        raise ContractError("Unable to preserve cwd expression.")
    replacement = f"{HELPER_NAME}(cwd={cwd_text})" if cwd_text else f"{HELPER_NAME}()"
    segment = ast.get_source_segment(source, call)
    if not segment or source.count(segment) != 1:
        raise ContractError("Target source segment is not unique.")
    replaced = source.replace(segment, replacement, 1)

    lines = source.splitlines(keepends=True)
    insertion_line = 0
    for index, node in enumerate(tree.body):
        is_docstring = (
            index == 0 and isinstance(node, ast.Expr)
            and isinstance(node.value, ast.Constant) and isinstance(node.value.value, str)
        )
        if is_docstring or isinstance(node, (ast.Import, ast.ImportFrom)):
            insertion_line = node.end_lineno
            continue
        break
    insertion = sum(len(line) for line in lines[:insertion_line])
    patched = replaced[:insertion] + "\n" + HELPER_SOURCE + "\n" + replaced[insertion:]
    compile(patched, "remaining_pr_release_train.py", "exec")
    metadata = {
        "marker": MARKER,
        "replacements": 1,
        "cwd_preserved": bool(cwd_text),
    }
    return patched, metadata


def self_test():
    sample = (
        "from pathlib import Path\n\n"
        "def run(command, *, cwd=None): pass\n"
        "def validate(repo):\n"
        "    run(['git', 'diff', '--exit-code'], cwd=repo)\n"
    )
    patched, metadata = patch_source(sample)
    assert metadata["replacements"] == 1
    assert HELPER_NAME in patched

    namespace = {}
    exec(HELPER_SOURCE, namespace)
    helper = namespace[HELPER_NAME]
    with tempfile.TemporaryDirectory(prefix="release-train-convergence-") as temporary:
        repo = Path(temporary)
        frontend = repo / "src/frontend/project-time-web"
        api = repo / "src/backend/ProjectTime.Api"
        fake_bin = repo / "fake-bin"
        (frontend / "src").mkdir(parents=True)
        (frontend / "scripts").mkdir()
        api.mkdir(parents=True)
        fake_bin.mkdir()
        (frontend / "src/App.jsx").write_text("base\n")
        (api / "ProjectTime.Api.csproj").write_text("<Project />\n")
        (repo / ".gitattributes").write_text(
            "src/backend/ProjectTime.Api/*.g.cs whitespace=-trailing-space\n"
        )
        (frontend / "package.json").write_text(json.dumps({
            "name": "convergence-test", "version": "1.0.0", "private": True,
            "scripts": {"prebuild": "node scripts/generate.mjs"},
        }))
        (frontend / "scripts/generate.mjs").write_text(
            "import fs from 'node:fs';\n"
            "fs.writeFileSync(new URL('../src/App.jsx', import.meta.url), 'generated\\n');\n"
        )
        dotnet = fake_bin / "dotnet"
        dotnet.write_text(
            "#!/usr/bin/env bash\nset -Eeuo pipefail\n"
            "printf 'generated backend  \\n' > src/backend/ProjectTime.Api/Synthetic.g.cs\n"
        )
        dotnet.chmod(0o755)

        def run(*command, cwd=repo, env=None):
            subprocess.run(command, cwd=cwd, env=env, check=True, stdout=subprocess.DEVNULL)
        run("git", "init", "-q")
        run("git", "config", "user.name", "Release Train Test")
        run("git", "config", "user.email", "release-train@example.invalid")
        run("git", "add", "-A")
        run("git", "commit", "-qm", "baseline")
        run("npm", "run", "prebuild", cwd=frontend)
        env = os.environ.copy()
        env["PATH"] = f"{fake_bin}{os.pathsep}{env.get('PATH', '')}"
        run("dotnet", "build", str(api / "ProjectTime.Api.csproj"), env=env)
        old_path = os.environ.get("PATH", "")
        os.environ["PATH"] = env["PATH"]
        try:
            helper(repo)
        finally:
            os.environ["PATH"] = old_path
        status = subprocess.run(
            ["git", "status", "--porcelain=v1"], cwd=repo,
            text=True, stdout=subprocess.PIPE, check=True,
        ).stdout.strip()
        assert not status
        assert (frontend / "src/App.jsx").read_text() == "base\n"
        assert not (api / "Synthetic.g.cs").exists()
        (repo / "README.md").write_text("unexpected\n")
        try:
            helper(repo)
        except RuntimeError as error:
            assert "outside the reviewed deterministic generation boundary" in str(error)
        else:
            raise AssertionError("Unexpected dirty source did not fail closed.")
    print(f"{MARKER}_SELF_TEST=PASSED")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("controller", nargs="?", type=Path)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        if args.controller is None:
            return 0
    if args.controller is None:
        parser.error("controller path is required")
    source = args.controller.read_text(encoding="utf-8")
    patched, metadata = patch_source(source)
    if not args.check:
        args.controller.write_text(patched, encoding="utf-8")
    print(json.dumps(metadata, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
