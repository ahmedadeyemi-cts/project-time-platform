#!/usr/bin/env python3
from __future__ import annotations

import subprocess
from pathlib import Path

SOURCE_COMMIT = "5ac67a4470bf0803889bdc9ef19528bebdf1979f"
SOURCE_PATH = ".github/scripts/temporary_finalize_flowhive_authoritative_repair.py"

source = subprocess.check_output(
    ["git", "show", f"{SOURCE_COMMIT}:{SOURCE_PATH}"],
    text=True,
)
old = "ROOT = Path(__file__).resolve().parents[2]"
new = "ROOT = Path.cwd()"
if source.count(old) != 1:
    raise SystemExit("Pinned FlowHive finalizer root anchor is unavailable or ambiguous.")
source = source.replace(old, new, 1)
namespace = {
    "__name__": "__main__",
    "__file__": str(Path.cwd() / SOURCE_PATH),
}
exec(compile(source, SOURCE_PATH, "exec"), namespace)
