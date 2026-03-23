#!/usr/bin/env python3
"""split_train_val_test.py

Split an A/B/label paired dataset into train/val/test folders.

Expected input structure
------------------------
IN_DIR/
  A/      (pre images)
  B/      (post images)
  label/  (segmentation masks)

Filenames must match across A, B, and label, e.g.
  A/0001.tif
  B/0001.tif
  label/0001.tif

Output structure
----------------
OUT_DIR/
  train/A, train/B, train/label
  val/A,   val/B,   val/label
  test/A,  test/B,  test/label

Examples
--------
# Dry-run split 70/15/15
python split_train_val_test.py --in_dir data/my_cd --out_dir data/my_cd_split --train 0.7 --val 0.15 --test 0.15

# Apply and copy
python split_train_val_test.py --in_dir data/my_cd --out_dir data/my_cd_split --apply --method copy

# Apply and move (will remove from IN_DIR)
python split_train_val_test.py --in_dir data/my_cd --out_dir data/my_cd_split --apply --method move

# Apply and symlink (fast, minimal disk)
python split_train_val_test.py --in_dir data/my_cd --out_dir data/my_cd_split --apply --method symlink

Notes
-----
- Default is dry-run; add --apply to actually create files.
- If you want a deterministic split, set --seed.
"""

from __future__ import annotations

import argparse
import os
import random
import shutil
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Sequence, Tuple


SPLITS = ("train", "val", "test")


@dataclass(frozen=True)
class PairPaths:
    stem: str
    a: Path
    b: Path
    label: Path


def list_files(dir_path: Path, exts: Sequence[str] | None) -> Dict[str, Path]:
    """Return mapping stem+suffix filename -> full path (key is filename)."""
    if not dir_path.exists() or not dir_path.is_dir():
        raise FileNotFoundError(f"Missing directory: {dir_path}")

    out: Dict[str, Path] = {}
    for p in dir_path.iterdir():
        if not p.is_file():
            continue
        if exts is not None:
            if p.suffix.lower() not in exts:
                continue
        out[p.name] = p
    return out


def build_pairs(in_dir: Path, exts: Sequence[str] | None) -> List[PairPaths]:
    a_dir = in_dir / "A"
    b_dir = in_dir / "B"
    l_dir = in_dir / "label"

    a = list_files(a_dir, exts)
    b = list_files(b_dir, exts)
    l = list_files(l_dir, exts)

    keys = sorted(set(a) & set(b) & set(l))

    missing_a = sorted((set(b) | set(l)) - set(a))
    missing_b = sorted((set(a) | set(l)) - set(b))
    missing_l = sorted((set(a) | set(b)) - set(l))

    if missing_a or missing_b or missing_l:
        msg = ["Input pairs are inconsistent (filenames must match across A/B/label)."]
        if missing_a:
            msg.append(f"Missing in A: {missing_a[:20]}" + (" ..." if len(missing_a) > 20 else ""))
        if missing_b:
            msg.append(f"Missing in B: {missing_b[:20]}" + (" ..." if len(missing_b) > 20 else ""))
        if missing_l:
            msg.append(f"Missing in label: {missing_l[:20]}" + (" ..." if len(missing_l) > 20 else ""))
        raise RuntimeError("\n".join(msg))

    pairs: List[PairPaths] = []
    for name in keys:
        stem = Path(name).stem
        pairs.append(PairPaths(stem=stem, a=a[name], b=b[name], label=l[name]))

    if not pairs:
        raise RuntimeError(f"No paired files found under {in_dir} (check A/B/label folders and extensions).")

    return pairs


def split_indices(n: int, train: float, val: float, test: float) -> Tuple[int, int, int]:
    if n <= 0:
        return 0, 0, 0

    total = train + val + test
    if abs(total - 1.0) > 1e-6:
        raise ValueError(f"train+val+test must sum to 1.0 (got {total})")

    n_train = int(round(n * train))
    n_val = int(round(n * val))
    # Ensure totals match
    n_test = n - n_train - n_val

    # Fix possible negative due to rounding
    if n_test < 0:
        n_test = 0
        # reduce train/val to fit
        extra = (n_train + n_val) - n
        if extra > 0:
            # reduce train first
            reduce_train = min(extra, n_train)
            n_train -= reduce_train
            extra -= reduce_train
            if extra > 0:
                n_val = max(0, n_val - extra)

    return n_train, n_val, n_test


