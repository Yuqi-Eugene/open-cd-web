# train_cpu.py

# caffeinate -imsu python train_cpu.py   configs/changer/changer_ex_r18_512x512_40k_wj1024.py   --work-dir work_dirs/wj1024_changerex_r18

# to resume: 
# refer to the config to modify, and then:
# caffeinate -imsu python train_cpu.py \ 
# configs/changer/changer_ex_r18_512x512_40k_wj1024.py \ 
# --work-dir work_dirs/wj1024_changerex_r18 \
# --resume


import mmengine.device.utils as dev_utils
dev_utils.DEVICE = "cpu"  # force MMEngine to use CPU even if MPS exists

import runpy
import sys

# pass through whatever you type after train_cpu.py
# Example usage below.
runpy.run_path("tools/train.py", run_name="__main__")