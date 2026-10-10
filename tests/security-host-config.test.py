import importlib.util
import os
from pathlib import Path
import stat
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('host_config', ROOT/'deployment/rocky-linux/secure-config-directory.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class HostConfigurationTests(unittest.TestCase):
    def secure(self, directory):
        return module.secure_directory(directory, os.geteuid(), os.getegid())

    def test_existing_nested_files_are_secured_without_changing_content(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)/'config'
            directory.mkdir(mode=0o755)
            nested = directory/'nested'
            nested.mkdir(mode=0o755)
            for name in ('backup-sftp.env', 'backup-azure.env'):
                (nested/name).write_text('test-only configuration\n')
                (nested/name).chmod(0o644)
            self.assertEqual(self.secure(directory), 3)
            self.assertEqual(stat.S_IMODE(directory.stat().st_mode), 0o700)
            self.assertEqual(stat.S_IMODE(nested.stat().st_mode), 0o700)
            for path in nested.iterdir():
                self.assertEqual(stat.S_IMODE(path.stat().st_mode), 0o600)
                self.assertEqual(path.read_text(), 'test-only configuration\n')
            self.assertEqual(self.secure(directory), 3)

    def test_symlink_configuration_root_and_ancestor_are_refused(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            target = base/'target'
            target.mkdir(mode=0o755)
            (base/'link').symlink_to(target, target_is_directory=True)
            for path in (base/'link', base/'link'/'child'):
                with self.assertRaises(OSError): self.secure(path)
            self.assertEqual(stat.S_IMODE(target.stat().st_mode), 0o755)

    def test_symlink_hardlink_and_special_entries_fail_before_mutation(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            outside = base/'outside'
            outside.write_text('test-only')
            outside.chmod(0o644)
            for kind in ('symlink', 'hardlink', 'fifo'):
                directory = base/kind
                directory.mkdir(mode=0o755)
                entry = directory/'entry'
                if kind == 'symlink': entry.symlink_to(outside)
                elif kind == 'hardlink': os.link(outside, entry)
                else: os.mkfifo(entry)
                with self.assertRaises(ValueError): self.secure(directory)
                self.assertEqual(stat.S_IMODE(directory.stat().st_mode), 0o755)
                self.assertEqual(stat.S_IMODE(outside.stat().st_mode), 0o644)
                entry.unlink()

    def test_entry_budget_refuses_unbounded_tree(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            for index in range(257): (directory/str(index)).write_text('test-only')
            with self.assertRaisesRegex(ValueError, 'budget'): self.secure(directory)

    def test_concurrent_insert_or_replacement_cannot_be_reported_as_secured(self):
        for action in ('insert', 'replace'):
            with self.subTest(action=action), tempfile.TemporaryDirectory() as temp:
                directory = Path(temp)
                existing = directory/'existing.env'
                existing.write_text('test-only')
                original = os.fchmod
                mutated = False
                def chmod(fd, mode):
                    nonlocal mutated
                    original(fd, mode)
                    if not mutated:
                        mutated = True
                        if action == 'replace': existing.unlink()
                        (directory/('existing.env' if action == 'replace' else 'new.env')).write_text('test-only')
                with patch.object(os, 'fchmod', side_effect=chmod):
                    with self.assertRaisesRegex(ValueError, 'configuration_changed'):
                        self.secure(directory)

    def test_baseline_uses_fixed_helper_without_recursive_ownership_changes(self):
        source = (ROOT/'deployment/rocky-linux/install-baseline-tools-oraclelinux9.sh').read_text()
        self.assertIn('/secure-config-directory.py', source)
        self.assertNotIn('chown -R', source)
        self.assertIn('chown --no-dereference', source)


if __name__ == '__main__': unittest.main()
