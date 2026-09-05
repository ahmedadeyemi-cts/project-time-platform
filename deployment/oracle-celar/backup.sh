#!/usr/bin/env bash
set -Eeuo pipefail

BACKUP_EXCLUDES=(
  'etc/celar-ai/backup.env'
  'etc/celar-ai/gateway/runtime-token'
  'var/lib/celar-ai/recovery'
  'var/lib/celar-ai/recovery/**'
  'var/lib/celar-ai/ollama-models'
  'var/lib/ollama'
  'var/lib/clamav'
)

PROTECTED_ARCHIVE_PATH_REGEX='^(\./)?(etc/celar-ai/backup\.env|etc/celar-ai/gateway/runtime-token|var/lib/celar-ai/recovery(/|$)|var/lib/celar-ai/ollama-models(/|$)|var/lib/ollama(/|$)|var/lib/clamav(/|$))'
LIST_FILE_TO_CLEAN=''
ARCHIVE_TEMP_TO_CLEAN=''
ARCHIVE_LISTING_TO_CLEAN=''

cleanup_backup_temp_files() {
  [[ -z "${ARCHIVE_TEMP_TO_CLEAN:-}" ]] || rm -f -- "$ARCHIVE_TEMP_TO_CLEAN"
  [[ -z "${ARCHIVE_LISTING_TO_CLEAN:-}" ]] || rm -f -- "$ARCHIVE_LISTING_TO_CLEAN"
  [[ -z "${LIST_FILE_TO_CLEAN:-}" ]] || rm -f -- "$LIST_FILE_TO_CLEAN"
}

backup_interrupted() {
  local code="$1"
  cleanup_backup_temp_files
  trap - EXIT INT TERM
  exit "$code"
}

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

create_local_archive() {
  local source_root="$1"
  local list_file="$2"
  local archive="$3"
  local archive_dir temp_archive archive_listing
  local -a tar_exclude_args=()

  archive_dir="$(dirname -- "$archive")"
  if ! install -d -m 0700 "$archive_dir"; then
    return 1
  fi
  if ! temp_archive="$(mktemp "$archive_dir/.celar-state-partial.XXXXXX.tar.zst")"; then
    return 1
  fi
  ARCHIVE_TEMP_TO_CLEAN="$temp_archive"
  if ! archive_listing="$(mktemp)"; then
    rm -f -- "$temp_archive"
    ARCHIVE_TEMP_TO_CLEAN=''
    return 1
  fi
  ARCHIVE_LISTING_TO_CLEAN="$archive_listing"
  if ! chmod 0600 "$temp_archive"; then
    rm -f -- "$temp_archive" "$archive_listing"
    ARCHIVE_TEMP_TO_CLEAN=''
    ARCHIVE_LISTING_TO_CLEAN=''
    return 1
  fi

  for pattern in "${BACKUP_EXCLUDES[@]}"; do
    tar_exclude_args+=("--exclude=$pattern")
  done

  # GNU tar's --exclude options are positional. They MUST appear before
  # --files-from so every listed tree is filtered before tar starts reading it.
  # Write through a root-only temporary file and publish with mv only after both
  # compression and a post-build secret-path inspection succeed.
  if ! tar \
    --create \
    --directory="$source_root" \
    "${tar_exclude_args[@]}" \
    --warning=no-file-changed \
    --ignore-failed-read \
    --files-from="$list_file" \
    | zstd -f -T0 -8 -q -o "$temp_archive"; then
    rm -f -- "$temp_archive" "$archive_listing"
    ARCHIVE_TEMP_TO_CLEAN=''
    ARCHIVE_LISTING_TO_CLEAN=''
    return 1
  fi

  if ! zstd -t -q -- "$temp_archive"; then
    rm -f -- "$temp_archive" "$archive_listing"
    ARCHIVE_TEMP_TO_CLEAN=''
    ARCHIVE_LISTING_TO_CLEAN=''
    return 1
  fi

  if ! zstd -dc -- "$temp_archive" | tar -tf - > "$archive_listing"; then
    rm -f -- "$temp_archive" "$archive_listing"
    ARCHIVE_TEMP_TO_CLEAN=''
    ARCHIVE_LISTING_TO_CLEAN=''
    return 1
  fi

  if grep -Eq "$PROTECTED_ARCHIVE_PATH_REGEX" "$archive_listing"; then
    echo 'ERROR: Local backup archive contains a protected/excluded path; refusing to publish it.' >&2
    rm -f -- "$temp_archive" "$archive_listing"
    ARCHIVE_TEMP_TO_CLEAN=''
    ARCHIVE_LISTING_TO_CLEAN=''
    return 1
  fi

  if ! chmod 0600 "$temp_archive"; then
    rm -f -- "$temp_archive" "$archive_listing"
    ARCHIVE_TEMP_TO_CLEAN=''
    ARCHIVE_LISTING_TO_CLEAN=''
    return 1
  fi
  if ! mv -f -- "$temp_archive" "$archive"; then
    rm -f -- "$temp_archive" "$archive_listing"
    ARCHIVE_TEMP_TO_CLEAN=''
    ARCHIVE_LISTING_TO_CLEAN=''
    return 1
  fi

  # The final path now owns the validated bytes. Clear staged-path cleanup state
  # before removing the member-list file so EXIT/TERM cannot delete publication.
  ARCHIVE_TEMP_TO_CLEAN=''
  rm -f -- "$archive_listing" || true
  ARCHIVE_LISTING_TO_CLEAN=''
  return 0
}

