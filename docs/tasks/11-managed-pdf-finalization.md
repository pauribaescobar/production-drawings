# 11 - Managed PDF Finalization

## Goal

Replace the Windows print-pipeline finalization step with a managed .NET PDF library inside the Windows worker, keeping `Microsoft Print to PDF` only as a fallback.

## Why this exists

The worker already exports EMF sheets. The next reliable step is to convert those sheets into the final PDF in a deterministic, library-backed way so the project does not depend on the Windows printer feature being present and configured.

## Decision

Use `Syncfusion.Pdf` for the PDF finalization and annotation stage.

## Why this path

* It stays inside the existing Windows worker and .NET runtime.
* It avoids a machine-level dependency on the Windows print subsystem.
* It supports drawing annotations directly into the final PDF.
* It keeps the output more deterministic and easier to automate.
* `Microsoft Print to PDF` remains available only as a fallback.

## Scope

* integrate a managed PDF library into the worker project
* replace the print-pipeline finalizer with a library-backed PDF writer
* keep the EMF export stage unchanged
* keep the existing worker CLI and JSON contract stable
* preserve the ability to fall back to the print pipeline if needed

## Inputs

* `TECH_SPEC.md`
* `docs/tasks/10-annotation-and-pdf-final.md`
* `experiments/solidedge-reader-poc/`

## Expected implementation shape

* take the worker's EMF/intermediate output
* replay or place that content into a PDF page using the PDF library
* add annotations directly into the PDF
* emit `final.pdf` in the worker output directory
* return the final PDF path in the worker result payload

## Acceptance criteria

* the default worker path no longer depends on `Microsoft Print to PDF`
* the final PDF is produced by a managed .NET PDF library
* annotations are drawn or embedded in the final PDF
* the worker contract remains stable for the web app
* a fallback path is documented if the library cannot be used on a target host

## Notes for the implementer

* keep the change isolated to the worker and its immediate support code
* do not change the public web contract unless absolutely necessary
* prefer simple overlays/stamps first; do not over-engineer PDF internals unless required
* if the library choice requires a license key, document that clearly

