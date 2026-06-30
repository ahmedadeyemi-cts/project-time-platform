#!/usr/bin/env python3
from http.server import ThreadingHTTPServer
from pathlib import Path
import argparse
import importlib.util
import json

LOCAL_PROXY_FILE = Path(__file__).resolve().parent / "serve-frontend-local.py"

spec = importlib.util.spec_from_file_location("project_pulse_frontend_local", LOCAL_PROXY_FILE)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def parse_allowed_ips(values):
    allowed = {"127.0.0.1", "::1"}

    for value in values or []:
        for item in value.split(","):
            clean = item.strip()
            if clean:
                allowed.add(clean)

    return allowed



def projectpulse_is_public_auth_endpoint(path):
    public_prefixes = (
        "/api/auth/login/route",
        "/api/auth/local/login",
        "/api/auth/password-reset/request",
        "/api/auth/sso/start",
        "/api/auth/sso/callback",
        "/api/auth/sso/test-config",
    )

    return any(path.startswith(prefix) for prefix in public_prefixes)

class RestrictedFrontendProxyHandler(module.FrontendProxyHandler):
    allowed_source_ips = {"127.0.0.1", "::1"}

    def client_is_allowed(self):
        return self.client_address[0] in self.allowed_source_ips

    def reject_client(self):
        payload = json.dumps({
            "status": "forbidden",
            "message": "This Project Pulse frontend is restricted to approved source IP addresses."
        }).encode("utf-8")

        self.send_response(403)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(payload)

    def do_GET(self):
        if not self.client_is_allowed():
            self.reject_client()
            return

        super().do_GET()

    def do_POST(self):
        if not self.client_is_allowed():
            self.reject_client()
            return

        super().do_POST()

    def do_PUT(self):
        if not self.client_is_allowed():
            self.reject_client()
            return

        super().do_PUT()

    def do_PATCH(self):
        if not self.client_is_allowed():
            self.reject_client()
            return

        super().do_PATCH()

    def do_DELETE(self):
        if not self.client_is_allowed():
            self.reject_client()
            return

        super().do_DELETE()

    def do_OPTIONS(self):
        if not self.client_is_allowed():
            self.reject_client()
            return

        super().do_OPTIONS()


def main():
    parser = argparse.ArgumentParser(description="Restricted Project Pulse public frontend.")
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=5173)
    parser.add_argument("--allowed-source-ip", action="append", default=[])
    args = parser.parse_args()

    RestrictedFrontendProxyHandler.allowed_source_ips = parse_allowed_ips(args.allowed_source_ip)

    server = ThreadingHTTPServer((args.host, args.port), RestrictedFrontendProxyHandler)

    print(f"Serving frontend from {module.DIST_DIR}")
    print(f"Proxying /health and /api/* to {module.BACKEND_BASE_URL}")
    print(f"Listening on http://{args.host}:{args.port}")
    print(f"Allowed source IPs: {', '.join(sorted(RestrictedFrontendProxyHandler.allowed_source_ips))}")

    server.serve_forever()


if __name__ == "__main__":
    main()
