#!/usr/bin/env python3
"""Import annual company holidays for Project Pulse.

CSV format:
  holiday_date,holiday_name,holiday_type,is_floating_holiday,auto_populate_hours

Required columns:
  holiday_date, holiday_name

Optional columns default to:
  holiday_type=company_paid
  is_floating_holiday=false
  auto_populate_hours=8.00

Example:
  2026-01-01,New Year's Day,company_paid,false,8
  2026-07-03,Floating Holiday,floating,true,8
"""

from __future__ import annotations

import csv
import os
import subprocess
import sys
from pathlib import Path

APP_ROOT = Path("/opt/project-time-platform")
ENV_FILE = APP_ROOT / "config/postgres.env"


def load_env(path: Path) -> dict[str, str]:
    env: dict[str, str] = {}
    for line in path.read_text().splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        env[key.strip()] = value.strip().strip('"').strip("'")
    return env


def truthy(value: str | None) -> bool:
    return (value or "").strip().lower() in {"true", "1", "yes", "y"}


def main() -> int:
    if len(sys.argv) < 3:
        print("Usage: import-company-holidays.py <year> <csv-file> [uploaded-by-email]")
        return 2

    year = int(sys.argv[1])
    csv_path = Path(sys.argv[2]).expanduser().resolve()
    uploaded_by_email = sys.argv[3] if len(sys.argv) > 3 else "ahmed.adeyemi@ussignal.com"

    if not ENV_FILE.exists():
        raise SystemExit(f"Missing environment file: {ENV_FILE}")
    if not csv_path.exists():
        raise SystemExit(f"Missing CSV file: {csv_path}")

    env = load_env(ENV_FILE)
    rows: list[dict[str, str]] = []
    with csv_path.open(newline="") as handle:
        reader = csv.DictReader(handle)
        required = {"holiday_date", "holiday_name"}
        if not required.issubset(set(reader.fieldnames or [])):
            raise SystemExit("CSV must include holiday_date and holiday_name columns")
        for row in reader:
            if not row.get("holiday_date") or not row.get("holiday_name"):
                continue
            if not row["holiday_date"].startswith(f"{year}-"):
                print(f"Skipping {row['holiday_date']} because it is not in {year}")
                continue
            rows.append(row)

    if not rows:
        raise SystemExit("No holiday rows found to import")

    values_sql = []
    params = []
    for row in rows:
        values_sql.append("(%s, %s, %s, %s, %s)")
        params.extend([
            row["holiday_date"],
            row["holiday_name"],
            row.get("holiday_type") or "company_paid",
            "true" if truthy(row.get("is_floating_holiday")) else "false",
            row.get("auto_populate_hours") or "8.00",
        ])

    temp_sql = "\n".join([
        "BEGIN;",
        "INSERT INTO holiday_upload_batches (upload_year, original_filename, uploaded_by_user_id, row_count, notes)",
        "SELECT %s, %s, u.user_id, %s, 'Imported by import-company-holidays.py'",
        "FROM app_users u WHERE u.email = %s",
        "ON CONFLICT (upload_year, original_filename) DO UPDATE",
        "SET uploaded_at = NOW(), row_count = EXCLUDED.row_count, notes = EXCLUDED.notes",
        "RETURNING holiday_upload_batch_id;",
        "COMMIT;",
    ])

    psql_env = os.environ.copy()
    psql_env["PGPASSWORD"] = env["PTP_DB_PASSWORD"]

    batch_cmd = [
        "psql",
        "-h", env["PTP_DB_HOST"],
        "-p", env["PTP_DB_PORT"],
        "-U", env["PTP_DB_USER"],
        "-d", env["PTP_DB_NAME"],
        "-At",
        "-v", "ON_ERROR_STOP=1",
        "-c",
        temp_sql,
        "-v", f"year={year}",
    ]

    # Use a temporary SQL file for simple, safe quoting through psql variables is verbose;
    # instead generate literal SQL with escaped values for this internal utility.
    def q(value: object) -> str:
        return "'" + str(value).replace("'", "''") + "'"

    sql_lines = ["BEGIN;"]
    sql_lines.append("WITH batch AS (")
    sql_lines.append("  INSERT INTO holiday_upload_batches (upload_year, original_filename, uploaded_by_user_id, row_count, notes)")
    sql_lines.append(f"  SELECT {year}, {q(csv_path.name)}, u.user_id, {len(rows)}, 'Imported by import-company-holidays.py'")
    sql_lines.append(f"  FROM app_users u WHERE u.email = {q(uploaded_by_email)}")
    sql_lines.append("  ON CONFLICT (upload_year, original_filename) DO UPDATE")
    sql_lines.append("  SET uploaded_at = NOW(), row_count = EXCLUDED.row_count, notes = EXCLUDED.notes")
    sql_lines.append("  RETURNING holiday_upload_batch_id")
    sql_lines.append(")")

    for row in rows:
        sql_lines.append("INSERT INTO company_holidays (holiday_date, holiday_name, holiday_code, holiday_type, is_floating_holiday, auto_populate_hours, source_batch_id)")
        sql_lines.append(
            "SELECT "
            f"DATE {q(row['holiday_date'])}, {q(row['holiday_name'])}, 'HOLIDAY', {q(row.get('holiday_type') or 'company_paid')}, "
            f"{'TRUE' if truthy(row.get('is_floating_holiday')) else 'FALSE'}, {row.get('auto_populate_hours') or '8.00'}, holiday_upload_batch_id FROM batch "
            "ON CONFLICT (holiday_date) DO UPDATE "
            "SET holiday_name = EXCLUDED.holiday_name, holiday_type = EXCLUDED.holiday_type, is_floating_holiday = EXCLUDED.is_floating_holiday, auto_populate_hours = EXCLUDED.auto_populate_hours, is_active = TRUE, source_batch_id = EXCLUDED.source_batch_id, updated_at = NOW();"
        )

    sql_lines.append("COMMIT;")
    sql = "\n".join(sql_lines)

    result = subprocess.run(
        [
            "psql",
            "-h", env["PTP_DB_HOST"],
            "-p", env["PTP_DB_PORT"],
            "-U", env["PTP_DB_USER"],
            "-d", env["PTP_DB_NAME"],
            "-v", "ON_ERROR_STOP=1",
        ],
        input=sql,
        text=True,
        env=psql_env,
        check=False,
    )
    if result.returncode != 0:
        return result.returncode

    print(f"Imported {len(rows)} holiday rows for {year} from {csv_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
