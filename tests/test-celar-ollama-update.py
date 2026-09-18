#!/usr/bin/env python3
"""Execute the maintenance transaction with isolated files and fake runtime commands.

No host service, model, credential or network endpoint is accessed. Only the
script's fixed filesystem roots are relocated; its control flow runs unchanged.
"""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
UPDATER = ROOT / 'deployment/oracle-celar/ollama-update.sh'
MODELS = ['gemma3:4b', 'qwen3:4b-instruct', 'llama3.2:3b', 'embeddinggemma:latest']

MOCK = r'''
import json, os, signal, sys
from pathlib import Path
signal.signal(signal.SIGPIPE, signal.SIG_DFL)
root = Path(os.environ['CELAR_TEST_ROOT'])
mode = os.environ.get('CELAR_TEST_MODE', '')
command = Path(sys.argv[0]).name
args = sys.argv[1:]
with (root / 'events').open('a') as f:
    f.write(json.dumps([command] + args) + '\n')
state_path = root / 'models.json'
def read():
    return json.loads(state_path.read_text())
def write(value):
    state_path.write_text(json.dumps(value))
def canonical(name):
    return name if ':' in name else name + ':latest'
if command == 'id':
    print('0')
elif command in ('chown', 'sleep'):
    pass
elif command == 'tar':
    if mode == 'runtime_backup_failure' and args[0] == '-cpf':
        sys.exit(47)
    if mode == 'runtime_restore_failure' and args[0] == '-xpf':
        sys.exit(48)
    os.execv('/usr/bin/tar', ['tar'] + args)
elif command == 'install':
    clean = []
    while args:
        item = args.pop(0)
        if item in ('-o', '-g'):
            args.pop(0)
        else:
            clean.append(item)
    os.execv('/usr/bin/install', ['install'] + clean)
elif command == 'systemctl':
    if mode == 'rollback_service_failure' and args[0] == 'stop':
        sys.exit(46)
elif command == 'health-check.sh':
    changed = any(value == 'new' for name, value in read().items() if '-rollback-' not in name)
    if mode == 'health_failure' and changed:
        sys.exit(44)
    old_engine = '# engine=old' in (root / 'bin/ollama').read_text().splitlines()[-1]
    if old_engine and (root / 'runtime-state.json').read_text() != json.dumps(runtime_state(), sort_keys=True):
        sys.exit(49)
elif command == 'curl':
    if 'https://ollama.com/install.sh' in args:
        if mode == 'download_failure':
            sys.exit(22)
        destination = Path(args[args.index('--output') + 1])
        installer = ('#!/bin/sh\n'
                     'if [ -L "$CELAR_TEST_ROOT/bin/ollama" ]; then rm "$CELAR_TEST_ROOT/bin/ollama"; fi\n'
                     'cp "$CELAR_TEST_ROOT/new-engine" "$CELAR_TEST_ROOT/bin/ollama"\n'
                     'rm -rf "$CELAR_TEST_ROOT/local/lib/ollama" "$CELAR_TEST_ROOT/usr/lib/ollama"\n'
                     'mkdir -p "$CELAR_TEST_ROOT/local/lib/ollama" "$CELAR_TEST_ROOT/usr/lib/ollama"\n'
                     'printf new > "$CELAR_TEST_ROOT/local/lib/ollama/libggml.so"\n'
                     'printf new > "$CELAR_TEST_ROOT/usr/lib/ollama/new-only.so"\n'
                     'printf new > "$CELAR_TEST_ROOT/systemd/ollama.service"\n')
        if mode == 'term_after_update':
            installer += 'kill -TERM "$PPID"\n'
        if mode in ('installer_failure', 'rollback_failure', 'rollback_service_failure', 'runtime_restore_failure'):
            installer += 'exit 42\n'
        destination.write_text(installer)
    elif 'http://127.0.0.1:11434/api/version' in args:
        print('{"version":"new"}')
    elif 'http://127.0.0.1:11434/api/generate' in args:
        print('{"response":"OK"}')
    elif 'http://127.0.0.1:11434/api/embed' in args:
        print('{"embeddings":[[0.1,0.2]]}')
    else:
        raise RuntimeError('Unexpected test URL')
elif command == 'ollama':
    if args == ['--version']:
        print('ollama version is ' + ('new' if '# engine=new' in Path(__file__).read_text().splitlines()[-1] else 'old'))
    elif args == ['list']:
        rows = ['NAME ID SIZE MODIFIED'] + [name + ' digest 1GB today' for name in read()]
        if mode == 'noisy_list':
            rows += ['unrelated:tag digest 1GB today'] * 50000
        sys.stdout.write('\n'.join(rows) + '\n')
        sys.stdout.flush()
        if mode == 'list_failure':
            sys.exit(7)
    elif args[0] == 'cp':
        source, target = map(canonical, args[1:])
        if mode == 'copy_failure' and source.startswith('qwen3:') and '-rollback-' not in source:
            sys.exit(37)
        if mode == 'rollback_failure' and '-rollback-' in source:
            sys.exit(45)
        values = read()
        values[target] = values[source]
        write(values)
        if mode == 'term_before_update' and '-rollback-' in target:
            os.kill(os.getppid(), signal.SIGTERM)
    elif args[0] == 'pull':
        if mode == 'pull_failure' and args[1].startswith('qwen3:'):
            sys.exit(43)
        values = read()
        values[canonical(args[1])] = 'new'
        write(values)
    elif args[0] == 'rm':
        values = read()
        values.pop(canonical(args[1]), None)
        write(values)
    else:
        raise RuntimeError('Unexpected Ollama command')
else:
    raise RuntimeError('Unexpected fixture command: ' + command)
'''


