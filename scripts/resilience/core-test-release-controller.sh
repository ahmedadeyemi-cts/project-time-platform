#!/usr/bin/env bash
# Only invoked by the registered Protected Test workflow after exact-main CI admission.
set -Eeuo pipefail
exec python3 scripts/resilience/core_test_controller.py
