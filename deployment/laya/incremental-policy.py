"""Decide whether a GitOps update is strictly an additive Laya adapter cutover."""
from pathlib import Path
import re
import subprocess

ALLOWED = frozenset({'deploy.sh', 'gateway/laya_decisions.py', 'gateway/wsgi_decisions.py'})


def eligible(changed: set[str]) -> bool:
    return bool(changed) and changed <= ALLOWED


def main() -> None:
    root = Path(__file__).resolve().parents[2]
    state = Path('/var/lib/celar-ai/gitops-applied-tree')
    if not state.is_file():
        raise SystemExit(10)  # Fresh deployment: retain the existing provisioning path.
    previous = state.read_text().strip()
    if not re.fullmatch('[0-9a-f]{40}', previous):
        raise SystemExit('Invalid existing Oracle GitOps applied-tree record')
    def git(*args):
        return subprocess.check_output(['git', '-C', str(root), *args], text=True).strip()
    if git('cat-file', '-t', previous) != 'tree':
        raise SystemExit('Oracle GitOps baseline is not a tree object')
    target = git('rev-parse', 'HEAD:deployment/oracle-celar')
    changed = set(git('diff', '--name-only', previous, target, '--').splitlines())
    if not eligible(changed):
        raise SystemExit(10)
    # An incremental rollout cannot repair unrelated local edits by overwriting.
    expected = root / 'deployment/oracle-celar'
    for relative, installed in (
        ('gateway/gateway.py', '/opt/celar-ai/gateway/gateway.py'),
        ('gateway/wsgi.py', '/opt/celar-ai/gateway/wsgi.py'),
        ('systemd/celar-ai-gateway.service', '/etc/systemd/system/celar-ai-gateway.service'),
    ):
        if Path(installed).read_bytes() != (expected / relative).read_bytes():
            raise SystemExit('Existing gateway differs from the reviewed incremental baseline')
    if not Path('/etc/systemd/system/celar-laya.service').is_file():
        raise SystemExit('Laya runtime must already be installed before this cutover')
    print('LAYA_INCREMENTAL_BASELINE=VERIFIED')


if __name__ == '__main__':
    main()
