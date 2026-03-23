import os
import glob
import numpy as np
import rasterio
from tqdm import tqdm

# scalling images, make it to uint8, write nodata value to 0

# python prep_uint8_nodata0.py \
#   --in_dir /Users/heyuqi/Downloads/遥感样本检测/weijian1024/2023_2024/images-pre \
#   --out_dir /Users/heyuqi/Downloads/遥感样本检测/weijian1024/2023_2024/images-pre_uint8 \
#   --nodata_value -1

# python prep_uint8_nodata0.py \
#   --in_dir /Users/heyuqi/Downloads/遥感样本检测/weijian1024/2023_2024/labels \
#   --out_dir /Users/heyuqi/Downloads/遥感样本检测/weijian1024/2023_2024/labels_uint8 \
#   --no_scale \
#   --map_nodata_to 0 \
#   --nodata_value -1

def to_uint8(arr, clip_percentiles=(2, 98)):
    """Convert array to uint8 via per-band percentile clipping then linear scaling to 0..255.

    arr shape: (bands, H, W)
    """
    out = np.empty(arr.shape, dtype=np.uint8)

    for b in range(arr.shape[0]):
        band = arr[b].astype(np.float32)
        finite = np.isfinite(band)
        if not finite.any():
            out[b] = 0
            continue

        lo, hi = np.percentile(band[finite], clip_percentiles)
        if not np.isfinite(lo) or not np.isfinite(hi) or hi <= lo:
            out[b] = 0
            continue

        band = np.clip(band, lo, hi)
        scaled = (band - lo) * (255.0 / (hi - lo))
        out[b] = np.clip(scaled, 0, 255).astype(np.uint8)

    return out


def process_one(
    in_path,
    out_path,
    clip_percentiles=(2, 98),
    no_scale=False,
    map_nodata_to=0,
    nodata_value=0,
):
    with rasterio.open(in_path) as src:
        profile = src.profile.copy()

        # Read all bands; if src has nodata tag, rasterio will mask those pixels.
        data = src.read(masked=True)  # (bands,H,W) possibly masked

        # If nodata not defined but dtype float, also mask NaNs
        if src.nodata is None and np.issubdtype(data.dtype, np.floating):
            data = np.ma.masked_invalid(data)

        mask = np.ma.getmaskarray(data)  # (bands,H,W) boolean

        # Fill masked pixels with map_nodata_to BEFORE conversion
        filled = np.ma.filled(data, fill_value=map_nodata_to)

        if filled.dtype == np.uint8:
            out = filled
        else:
            if no_scale:
                # No scaling: just clip to [0,255] then cast.
                out = np.clip(filled, 0, 255).astype(np.uint8)
            else:
                # Percentile scaling (good for imagery, can change contrast tile-to-tile)
                out = to_uint8(filled, clip_percentiles=clip_percentiles)

        # Ensure masked pixels are exactly map_nodata_to
        if mask.any():
            out[mask] = np.uint8(map_nodata_to)

        # Update profile for uint8 GeoTIFF
        profile.update(
            dtype=rasterio.uint8,
            compress="LZW",
            tiled=True,
            bigtiff="IF_SAFER",
            count=out.shape[0],
        )

        # Nodata tag handling:
        # - If nodata_value is None, omit the nodata tag (important when 0 is a valid class)
        if nodata_value is None:
            profile.pop("nodata", None)
        else:
            profile.update(nodata=float(nodata_value))

        os.makedirs(os.path.dirname(out_path), exist_ok=True)
        with rasterio.open(out_path, "w", **profile) as dst:
            dst.write(out)


def main(in_dir, out_dir, clip_p2=2, clip_p98=98, no_scale=False, map_nodata_to=0, nodata_value=0):
    # Recursive glob to preserve nested structure
    patterns = ["**/*.tif", "**/*.tiff"]
    tifs = []
    for pat in patterns:
        tifs.extend(glob.glob(os.path.join(in_dir, pat), recursive=True))
    tifs = sorted([p for p in tifs if os.path.isfile(p)])

    if not tifs:
        raise SystemExit(f"No .tif/.tiff found under {in_dir}")

    for p in tqdm(tifs, desc="Converting"):
        rel = os.path.relpath(p, in_dir)
        out_path = os.path.join(out_dir, rel)
        process_one(
            p,
            out_path,
            clip_percentiles=(clip_p2, clip_p98),
            no_scale=no_scale,
            map_nodata_to=map_nodata_to,
            nodata_value=nodata_value,
        )


if __name__ == "__main__":
    import argparse

    ap = argparse.ArgumentParser()
    ap.add_argument("--in_dir", required=True)
    ap.add_argument("--out_dir", required=True)
    ap.add_argument("--clip_p2", type=float, default=2)
    ap.add_argument("--clip_p98", type=float, default=98)

    ap.add_argument(
        "--no_scale",
        action="store_true",
        help="Do not scale values; just clip to [0,255] and cast to uint8 (good for masks/labels).",
    )
    ap.add_argument(
        "--map_nodata_to",
        type=float,
        default=0,
        help="Value to write into nodata/masked pixels in the output (e.g., 255 for label ignore_index).",
    )
    ap.add_argument(
        "--nodata_value",
        type=float,
        default=0,
        help="Value to write as the output GeoTIFF nodata tag. Use 255 for labels, or -1 to OMIT nodata tag.",
    )

    args = ap.parse_args()
    nodata_value = None if args.nodata_value == -1 else args.nodata_value

    main(
        args.in_dir,
        args.out_dir,
        clip_p2=args.clip_p2,
        clip_p98=args.clip_p98,
        no_scale=args.no_scale,
        map_nodata_to=args.map_nodata_to,
        nodata_value=nodata_value,
    )