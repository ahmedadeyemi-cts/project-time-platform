#!/usr/bin/env python3
"""Restricted public frontend server for Project Pulse validation.

This is intended only for temporary validation. It binds to 0.0.0.0:5173,
serves the built frontend, and proxies /api/* to the local backend. It blocks
requests from source IPs that are not explicitly allowed.

The server always allows loopback addresses so local curl tests and SSH tunnel
checks do not get blocked while public access remains restricted by source IP.
"""

from __future__ import annotations

import argparse
import importlib.util
import os
from http.server import ThreadingHTTPServer
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
LOCAL_SERVER_PATH = SCRIPT_DIR / "serve-frontend-local.py"

spec = importlib.util.spec_from_file_location("serve_frontend_local", LOCAL_SERVER_PATH)
if spec is None or spec.loader is None:
    raise SystemExit(f"Unable to load {LOCAL_SERVER_PATH}")

module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class RestrictedFrontendProxyHandler(module.FrontendProxyHandler):
    allowed_source_ips = {"45.19.161.17", "127.0.0.1", "::1"}

    def handle_one_request(self) -> None:
        client_ip = self.client_address[0]
        if client_ip not in self.allowed_source_ips:
            try:
                self.send_response(403)
                self.send_header("Content-Type", "text/plain; charset=utf-8")
                self.send_header("Cache-Control", "no-store")
                self.end_headers()
                self.wfile.write(b"Forbidden: source IP is not allowed for this temporary validation server.\n")
            except Exception:
                pass
            print(f"Blocked request from {client_ip}")
            return

        super().handle_one_request()


def parse_allowed_ips(value: str) -> set[str]:
    configured_ips = {item.strip() for item in value.split(",") if item.strip()}
    return configured_ips | {"127.0.0.1", "::1"}


def main() -> None:
    parser = argparse.ArgumentParser(description="Serve Project Pulse frontend publicly with source-IP restriction.")
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=5173)
    parser.add_argument("--allowed-source-ip", default=os.environ.get("PROJECT_PULSE_ALLOWED_SOURCE_IP", "45.19.161.17"))
    args = parser.parse_args()

    if not module.DIST_DIR.exists():
        raise SystemExit(f"Missing frontend build directory: {module.DIST_DIR}. Run build-frontend.sh first.")

    RestrictedFrontendProxyHandler.allowed_source_ips = parse_allowed_ips(args.allowed_source_ip)
    os.chdir(module.DIST_DIR)

    server = ThreadingHTTPServer((args.host, args.port), RestrictedFrontendProxyHandler)
    print(f"Serving frontend from {module.DIST_DIR}")
    print(f"Proxying /health and /api/* to {module.BACKEND_BASE_URL}")
    print(f"Public validation URL: http://<server-public-ip>:{args.port}/")
    print(f"Allowed source IPs: {', '.join(sorted(RestrictedFrontendProxyHandler.allowed_source_ips))}")
    print("Press CTRL+C to stop.")
    server.serve_forever()


if __name__ == "__main__":
    main()
