# This block is executed by the root-owned gateway cutover script.
import hashlib
import json
from pathlib import Path
import re
import stat
import subprocess
import sys

NAMES = ('laya_decisions.py', 'wsgi_decisions.py')


def checked_file(path, owner_uid=0, limit=262144):
    info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != owner_uid or info.st_mode & 0o022 or info.st_nlink != 1:
        raise RuntimeError('STOP: Adapter ownership, permissions or file type is not managed.')
    if info.st_size > limit:
        raise RuntimeError('STOP: Managed adapter evidence exceeds its size limit.')
    return path.read_bytes()


def verify_managed(root, target, manifest, mode, legacy, owner_uid=0):
    if manifest.exists() or manifest.is_symlink():
        evidence = json.loads(checked_file(manifest, owner_uid, 8192))
        if set(evidence) != {'version', 'source_commit', 'adapters'} or evidence['version'] != 1:
            raise RuntimeError('STOP: Invalid managed adapter evidence format.')
        release = evidence['source_commit']
        if not isinstance(release, str) or re.fullmatch('[0-9a-f]{40}', release) is None:
            raise RuntimeError('STOP: Invalid managed adapter source commit.')
        if set(evidence['adapters']) != set(NAMES):
            raise RuntimeError('STOP: Managed adapter evidence has an unexpected file set.')
    else:
        # Adopt only the exact initial reviewed installation, not arbitrary
        # pre-existing files. Subsequent releases always have durable evidence.
        release = legacy
        evidence = None
    if mode == 'apply':
        subprocess.run(['git', '-C', str(root), 'merge-base', '--is-ancestor', release, 'HEAD'], check=True)
    for name in NAMES:
        installed = checked_file(target / name, owner_uid)
        reviewed = subprocess.check_output(['git', '-C', str(root), 'show', release + ':deployment/oracle-celar/gateway/' + name])
        if installed != reviewed:
            raise RuntimeError('STOP: Installed adapter differs from its recorded reviewed source.')
        if evidence is not None and evidence['adapters'][name] != hashlib.sha256(installed).hexdigest():
            raise RuntimeError('STOP: Managed adapter checksum mismatch.')
    return release


if __name__ == '__main__':
    verify_managed(Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3]), sys.argv[4], sys.argv[5])
