"""Exercise GET/HEAD confinement through the real local HTTP handler."""
import http.client
import importlib.util
from pathlib import Path
import tempfile
import threading
import unittest
from http.server import ThreadingHTTPServer

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('frontend_proxy', ROOT / 'deployment/rocky-linux/serve-frontend-local.py')
proxy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(proxy)

class ProxyTests(unittest.TestCase):
    def test_get_and_head_use_same_confined_files_and_headers(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dist = root / 'dist'; dist.mkdir()
            (dist / 'index.html').write_text('safe application')
            (dist / 'asset.js').write_text('safe asset')
            (root / 'private.env').write_text('NEVER_SERVE_THIS')
            (dist / 'link').symlink_to(root / 'private.env')
            original = proxy.DIST_DIR; proxy.DIST_DIR = dist
            server = ThreadingHTTPServer(('127.0.0.1', 0), proxy.FrontendProxyHandler)
            worker = threading.Thread(target=server.serve_forever, daemon=True); worker.start()
            try:
                for path in ['/asset.js', '/private.env', '/../private.env', '/%2e%2e/private.env', '/link']:
                    results = []
                    for method in ['GET', 'HEAD']:
                        client = http.client.HTTPConnection(*server.server_address)
                        client.request(method, path)
                        response = client.getresponse(); body = response.read()
                        results.append((response.status, response.getheader('Content-Length'), response.getheader('Content-Type')))
                        self.assertNotIn(b'NEVER_SERVE_THIS', body)
                        if method == 'HEAD': self.assertEqual(body, b'')
                        else: self.assertIn(body, [b'safe application', b'safe asset'])
                        client.close()
                    self.assertEqual(*results)
            finally:
                server.shutdown(); server.server_close(); worker.join(); proxy.DIST_DIR = original

if __name__ == '__main__': unittest.main()
