"""Exercise the exporter's production helper without running host operations."""
import ast
import os
from pathlib import Path
import pwd
import stat
import subprocess
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'ops/projectpulse/scripts/projectpulse-sync-status-export.sh'
code = SOURCE.read_text().split("python3 - <<'PY'\n", 1)[1].rsplit('\nPY', 1)[0]
tree = ast.parse(code)
helper = next(n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == 'repository_git')
namespace = dict(os=os, pwd=pwd, stat=stat, subprocess=subprocess)
exec(compile(ast.Module(body=[helper], type_ignores=[]), str(SOURCE), 'exec'), namespace)
repository_git = namespace['repository_git']


class ExporterTests(unittest.TestCase):
    def test_root_owned_repository_is_refused_before_process_start(self):
        metadata = type('Metadata', (), {'st_mode': stat.S_IFDIR | 0o755, 'st_uid': 0})()
        with patch.object(os, 'lstat', return_value=metadata), patch.object(subprocess, 'run') as execute:
            self.assertFalse(repository_git(['status', '--short'], Path('/unused'))['ok'])
            execute.assert_not_called()

    def test_symlink_repository_is_refused(self):
        metadata = type('Metadata', (), {'st_mode': stat.S_IFLNK | 0o777, 'st_uid': 65534})()
        with patch.object(os, 'lstat', return_value=metadata), patch.object(subprocess, 'run') as execute:
            self.assertFalse(repository_git(['status', '--short'], Path('/unused'))['ok'])
            execute.assert_not_called()

    def test_root_drops_all_groups_and_does_not_forward_secrets(self):
        metadata = type('Metadata', (), {'st_mode': stat.S_IFDIR | 0o755, 'st_uid': 65534})()
        owner = type('Owner', (), {'pw_uid': 65534, 'pw_gid': 65534, 'pw_dir': '/nonexistent'})()
        result = subprocess.CompletedProcess([], 0, stdout='main\n', stderr='')
        with patch.object(os, 'lstat', return_value=metadata), patch.object(os, 'geteuid', return_value=0), patch.object(pwd, 'getpwuid', return_value=owner), patch.object(subprocess, 'run', return_value=result) as execute, patch.dict(os.environ, {'DATABASE_URL': 'DO_NOT_FORWARD'}):
            self.assertTrue(repository_git(['rev-parse', '--abbrev-ref', 'HEAD'], Path('/unused'))['ok'])
            kwargs = execute.call_args.kwargs
            self.assertEqual((kwargs['user'], kwargs['group'], kwargs['extra_groups']), (65534, 65534, []))
            self.assertNotIn('DATABASE_URL', kwargs['env'])
            self.assertEqual(kwargs['env']['GIT_CONFIG_GLOBAL'], '/dev/null')
            self.assertIn('core.fsmonitor=false', execute.call_args.args[0])

    def test_nonowner_identity_is_refused(self):
        metadata = type('Metadata', (), {'st_mode': stat.S_IFDIR | 0o755, 'st_uid': 65534})()
        with patch.object(os, 'lstat', return_value=metadata), patch.object(os, 'geteuid', return_value=1234), patch.object(subprocess, 'run') as execute:
            self.assertFalse(repository_git(['status', '--short'], Path('/unused'))['ok'])
            execute.assert_not_called()

    @unittest.skipUnless(os.geteuid() == 0 and Path('/usr/bin/git').exists(), 'Requires an isolated root Linux fixture')
    def test_real_repository_runs_unprivileged_and_hostile_monitor_is_not_executed(self):
        owner = pwd.getpwnam('nobody')
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            base.chmod(0o755)
            repository = base / 'repository'
            repository.mkdir()
            subprocess.run(['/usr/bin/git', 'init', '-q', str(repository)], check=True)
            marker = base / 'root-authority-marker'
            monitor = repository / 'monitor'
            monitor.write_text('#!/bin/sh\ntouch "' + str(marker) + '"\n')
            monitor.chmod(0o755)
            subprocess.run(['/usr/bin/git', '-C', str(repository), 'config', 'core.fsmonitor', str(monitor)], check=True)
            for path in [repository, *repository.rglob('*')]:
                try:
                    os.chown(path, owner.pw_uid, owner.pw_gid)
                except OSError as error:
                    if error.errno == 22 and not os.environ.get('SECURITY_REQUIRE_ROOT_EXECUTION'):
                        self.skipTest('Local user namespace maps root only; native Linux CI must execute the real UID fixture')
                    raise
            response = repository_git(['status', '--short'], repository)
            self.assertTrue(response['ok'], response['stderr'])
            self.assertIn('monitor', response['stdout'])
            self.assertFalse(marker.exists())
            identity = repository_git(['-c', 'alias.security-identity=!id -u', 'security-identity'], repository)
            self.assertTrue(identity['ok'], identity['stderr'])
            self.assertEqual(identity['stdout'], str(owner.pw_uid))
            print('SECURITY_ROOT_EXPORTER_REAL_UID=PASS', flush=True)

    def test_all_repository_calls_use_the_unprivileged_helper(self):
        self.assertNotIn('run(["git"', code)
        self.assertEqual(code.count('repository_git(['), 3)


if __name__ == '__main__':
    unittest.main()
