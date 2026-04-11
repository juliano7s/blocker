#!/usr/bin/env bash
# scripts/deploy-relay.sh — build self-contained Linux binary and deploy to droplet.
set -euo pipefail
DROPLET="${DROPLET:-root@julianoschroeder.com}"
REMOTE_DIR="/opt/blocker-relay"

echo "Building self-contained Linux binary…"
dotnet publish src/Blocker.Relay -c Release -r linux-x64 \
    --self-contained -p:PublishSingleFile=true \
    -o publish/blocker-relay

echo "Uploading to ${DROPLET}:${REMOTE_DIR}/Blocker.Relay.new …"
ssh "${DROPLET}" "mkdir -p ${REMOTE_DIR}"
scp publish/blocker-relay/Blocker.Relay "${DROPLET}:${REMOTE_DIR}/Blocker.Relay.new"

echo "Installing and restarting service…"
ssh "${DROPLET}" "
    mv ${REMOTE_DIR}/Blocker.Relay.new ${REMOTE_DIR}/Blocker.Relay &&
    chmod +x ${REMOTE_DIR}/Blocker.Relay &&
    systemctl restart blocker-relay &&
    systemctl status blocker-relay --no-pager
"
echo "Done."
