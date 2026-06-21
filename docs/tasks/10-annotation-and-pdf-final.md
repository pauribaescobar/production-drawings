# 10 - Annotation and PDF Finalization

## Goal

Extend the Windows DFT worker beyond EMF export so it can apply annotations and produce the final PDF artifact.

## Why this exists

The worker now has a reliable EMF intermediate, but the pipeline still needs a Windows-native finalization step so the web app can receive a finished PDF rather than a vector staging format.

## Decision

Use a managed .NET PDF library inside the Windows worker to finalize the PDF, with the Windows print subsystem kept only as a fallback.

## Why this path

* It keeps the conversion boundary Windows-only.
* It avoids editing EMF files in place.
* It preserves the existing EMF intermediate for debugging and fallback workflows.
* It avoids a hard dependency on the Windows print feature.
* It keeps the final PDF generation inside the same worker process and contract.

## Scope

* keep EMF export as the first stage
* generate per-sheet annotations in the worker
* finalize a PDF file in the worker output directory
* return the final PDF path alongside the intermediate EMF artifacts
* keep the worker callable through the existing local boundary

## Expected worker behavior

1. Open the input `.dft`.
2. Export one EMF per sheet.
3. Build a per-sheet annotation payload.
4. Render or replay the EMF into a PDF page using the managed PDF library.
5. Write `final.pdf` in the output directory.
6. Return structured JSON with all generated artifact paths.

## Assumptions

* The worker host is Windows.
* The host has a managed .NET PDF library available through the worker's project dependencies.
* The current annotation payload can stay simple until the business-rule payload is wired in from the orchestrator.

## Acceptance criteria

* the worker no longer stops after EMF export
* the worker writes a final PDF artifact
* annotations are represented in the implementation or the worker has a clear library-backed scaffold for them
* the local worker contract remains intact

## Follow-up

If the managed PDF library path cannot be used on a target host, the worker should fail with a specific PDF-finalization error so the caller can distinguish dependency problems from input conversion problems. `Microsoft Print to PDF` remains a fallback only.
