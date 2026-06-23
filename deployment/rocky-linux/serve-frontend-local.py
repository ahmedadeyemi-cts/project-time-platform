#!/usr/bin/env python3
"""Local development frontend server for Project Time Platform.

This server is intentionally bound to 127.0.0.1 and is intended for SSH tunnel
based testing only. It serves the built React frontend from dist/ and proxies
/health and /api/* requests to the local ASP.NET Core API on 127.0.0.1:5080.
"""

from __future__ import annotations

import argparse
import mimetypes
import os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen


REPO_ROOT = Path("/opt/project-time-platform/app/project-time-platform")
DIST_DIR = REPO_ROOT / "src/frontend/project-time-web/dist"
BACKEND_BASE_URL = "http://127.0.0.1:5080"


class FrontendProxyHandler(BaseHTTPRequestHandler):
    server_version = "ProjectTimeLocalFrontend/0.2"

    def do_GET(self) -> None:  # noqa: N802
        if self.path == "/health" or self.path.startswith("/api/"):
            self.proxy_to_backend(include_body=True)
            return

        self.serve_static_file(include_body=True)

    def do_HEAD(self) -> None:  # noqa: N802
        if self.path == "/health" or self.path.startswith("/api/"):
            self.proxy_to_backend(include_body=False)
            return

        self.serve_static_file(include_body=False)

    def proxy_to_backend(self, include_body: bool) -> None:
        target_url = f"{BACKEND_BASE_URL}{self.path}"
        method = "GET"
        request = Request(target_url, method=method)
        request.add_header("Accept", self.headers.get("Accept", "*/*"))

        try:
            with urlopen(request, timeout=15) as response:
                body = response.read()
                self.send_response(response.status)
                self.send_header("Content-Type", response.headers.get("Content-Type", "application/json"))
                self.send_header("Content-Length", str(len(body)))
                self.send_header("Cache-Control", "no-store")
                self.end_headers()
                if include_body:
                    self.wfile.write(body)
        except HTTPError as error:
            body = error.read()
            self.send_response(error.code)
            self.send_header("Content-Type", error.headers.get("Content-Type", "application/json"))
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            if include_body:
                self.wfile.write(body)
        except URLError as error:
            message = f"Backend proxy failed: {error.reason}".encode("utf-8")
            self.send_response(502)
            self.send_header("Content-Type", "text/plain; charset=utf-8")
            self.send_header("Content-Length", str(len(message)))
            self.end_headers()
            if include_body:
                self.wfile.write(message)

    def serve_static_file(self, include_body: bool) -> None:
        requested_path = self.path.split("?", 1)[0]
        if requested_path in ("", "/"):
            requested_path = "/index.html"

        relative_path = requested_path.lstrip("/")
        candidate_path = (DIST_DIR / relative_path).resolve()
        dist_root = DIST_DIR.resolve()

        if not str(candidate_path).startswith(str(dist_root)):
            self.send_error(403)
            return

        if not candidate_path.exists() or not candidate_path.is_file():
            candidate_path = DIST_DIR / "index.html"

        if not candidate_path.exists():
            self.send_error(404, "Frontend build not found. Run build-frontend.sh first.")
            return

        body = candidate_path.read_bytes()
        content_type = mimetypes.guess_type(str(candidate_path))[0] or "application/octet-stream"

        self.send_response(200)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        if include_body:
            self.wfile.write(body)

    def log_message(self, format: str, *args: object) -> None:
        print(f"{self.address_string()} - {format % args}")


def main() -> None:
    parser = argparse.ArgumentParser(description="Serve Project Time frontend locally with API proxy.")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=5173)
    args = parser.parse_args()

    if args.host != "127.0.0.1":
        raise SystemExit("This development server must remain bound to 127.0.0.1.")

    if not DIST_DIR.exists():
        raise SystemExit(f"Missing frontend build directory: {DIST_DIR}. Run build-frontend.sh first.")

    os.chdir(DIST_DIR)
    server = ThreadingHTTPServer((args.host, args.port), FrontendProxyHandler)
    print(f"Serving frontend from {DIST_DIR}")
    print(f"Proxying /health and /api/* to {BACKEND_BASE_URL}")
    print(f"Local URL: http://{args.host}:{args.port}/")
    print("Press CTRL+C to stop.")
    server.serve_forever()


if __name__ == "__main__":
    main()
