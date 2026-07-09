# Python processing

Python package for:

* parsing order PDFs
* later processing `.dft` drawings
* merging PDF artifacts when the web app needs a browser-facing final PDF

## Parse the order PDF

The package exposes a CLI entry point:

```bash
parse-order-pdf path/to/order.pdf --compact
```

By default, the command prints pretty JSON to stdout. Use `--compact` if you want a single-line JSON payload.

The same command is also available through Python module execution:

```bash
python -m production_drawings.cli path/to/order.pdf --compact
```

The parser is the first step in the end-to-end flow. It reads the order PDF and produces the parsed-order JSON that the Windows worker consumes later.
