#!/usr/bin/env bash
# Backs up the production database on the VPS and copies it off the box, to S3.
#
# Runs nightly from docker/systemd/evangelion-db-backup.timer, and before every migration from
# vps-db-update.sh (label "premigration"). The RDS snapshots this replaces were taken by AWS; here
# nothing else does it, so a failure exits non-zero and the timer's journal says so.
#
# Three things that are not obvious:
#   * No WITH COMPRESSION. SQL Server 2022 Express refuses it (Msg 3013, checked 2026-10-30), so
#     the file is gzipped afterwards instead, inside the container, by the user that owns it.
#   * CHECKSUM plus RESTORE VERIFYONLY: a backup that cannot be read back is worse than none,
#     because it looks like one.
#   * The upload is the backup. A .bak that only exists on this disk dies with the disk.
#
# Usage: vps-db-backup.sh [label]      label defaults to "nightly"
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/vps-common.sh
source "$SCRIPT_DIR/lib/vps-common.sh"

LABEL="${1:-nightly}"
KEEP_LOCAL="${KEEP_LOCAL:-3}"
CONTAINER_DIR="/var/opt/mssql/backup"
HOST_DIR="$(env_value SQLSERVER_BACKUP_DIR)"
HOST_DIR="${HOST_DIR:-$APP_DIR/backups}"
BACKUP_S3_URI="$(env_value BACKUP_S3_URI)"
BACKUP_PING_URL="$(env_value BACKUP_PING_URL)"

if ! database_exists; then
  echo "No database $DATABASE yet; nothing to back up."
  exit 0
fi

NAME="${DATABASE}-${LABEL}-$(date -u +%Y%m%dT%H%M%SZ).bak"

echo "==> Backing up $DATABASE to $NAME"
sa_sqlcmd -Q "BACKUP DATABASE [$DATABASE] TO DISK = N'$CONTAINER_DIR/$NAME' WITH INIT, CHECKSUM;"
sa_sqlcmd -Q "RESTORE VERIFYONLY FROM DISK = N'$CONTAINER_DIR/$NAME' WITH CHECKSUM;"

docker exec "$SQL_CONTAINER" gzip -f "$CONTAINER_DIR/$NAME"

if [[ -n "$BACKUP_S3_URI" ]]; then
  DEST="${BACKUP_S3_URI%/}/$(date -u +%Y/%m)/$NAME.gz"
  echo "==> Uploading to $DEST"
  aws s3 cp --only-show-errors "$HOST_DIR/$NAME.gz" "$DEST"
else
  echo "BACKUP_S3_URI is not set: this backup exists only on this disk." >&2
  [[ "$LABEL" == "nightly" ]] && exit 1
fi

# Oldest first out, newest $KEEP_LOCAL stay for a quick restore without a download.
docker exec "$SQL_CONTAINER" sh -c \
  "ls -1t '$CONTAINER_DIR'/*.bak.gz 2>/dev/null | tail -n +$((KEEP_LOCAL + 1)) | xargs -r rm -f"

# A dead man's switch (healthchecks.io or similar): silence from it is how a stopped timer is noticed.
if [[ -n "$BACKUP_PING_URL" && "$LABEL" == "nightly" ]]; then
  curl -fsS -m 10 --retry 3 "$BACKUP_PING_URL" >/dev/null || echo "Backup ping failed." >&2
fi

echo "==> Done: $NAME.gz"
