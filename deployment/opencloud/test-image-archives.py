#!/usr/bin/env python3
"""Small negative/format tests; real export/import runs separately in Docker CI."""
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest


def module(filename, name):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(filename))
    obj = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(obj)
    return obj


archives = module('image-archives.py', 'image_archives')
loader = module('load-images.py', 'image_loader')


class ArchiveContract(unittest.TestCase):
    def archive(self, path, tag='test:one', config=b'{"architecture":"amd64"}', unsafe=False):
        key = hashlib.sha256(config).hexdigest() + '.json'
        data = {'manifest.json': json.dumps([{'Config': key, 'RepoTags': [tag], 'Layers': []}]).encode(), key: config}
        if unsafe:
            data['../bad'] = b'x'
        with tarfile.open(path, 'w:gz') as target:
            for name, content in data.items():
                entry = tarfile.TarInfo(name)
                entry.size = len(content)
                target.addfile(entry, io.BytesIO(content))
        return 'sha256:' + hashlib.sha256(config).hexdigest()

    def test_archive_identifies_config_and_tag(self):
        with tempfile.TemporaryDirectory() as t:
            path = Path(t) / 'image.tar.gz'
            image_id = self.archive(path)
            archives.validate_archive(path, 'test:one', image_id)

    def test_archive_rejects_wrong_tag(self):
        with tempfile.TemporaryDirectory() as t:
            path = Path(t) / 'image.tar.gz'
            image_id = self.archive(path)
            with self.assertRaises(ValueError):
                archives.validate_archive(path, 'test:two', image_id)

    def test_archive_rejects_wrong_id(self):
        with tempfile.TemporaryDirectory() as t:
            path = Path(t) / 'image.tar.gz'
            self.archive(path)
            with self.assertRaises(ValueError):
                archives.validate_archive(path, 'test:one', 'sha256:' + '0' * 64)

    def test_archive_rejects_traversal(self):
        with tempfile.TemporaryDirectory() as t:
            path = Path(t) / 'image.tar.gz'
            image_id = self.archive(path, unsafe=True)
            with self.assertRaises(ValueError):
                archives.validate_archive(path, 'test:one', image_id)

    def test_loader_rejects_traversal(self):
        with self.assertRaises(ValueError):
            loader.safe_file('../outside')

    def test_loader_rejects_symlink(self):
        with tempfile.TemporaryDirectory() as t:
            old = loader.ROOT
            try:
                loader.ROOT = Path(t)
                (Path(t) / 'target').write_text('test')
                (Path(t) / 'link').symlink_to(Path(t) / 'target')
                with self.assertRaises(ValueError):
                    loader.safe_file('link')
            finally:
                loader.ROOT = old


if __name__ == '__main__':
    unittest.main()
