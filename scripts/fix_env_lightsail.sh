#!/usr/bin/env bash
set -euo pipefail

# Rebuild and fix OpenCD python env on Lightsail (CPU stable stack).
# Includes preprocessing deps (numpy/rasterio/tqdm) and pins PYTHON for web service.
# Usage:
#   bash scripts/fix_env_lightsail.sh
#
# Optional env vars:
#   CONDA_ROOT=~/miniforge3
#   ENV_NAME=opencd
#   PROJECT_DIR=/opt/open-cd

CONDA_ROOT="${CONDA_ROOT:-$HOME/miniforge3}"
ENV_NAME="${ENV_NAME:-opencd}"
PROJECT_DIR="${PROJECT_DIR:-/opt/open-cd}"
SERVICE_NAME="${SERVICE_NAME:-opencd-web}"
PYTHON_PATH="${PYTHON_PATH:-${CONDA_ROOT}/envs/${ENV_NAME}/bin/python}"
API_PORT="${API_PORT:-5099}"

export PIP_DISABLE_PIP_VERSION_CHECK=1
export PIP_NO_INPUT=1

echo "==> OpenCD env fixer"
echo "    CONDA_ROOT=${CONDA_ROOT}"
echo "    ENV_NAME=${ENV_NAME}"
echo "    PROJECT_DIR=${PROJECT_DIR}"
echo

if [[ ! -f "${CONDA_ROOT}/etc/profile.d/conda.sh" ]]; then
  echo "ERROR: conda.sh not found at ${CONDA_ROOT}/etc/profile.d/conda.sh"
  exit 1
fi

cd "${PROJECT_DIR}"
source "${CONDA_ROOT}/etc/profile.d/conda.sh"

echo "==> Recreate conda env"
conda deactivate || true
conda env remove -n "${ENV_NAME}" -y || true
conda create -n "${ENV_NAME}" python=3.10 -y
conda activate "${ENV_NAME}"

echo "==> Python / pip info"
which python
python -V
python -m pip -V

echo "==> Clean old conflicting packages"
python -m pip uninstall -y \
  mmcv mmcv-lite mmengine mmsegmentation mmdet \
  torch torchvision torchaudio \
  opencv-python opencv-python-headless \
  numpy opencd openxlab || true
python -m pip cache purge || true

echo "==> Install stable CPU stack"
python -m pip install --no-cache-dir "pip==24.3.1" "setuptools>=68,<76" wheel openmim
python -m pip install --no-cache-dir "numpy==1.26.4"
python -m pip install --no-cache-dir \
  torch==2.0.1 torchvision==0.15.2 torchaudio==2.0.2 \
  --index-url https://download.pytorch.org/whl/cpu
if command -v apt-get >/dev/null 2>&1 && command -v sudo >/dev/null 2>&1; then
  echo "==> Install OpenCV runtime libs (libGL/libglib)"
  sudo apt-get update
  sudo apt-get install -y libgl1 libglib2.0-0
fi
python -m pip install --no-cache-dir "opencv-python==4.10.0.84"

echo "==> Install OpenMMLab deps"
python -m pip install --no-cache-dir "mmengine>=0.6.0,<1.0.0"
python -m pip install --no-cache-dir \
  "mmcv==2.0.1" \
  -f https://download.openmmlab.com/mmcv/dist/cpu/torch2.0/index.html
python -m pip install --no-cache-dir "mmsegmentation==1.2.2" "mmdet==3.2.0"
python -m pip install --no-cache-dir "mmpretrain>=1.0.0rc7,<1.3.0"

echo "==> Install project + extras"
python -m pip install --no-cache-dir --no-build-isolation -e .
python -m pip install --no-cache-dir ftfy regex "packaging>=24,<25"
python -m pip install --no-cache-dir "rasterio==1.4.3" "tqdm>=4.65,<4.66"

echo "==> Sanity pin check"
python - <<'PY'
import platform, sys
print("platform:", platform.platform())
print("machine :", platform.machine())
print("python  :", sys.version.split()[0])
PY

echo "==> Verify imports"
python - <<'PY'
import numpy, torch, mmcv, mmcv._ext, mmengine, mmseg, mmdet, mmpretrain, opencd, ftfy, regex, rasterio, tqdm
_ = torch.from_numpy(numpy.zeros((2, 2), dtype=numpy.float32))
print("python:", __import__("sys").version.split()[0])
print("numpy :", numpy.__version__)
print("torch :", torch.__version__)
print("mmcv  :", mmcv.__version__)
print("mmseg :", mmseg.__version__)
print("mmdet :", mmdet.__version__)
print("mmpre :", mmpretrain.__version__)
print("opencd:", opencd.__version__)
print("rasterio:", rasterio.__version__)
print("tqdm   :", tqdm.__version__)
print("verify: ALL OK")
PY

echo
echo "==> Pin web service python to opencd env"
if command -v systemctl >/dev/null 2>&1 && command -v sudo >/dev/null 2>&1; then
  sudo mkdir -p "/etc/systemd/system/${SERVICE_NAME}.service.d"
  sudo tee "/etc/systemd/system/${SERVICE_NAME}.service.d/python.conf" >/dev/null <<EOF
[Service]
Environment=PYTHON=${PYTHON_PATH}
EOF
  sudo systemctl daemon-reload
  sudo systemctl restart "${SERVICE_NAME}"
  echo "==> Service restarted: ${SERVICE_NAME}"
  echo "==> Python detect check:"
  curl -s "http://127.0.0.1:${API_PORT}/api/system/python/detect?refresh=true" || true
  echo
else
  echo "Skip systemd pin: systemctl/sudo not available."
fi

echo "==> Done."
