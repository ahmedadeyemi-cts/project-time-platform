#!/usr/bin/env python3
"""Fail closed before compose interpolation; never print credential values."""
import os
import re
import sys


def validate(environ):
    password = environ.get("RUNTIME_DB_PASSWORD", "")
    # A generated hex secret cannot inject additional Npgsql connection options.
    if not re.fullmatch(r"[0-9a-fA-F]{64,128}", password):
        raise ValueError("RUNTIME_DB_PASSWORD must be a separately generated 32–64 byte hex secret")
    if password == environ.get("POSTGRES_PASSWORD"):
        raise ValueError("Runtime and provisioning passwords must be different")
    if environ.get("POSTGRES_USER", "projectpulse") in ("ptp_runtime", "ptp_app"):
        raise ValueError("Provisioning identity must be separate from application roles")
    if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]{0,62}", environ.get("POSTGRES_DB", "ProjectPulse")):
        raise ValueError("POSTGRES_DB must be a simple database identifier")


if __name__ == "__main__":
    try:
        validate(os.environ)
    except ValueError as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
