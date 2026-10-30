#!/usr/bin/env bash
# Turns a fresh Ubuntu 24.04 VPS (HostGator VPS NVMe 8, or any x86_64/arm64 box) into a host
# buildPushDeploy.yml can deploy to. Run once, as root, from a copy of this repository:
#
#   scp -r scripts docker .env.hostgator.example root@<vps>:/root/evangelion-bootstrap/
#   ssh root@<vps> /root/evangelion-bootstrap/scripts/vps-bootstrap.sh \
#     --deploy-key "ssh-ed25519 AAAA... github-actions-deploy" \
#     --ecr-registry 123456789012.dkr.ecr.us-east-1.amazonaws.com
#
# Safe to re-run: every step checks before it changes anything. What it leaves to you, printed at
# the end: the IAM key in .docker/aws/credentials and the values in .env.
#
# Why each piece is here:
#   * Docker from Docker's own repository: Ubuntu's docker.io lags, and the compose files need
#     Compose 2.24+ for `!reset`.
#   * Log limits in daemon.json: json-file logs grow without bound, and a full disk stops SQL Server.
#   * 4 GB swap: a memory spike then slows the box down instead of OOM-killing SQL Server.
#   * ufw allows 22, 80 and 443 only. Docker bypasses ufw for published ports, which is why the
#     compose files publish nothing but Caddy's.
#   * A deploy user for the pipeline. It is in the docker group, which is root-equivalent, so its
#     key is the production key.
#   * The ECR credential helper: ECR tokens last 12 hours, and the helper fetches a fresh one on
#     every pull, so there is no docker login to expire.
set -euo pipefail

DEPLOY_KEY=""
ECR_REGISTRY=""
APP_DIR="/opt/evangelion"
DEPLOY_USER="deploy"
# The containers run as 1654 (the aspnet image's app user). A host group with the same id lets them
# read the AWS credentials file without making it world-readable.
APP_GID=1654
SQL_UID=10001

while [[ $# -gt 0 ]]; do
  case "$1" in
    --deploy-key) DEPLOY_KEY="$2"; shift 2 ;;
    --ecr-registry) ECR_REGISTRY="$2"; shift 2 ;;
    --app-dir) APP_DIR="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 1 ;;
  esac
done

[[ $EUID -eq 0 ]] || { echo "Run as root." >&2; exit 1; }
[[ -n "$ECR_REGISTRY" ]] || { echo "--ecr-registry is required (<account>.dkr.ecr.<region>.amazonaws.com)." >&2; exit 1; }
. /etc/os-release
[[ "${ID:-}" == "ubuntu" ]] || echo "Warning: written for Ubuntu 24.04, found ${PRETTY_NAME:-unknown}." >&2

SOURCE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DEBIAN_FRONTEND=noninteractive

echo "==> Packages"
apt-get update -q
apt-get install -y -q ca-certificates curl gnupg ufw fail2ban unattended-upgrades python3 unzip \
  amazon-ecr-credential-helper

if ! command -v docker >/dev/null || ! docker compose version >/dev/null 2>&1; then
  echo "==> Docker Engine and Compose from download.docker.com"
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu ${VERSION_CODENAME} stable" \
    > /etc/apt/sources.list.d/docker.list
  apt-get update -q
  apt-get install -y -q docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
fi

if [[ ! -f /etc/docker/daemon.json ]]; then
  echo "==> Docker log limits"
  cat > /etc/docker/daemon.json <<'JSON'
{
  "log-driver": "json-file",
  "log-opts": { "max-size": "10m", "max-file": "5" }
}
JSON
  systemctl restart docker
fi
systemctl enable --now docker

if ! command -v aws >/dev/null; then
  echo "==> AWS CLI v2"
  tmp="$(mktemp -d)"
  curl -fsSL "https://awscli.amazonaws.com/awscli-exe-linux-$(uname -m).zip" -o "$tmp/awscli.zip"
  unzip -q "$tmp/awscli.zip" -d "$tmp"
  "$tmp/aws/install" --update
  rm -rf "$tmp"
fi

if ! swapon --show | grep -q .; then
  echo "==> 4 GB swapfile"
  fallocate -l 4G /swapfile
  chmod 600 /swapfile
  mkswap /swapfile >/dev/null
  swapon /swapfile
  grep -q '^/swapfile' /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
  echo 'vm.swappiness=10' > /etc/sysctl.d/90-evangelion-swap.conf
  sysctl -q -p /etc/sysctl.d/90-evangelion-swap.conf
fi

echo "==> Automatic security updates"
cat > /etc/apt/apt.conf.d/20auto-upgrades <<'APT'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
APT
systemctl enable --now unattended-upgrades fail2ban

echo "==> Firewall"
ufw allow OpenSSH >/dev/null
ufw allow 80/tcp >/dev/null
ufw allow 443/tcp >/dev/null
ufw allow 443/udp >/dev/null
ufw --force enable >/dev/null

