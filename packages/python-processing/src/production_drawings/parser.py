from __future__ import annotations

from dataclasses import asdict, dataclass
from pathlib import Path
import re

import pypdfium2 as pdfium


TABLE_HEADER_RE = re.compile(r"\breferencia\b.*\bcant\b", re.IGNORECASE)
DETAIL_LINE_RE = re.compile(
    r"^(?P<ot_code>OT\d+)(?:\s+(?P<delivery_date>\d{2}/\d{2}/\d{2}))?(?:\s+(?P<description>.*))?$",
    re.IGNORECASE,
)
PROJECT_TOKEN_RE = re.compile(r"^\d+-\d+$")
INTEGER_TOKEN_RE = re.compile(r"^\d+$")
PRICE_TOKEN_RE = re.compile(r"^\d+(?:[.,]\d+)?$")
ORDER_NUMBER_RE = re.compile(r"\bPC\d+-\d+\b", re.IGNORECASE)

SEGMENT_GAP_THRESHOLD = 24.0


@dataclass(slots=True)
class ParsedOrderRow:
    reference: str
    quantity: int
    material: str
    treatment: str | None
    deliveryDate: str
    dimensions: dict[str, str]


@dataclass(slots=True)
class TextChar:
    ch: str
    x0: float
    y0: float
    x1: float
    y1: float


@dataclass(slots=True)
class TextLine:
    text: str
    chars: list[TextChar]


def _clean_text(value: str | None) -> str | None:
    if value is None:
        return None

    cleaned = value.strip()
    return cleaned or None


def _extract_order_number(lines: list[str]) -> str | None:
    for line in lines:
        match = ORDER_NUMBER_RE.search(line)
        if match:
            return match.group(0).strip()
    return None


def _collect_text_lines(page: pdfium.PdfPage) -> list[TextLine]:
    text_page = page.get_textpage()
    text = text_page.get_text_range()
    lines: list[TextLine] = []
    current_chars: list[TextChar] = []

    for index, ch in enumerate(text):
        if ch in "\r\n":
            if current_chars:
                lines.append(
                    TextLine(
                        text="".join(char.ch for char in current_chars).strip(),
                        chars=current_chars,
                    )
                )
                current_chars = []
            continue

        x0, y0, x1, y1 = text_page.get_charbox(index)
        current_chars.append(TextChar(ch=ch, x0=x0, y0=y0, x1=x1, y1=y1))

    if current_chars:
        lines.append(
            TextLine(
                text="".join(char.ch for char in current_chars).strip(),
                chars=current_chars,
            )
        )

    return lines


def _segment_line(line: TextLine) -> list[str]:
    if not line.chars:
        return []

    segments: list[list[TextChar]] = []
    current: list[TextChar] = [line.chars[0]]

    for previous, current_char in zip(line.chars, line.chars[1:]):
        gap = current_char.x0 - previous.x1
        if gap > SEGMENT_GAP_THRESHOLD:
            segments.append(current)
            current = [current_char]
            continue

        current.append(current_char)

    segments.append(current)

    return ["".join(char.ch for char in segment).strip() for segment in segments if "".join(char.ch for char in segment).strip()]


def _split_material_and_treatment_segments(segments: list[str]) -> tuple[str, str | None]:
    if not segments:
        return "", None

    material = segments[0].strip()
    treatment = segments[1].strip() if len(segments) > 1 else None
    return material, _clean_text(treatment)


def _parse_dimensions(description: str) -> dict[str, str]:
    dimension_pattern = re.compile(
        r"(?P<axis>[A-Z])\s*=\s*(?P<value>[0-9]+(?:[.,][0-9]+)?)\s*MM",
        re.IGNORECASE,
    )
    dimensions: dict[str, str] = {}

    for match in dimension_pattern.finditer(description):
        axis = match.group("axis").strip()
        value = match.group("value").strip()
        if axis and value:
            dimensions[axis] = value

    return dimensions


