#!/usr/bin/env bash
# backup.sh — SPEC-20260914-backup-restore
# Consistent backup of the KnowledgeHub SQLite database (VACUUM INTO) plus an
# archive of every ObsidianVault source path that is accessible from this host.
set -euo pipefail

DATA_DIR="./data"
OUT_DIR="./backups"
DB_PATH=""

usage() {
    cat <<'USAGE'
Usage: ./backup.sh [options]

  --data-dir <dir>   Directory containing knowledgehub.db (default: ./data)
  --db <path>        Explicit path to the database file (overrides --data-dir)
  --out <dir>        Backup output directory (default: ./backups)
  -h, --help         Show this help

Produces backups/<timestamp>/ containing:
  knowledgehub.db    consistent SQLite snapshot (VACUUM INTO)
  vaults/            tar.gz per accessible Obsidian vault source
  manifest.txt       backup metadata (timestamp, db, vault paths)
USAGE
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --data-dir) DATA_DIR="${2:?--data-dir requires a value}"; shift 2 ;;
        --db)       DB_PATH="${2:?--db requires a value}"; shift 2 ;;
        --out)      OUT_DIR="${2:?--out requires a value}"; shift 2 ;;
        -h|--help)  usage; exit 0 ;;
        *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
done

DB_PATH="${DB_PATH:-$DATA_DIR/knowledgehub.db}"
if [[ ! -f "$DB_PATH" ]]; then
    echo "error: database not found at '$DB_PATH' (use --db or --data-dir)" >&2
    exit 1
fi
if ! command -v sqlite3 >/dev/null 2>&1; then
    echo "error: sqlite3 CLI is required for a consistent (VACUUM INTO) backup" >&2
    exit 1
fi

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
DEST="$OUT_DIR/$STAMP"
mkdir -p "$DEST/vaults"

echo "==> Backing up database $DB_PATH"
sqlite3 "$DB_PATH" "VACUUM INTO '$DEST/knowledgehub.db';"

# Archive linked Obsidian vaults whose path is accessible.
VAULT_COUNT=0
if command -v jq >/dev/null 2>&1; then
    while IFS= read -r vault_path; do
        [[ -z "$vault_path" || "$vault_path" == "null" ]] && continue
        if [[ -d "$vault_path" ]]; then
            slug="$(basename "$vault_path" | tr -c '[:alnum:]_-' '_')"
            echo "==> Archiving vault $vault_path -> vaults/$slug.tar.gz"
            tar -czf "$DEST/vaults/$slug.tar.gz" -C "$(dirname "$vault_path")" "$(basename "$vault_path")"
            VAULT_COUNT=$((VAULT_COUNT + 1))
        else
            echo "warn: vault path '$vault_path' not accessible — skipped" >&2
        fi
    done < <(sqlite3 -json "$DB_PATH" \
        "SELECT ConfigurationJson FROM Sources WHERE SourceType='ObsidianVault'" \
        | jq -r '.[].ConfigurationJson | fromjson | .path // empty')
else
    echo "warn: jq not found — vault paths cannot be read from the DB; DB-only backup" >&2
fi

cat > "$DEST/manifest.txt" <<EOF
created_utc=$STAMP
db_source=$DB_PATH
vaults_archived=$VAULT_COUNT
EOF

echo "==> Backup complete: $DEST"
