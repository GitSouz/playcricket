#!/usr/bin/env python3
"""Combine multiple JSON files that share the same structure into one file.

Two common shapes are supported:

  * Each input file is a JSON *array* — the elements of every file are
    concatenated into a single flat array.
  * Each input file is a JSON *object* — every object is collected into a
    single array (one element per file).

You can mix nothing: all inputs must be the same shape (all arrays or all
objects). This keeps the "same structure for all files" guarantee honest and
avoids silently producing something surprising.

Examples
--------
    # Merge every .json file in ./data into combined.json
    python merge_json.py ./data -o combined.json

    # Merge an explicit list of files
    python merge_json.py a.json b.json c.json -o all.json

    # Recurse into sub-directories, pretty-printed with 2-space indent
    python merge_json.py ./data --recursive -o combined.json --indent 2

    # Split the merged records into files of 100 each: combined_001.json, ...
    python merge_json.py ./data -o combined.json --batch-size 100
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


def collect_input_files(paths: list[str], recursive: bool) -> list[Path]:
    """Expand the given paths into a sorted list of .json files.

    Directories are scanned for *.json (recursively with --recursive);
    individual files are taken as-is.
    """
    files: list[Path] = []
    for raw in paths:
        p = Path(raw)
        if p.is_dir():
            pattern = "**/*.json" if recursive else "*.json"
            files.extend(sorted(p.glob(pattern)))
        elif p.is_file():
            files.append(p)
        else:
            raise FileNotFoundError(f"No such file or directory: {raw}")
    # De-duplicate while preserving order.
    seen: set[Path] = set()
    unique: list[Path] = []
    for f in files:
        resolved = f.resolve()
        if resolved not in seen:
            seen.add(resolved)
            unique.append(f)
    return unique


def merge(files: list[Path]) -> list:
    """Load every file and merge them into a single list.

    Array inputs are extended; object inputs are appended. All files must
    share the same top-level shape.
    """
    combined: list = []
    shape: str | None = None  # "array" or "object"

    for f in files:
        try:
            with f.open(encoding="utf-8") as fh:
                data = json.load(fh)
        except json.JSONDecodeError as exc:
            raise ValueError(f"{f}: invalid JSON ({exc})") from exc

        this_shape = "array" if isinstance(data, list) else "object" if isinstance(data, dict) else None
        if this_shape is None:
            raise ValueError(
                f"{f}: top-level JSON must be an array or object, got {type(data).__name__}"
            )

        if shape is None:
            shape = this_shape
        elif shape != this_shape:
            raise ValueError(
                f"{f}: is a JSON {this_shape} but earlier files are JSON {shape}s — "
                "all files must share the same structure"
            )

        if this_shape == "array":
            combined.extend(data)
        else:
            combined.append(data)

    return combined


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Combine multiple same-structure JSON files into one.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument(
        "inputs",
        nargs="+",
        help="JSON files and/or directories containing .json files",
    )
    parser.add_argument(
        "-o",
        "--output",
        help="Output file (defaults to stdout)",
    )
    parser.add_argument(
        "-r",
        "--recursive",
        action="store_true",
        help="Recurse into sub-directories when an input is a directory",
    )
    parser.add_argument(
        "--indent",
        type=int,
        default=2,
        help="Indentation for the output (default: 2; use 0 for compact)",
    )
    parser.add_argument(
        "--batch-size",
        type=int,
        default=0,
        help="Split output into files of at most this many records "
        "(0 = single file). Requires --output.",
    )
    args = parser.parse_args(argv)

    if args.batch_size < 0:
        print("error: --batch-size cannot be negative", file=sys.stderr)
        return 1
    if args.batch_size and not args.output:
        print("error: --batch-size requires --output", file=sys.stderr)
        return 1

    try:
        files = collect_input_files(args.inputs, args.recursive)
    except FileNotFoundError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1

    if not files:
        print("error: no JSON files found in the given inputs", file=sys.stderr)
        return 1

    try:
        combined = merge(files)
    except ValueError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1

    indent = args.indent if args.indent > 0 else None

    def dump(records: list) -> str:
        return json.dumps(records, indent=indent, ensure_ascii=False)

    if args.batch_size:
        out = Path(args.output)
        total = len(combined)
        batches = [combined[i : i + args.batch_size] for i in range(0, total, args.batch_size)]
        width = max(3, len(str(len(batches))))
        for idx, batch in enumerate(batches, start=1):
            batch_path = out.with_name(f"{out.stem}_{idx:0{width}d}{out.suffix}")
            batch_path.write_text(dump(batch) + "\n", encoding="utf-8")
        print(
            f"Merged {len(files)} file(s) into {total} record(s), "
            f"written as {len(batches)} file(s) of up to {args.batch_size} "
            f"(e.g. {out.with_name(f'{out.stem}_{1:0{width}d}{out.suffix}').name})",
            file=sys.stderr,
        )
    elif args.output:
        Path(args.output).write_text(dump(combined) + "\n", encoding="utf-8")
        print(
            f"Merged {len(files)} file(s) into {len(combined)} record(s) -> {args.output}",
            file=sys.stderr,
        )
    else:
        print(dump(combined))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
