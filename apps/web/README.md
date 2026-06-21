# Web app

Next.js UI for uploading the order PDF and ZIP, orchestrating the parser + worker flow, and returning the final PDF.

The web app also exposes a narrow localhost-style bridge to the Windows DFT worker through `POST /api/dft-worker` for internal reuse.

## Planned responsibilities

* upload form
* backend upload route for `pdf` and `zip`
* end-to-end generation route
* browser-friendly PDF response

## Local run

From the repository root:

```bash
npm install
python3 -m pip install -e packages/python-processing
npm run dev --workspace web
```

Then open:

```text
http://localhost:3000
```

## What to verify

* the page renders
* the PDF input accepts a `.pdf`
* the ZIP input accepts a `.zip`
* the `Generar planos nuevos` button stays disabled until both files are present
* submitting both files opens the generated PDF in a new tab
* malformed or missing files return a clear JSON error

## Backend route

`POST /api/generate` expects multipart form data with:

* `pdf`: the order PDF
* `zip`: the drawings ZIP

The route validates both files, creates a job workspace, runs the Python parser, extracts and matches DFT files, invokes the Windows worker, merges the worker PDFs, and returns the final PDF with `Content-Type: application/pdf`.

## Windows DFT worker bridge

`POST /api/dft-worker` expects multipart form data with:

* `dft`: a single `.dft` file
* `parsedOrder`: the parsed order JSON file for the matching drawing
* `jobId`: optional correlation id

The route writes the DFT and parsed order JSON to temp files, creates a local output directory, and invokes the Windows worker executable configured by `DFT_WORKER_COMMAND`.

If `DFT_WORKER_COMMAND` is not set, the route falls back to the built worker executable at:

```text
experiments/solidedge-reader-poc/bin/Release/net48/SolidedgeReaderPoc.exe
```

The worker returns structured JSON with:

* `status`
* `jobId`
* `inputPath`
* `outputDirectory`
* `sheetCount`
* `artifacts`
* `errorCode`
* `errorMessage`

## Quick test

```bash
curl -X POST http://localhost:3000/api/generate \
  -F "pdf=@./samples/PC26-04499 EMBEIM.pdf" \
  -F "zip=@./samples/PC26-04499.Zip" \
  --output generated.pdf
```
