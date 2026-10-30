#!/usr/bin/env bash
# Proves a backup can be restored, without touching the production database.
#
# Restores a .bak.gz from vps-db-backup.sh into a scratch database on the same SQL Server, checks
# it, and drops it again. Run it monthly and before the AWS teardown: an untested backup is a
# guess. With no argument it uses the newest local backup; pass an s3:// URI to test what is off
# the box, which is the copy that matters when the disk is gone.
#
# Usage: vps-db-restore-drill.sh [backup.bak.gz | s3://.../backup.bak.gz]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/vps-common.sh
source "$SCRIPT_DIR/lib/vps-common.sh"

CONTAINER_DIR="/var/opt/mssql/backup"
HOST_DIR="$(env_value SQLSERVER_BACKUP_DIR)"
HOST_DIR="${HOST_DIR:-$APP_DIR/backups}"
SCRATCH_DB="${DATABASE}_RestoreDrill"
SOURCE="${1:-}"

if [[ -z "$SOURCE" ]]; then
  SOURCE="$(ls -1t "$HOST_DIR"/*.bak.gz 2>/dev/null | head -n 1 || true)"
  [[ -n "$SOURCE" ]] || { echo "No backup in $HOST_DIR." >&2; exit 1; }
fi

WORK="drill-$(date -u +%Y%m%dT%H%M%SZ).bak"
if [[ "$SOURCE" == s3://* ]]; then
  echo "==> Downloading $SOURCE"
  TMP="$(mktemp -t evangelion-drill-XXXXXX.bak.gz)"
  trap 'rm -f "$TMP"' EXIT
  aws s3 cp --only-show-errors "$SOURCE" "$TMP"
  SOURCE="$TMP"
fi

echo "==> Restoring $(basename "$SOURCE") as $SCRATCH_DB"
gunzip -c "$SOURCE" | docker exec -i "$SQL_CONTAINER" sh -c "cat > '$CONTAINER_DIR/$WORK'"

cleanup_container() {
  sa_sqlcmd -Q "IF DB_ID(N'$SCRATCH_DB') IS NOT NULL BEGIN ALTER DATABASE [$SCRATCH_DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$SCRATCH_DB]; END" >/dev/null || true
  docker exec "$SQL_CONTAINER" rm -f "$CONTAINER_DIR/$WORK" || true
}
trap 'cleanup_container; rm -f "${TMP:-}"' EXIT

# The logical file names come from the backup itself, so MOVE works whatever they are called.
FILES="$(sa_sqlcmd -h -1 -W -s '|' -Q "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK = N'$CONTAINER_DIR/$WORK';" |
  awk -F'|' 'NF > 2 { print $1 "|" $3 }')"
MOVES=""
while IFS='|' read -r logical type; do
  [[ -n "$logical" ]] || continue
  extension="mdf"; [[ "$type" == "L" ]] && extension="ldf"
  MOVES+=", MOVE N'$logical' TO N'/var/opt/mssql/data/${SCRATCH_DB}_${logical}.$extension'"
done <<< "$FILES"

sa_sqlcmd -Q "RESTORE DATABASE [$SCRATCH_DB] FROM DISK = N'$CONTAINER_DIR/$WORK' WITH REPLACE, CHECKSUM$MOVES;" >/dev/null
sa_sqlcmd -d "$SCRATCH_DB" -Q "DBCC CHECKDB WITH NO_INFOMSGS;"

echo "==> Restored database"
sa_sqlcmd -d "$SCRATCH_DB" -h -1 -W -s ' ' -Q "SET NOCOUNT ON;
  SELECT 'migrations', COUNT(*) FROM __EFMigrationsHistory;
  SELECT 'newest', MAX(MigrationId) FROM __EFMigrationsHistory;
  SELECT 'tables', COUNT(*) FROM sys.tables;"
echo "==> Drill passed. $SCRATCH_DB is dropped on exit."
