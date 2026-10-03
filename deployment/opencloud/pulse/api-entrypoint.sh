#!/bin/sh
set -eu
# Never log runtime values. Prevent legacy connection strings from silently
# taking precedence over the package's local database identity and secret.
if [ -n "${ConnectionStrings__DefaultConnection:-}${ConnectionStrings__ProjectPulse:-}${ConnectionStrings__ProjectTime:-}${PROJECTPULSE_CONNECTION_STRING:-}${PROJECTTIME_DATABASE_CONNECTION:-}" ]; then
    echo 'STOP: legacy database connection overrides must be reconciled before startup.' >&2
    exit 64
fi
if [ "${PROJECTPULSE_ENVIRONMENT:-}" != test ]; then
    echo 'STOP: this development package is not qualified for production.' >&2
    exit 64
fi
if [ ! -s /run/secrets/api-database-password ]; then
    echo 'STOP: runtime database password secret is unavailable.' >&2
    exit 64
fi
PTP_DB_PASSWORD="$(cat /run/secrets/api-database-password)"
[ -n "$PTP_DB_PASSWORD" ] || exit 64
export PTP_DB_PASSWORD
exec dotnet /app/ProjectTime.Api.dll
