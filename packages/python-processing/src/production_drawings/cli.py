from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys

from .parser import parse_order_pdf


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Parse an order PDF into JSON.")
    parser.add_argument("pdf", type=Path, help="Path to the order PDF")
    parser.add_argument(
        "--compact",
        action="store_true",
        help="Print compact JSON instead of pretty-printed JSON",
    )
    return parser


def main() -> int:
    args = build_parser().parse_args()
    result = parse_order_pdf(args.pdf)
    if args.compact:
        json.dump(result, sys.stdout, ensure_ascii=False, separators=(",", ":"))
        sys.stdout.write("\n")
    else:
        json.dump(result, sys.stdout, ensure_ascii=False, indent=2)
        sys.stdout.write("\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
