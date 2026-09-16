#!/usr/bin/env bash
# restore.sh — SPEC-20260914-backup-restore
# Restore knowledgehub.db from a backup produced by backup.sh, then restart the
# service (docker container `knowledgehub` or systemd unit `knowledgehub.service`,
# whichever is present).
set -euo pipefail

BACKUP_DIR=""
DATA_DIR="./data"
DB_PATH=""

usage() {
    cat <<'USAGE'
Usage: ./restore.sh --backup <dir> [options]

  --backup <dir>     Backup directory created by backup.sh (required)
  --data-dir <dir>   Directory containing knowledgehub.db (default: ./data)
  --db <path>        Explicit database path (overrides --data-dir)
  --no-restart       Do not restart the service after restoring
  -h, --help         Show this help

The service is stopped before the restore and started afterwards when
--no-restart is not given.
USAGE
}

NO_RESTART=0
while [[ $# -gt 0 ]]; do
    case "$1" in
        --backup)     BACKUP_DIR="${2:?--backup requires a value}"; shift 2 ;;
        --data-dir)   DATA_DIR="${2:?--data-dir requires a value}"; shift 2 ;;
        --db)         DB_PATH="${2:?--db requires a value}"; shift 2 ;;
        --no-restart) NO_RESTART=1; shift ;;
        -h|--help)    usage; exit 0 ;;
        *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
done

if [[ -z "$BACKUP_DIR" || ! -f "$BACKUP_DIR/knowledgehub.db" ]]; then
    echo "error: --backup must point at a backup dir containing knowledgehub.db" >&2
    exit 1
fi

DB_PATH="${DB_PATH:-$DATA_DIR/knowledgehub.db}"

stop_service() {
    if command -v docker >/dev/null 2>&1 && docker ps -a --format '{{.Names}}' | grep -qx knowledgehub; then
        echo "==> Stopping docker container knowledgehub"
        docker stop knowledgehub
        return 0
    fi
    if command -v systemctl >/dev/null 2>&1 && systemctl list-unit-files knowledgehub.service >/dev/null 2>&1; then
        echo "==> Stopping systemd unit knowledgehub.service"
        sudo systemctl stop knowledgehub.service || true
        return 0
    fi
    echo "warn: no knowledgehub container/service found — restore is file-only" >&2
    return 1
}

start_service() {
    if command -v docker >/dev/null 2>&1 && docker ps -a --format '{{.Names}}' | grep -qx knowledgehub; then
        echo "==> Starting docker container knowledgehub"
        docker start knowledgehub
        return 0
    fi
    if command -v systemctl >/dev/null 2>&1 && systemctl list-unit-files knowledgehub.service >/dev/null 2>&1; then
        echo "==> Starting systemd unit knowledgehub.service"
        sudo systemctl start knowledgehub.service || true
        return 0
    fi
    return 1
}

STOPPED=0
if stop_service; then
    STOPPED=1
fi

mkdir -p "$(dirname "$DB_PATH")"
echo "==> Restoring $BACKUP_DIR/knowledgehub.db -> $DB_PATH"
cp "$BACKUP_DIR/knowledgehub.db" "$DB_PATH"

# SPEC-20260916-firecrawl-mcp-proxy: restore the Data Protection key ring so
# IntegrationSecrets (upstream API keys) remain decryptable.
if [[ -f "$BACKUP_DIR/dataprotection-keys.tar.gz" ]]; then
    echo "==> Restoring Data Protection keys -> $(dirname "$DB_PATH")/dataprotection-keys"
    tar -xzf "$BACKUP_DIR/dataprotection-keys.tar.gz" -C "$(dirname "$DB_PATH")"
else
    echo "warn: backup has no dataprotection-keys — stored integration secrets won't decrypt" >&2
fi

if [[ $NO_RESTART -eq 0 && $STOPPED -eq 1 ]]; then
    start_service || true
fi

echo "==> Restore complete."
