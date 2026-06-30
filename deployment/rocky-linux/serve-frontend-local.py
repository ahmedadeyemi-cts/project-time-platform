#!/usr/bin/env python3
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import argparse
import http.client
import json
import mimetypes
import urllib.parse

BACKEND_HOST = "127.0.0.1"
BACKEND_PORT = 5080
BACKEND_BASE_URL = f"http://{BACKEND_HOST}:{BACKEND_PORT}"

REPO_ROOT = Path(__file__).resolve().parents[2]
DIST_DIR = REPO_ROOT / "src/frontend/project-time-web/dist"

HOP_BY_HOP_HEADERS = {
    "connection",
    "keep-alive",
    "proxy-authenticate",
    "proxy-authorization",
    "te",
    "trailer",
    "transfer-encoding",
    "upgrade",
    "host",
    "content-length"
}


class FrontendProxyHandler(SimpleHTTPRequestHandler):
    server_version = "ProjectTimeLocalFrontend/0.4"

    def log_message(self, format, *args):
        return

    def do_GET(self):
        parsed = urllib.parse.urlparse(self.path)

        if parsed.path == "/health" or parsed.path.startswith("/api/"):
            self.proxy_to_backend()
            return

        self.serve_static_file()

    def do_POST(self):
        if urllib.parse.urlparse(self.path).path.startswith("/api/"):
            self.proxy_to_backend()
            return

        self.send_error(405, "POST is only supported for /api/* routes.")

    def do_PUT(self):
        if urllib.parse.urlparse(self.path).path.startswith("/api/"):
            self.proxy_to_backend()
            return

        self.send_error(405, "PUT is only supported for /api/* routes.")

    def do_PATCH(self):
        if urllib.parse.urlparse(self.path).path.startswith("/api/"):
            self.proxy_to_backend()
            return

        self.send_error(405, "PATCH is only supported for /api/* routes.")

    def do_DELETE(self):
        if urllib.parse.urlparse(self.path).path.startswith("/api/"):
            self.proxy_to_backend()
            return

        self.send_error(405, "DELETE is only supported for /api/* routes.")

    def do_OPTIONS(self):
        if urllib.parse.urlparse(self.path).path.startswith("/api/"):
            self.proxy_to_backend()
            return

        self.send_response(204)
        self.send_header("Cache-Control", "no-store")
        self.end_headers()

    def forward_request_headers(self):
        headers = {}

        for header_name, header_value in self.headers.items():
            if header_name.lower() in HOP_BY_HOP_HEADERS:
                continue

            headers[header_name] = header_value

        headers["Host"] = f"{BACKEND_HOST}:{BACKEND_PORT}"

        existing_forwarded_for = self.headers.get("X-Forwarded-For")
        client_ip = self.client_address[0]

        if existing_forwarded_for:
            headers["X-Forwarded-For"] = f"{existing_forwarded_for}, {client_ip}"
        else:
            headers["X-Forwarded-For"] = client_ip

        headers["X-Forwarded-Proto"] = "http"

        return headers

    def proxy_to_backend(self):
        body = None
        content_length = self.headers.get("Content-Length")

        if content_length:
            body = self.rfile.read(int(content_length))

        headers = self.forward_request_headers()

        try:
            connection = http.client.HTTPConnection(BACKEND_HOST, BACKEND_PORT, timeout=30)
            connection.request(self.command, self.path, body=body, headers=headers)

            response = connection.getresponse()
            response_body = response.read()

            self.send_response(response.status, response.reason)

            for header_name, header_value in response.getheaders():
                if header_name.lower() in HOP_BY_HOP_HEADERS:
                    continue

                self.send_header(header_name, header_value)

            self.send_header("Content-Length", str(len(response_body)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()

            if response_body:
                self.wfile.write(response_body)

        except Exception as exc:
            payload = json.dumps({
                "status": "frontend_proxy_error",
                "message": str(exc)
            }).encode("utf-8")

            self.send_response(502)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(payload)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(payload)

        finally:
            try:
                connection.close()
            except Exception:
                pass

    def serve_static_file(self):
        if not DIST_DIR.exists():
            payload = f"Missing frontend build directory: {DIST_DIR}. Run build-frontend.sh first.\n".encode("utf-8")
            self.send_response(500)
            self.send_header("Content-Type", "text/plain; charset=utf-8")
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)
            return

        parsed = urllib.parse.urlparse(self.path)
        request_path = urllib.parse.unquote(parsed.path)

        if request_path == "/":
            request_path = "/index.html"

        candidate = (DIST_DIR / request_path.lstrip("/")).resolve()
        dist_root = DIST_DIR.resolve()

        try:
            candidate.relative_to(dist_root)
        except ValueError:
            candidate = dist_root / "index.html"

        if not candidate.exists() or not candidate.is_file():
            candidate = dist_root / "index.html"

        content_type = mimetypes.guess_type(str(candidate))[0] or "application/octet-stream"
        data = candidate.read_bytes()

        self.send_response(200)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store" if candidate.name == "index.html" else "public, max-age=3600")
        self.end_headers()
        self.wfile.write(data)


def main():
    parser = argparse.ArgumentParser(description="Serve Project Pulse frontend and proxy API calls.")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=5173)
    args = parser.parse_args()

    server = ThreadingHTTPServer((args.host, args.port), FrontendProxyHandler)

    print(f"Serving frontend from {DIST_DIR}")
    print(f"Proxying /health and /api/* to {BACKEND_BASE_URL}")
    print(f"Listening on http://{args.host}:{args.port}")

    server.serve_forever()


if __name__ == "__main__":
    main()