def _parse_header_line(line: TextLine) -> dict[str, object] | None:
    segments = _segment_line(line)
    if len(segments) < 4:
        return None

    if not PROJECT_TOKEN_RE.match(segments[0].strip()):
        return None

    reference = segments[1].strip()
    if not reference:
        return None

    quantity_index: int | None = None
    quantity_value: int | None = None
    price_in_same_segment = False

    for index, segment in enumerate(segments[2:], start=2):
        tokens = segment.split()
        if not tokens:
            continue

        if INTEGER_TOKEN_RE.match(tokens[0]):
            quantity_index = index
            quantity_value = int(tokens[0])
            if len(tokens) > 1 and PRICE_TOKEN_RE.match(tokens[1]):
                price_in_same_segment = True
            break

    if quantity_index is None or quantity_value is None:
        return None

    quantity = quantity_value
    tail_segments = segments[quantity_index + 1 :]

    if not price_in_same_segment and tail_segments and PRICE_TOKEN_RE.match(tail_segments[0].strip()):
        tail_segments = tail_segments[1:]

    if not tail_segments:
        return {
            "reference": reference,
            "quantity": quantity,
            "material": "",
            "treatment": None,
        }

    if PRICE_TOKEN_RE.match(tail_segments[0].strip()) and len(tail_segments) > 1:
        tail_segments = tail_segments[1:]

    material, treatment = _split_material_and_treatment_segments(tail_segments)
    if not material:
        return None

    return {
        "reference": reference,
        "quantity": quantity,
        "material": material,
        "treatment": treatment,
    }


def _parse_detail_line(line: TextLine) -> tuple[str, dict[str, str]]:
    match = DETAIL_LINE_RE.match(line.text.strip())
    if not match:
        return "", {}

    description = match.group("description") or ""
    return _clean_text(match.group("delivery_date")) or "", _parse_dimensions(description)


def _materialize_row(pending_header: dict[str, object], detail_date: str, dimensions: dict[str, str]) -> ParsedOrderRow:
    return ParsedOrderRow(
        reference=str(pending_header["reference"]),
        quantity=int(pending_header["quantity"]),
        material=str(pending_header["material"]),
        treatment=_clean_text(str(pending_header["treatment"])) if pending_header["treatment"] is not None else None,
        deliveryDate=detail_date,
        dimensions=dimensions,
    )


def _parse_page(
    page: pdfium.PdfPage,
    pending_header: dict[str, object] | None = None,
) -> tuple[list[ParsedOrderRow], dict[str, object] | None, list[str]]:
    lines = _collect_text_lines(page)
    parsed: list[ParsedOrderRow] = []
    table_started = False
    all_text_lines = [line.text for line in lines]

    for line in lines:
        if line.text.upper() == "IMPORTANTE:":
            break

        if not table_started:
            if TABLE_HEADER_RE.search(line.text):
                table_started = True
            continue

        detail_date, dimensions = _parse_detail_line(line)
        if pending_header is not None and (detail_date or line.text.startswith("OT")):
            parsed.append(_materialize_row(pending_header, detail_date, dimensions))
            pending_header = None
            continue

        header_candidate = _parse_header_line(line)
        if header_candidate is not None:
            if pending_header is not None:
                parsed.append(_materialize_row(pending_header, "", {}))
            pending_header = header_candidate

    return parsed, pending_header, all_text_lines


def parse_order_pdf(pdf_path: str | Path) -> dict[str, object]:
    path = Path(pdf_path)
    document = pdfium.PdfDocument(str(path))

    rows: list[ParsedOrderRow] = []
    all_lines: list[str] = []
    pending_header: dict[str, object] | None = None

    for page in document:
        page_rows, pending_header, page_lines = _parse_page(page, pending_header)
        rows.extend(page_rows)
        all_lines.extend(page_lines)

    if pending_header is not None:
        rows.append(_materialize_row(pending_header, "", {}))

    order_number = _extract_order_number(all_lines)
    serialized_rows = [asdict(item) for item in rows]

    return {
        "orderNumber": order_number or "",
        "rows": serialized_rows,
        "lines": serialized_rows,
    }
