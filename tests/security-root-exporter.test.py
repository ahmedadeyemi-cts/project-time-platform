"""Exercise the exporter's production helper without running host operations."""
import ast
import os
from pathlib import Path
import pwd
import re
import secrets
import shlex
import stat
import subprocess
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'ops/projectpulse/scripts/projectpulse-sync-status-export.sh'
code = SOURCE.read_text().split("python3 - <<'PY'\n", 1)[1].rsplit('\nPY', 1)[0]
tree = ast.parse(code)
helper = next(n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == 'repository_git')
namespace = dict(os=os, pwd=pwd, stat=stat, subprocess=subprocess)
exec(compile(ast.Module(body=[helper], type_ignores=[]), str(SOURCE), 'exec'), namespace)
repository_git = namespace['repository_git']
BACKUP_SOURCE = ROOT / 'ops/projectpulse/scripts/projectpulse-backup.sh'
backup_code = BACKUP_SOURCE.read_text().split("<<'PY_BACKUP_GIT'\n", 1)[1].split('\nPY_BACKUP_GIT', 1)[0]
backup_helper = next(n for n in ast.parse(backup_code).body if isinstance(n, ast.FunctionDef) and n.name == 'repository_git')
backup_namespace = dict(os=os, pwd=pwd, stat=stat, subprocess=subprocess)
exec(compile(ast.Module(body=[backup_helper], type_ignores=[]), str(BACKUP_SOURCE), 'exec'), backup_namespace)
backup_repository_git = backup_namespace['repository_git']


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
        identity_provider = SimpleNamespace(getpwuid=lambda uid: owner)
        environment_key = 'DATABASE_URL'
        fixture_environment = {environment_key: secrets.token_hex(32)}
        result = subprocess.CompletedProcess([], 0, stdout='main\n', stderr='')
        with patch.object(os, 'lstat', return_value=metadata), patch.object(os, 'geteuid', return_value=0), patch.dict(namespace, pwd=identity_provider), patch.object(subprocess, 'run', return_value=result) as execute, patch.dict(os.environ, fixture_environment):
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
            marker = repository / 'monitor-executed-marker'
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
            # Positive control: the same monitor can write its marker as the owner.
            control = subprocess.run(
                ['/usr/bin/git', '--no-optional-locks', '-C', str(repository),
                 '-c', 'core.fsmonitor=' + str(monitor), 'status', '--short'],
                user=owner.pw_uid, group=owner.pw_gid, extra_groups=[],
                env={'PATH': '/usr/bin:/bin', 'HOME': owner.pw_dir,
                     'GIT_CONFIG_NOSYSTEM': '1', 'GIT_CONFIG_GLOBAL': '/dev/null'},
                capture_output=True, text=True, timeout=10)
            self.assertEqual(control.returncode, 0, control.stderr)
            self.assertTrue(marker.exists(), 'Hostile monitor positive control did not execute')
            marker.unlink()
            for production_helper in (repository_git, backup_repository_git):
                response = production_helper(['status', '--short'], repository)
                self.assertTrue(response['ok'], response['stderr'])
                self.assertIn('monitor', response['stdout'])
                self.assertFalse(marker.exists())
                identity = production_helper(['-c', 'alias.security-identity=!id -u', 'security-identity'], repository)
                self.assertTrue(identity['ok'], identity['stderr'])
                self.assertEqual(identity['stdout'], str(owner.pw_uid))
            print('SECURITY_ROOT_EXPORTER_REAL_UID=PASS', flush=True)
            # A fresh baseline must still allow the service owner to create its
            # runtime request directories without recursively owning the repository.
            application = base / 'application'
            application.mkdir(mode=0o755)
            for name in ('app', 'data', 'logs', 'backups', 'scripts'):
                (application/name).mkdir()
            create = ['/usr/bin/python3', '-c',
                      'import pathlib,sys; pathlib.Path(sys.argv[1]).mkdir(parents=True)',
                      str(application/'backup-requests/pending')]
            identity = dict(user=owner.pw_uid, group=owner.pw_gid, extra_groups=[])
            denied = subprocess.run(create, capture_output=True, text=True, **identity)
            self.assertNotEqual(denied.returncode, 0, 'Fresh root-owned baseline positive control must deny the service owner')
            baseline = (ROOT/'deployment/rocky-linux/install-baseline-tools-oraclelinux9.sh').read_text()
            ownership = next(line for line in baseline.splitlines() if line.startswith('sudo chown --no-dereference '))
            command = ownership.replace('sudo chown --no-dereference opc:opc',
                                        f'chown --no-dereference {owner.pw_uid}:{owner.pw_gid}')
            command = command.replace('/opt/project-time-platform', shlex.quote(str(application)))
            subprocess.run(['bash', '-eu', '-c', command], check=True, capture_output=True, text=True)
            allowed = subprocess.run(create, capture_output=True, text=True, **identity)
            self.assertEqual(allowed.returncode, 0, allowed.stderr)
            print('HOST_RUNTIME_DIRECTORY_WRITE=PASS', flush=True)

    def test_service_default_file_and_directory_permissions_are_private(self):
        unit = (ROOT / 'deployment/rocky-linux/projecttime-api.service').read_text()
        masks = re.findall(r'^UMask=([0-7]{3,4})$', unit, re.MULTILINE)
        self.assertEqual(len(masks), 1, 'The API unit must specify one creation mask')
        mask = int(masks[0], 8)
        with tempfile.TemporaryDirectory() as directory:
            process = subprocess.run(
                ['/usr/bin/python3', '-c',
                 'import os,pathlib,sys; os.umask(int(sys.argv[1],8)); '
                 'p=pathlib.Path(sys.argv[2]); (p/"fixture").write_text("test-only"); '
                 '(p/"private-dir").mkdir()', masks[0], directory],
                check=True, capture_output=True, text=True)
            self.assertEqual(stat.S_IMODE((Path(directory)/'fixture').stat().st_mode), 0o600)
            self.assertEqual(stat.S_IMODE((Path(directory)/'private-dir').stat().st_mode), 0o700)

    def test_all_repository_calls_use_the_unprivileged_helper(self):
        self.assertNotIn('run(["git"', code)
        self.assertEqual(code.count('repository_git(['), 3)

    def test_backup_uses_identical_privilege_boundary(self):
        self.assertEqual(ast.dump(helper), ast.dump(backup_helper))
        self.assertNotIn('git -C', BACKUP_SOURCE.read_text())
        self.assertIn('repository_git(arguments, repository)', backup_code)


if __name__ == '__main__':
    unittest.main()
