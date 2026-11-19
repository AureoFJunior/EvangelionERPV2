#!/usr/bin/env bash
# Stub-based checks for vps-db-backup.sh and vps-db-restore-drill.sh: docker, aws and gpg are
# fakes on PATH, so nothing real is touched. Exits non-zero if any case fails.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
T="$(mktemp -d)"
trap 'rm -rf "$T"' EXIT
mkdir -p "$T/bin" "$T/app"

cat > "$T/bin/docker" <<'STUB'
#!/usr/bin/env bash
echo "docker $*" >> "$STUB_LOG"
case "$*" in
  *"SELECT CASE WHEN DB_ID"*) echo 1 ;;
  *FILELISTONLY*) printf '%s\n' "$STUB_LOGICAL_NAMES" ;;
  *" cat /var/opt/mssql/backup/"*) printf 'PLAINTEXT' ;;
  *"sh -c cat >"*) cat > /dev/null ;;
esac
exit 0
STUB
cat > "$T/bin/gpg" <<'STUB'
#!/usr/bin/env bash
echo "gpg $*" >> "$STUB_LOG"
case "$*" in
  *--encrypt*) cat > /dev/null; printf 'CIPHERTEXT' ;;
  *--decrypt*) gzip -c <<< "backup" ;;
esac
STUB
cat > "$T/bin/aws" <<'STUB'
#!/usr/bin/env bash
echo "aws $*" >> "$STUB_LOG"
if [[ "$*" == *" - "* ]]; then cat >> "$STUB_STDIN"; fi
STUB
chmod +x "$T/bin/"*

FAILED=0
export STUB_LOG="$T/calls.log" STUB_STDIN="$T/aws-stdin" APP_DIR="$T/app" ENV_FILE="$T/app/.env"
export PATH="$T/bin:$PATH"
export STUB_LOGICAL_NAMES='EvangelionERPV2|/data/a.mdf|D
EvangelionERPV2_log|/data/a.ldf|L'

write_env() {
  {
    echo "SQLSERVER_SA_PASSWORD=pw"
    echo "SQLSERVER_BACKUP_DIR=$T/app/backups"
    echo "BACKUP_S3_URI=s3://bucket/sql"
    echo "BACKUP_GPG_PUBLIC_KEY=$T/app/backup-public.asc"
  } > "$ENV_FILE"
}

reset() { : > "$STUB_LOG"; : > "$STUB_STDIN"; rm -f "$T/app/backup-public.asc"; write_env; }

check() {
  local name="$1" result="$2"
  if [[ "$result" == ok ]]; then echo "PASS $name"; else echo "FAIL $name"; FAILED=1; fi
}

reset
rc=0; "$REPO/scripts/vps-db-backup.sh" manual >/dev/null 2>&1 || rc=$?
[[ $rc -ne 0 ]] && ! grep -q '^aws ' "$STUB_LOG" && ! grep -q BACKUP "$STUB_LOG" && r=ok || r=bad
check "backup without key file fails before backing up or uploading" "$r"

reset; echo key > "$T/app/backup-public.asc"
rc=0; "$REPO/scripts/vps-db-backup.sh" manual >/dev/null 2>&1 || rc=$?
r=bad
if [[ $rc -eq 0 ]] && grep -qE '^aws s3 cp --only-show-errors - s3://bucket/sql/.*\.bak\.gz\.gpg$' "$STUB_LOG" &&
   grep -q -- '--recipient-file' "$STUB_LOG" && [[ "$(cat "$STUB_STDIN")" == CIPHERTEXT ]] &&
   ! grep -q PLAINTEXT "$STUB_STDIN"; then r=ok; fi
check "backup with key streams ciphertext to a .gpg destination via stdin" "$r"

reset; echo "x" | gzip > "$T/good.bak.gz"
rc=0; STUB_LOGICAL_NAMES="Evil'name|/d/a.mdf|D" "$REPO/scripts/vps-db-restore-drill.sh" "$T/good.bak.gz" >/dev/null 2>&1 || rc=$?
[[ $rc -ne 0 ]] && ! grep -q 'RESTORE DATABASE' "$STUB_LOG" && r=ok || r=bad
check "drill refuses a logical name containing a quote" "$r"

reset
rc=0; "$REPO/scripts/vps-db-restore-drill.sh" "$T/good.bak.gz" >/dev/null 2>&1 || rc=$?
[[ $rc -eq 0 ]] && grep -q 'RESTORE DATABASE' "$STUB_LOG" && r=ok || r=bad
check "drill accepts normal logical names" "$r"

reset; : > "$T/enc.bak.gz.gpg"
rc=0; "$REPO/scripts/vps-db-restore-drill.sh" "$T/enc.bak.gz.gpg" >/dev/null 2>&1 || rc=$?
[[ $rc -eq 0 ]] && grep -q 'gpg --batch --decrypt' "$STUB_LOG" && r=ok || r=bad
check "drill decrypts a .gpg source" "$r"

exit $FAILED