main() {
  local root manifest backup_root restic_env
  local retention_days keep_daily keep_weekly keep_monthly stamp archive list_file
  local restic_status pattern
  local -a restic_paths=() restic_exclude_args=()

  root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
  manifest="$root/release.json"
  [[ -s "$manifest" ]] || manifest='/opt/celar-ai/deploy/release.json'
  backup_root='/var/backups/celar-ai'
  restic_env='/etc/celar-ai/backup.env'

  [[ "$(id -u)" -eq 0 ]] || fail 'backup.sh requires root.'
  command -v jq >/dev/null 2>&1 || fail 'jq is required.'
  command -v tar >/dev/null 2>&1 || fail 'tar is required.'
  command -v zstd >/dev/null 2>&1 || fail 'zstd is required.'

  retention_days="$(jq -r '.localBackupRetentionDays' "$manifest")"
  keep_daily="$(jq -r '.resticKeepDaily' "$manifest")"
  keep_weekly="$(jq -r '.resticKeepWeekly' "$manifest")"
  keep_monthly="$(jq -r '.resticKeepMonthly' "$manifest")"
  stamp="$(date -u +%Y%m%dT%H%M%SZ)"
  archive="$backup_root/celar-state-$stamp.tar.zst"
  list_file="$(mktemp)"
  LIST_FILE_TO_CLEAN="$list_file"
  trap cleanup_backup_temp_files EXIT
  trap 'backup_interrupted 130' INT
  trap 'backup_interrupted 143' TERM

  install -d -m 0700 "$backup_root"

  add_path() {
    local candidate="$1"
    [[ -e "$candidate" ]] && printf '%s\n' "${candidate#/}" >> "$list_file"
  }

  add_path /etc/celar-ai
  add_path /etc/caddy
  add_path /etc/clamav/clamd.conf
  add_path /etc/iptables/rules.v4
  add_path /etc/iptables/rules.v6
  add_path /etc/systemd/system/ollama.service.d
  add_path /var/lib/celar-ai
  add_path /var/lib/caddy
  add_path /root/celar-firewall-backup

  sort -u -o "$list_file" "$list_file"
  [[ -s "$list_file" ]] || fail 'No backup paths were found.'

  # Local quick-rollback archive. Reproducible models/signatures, credentials,
  # and staged recovery trees are excluded. The archive is published atomically
  # only after its compressed stream and member list have both been verified.
  if ! create_local_archive / "$list_file" "$archive"; then
    fail 'Local backup archive creation or exclusion validation failed.'
  fi

  find "$backup_root" -maxdepth 1 -type f -name 'celar-state-*.tar.zst' -mtime "+$retention_days" -delete

  restic_status='NOT_CONFIGURED'
  if [[ -s "$restic_env" ]]; then
    command -v restic >/dev/null 2>&1 || fail 'restic is required when backup.env exists.'
    chmod 0600 "$restic_env"
    # shellcheck disable=SC1090
    . "$restic_env"
    : "${RESTIC_REPOSITORY:?RESTIC_REPOSITORY is required in backup.env}"
    : "${RESTIC_PASSWORD:?RESTIC_PASSWORD is required in backup.env}"
    export RESTIC_REPOSITORY RESTIC_PASSWORD
    export AWS_ACCESS_KEY_ID="${AWS_ACCESS_KEY_ID:-}"
    export AWS_SECRET_ACCESS_KEY="${AWS_SECRET_ACCESS_KEY:-}"
    export AWS_DEFAULT_REGION="${AWS_DEFAULT_REGION:-}"

    if ! restic snapshots >/dev/null 2>&1; then
      restic init
    fi

    mapfile -t restic_paths < <(sed 's#^#/#' "$list_file")
    for pattern in "${BACKUP_EXCLUDES[@]}"; do
      restic_exclude_args+=("--exclude=/$pattern")
    done

    restic backup \
      --tag celar-oracle \
      "${restic_exclude_args[@]}" \
      "${restic_paths[@]}"
    restic forget \
      --tag celar-oracle \
      --keep-daily "$keep_daily" \
      --keep-weekly "$keep_weekly" \
      --keep-monthly "$keep_monthly" \
      --prune
    restic_status='PASS'
  fi

  echo "CELAR_LOCAL_BACKUP=PASS ARCHIVE=$archive"
  echo "CELAR_EXTERNAL_RESTIC_BACKUP=$restic_status"
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  main "$@"
fi
