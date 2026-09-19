"""Exact six-file migration replay recovery; deployment controller is unchanged."""
from pathlib import Path
import hashlib, os, subprocess
BASE = '1544cad48bcad67bfa885a691b772ce7551eab85'
SELF = 'tests/connectwise-sell-replay-scope.py'
EXPECTED = {'database/migrations/098_customer_directory_source_authority.sql': '8de8aafb6ec2cb57f480654d564a7cd7a0736395fe7cf6c39eb2ff2dd296db49', 'tests/connectwise-sell-migration.test.mjs': 'a6d8e3b3e11b286df8a2fd782b1bdd5b12af513b1462921e7f1c49a3537874d2', '.github/workflows/connectwise-sell-configuration-ci.yml': '0bcbda8951aed2e7888c552cae1da8654079f423e335d076b06a55b2847a77e5', '.github/workflows/module025-protected-uat-control.yml': '9f76fde9d4d4a79571fc7baf60ca4bf89eb962c4e7802e49f3d383926e113c22', 'scripts/release-test/validate-protected-test-controller-branches.sh': '9544bc223c498628b19ececc1d1106def38e486f4ec86c7f725b6e4c22996a1a'}
assert (os.environ.get('GITHUB_HEAD_REF') or os.environ.get('GITHUB_REF_NAME')) == 'fix/connectwise-sell-migration-replay'
assert subprocess.check_output(['git','merge-base',BASE,'HEAD']).decode().strip() == BASE
assert set(subprocess.check_output(['git','diff','--name-only',BASE]).decode().splitlines()) == set(EXPECTED) | {SELF}
for name, digest in EXPECTED.items():
    assert hashlib.sha256(Path(name).read_bytes()).hexdigest() == digest, name
print('CONNECTWISE_SELL_REPLAY_SCOPE=PASS files=6')
