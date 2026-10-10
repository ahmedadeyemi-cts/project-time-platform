#!/usr/bin/env bash
set -Eeuo pipefail
cd "$(dirname "$0")/.."
container="pulse-accounting-test-${RANDOM}-${RANDOM}"
cleanup(){ docker rm -f "$container" >/dev/null 2>&1 || true; }
trap cleanup EXIT
# Disposable data only; never use application connection strings.
docker run --name "$container" -e POSTGRES_HOST_AUTH_METHOD=trust -p 127.0.0.1::5432 -d postgres:16 >/dev/null
port="$(docker port "$container" 5432/tcp | sed 's/.*://')"
for attempt in {1..30}; do docker exec "$container" pg_isready -U postgres >/dev/null 2>&1 && break; sleep 1; done
export ACCOUNTING_TEST_CONNECTION="Host=127.0.0.1;Port=$port;Username=postgres;Database=postgres;Pooling=false"
dotnet run --project tests/AccountingReportingTests --configuration Release
