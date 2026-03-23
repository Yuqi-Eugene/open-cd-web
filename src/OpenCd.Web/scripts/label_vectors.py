#!/usr/bin/env python3
import argparse
import json
from typing import List, Dict, Any

import numpy as np
from PIL import Image


def load_label(path: str) -> np.ndarray:
    lower = path.lower()
    if lower.endswith((".tif", ".tiff")):
        try:
            import rasterio
            with rasterio.open(path) as src:
                arr = src.read(1)
                return arr
        except Exception:
            pass
    img = Image.open(path)
    if img.mode not in ("L", "I", "I;16", "F"):
        img = img.convert("L")
    return np.array(img)


def downsample_ring(points: List[List[float]], max_points: int = 120) -> List[List[float]]:
    if len(points) <= max_points:
        return points
    step = max(1, len(points) // max_points)
    sampled = points[::step]
    if sampled and sampled[0] != sampled[-1]:
        sampled.append(sampled[0])
    return sampled


def polygon_features(label: np.ndarray, max_features: int = 400) -> List[Dict[str, Any]]:
    try:
        import rasterio.features
        from rasterio.transform import Affine
    except Exception as ex:
        raise RuntimeError(f"rasterio.features unavailable: {ex}")

    h, w = label.shape
    features: List[Dict[str, Any]] = []

    values = np.unique(label)
    values = values[values > 0]

    for cls in values.tolist():
        mask = (label == cls)
        if not mask.any():
            continue

        for geom, val in rasterio.features.shapes(mask.astype(np.uint8), mask=mask, transform=Affine.identity()):
            if val != 1:
                continue

            coords_groups = []
            gtype = geom.get("type", "")
            if gtype == "Polygon":
                coords_groups = [geom.get("coordinates", [])]
            elif gtype == "MultiPolygon":
                coords_groups = geom.get("coordinates", [])

            for poly in coords_groups:
                if not poly:
                    continue
                outer = poly[0]
                if len(outer) < 4:
                    continue

                ring = []
                for x, y in outer:
                    ring.append([float(x) / max(w, 1), float(y) / max(h, 1)])
                ring = downsample_ring(ring)

                features.append({
                    "ClassValue": int(cls),
                    "Rings": [
                        [{"X": p[0], "Y": p[1]} for p in ring]
                    ]
                })

                if len(features) >= max_features:
                    return features

    return features


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    label = load_label(args.input)
    if label.ndim > 2:
        label = label[..., 0]

    h, w = label.shape
    result = {
        "Width": int(w),
        "Height": int(h),
        "Features": polygon_features(label)
    }

    with open(args.output, "w", encoding="utf-8") as f:
        json.dump(result, f)


if __name__ == "__main__":
    main()