echo "==> Users"
getent group evangelion-app >/dev/null || groupadd --gid "$APP_GID" evangelion-app
id "$DEPLOY_USER" >/dev/null 2>&1 || useradd --create-home --shell /bin/bash "$DEPLOY_USER"
usermod -aG docker,evangelion-app "$DEPLOY_USER"
DEPLOY_HOME="$(getent passwd "$DEPLOY_USER" | cut -d: -f6)"
install -d -m 700 -o "$DEPLOY_USER" -g "$DEPLOY_USER" "$DEPLOY_HOME/.ssh"
if [[ -n "$DEPLOY_KEY" ]]; then
  touch "$DEPLOY_HOME/.ssh/authorized_keys"
  grep -qxF "$DEPLOY_KEY" "$DEPLOY_HOME/.ssh/authorized_keys" || echo "$DEPLOY_KEY" >> "$DEPLOY_HOME/.ssh/authorized_keys"
  chown "$DEPLOY_USER:$DEPLOY_USER" "$DEPLOY_HOME/.ssh/authorized_keys"
  chmod 600 "$DEPLOY_HOME/.ssh/authorized_keys"
fi

# Keys only, but never before a key is in place: on a fresh HostGator box the first login is often
# root with a password, and switching that off with no key would lock everyone out.
if [[ -s "$DEPLOY_HOME/.ssh/authorized_keys" ]]; then
  echo "==> SSH: keys only"
  cat > /etc/ssh/sshd_config.d/10-evangelion.conf <<'SSHD'
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin prohibit-password
SSHD
  systemctl reload ssh 2>/dev/null || systemctl reload sshd
else
  echo "Warning: no deploy key yet, so SSH password login was left as it is." >&2
fi

echo "==> $APP_DIR"
install -d -m 755 -o "$DEPLOY_USER" -g "$DEPLOY_USER" "$APP_DIR" "$APP_DIR/releases"
# SQL Server writes backups as uid 10001; the deploy user reads them for the upload through the group.
install -d -m 2750 -o "$SQL_UID" -g "$DEPLOY_USER" "$APP_DIR/backups"
install -d -m 750 -o "$DEPLOY_USER" -g evangelion-app "$APP_DIR/.docker" "$APP_DIR/.docker/aws"

AWS_DIR="$APP_DIR/.docker/aws"
if [[ ! -f "$AWS_DIR/config" ]]; then
  printf '[profile evangelion]\nregion = us-east-1\n' > "$AWS_DIR/config"
fi
if [[ ! -f "$AWS_DIR/credentials" ]]; then
  printf '[evangelion]\naws_access_key_id = REPLACE\naws_secret_access_key = REPLACE\n' > "$AWS_DIR/credentials"
fi
chown "$DEPLOY_USER:evangelion-app" "$AWS_DIR/config" "$AWS_DIR/credentials"
chmod 640 "$AWS_DIR/config" "$AWS_DIR/credentials"

# The deploy user's own aws and docker commands use the same key.
ln -sfn "$AWS_DIR" "$DEPLOY_HOME/.aws"
grep -q 'AWS_PROFILE=evangelion' "$DEPLOY_HOME/.profile" 2>/dev/null ||
  echo 'export AWS_PROFILE=evangelion' >> "$DEPLOY_HOME/.profile"
install -d -m 700 -o "$DEPLOY_USER" -g "$DEPLOY_USER" "$DEPLOY_HOME/.docker"
python3 - "$DEPLOY_HOME/.docker/config.json" "$ECR_REGISTRY" <<'PY'
import json, os, sys
path, registry = sys.argv[1], sys.argv[2]
config = json.load(open(path)) if os.path.exists(path) else {}
config.setdefault("credHelpers", {})[registry] = "ecr-login"
json.dump(config, open(path, "w"), indent=2)
PY
chown "$DEPLOY_USER:$DEPLOY_USER" "$DEPLOY_HOME/.docker/config.json"
chmod 600 "$DEPLOY_HOME/.docker/config.json"

if [[ ! -f "$APP_DIR/.env" ]]; then
  install -m 600 -o "$DEPLOY_USER" -g "$DEPLOY_USER" "$SOURCE_DIR/.env.hostgator.example" "$APP_DIR/.env"
  sed -i "s#^SQLSERVER_BACKUP_DIR=.*#SQLSERVER_BACKUP_DIR=$APP_DIR/backups#; s#^AWS_CREDENTIALS_DIR=.*#AWS_CREDENTIALS_DIR=$AWS_DIR#" "$APP_DIR/.env"
fi

echo "==> Nightly backup timer"
install -m 644 "$SOURCE_DIR/docker/systemd/evangelion-db-backup.service" /etc/systemd/system/
install -m 644 "$SOURCE_DIR/docker/systemd/evangelion-db-backup.timer" /etc/systemd/system/
sed -i "s#/opt/evangelion#$APP_DIR#g" /etc/systemd/system/evangelion-db-backup.service
systemctl daemon-reload
systemctl enable --now evangelion-db-backup.timer

cat <<DONE

Host ready. Still to do by hand:
  1. Put the evangelion-vps IAM key (terraform/vps-aws output) in $AWS_DIR/credentials.
  2. Fill every REPLACE value in $APP_DIR/.env.
  3. Create the evangelion/vps/* secrets: scripts/vps-secrets.sh, from a machine with admin AWS access.
  4. Check AWS access as the deploy user:
       sudo -iu $DEPLOY_USER aws sts get-caller-identity
       sudo -iu $DEPLOY_USER docker pull $ECR_REGISTRY/<repository>:latest
  5. Point DNS for CADDY_DOMAIN at this server, then run the Build, Push and Deploy workflow.
DONE
