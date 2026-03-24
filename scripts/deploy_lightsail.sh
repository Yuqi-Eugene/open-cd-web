#!/usr/bin/env bash
set -euo pipefail

# Usage:
#   bash scripts/deploy_lightsail.sh
#   bash scripts/deploy_lightsail.sh <branch>
# Example:
#   bash scripts/deploy_lightsail.sh main

BRANCH="${1:-main}"
SERVICE_NAME="${SERVICE_NAME:-opencd-web}"
PROJECT_PATH="${PROJECT_PATH:-src/OpenCd.Web/OpenCd.Web.csproj}"
PUBLISH_DIR="${PUBLISH_DIR:-/opt/open-cd/publish}"
HEALTH_URL="${HEALTH_URL:-http://127.0.0.1:5099/api/health}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

echo "==> Deploying OpenCD Web"
echo "    Repo      : ${REPO_ROOT}"
echo "    Branch    : ${BRANCH}"
echo "    Service   : ${SERVICE_NAME}"
echo "    Project   : ${PROJECT_PATH}"
echo "    Publish   : ${PUBLISH_DIR}"
echo

cd "${REPO_ROOT}"

echo "==> Sync code"
git fetch origin
git reset --hard "origin/${BRANCH}"
git clean -fd

echo "==> Clean build artifacts"
rm -rf src/OpenCd.Web/bin src/OpenCd.Web/obj "${PUBLISH_DIR}"
mkdir -p "${PUBLISH_DIR}"

echo "==> Restore and publish"
dotnet restore "${PROJECT_PATH}"
dotnet publish "${PROJECT_PATH}" -c Release -o "${PUBLISH_DIR}"

echo "==> Restart service"
sudo systemctl daemon-reload
sudo systemctl restart "${SERVICE_NAME}"

echo "==> Wait for health check"
ok=0
for i in {1..30}; do
  if curl -fsS "${HEALTH_URL}" >/dev/null 2>&1; then
    ok=1
    break
  fi
  sleep 1
done

echo "==> Service status"
sudo systemctl status "${SERVICE_NAME}" --no-pager -l || true

if [[ "${ok}" -eq 1 ]]; then
  echo
  echo "==> HEALTH OK: ${HEALTH_URL}"
  curl -sS "${HEALTH_URL}" || true
  echo
else
  echo
  echo "==> HEALTH FAILED: ${HEALTH_URL}"
  echo "==> Last logs:"
  sudo journalctl -u "${SERVICE_NAME}" -n 80 --no-pager || true
  exit 1
fi