def ensure_dirs(out_dir: Path, apply: bool) -> None:
    for s in SPLITS:
        for sub in ("A", "B", "label"):
            d = out_dir / s / sub
            if apply:
                d.mkdir(parents=True, exist_ok=True)


def copy_move_symlink(src: Path, dst: Path, method: str) -> None:
    if method == "copy":
        shutil.copy2(src, dst)
    elif method == "move":
        shutil.move(str(src), str(dst))
    elif method == "symlink":
        # Create a relative symlink where possible
        try:
            rel = os.path.relpath(src, start=dst.parent)
            dst.symlink_to(rel)
        except Exception:
            dst.symlink_to(src)
    else:
        raise ValueError(f"Unknown method: {method}")


def check_out_collisions(plan: List[Tuple[Path, Path]]) -> None:
    dsts = [d for _, d in plan]
    if len(dsts) != len(set(dsts)):
        raise RuntimeError("Output name collision: two items would write to the same destination path.")
    exists = [d for d in dsts if d.exists()]
    if exists:
        sample = [str(p) for p in exists[:20]]
        raise RuntimeError(
            "Some output files already exist; refusing to overwrite. Example(s):\n" + "\n".join(sample)
        )


def main(argv: List[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Split A/B/label pairs into train/val/test.")
    ap.add_argument("--in_dir", required=True, help="Input dataset root containing A/B/label")
    ap.add_argument("--out_dir", required=True, help="Output dataset root to create")

    ap.add_argument("--train", type=float, default=0.7, help="Train ratio")
    ap.add_argument("--val", type=float, default=0.15, help="Val ratio")
    ap.add_argument("--test", type=float, default=0.15, help="Test ratio")

    ap.add_argument("--seed", type=int, default=42, help="Random seed")
    ap.add_argument(
        "--method",
        choices=["copy", "move", "symlink"],
        default="copy",
        help="How to place files into split folders",
    )

    ap.add_argument(
        "--ext",
        default=None,
        help="Optional comma-separated list of extensions to include (e.g. .tif,.tiff,.png). If omitted, all files are included.",
    )

    ap.add_argument(
        "--apply",
        action="store_true",
        help="Actually create directories and write files (default: dry-run)",
    )

    args = ap.parse_args(argv)

    in_dir = Path(args.in_dir).expanduser().resolve()
    out_dir = Path(args.out_dir).expanduser().resolve()

    exts = None
    if args.ext:
        exts = [e.strip().lower() for e in args.ext.split(",") if e.strip()]
        exts = [e if e.startswith(".") else f".{e}" for e in exts]

    pairs = build_pairs(in_dir, exts)

    rng = random.Random(args.seed)
    rng.shuffle(pairs)

    n = len(pairs)
    n_train, n_val, n_test = split_indices(n, args.train, args.val, args.test)

    train_pairs = pairs[:n_train]
    val_pairs = pairs[n_train : n_train + n_val]
    test_pairs = pairs[n_train + n_val :]

    splits: Dict[str, List[PairPaths]] = {
        "train": train_pairs,
        "val": val_pairs,
        "test": test_pairs,
    }

    print(f"Found {n} pairs")
    print(f"Split sizes: train={len(train_pairs)} val={len(val_pairs)} test={len(test_pairs)}")
    print(f"Method: {args.method} | Seed: {args.seed} | Dry-run: {not args.apply}")

    ensure_dirs(out_dir, apply=args.apply)

    # Build action plan
    plan: List[Tuple[Path, Path]] = []
    for split_name, items in splits.items():
        for it in items:
            plan.append((it.a, out_dir / split_name / "A" / it.a.name))
            plan.append((it.b, out_dir / split_name / "B" / it.b.name))
            plan.append((it.label, out_dir / split_name / "label" / it.label.name))

    # Collision checks (including pre-existing outputs)
    check_out_collisions(plan)

    # Print a small preview
    preview = 10
    print("\nPreview (first few moves):")
    for src, dst in plan[:preview]:
        print(f"  {src} -> {dst}")
    if len(plan) > preview:
        print(f"  ... ({len(plan) - preview} more file operations)")

    if not args.apply:
        print("\nDry-run complete. Re-run with --apply to perform the split.")
        return 0

    # Execute
    for src, dst in plan:
        copy_move_symlink(src, dst, args.method)

    print("\nDone.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
