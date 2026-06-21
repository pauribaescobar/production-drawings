# Windows Worker Validation Runbook

This runbook explains how to validate the Solid Edge DFT worker on a Windows machine or VM.

## What this covers

- building the worker
- running it directly from the command line
- validating the expected JSON output
- optionally wiring it into the web app through `DFT_WORKER_COMMAND`

## What you need

- Windows 10/11 or Windows Server
- Git
- .NET Framework 4.8 build tools
- Python 3.11+ if you want to run the PDF parser locally
- Node.js 20+ if you want to run the web app or the `/api/generate` flow
- A cloned copy of this repository
- At least one `.dft` sample file
- A matching parsed-order JSON file
- The `Microsoft Print to PDF` printer available on the machine

## Recommended install set

For the full end-to-end setup on a Windows VM, install:

- Git
- .NET Framework 4.8 Developer Pack or Visual Studio Build Tools with the .NET Framework 4.8 targeting pack
- Python 3.11 or newer
- Node.js 20 or newer

You only need the full set if you want to test both the worker and the web app locally. For worker-only validation, Git + .NET 4.8 are enough, plus the sample files and the PDF printer.

## 1. Build the worker

From the repository root:

```powershell
dotnet restore .\experiments\solidedge-reader-poc\SolidEdgeReaderPoc.csproj
dotnet build .\experiments\solidedge-reader-poc\SolidEdgeReaderPoc.csproj -c Release
```

The executable should end up at:

```text
experiments\solidedge-reader-poc\bin\Release\net48\SolidedgeReaderPoc.exe
```

## 2. Prepare test inputs

You need two inputs for the worker:

- one `.dft` file
- one parsed-order JSON file

The parsed-order JSON must follow the worker contract:

- top-level `orderNumber`
- top-level `lines`
- each line must contain:
  - `reference`
  - `quantity`
  - `material`
  - `deliveryDate`
  - optional `treatment`
  - optional `dimensions`

Important:

- the DFT file name must match one of the parsed-order `reference` values
- the worker uses the DFT basename to find the matching line

## 3. Run the worker directly

From PowerShell:

```powershell
.\experiments\solidedge-reader-poc\bin\Release\net48\SolidedgeReaderPoc.exe export `
  C:\work\samples\part-001.dft `
  C:\work\out `
  C:\work\samples\parsed-order.json `
  job-001
```

Expected result:

- exit code `0`
- JSON on stdout with `status: "succeeded"`
- artifacts written to the output directory
- `final.pdf` present in the output directory

If the worker fails, stdout still returns JSON with:

- `status: "failed"`
- `errorCode`
- `errorMessage`

## 4. Validate the output

Check these items:

- the JSON reports the right `jobId`
- `sheetCount` matches the number of exported DFT sheets
- `artifacts` includes the generated EMFs and `final.pdf`
- `final.pdf` opens correctly in a PDF viewer
- the annotations show the order data for the matching line

## 5. Validate through the web app

If you want to test the full integration from the web app:

1. Set `DFT_WORKER_COMMAND` to the full path of the worker executable on the Windows machine.
2. Start the web app.
3. Upload the order PDF and the ZIP with the DFT files.
4. Trigger the generation flow.
5. Confirm that the browser opens the resulting PDF directly.

Example environment variable:

```powershell
$env:DFT_WORKER_COMMAND = "C:\repo\production-drawings\experiments\solidedge-reader-poc\bin\Release\net48\SolidedgeReaderPoc.exe"
```

## 6. Common failure modes

- missing DFT file
- parsed-order JSON does not match the DFT basename
- `Microsoft Print to PDF` is unavailable
- output directory is not writable
- the Windows worker executable has not been built yet

## Current source of truth

- Worker contract: `experiments/solidedge-reader-poc/README.md`
- Web integration: `apps/web/README.md`
- UI and parsing flow: `docs/contracts/pdf-parser-output.md`
