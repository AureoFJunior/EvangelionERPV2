#!/usr/bin/env bash
# Stub-based checks for the VPS credential handling: placeholder passwords are refused, secrets
# never appear in an argv, and the deploy key is installed restricted exactly once.
# docker, aws and python3 are replaced by recorders on PATH; nothing touches a real host or AWS.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
REAL_PYTHON="$(command -v python3)"
FAILED=0

pass() { echo "ok   $1"; }
fail() { echo "FAIL $1"; FAILED=1; }

# Stubs: every call appends its argv to $WORK/argv.log.
mkdir "$WORK/bin"
cat > "$WORK/bin/docker" <<'EOF'
#!/usr/bin/env bash
printf 'docker %s\n' "$*" >> "$STUB_LOG"
cat > /dev/null || true
echo 1
EOF
cat > "$WORK/bin/aws" <<'EOF'
#!/usr/bin/env bash
printf 'aws %s\n' "$*" >> "$STUB_LOG"
case "$*" in
  *"sts get-caller-identity"*) echo arn:aws:iam::1:user/test ;;
  *"get-secret-value"*"evangelion/vps/database"*) [[ -z "${STUB_DB_EXISTS:-}" ]] && { echo "stub: ResourceNotFoundException" >&2; exit 254; }
    printf '{"DefaultConnection":"Server=s;User Id=evangelion_app;Password=%s;Encrypt=True"}' "$STUB_DB_PASSWORD" ;;
  *"get-secret-value"*) printf '{"DefaultConnection":"Server=s;User Id=evangelion_app;Password=%s;Encrypt=True"}' "$STUB_DB_PASSWORD" ;;
  *"describe-secret"*) exit 254 ;;
  *"--secret-string file://"*)
    for a in "$@"; do [[ "$a" == file://* ]] && f="${a#file://}"; done
    [[ "$(stat -c %a "$f")" == 600 ]] || echo "secret file mode is not 0600" >> "$STUB_ERR"
    printf '%s\n' "$(cat "$f")" >> "$STUB_SECRETS" ;;
esac
EOF
cat > "$WORK/bin/python3" <<EOF
#!/usr/bin/env bash
printf 'python3 %s\n' "\$*" >> "\$STUB_LOG"
exec "$REAL_PYTHON" "\$@"
EOF
chmod +x "$WORK/bin/"*
export PATH="$WORK/bin:$PATH" STUB_LOG="$WORK/argv.log" STUB_ERR="$WORK/err.log" STUB_SECRETS="$WORK/secrets.log"
: > "$STUB_LOG"; : > "$STUB_ERR"; : > "$STUB_SECRETS"

REDIS_PW='RedisPw-Zq81xK'
RABBIT_PW='RabbitPw-Mw27yL'
DB_PW='DbPw-Tn53vB'
SA_PW='SaPw-Ke90cD'
export STUB_DB_PASSWORD="$DB_PW"

# 1. Placeholder passwords.
printf '# REPLACE in a comment is fine\nREDIS_PASSWORD=REPLACE_ME\nSQLSERVER_SA_PASSWORD=%s\nAPI_IMAGE=REPLACE_registry/api\n' "$SA_PW" > "$WORK/bad.env"
printf 'REDIS_PASSWORD=%s\nSQLSERVER_SA_PASSWORD=%s\nAPI_IMAGE=REPLACE_registry/api\nWORKER_IMAGE=REPLACE\n' "$REDIS_PW" "$SA_PW" > "$WORK/good.env"
printf '#!/usr/bin/env bash\nset -euo pipefail\nsource %q/scripts/lib/vps-common.sh\necho sourced\n' "$ROOT" > "$WORK/source-common.sh"

if out="$(ENV_FILE="$WORK/bad.env" bash "$WORK/source-common.sh" 2>&1)"; then
  fail "1a placeholder password is refused"
elif grep -q 'REDIS_PASSWORD' <<<"$out" && ! grep -q 'SQLSERVER_SA_PASSWORD\|API_IMAGE' <<<"$out"; then
  pass "1a placeholder password is refused and named"
else
  fail "1a refusal does not name exactly REDIS_PASSWORD: $out"
fi
if ENV_FILE="$WORK/good.env" bash "$WORK/source-common.sh" >/dev/null 2>&1; then
  pass "1b REPLACE in *_IMAGE lines is allowed"
else
  fail "1b REPLACE in *_IMAGE lines is allowed"
fi

# 2. vps-secrets.sh: no password in any argv, value written through file://.
printf 'REDIS_PASSWORD=%s\nRABBITMQ_PASSWORD=%s\nRABBITMQ_USER=evangelion\n' "$REDIS_PW" "$RABBIT_PW" > "$WORK/secrets.env"
: > "$STUB_LOG"
if ! bash "$ROOT/scripts/vps-secrets.sh" "$WORK/secrets.env" > "$WORK/secrets.out" 2>&1; then
  fail "2 vps-secrets.sh ran: $(cat "$WORK/secrets.out")"
else
  leaked="$(grep -F -e "$REDIS_PW" -e "$RABBIT_PW" -e "$DB_PW" "$STUB_LOG" || true)"
  if [[ -n "$leaked" ]]; then
    fail "2a password in an aws/python3 argv: $leaked"
  elif [[ "$(grep -c -- '--secret-string file://' "$STUB_LOG")" == 3 ]] && ! grep -q -- '--secret-string [^f]' "$STUB_LOG"; then
    pass "2a no password in argv; all three secrets written through file://"
  else
    fail "2a secrets are not written through file://"
  fi
  if grep -q "$REDIS_PW" "$STUB_SECRETS" && grep -q "$RABBIT_PW" "$STUB_SECRETS" && [[ ! -s "$STUB_ERR" ]]; then
    pass "2b the files held the real values at 0600"
  else
    fail "2b file contents or mode wrong: $(cat "$STUB_ERR")"
  fi
fi

# 3. vps-db-init.sh: the connection string (and its password) never reaches python's argv.
mkdir "$WORK/app"
printf 'EVA_CONN_STR=evangelion/vps/database:DefaultConnection\nSQLSERVER_SA_PASSWORD=%s\n' "$SA_PW" > "$WORK/app/.env"
: > "$STUB_LOG"
if STUB_DB_EXISTS=1 APP_DIR="$WORK/app" ENV_FILE="$WORK/app/.env" bash "$ROOT/scripts/vps-db-init.sh" > "$WORK/init.out" 2>&1; then
  if grep -F -e "$DB_PW" -e "$SA_PW" "$STUB_LOG" | grep -q .; then
    fail "3 password in an argv: $(grep -F -e "$DB_PW" -e "$SA_PW" "$STUB_LOG")"
  else
    pass "3 connection_value keeps the password out of python's argv"
  fi
else
  fail "3 vps-db-init.sh ran: $(cat "$WORK/init.out")"
fi

# 4. vps-bootstrap.sh: install_authorized_key, extracted from the script (it cannot run unprivileged).
sed -n '/^install_authorized_key() {/,/^}/p' "$ROOT/scripts/vps-bootstrap.sh" > "$WORK/func.sh"
if [[ ! -s "$WORK/func.sh" ]]; then
  fail "4 install_authorized_key not found in vps-bootstrap.sh"
else
  KEY="ssh-ed25519 AAAAC3Nza deploy@ci"
  printf 'ssh-rsa OTHER admin\n%s\n' "$KEY" > "$WORK/authorized_keys"
  # shellcheck disable=SC1091
  source "$WORK/func.sh"
  install_authorized_key "$WORK/authorized_keys" "$KEY"
  install_authorized_key "$WORK/authorized_keys" "$KEY"
  if [[ "$(grep -c "$KEY" "$WORK/authorized_keys")" == 1 && "$(grep -cxF "restrict $KEY" "$WORK/authorized_keys")" == 1 ]] &&
     grep -qxF 'ssh-rsa OTHER admin' "$WORK/authorized_keys"; then
    pass "4 re-run leaves exactly one restrict line and keeps other keys"
  else
    fail "4 authorized_keys is wrong: $(cat "$WORK/authorized_keys")"
  fi
fi

exit "$FAILED"
