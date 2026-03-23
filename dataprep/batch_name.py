# batch name of the dataset in the folder

# input: folder path, batch add/delete prefix/postfix char
# output: batch named files in place

#!/usr/bin/env python3
"""batch_name.py

Batch-rename files in a folder:
- add/remove prefix/suffix
- optional regex find/replace

Examples
--------
# Dry-run: add prefix "pre_" to all .tif in a folder
python batch_name.py --dir /path/to/A --ext .tif --add-prefix pre_

# Apply: remove suffix "_label" before extension for all tif recursively
python batch_name.py --dir /path/to/labels --ext .tif --remove-suffix _label --recursive --apply

# Regex replace (dry-run): replace spaces with underscores
python batch_name.py --dir /path/to --regex-find "\\s+" --regex-repl "_"

Safety
------
- Default is dry-run (no changes). Use --apply to perform renames.
- The script blocks if two files would map to the same target name.
"""

from __future__ import annotations

import argparse
import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, List, Optional, Tuple


@dataclass(frozen=True)
class RenamePlan:
    src: Path
    dst: Path


def iter_files(root: Path, recursive: bool, pattern: Optional[str]) -> Iterable[Path]:
    if pattern:
        # If pattern includes directories, interpret relative to root.
        glob_root = root
        if recursive:
            yield from glob_root.rglob(pattern)
        else:
            yield from glob_root.glob(pattern)
    else:
        if recursive:
            yield from root.rglob("*")
        else:
            yield from root.glob("*")


def split_name(p: Path) -> Tuple[str, str]:
    """Return (stem, suffix) where suffix includes the leading dot or is ''."""
    return p.stem, p.suffix


def apply_prefix_suffix(
    stem: str,
    add_prefix: str,
    add_suffix: str,
    remove_prefix: str,
    remove_suffix: str,
) -> str:
    if remove_prefix and stem.startswith(remove_prefix):
        stem = stem[len(remove_prefix) :]

    if remove_suffix and stem.endswith(remove_suffix):
        stem = stem[: -len(remove_suffix)]

    if add_prefix:
        stem = add_prefix + stem

    if add_suffix:
        stem = stem + add_suffix

    return stem


def apply_regex(name: str, find_pat: Optional[str], repl: str) -> str:
    if not find_pat:
        return name
    return re.sub(find_pat, repl, name)


def build_plan(
    files: List[Path],
    add_prefix: str,
    add_suffix: str,
    remove_prefix: str,
    remove_suffix: str,
    regex_find: Optional[str],
    regex_repl: str,
    keep_ext: bool,
    new_ext: Optional[str],
) -> List[RenamePlan]:
    plan: List[RenamePlan] = []

    for src in files:
        stem, ext = split_name(src)

        new_stem = apply_prefix_suffix(
            stem=stem,
            add_prefix=add_prefix,
            add_suffix=add_suffix,
            remove_prefix=remove_prefix,
            remove_suffix=remove_suffix,
        )

        new_stem = apply_regex(new_stem, regex_find, regex_repl)

        # Extension handling
        if new_ext is not None:
            out_ext = new_ext if new_ext.startswith(".") else f".{new_ext}"
        else:
            out_ext = ext if keep_ext else ""

        dst = src.with_name(new_stem + out_ext)
        plan.append(RenamePlan(src=src, dst=dst))

    return plan


def check_collisions(plan: List[RenamePlan]) -> None:
    # Two sources mapping to same destination
    dst_map = {}
    for item in plan:
        dst_map.setdefault(item.dst, []).append(item.src)

    collisions = {dst: srcs for dst, srcs in dst_map.items() if len(srcs) > 1}
    if collisions:
        msg = ["Name collision detected (multiple files would become the same name):"]
        for dst, srcs in list(collisions.items())[:50]:
            msg.append(f"  -> {dst}")
            for s in srcs:
                msg.append(f"     from: {s}")
        if len(collisions) > 50:
            msg.append(f"  ... and {len(collisions) - 50} more collisions")
        raise RuntimeError("\n".join(msg))

    # Destination exists and is not the same file
    existing_conflicts = []
    for item in plan:
        if item.dst.exists() and item.dst.resolve() != item.src.resolve():
            existing_conflicts.append((item.src, item.dst))

    if existing_conflicts:
        msg = ["Target file already exists (would overwrite):"]
        for s, d in existing_conflicts[:50]:
            msg.append(f"  from: {s}")
            msg.append(f"    to: {d}")
        if len(existing_conflicts) > 50:
            msg.append(f"  ... and {len(existing_conflicts) - 50} more")
        raise RuntimeError("\n".join(msg))


