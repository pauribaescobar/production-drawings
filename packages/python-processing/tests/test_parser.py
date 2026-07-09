from __future__ import annotations

import unittest

from production_drawings.parser import TextChar, TextLine, _parse_header_line


def _make_chars(text: str, start_x: float) -> list[TextChar]:
    chars: list[TextChar] = []
    x = start_x

    for ch in text:
        chars.append(TextChar(ch=ch, x0=x, y0=0.0, x1=x + 1.0, y1=1.0))
        x += 2.0

    return chars


class ParseHeaderLineTests(unittest.TestCase):
    def test_hyphenated_reference_is_preserved_as_the_full_column_value(self) -> None:
        chars = []
        chars.extend(_make_chars("9-1", 0.0))
        chars.extend(_make_chars("733", 50.0))
        chars.extend(_make_chars("-", 100.0))
        chars.extend(_make_chars("8", 130.0))
        chars.extend(_make_chars("2", 180.0))
        chars.extend(_make_chars("AISI-304", 220.0))
        chars.extend(_make_chars("ZINCADO", 320.0))

        row = _parse_header_line(TextLine(text="9-1 733-8 2 AISI-304 ZINCADO", chars=chars))

        self.assertIsNotNone(row)
        assert row is not None
        self.assertEqual(row["reference"], "733-8")
        self.assertEqual(row["quantity"], 2)
        self.assertEqual(row["material"], "AISI-304")
        self.assertEqual(row["treatment"], "ZINCADO")


if __name__ == "__main__":
    unittest.main()
