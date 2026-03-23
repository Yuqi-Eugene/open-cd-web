#!/usr/bin/env python3
import argparse
import io
import numpy as np
from PIL import Image


def read_image(path: str) -> np.ndarray:
    lower = path.lower()
    if lower.endswith((".png", ".jpg", ".jpeg", ".bmp")):
        img = Image.open(path).convert("RGB")
        return np.array(img)

    if lower.endswith((".tif", ".tiff")):
        try:
            import rasterio
            with rasterio.open(path) as src:
                arr = src.read(masked=True)
                arr = np.ma.filled(arr, 0)
                if arr.ndim == 3:
                    if arr.shape[0] >= 3:
                        rgb = np.stack([arr[0], arr[1], arr[2]], axis=-1)
                    else:
                        rgb = np.repeat(arr[0][..., None], 3, axis=-1)
                else:
                    rgb = np.repeat(arr[..., None], 3, axis=-1)
                return rgb
        except Exception:
            img = Image.open(path).convert("RGB")
            return np.array(img)

    img = Image.open(path).convert("RGB")
    return np.array(img)


def normalize(arr: np.ndarray) -> np.ndarray:
    arr = arr.astype(np.float32)
    out = np.zeros_like(arr, dtype=np.uint8)
    for c in range(3):
        channel = arr[..., c]
        finite = np.isfinite(channel)
        if not finite.any():
            continue
        lo, hi = np.percentile(channel[finite], (2, 98))
        if hi <= lo:
            continue
        scaled = np.clip((channel - lo) * (255.0 / (hi - lo)), 0, 255)
        out[..., c] = scaled.astype(np.uint8)
    return out


def colorize_label(arr: np.ndarray) -> np.ndarray:
    if arr.ndim == 3:
        arr = arr[..., 0]
    arr = arr.astype(np.uint8)
    canvas = np.zeros((arr.shape[0], arr.shape[1], 3), dtype=np.uint8)
    canvas[arr == 0] = (15, 23, 42)
    canvas[arr == 1] = (255, 77, 77)
    canvas[arr == 2] = (46, 204, 113)
    canvas[arr >= 3] = (247, 220, 111)
    return canvas


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--label", action="store_true")
    args = parser.parse_args()

    arr = read_image(args.input)
    if args.label:
        img = colorize_label(arr)
    else:
        img = normalize(arr)

    im = Image.fromarray(img)
    im.thumbnail((1024, 1024), Image.Resampling.BILINEAR)
    im.save(args.output, format="PNG")


if __name__ == "__main__":
    main()