def runtime_state(root):
    state = {}
    for relative in ('local/lib/ollama', 'usr/lib/ollama', 'systemd/ollama.service'):
        path = root / relative
        if path.is_symlink():
            state[relative] = {'symlink': os.readlink(path)}
        elif path.is_file():
            state[relative] = {'content': path.read_text(), 'mode': path.stat().st_mode & 0o777}
        elif path.is_dir():
            state[relative] = {str(p.relative_to(path)): p.read_text() for p in path.rglob('*') if p.is_file()}
    return state


# The mock health probe checks the same runtime files, including preserved absence.
MOCK = MOCK.replace("if command == 'id':", '''
def runtime_state():
    state = {}
    for relative in ('local/lib/ollama', 'usr/lib/ollama', 'systemd/ollama.service'):
        path = root / relative
        if path.is_symlink():
            state[relative] = {'symlink': os.readlink(path)}
        elif path.is_file():
            state[relative] = {'content': path.read_text(), 'mode': path.stat().st_mode & 0o777}
        elif path.is_dir():
            state[relative] = {str(p.relative_to(path)): p.read_text() for p in path.rglob('*') if p.is_file()}
    return state
if command == 'id':''')


class UpdateTransactionTests(unittest.TestCase):
    def run_update(self, mode='', *, source=None, symlink=False, previous=None, runtime='present'):
        with tempfile.TemporaryDirectory(prefix='celar-update-test-') as directory:
            root = Path(directory)
            binary_dir = root / 'bin'
            binary_dir.mkdir()
            engine = '#!' + sys.executable + '\n' + MOCK + '\n# engine=old\n'
            for command in ('ollama', 'id', 'chown', 'sleep', 'install', 'systemctl', 'curl', 'health-check.sh', 'tar'):
                path = binary_dir / command
                path.write_text(engine)
                path.chmod(0o755)
            if symlink:
                (binary_dir / 'ollama').rename(root / 'original-engine')
                (binary_dir / 'ollama').symlink_to(root / 'original-engine')
            new = root / 'new-engine'
            new.write_text(engine.replace('# engine=old', '# engine=new'))
            new.chmod(0o755)
            (root / 'local/lib').mkdir(parents=True)
            (root / 'usr/lib').mkdir(parents=True)
            (root / 'systemd').mkdir()
            if runtime != 'absent':
                libraries = root / 'local/lib/ollama'
                if runtime == 'symlink':
                    libraries.symlink_to(root / 'original-libraries')
                    libraries = root / 'original-libraries'
                libraries.mkdir()
                (libraries / 'libggml.so').write_text('old')
                (libraries / 'old-only.so').write_text('old-only')
                (root / 'systemd/ollama.service').write_text('original service')
                (root / 'systemd/ollama.service').chmod(0o640)
            initial_runtime = runtime_state(root)
            (root / 'runtime-state.json').write_text(json.dumps(initial_runtime, sort_keys=True))
            models = {model: 'old' for model in MODELS}
            if mode == 'missing_model':
                models.pop('embeddinggemma:latest')
            # Exercise retention for both historical bare-name and canonical aliases.
            for stamp in ('20260801T060000Z', '20260808T060000Z', '20260815T060000Z'):
                models['embeddinggemma:latest-rollback-' + stamp] = 'old'
                models['embeddinggemma-rollback-' + stamp + ':latest'] = 'old'
            (root / 'models.json').write_text(json.dumps(models))
            gateway = root / 'state/gateway'
            gateway.mkdir(parents=True)
            status_path = gateway / 'update-status.json'
            if previous is not None:
                status_path.write_text(json.dumps(previous))
            shutil.copy(ROOT / 'deployment/oracle-celar/release.json', root / 'release.json')
            shutil.copy(binary_dir / 'health-check.sh', root / 'health-check.sh')
            script = source if source is not None else UPDATER.read_text()
            for old, replacement in (
                ('/var/lib/celar-ai', str(root / 'state')),
                ('/run/celar-runtime-mutation.lock', str(root / 'mutation.lock')),
                ('/opt/celar-ai/deploy', str(root)),
                ('/usr/local/lib/ollama', str(root / 'local/lib/ollama')),
                ('/usr/lib/ollama', str(root / 'usr/lib/ollama')),
                ('/etc/systemd/system/ollama.service', str(root / 'systemd/ollama.service')),
            ):
                script = script.replace(old, replacement)
            script_path = root / 'ollama-update.sh'
            script_path.write_text(script)
            environment = dict(os.environ, PATH=str(binary_dir) + os.pathsep + os.environ['PATH'],
                               CELAR_TEST_ROOT=str(root), CELAR_TEST_MODE=mode, TMPDIR=str(root))
            result = subprocess.run(['bash', str(script_path)], env=environment, capture_output=True,
                                    text=True, timeout=30)
            status = json.loads(status_path.read_text()) if status_path.exists() else None
            events = [json.loads(line) for line in (root / 'events').read_text().splitlines()]
            return {
                'code': result.returncode, 'status': status, 'events': events,
                'models': json.loads((root / 'models.json').read_text()),
                'engine_restored': (binary_dir / 'ollama').read_text() == engine,
                'symlink': (binary_dir / 'ollama').is_symlink(),
                'runtime_restored': runtime_state(root) == initial_runtime,
                'output': result.stdout + result.stderr,
            }

    def test_old_reader_reproduces_exit_141(self):
        # Exercise the original pipeline with a producer exceeding the pipe buffer.
        source = UPDATER.read_text().replace(
            'NR > 1 && ($1 == model || $1 == model ":latest") && !found { found=$1 }\n'
            '    END { if (found) print found }',
            'NR > 1 && ($1 == model || $1 == model ":latest") { print $1; exit }')
        self.assertNotEqual(source, UPDATER.read_text())
        result = self.run_update('noisy_list', source=source)
        self.assertEqual(result['code'], 141, result['output'])

    def test_large_listing_completes_update(self):
        result = self.run_update('noisy_list')
        self.assertEqual(result['code'], 0, result['output'])
        self.assertEqual(result['status']['lastResult'], 'success')
        self.assertTrue(result['status']['completedAt'])
        self.assertTrue(all(result['models'][model] == 'new' for model in MODELS))
        canonical = [name for name in result['models'] if name.startswith('embeddinggemma:latest-rollback-')]
        legacy = [name for name in result['models'] if name.startswith('embeddinggemma-rollback-')]
        self.assertEqual(len(canonical), 2)
        self.assertEqual(len(legacy), 2)

    def test_early_failures_are_terminal_without_service_mutation(self):
        for mode, code in (('list_failure', 7), ('copy_failure', 37), ('missing_model', 1),
                           ('download_failure', 22), ('term_before_update', 143), ('runtime_backup_failure', 47)):
            with self.subTest(mode=mode):
                result = self.run_update(mode)
                self.assertEqual(result['code'], code, result['output'])
                self.assertEqual(result['status']['lastResult'], 'failed')
                self.assertTrue(result['status']['lastFailedUpdateAt'])
                self.assertTrue(result['status']['completedAt'])
                self.assertFalse(result['status']['rollbackPerformed'])
                self.assertFalse(any(event[0] == 'systemctl' for event in result['events']))
                self.assertTrue(result['engine_restored'])
                self.assertTrue(result['runtime_restored'])
                self.assertTrue(all(value == 'old' for value in result['models'].values()))

    def test_update_failures_restore_engine_models_and_prior_success(self):
        previous = {'lastSuccessfulUpdateAt': '2026-08-30T06:05:00Z'}
        for mode, code in (('installer_failure', 42), ('pull_failure', 43), ('health_failure', 44),
                           ('term_after_update', 143)):
            for symlink in (False, True):
                with self.subTest(mode=mode, symlink=symlink):
                    result = self.run_update(mode, symlink=symlink, previous=previous)
                    self.assertEqual(result['code'], code, result['output'])
                    self.assertEqual(result['status']['lastResult'], 'rolled_back')
                    self.assertTrue(result['status']['rollbackPerformed'])
                    self.assertEqual(result['status']['lastSuccessfulUpdateAt'], previous['lastSuccessfulUpdateAt'])
                    self.assertTrue(result['status']['lastFailedUpdateAt'])
                    self.assertTrue(result['status']['completedAt'])
                    self.assertTrue(result['engine_restored'])
                    self.assertTrue(result['runtime_restored'])
                    self.assertEqual(result['symlink'], symlink)
                    self.assertTrue(all(result['models'][model] == 'old' for model in MODELS))

    def test_failed_rollback_is_not_reported_as_restored(self):
        for mode in ('rollback_failure', 'rollback_service_failure', 'runtime_restore_failure'):
            with self.subTest(mode=mode):
                result = self.run_update(mode)
                self.assertEqual(result['code'], 42, result['output'])
                self.assertEqual(result['status']['lastResult'], 'rollback_failed')
                self.assertFalse(result['status']['rollbackPerformed'])
                self.assertTrue(result['status']['completedAt'])
                self.assertIsNone(result['status']['lastSuccessfulUpdateAt'])

    def test_runtime_restore_preserves_absence_and_library_symlinks(self):
        for runtime in ('absent', 'symlink'):
            with self.subTest(runtime=runtime):
                result = self.run_update('installer_failure', runtime=runtime)
                self.assertEqual(result['code'], 42, result['output'])
                self.assertEqual(result['status']['lastResult'], 'rolled_back')
                self.assertTrue(result['runtime_restored'])


if __name__ == '__main__':
    unittest.main()
