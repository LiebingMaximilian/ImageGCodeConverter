#!/usr/bin/env bash
# One-time server setup for container deployments from GitHub Actions.
# Requires Docker with the compose plugin. Run once as root from the deploy/ folder:
#
#   sudo bash setup-server.sh --image ghcr.io/<github-user>/imageconverter --port 5080 \
#        "ssh-ed25519 AAAA... github-actions-deploy"
#
# Creates:
#   - user "deploy" (NOT in the docker group – that would be root-equivalent).
#     Its SSH key can only run /usr/local/bin/imageconverter-deploy (forced command + one sudo rule).
#   - /opt/imageconverter/docker-compose.yml       (root-owned, the deploy user cannot change it)
#   - /etc/imageconverter/imageconverter.env       (your settings: image, port, limits – kept on re-runs)
#   - /usr/local/bin/imageconverter-deploy         (pull, start, health-check, rollback)
set -euo pipefail

DEPLOY_USER=deploy
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
IMAGE=""
PORT=""
PUBKEY=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --image) IMAGE="${2:?}"; shift 2 ;;
    --port)  PORT="${2:?}";  shift 2 ;;
    -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
    *) PUBKEY="$1"; shift ;;
  esac
done

[[ $EUID -eq 0 ]] || { echo "Please run as root (sudo)." >&2; exit 1; }
[[ "$PUBKEY" == ssh-* || "$PUBKEY" == ecdsa-* ]] || { echo "Pass the PUBLIC deploy key as argument (see --help)." >&2; exit 1; }
command -v docker >/dev/null || { echo "Docker is not installed." >&2; exit 1; }
docker compose version >/dev/null 2>&1 || { echo "The docker compose plugin is missing (apt install docker-compose-plugin)." >&2; exit 1; }
if ! command -v curl >/dev/null; then
  if command -v apt-get >/dev/null; then apt-get install -y -qq curl >/dev/null; else echo "Please install curl." >&2; exit 1; fi
fi

# --- deploy user: SSH key only, no password, no docker group ---
if ! id "$DEPLOY_USER" &>/dev/null; then
  useradd --create-home --shell /bin/bash "$DEPLOY_USER"
fi
usermod -p '*' "$DEPLOY_USER"   # no password can ever match; unlike "passwd -l" this keeps SSH key login working with UsePAM=no
if id -nG "$DEPLOY_USER" | tr ' ' '\n' | grep -qx docker; then
  echo "WARNING: $DEPLOY_USER is in the docker group (root-equivalent). Remove it: gpasswd -d $DEPLOY_USER docker" >&2
fi

# The key may only run the deploy script ("forced command"); "restrict" disables
# shells/PTY, port/agent/X11 forwarding. Any old line with the same key is replaced.
DEPLOY_HOME="$(getent passwd "$DEPLOY_USER" | cut -d: -f6)"
install -d -m 700 -o "$DEPLOY_USER" -g "$DEPLOY_USER" "$DEPLOY_HOME/.ssh"
AUTH="$DEPLOY_HOME/.ssh/authorized_keys"
touch "$AUTH"
KEY_BODY="$(awk '{print $2}' <<< "$PUBKEY")"
grep -vF "$KEY_BODY" "$AUTH" > "$AUTH.tmp" || true
# shellcheck disable=SC2016  # $SSH_ORIGINAL_COMMAND must stay literal – sshd expands it at login
printf 'restrict,command="sudo -n /usr/local/bin/imageconverter-deploy $SSH_ORIGINAL_COMMAND" %s\n' "$PUBKEY" >> "$AUTH.tmp"
mv "$AUTH.tmp" "$AUTH"
chown "$DEPLOY_USER:$DEPLOY_USER" "$AUTH"
chmod 600 "$AUTH"

# --- files (all root-owned: whoever can edit them controls what runs as root) ---
install -d -m 755 /opt/imageconverter /etc/imageconverter
install -d -m 700 /var/lib/imageconverter
install -m 644 "$SCRIPT_DIR/docker-compose.yml" /opt/imageconverter/docker-compose.yml
install -m 755 "$SCRIPT_DIR/imageconverter-deploy" /usr/local/bin/imageconverter-deploy

ENV_FILE=/etc/imageconverter/imageconverter.env
[[ -f "$ENV_FILE" ]] || install -m 600 "$SCRIPT_DIR/imageconverter.env.example" "$ENV_FILE"
[[ -n "$IMAGE" ]] && sed -i "s#^IMAGECONVERTER_IMAGE=.*#IMAGECONVERTER_IMAGE=${IMAGE,,}#" "$ENV_FILE"
[[ -n "$PORT"  ]] && sed -i "s#^IMAGECONVERTER_PORT=.*#IMAGECONVERTER_PORT=$PORT#" "$ENV_FILE"
chmod 600 "$ENV_FILE"

# --- the ONE command the deploy user may run as root ---
SUDOERS=/etc/sudoers.d/imageconverter-deploy
cat > "$SUDOERS.tmp" <<EOF
# GitHub Actions deploy user: may only run the ImageConverter deploy script (it validates its arguments).
$DEPLOY_USER ALL=(root) NOPASSWD: /usr/local/bin/imageconverter-deploy
EOF
visudo -cf "$SUDOERS.tmp" >/dev/null
install -m 440 "$SUDOERS.tmp" "$SUDOERS"
rm -f "$SUDOERS.tmp"

# --- checks ---
# shellcheck source=/dev/null
CONF_PORT="$(set -a; source "$ENV_FILE"; echo "${IMAGECONVERTER_PORT:-5080}")"
if command -v ss >/dev/null && ss -ltnH "sport = :$CONF_PORT" | grep -q .; then
  if ! docker ps --filter name=^/imageconverter$ --format '{{.Ports}}' | grep -q ":$CONF_PORT->"; then
    echo "WARNING: port $CONF_PORT is already used by another program – change IMAGECONVERTER_PORT in $ENV_FILE" >&2
  fi
fi

echo
echo "Done. Settings: $ENV_FILE"
grep -E '^IMAGECONVERTER_(IMAGE|PORT|BIND)=' "$ENV_FILE" | sed 's/^/  /'
echo
echo "Next steps (details in deploy/README.md):"
echo "  1. Private repository? Let the server pull images:  sudo docker login ghcr.io -u <github-user>"
echo "     (password = GitHub token with only the read:packages scope)"
echo "  2. Add the GitHub secrets and push to main."
if [[ -f /etc/ssh/ssh_host_ed25519_key.pub ]]; then
  echo "  3. Host key for the DEPLOY_KNOWN_HOSTS secret:"
  echo "     <your-host> $(cut -d' ' -f1,2 /etc/ssh/ssh_host_ed25519_key.pub)"
fi
