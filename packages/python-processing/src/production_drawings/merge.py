from __future__ import annotations

import argparse
from pathlib import Path

from pypdf import PdfReader, PdfWriter


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Merge PDF files into a single PDF.")
    parser.add_argument("output", type=Path, help="Path to the merged PDF output")
    parser.add_argument("inputs", nargs="+", type=Path, help="Input PDF files to merge")
    return parser


def merge_pdfs(output_path: Path, input_paths: list[Path]) -> None:
    if not input_paths:
        raise ValueError("At least one PDF input is required.")

    writer = PdfWriter()

    for input_path in input_paths:
        reader = PdfReader(str(input_path))
        for page in reader.pages:
            writer.add_page(page)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    with output_path.open("wb") as handle:
        writer.write(handle)


def main() -> int:
    args = build_parser().parse_args()
    merge_pdfs(args.output, list(args.inputs))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