def do_renames(plan: List[RenamePlan], apply: bool) -> None:
    if not plan:
        print("No files matched. Nothing to do.")
        return

    # Print plan
    for item in plan:
        rel_src = str(item.src)
        rel_dst = str(item.dst)
        if item.src == item.dst:
            print(f"SKIP (unchanged): {rel_src}")
        else:
            print(f"{rel_src} -> {rel_dst}")

    if not apply:
        print("\nDry-run only. Re-run with --apply to perform these renames.")
        return

    # Perform renames
    for item in plan:
        if item.src == item.dst:
            continue
        item.src.rename(item.dst)

    print(f"\nDone. Renamed {sum(1 for i in plan if i.src != i.dst)} files.")


def main(argv: Optional[List[str]] = None) -> int:
    ap = argparse.ArgumentParser(description="Batch rename files (prefix/suffix/regex) safely.")
    ap.add_argument("--dir", required=True, help="Target directory")
    ap.add_argument(
        "--pattern",
        default=None,
        help="Optional glob pattern (e.g. '*.tif' or 'A/*.tif'). If omitted, all files are considered.",
    )
    ap.add_argument("--recursive", action="store_true", help="Recurse into subfolders")
    ap.add_argument(
        "--ext",
        default=None,
        help="Optional extension filter (e.g. .tif). Case-insensitive. Applied after --pattern.",
    )

    ap.add_argument("--add-prefix", default="", help="Prefix to add")
    ap.add_argument("--add-suffix", default="", help="Suffix to add (before extension)")
    ap.add_argument("--remove-prefix", default="", help="Prefix to remove if present")
    ap.add_argument("--remove-suffix", default="", help="Suffix to remove if present (before extension)")

    ap.add_argument("--regex-find", default=None, help="Regex pattern to find in the stem")
    ap.add_argument("--regex-repl", default="", help="Replacement string for --regex-find")

    ap.add_argument(
        "--keep-ext",
        action="store_true",
        help="Keep original extension (default: True unless --new-ext is provided)",
    )
    ap.add_argument(
        "--new-ext",
        default=None,
        help="Force a new extension (e.g. tif). Overrides --keep-ext.",
    )

    ap.add_argument(
        "--apply",
        action="store_true",
        help="Actually rename files (default is dry-run)",
    )

    args = ap.parse_args(argv)

    root = Path(args.dir).expanduser().resolve()
    if not root.exists() or not root.is_dir():
        print(f"ERROR: directory not found: {root}", file=sys.stderr)
        return 2

    # Collect files
    candidates = [p for p in iter_files(root, args.recursive, args.pattern) if p.is_file()]

    # Extension filter
    if args.ext:
        ext = args.ext.lower()
        if not ext.startswith("."):
            ext = "." + ext
        candidates = [p for p in candidates if p.suffix.lower() == ext]

    # No-op warning
    if not any(
        [
            args.add_prefix,
            args.add_suffix,
            args.remove_prefix,
            args.remove_suffix,
            args.regex_find,
            args.new_ext,
        ]
    ):
        print("ERROR: No rename operation specified. Use --add-prefix/--add-suffix/--remove-*/--regex-find/--new-ext.")
        return 2

    keep_ext = True if args.new_ext is None else False
    if args.keep_ext:
        keep_ext = True

    plan = build_plan(
        files=candidates,
        add_prefix=args.add_prefix,
        add_suffix=args.add_suffix,
        remove_prefix=args.remove_prefix,
        remove_suffix=args.remove_suffix,
        regex_find=args.regex_find,
        regex_repl=args.regex_repl,
        keep_ext=keep_ext,
        new_ext=args.new_ext,
    )

    # Remove unchanged items from collision checks? Keep them; harmless.
    try:
        check_collisions(plan)
    except RuntimeError as e:
        print(f"ERROR: {e}", file=sys.stderr)
        return 1

    do_renames(plan, apply=args.apply)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())