# 11 - Managed PDF Finalization Prompt

Derived from `docs/tasks/11-managed-pdf-finalization.md`, `docs/tasks/10-annotation-and-pdf-final.md`, `TECH_SPEC.md`, and the worker experiment. This prompt is intended for direct subagent delegation.

## Objective

Replace the Windows print-pipeline finalization step with a managed .NET PDF library inside the Windows worker, keeping `Microsoft Print to PDF` only as a fallback.

## Scope

Implement the library-backed PDF finalization path so the worker can:

* take the worker intermediate output
* add annotations directly into the final PDF
* emit `final.pdf` in the output directory
* return the final PDF path in the worker result
* keep the existing CLI and JSON contract stable

## Ownership / Files to Inspect First

* `TECH_SPEC.md`
* `docs/tasks/11-managed-pdf-finalization.md`
* `docs/tasks/10-annotation-and-pdf-final.md`
* `experiments/solidedge-reader-poc/README.md`
* `experiments/solidedge-reader-poc/DraftConversionPipeline.cs`
* `experiments/solidedge-reader-poc/MicrosoftPrintToPdfWriter.cs`
* `experiments/solidedge-reader-poc/IAnnotatedPdfWriter.cs`
* `experiments/solidedge-reader-poc/DraftPdfFinalizationException.cs`
* `experiments/solidedge-reader-poc/WorkerResult.cs`
* `experiments/solidedge-reader-poc/SolidEdgeReaderPoc.csproj`
* `apps/web/lib/windows-dft-worker.ts`

## Critical Constraints

* Use `Syncfusion.Pdf` as specified in the task doc.
* Keep the change isolated to the worker and its immediate support code.
* Keep the EMF export stage unchanged unless the finalization path forces a narrow adjustment.
* Preserve the worker CLI and JSON response shape.
* Keep `Microsoft Print to PDF` as a fallback only, not the primary path.
* Fail with a specific PDF-finalization error if the managed library path cannot be used.
* Do not broaden the scope into unrelated matching or frontend work.

## Acceptance Criteria

* The default worker path no longer depends on `Microsoft Print to PDF`.
* The worker writes `final.pdf` using the managed PDF library.
* The final PDF includes the annotation layer or a clear scaffold for it.
* The worker still returns the expected structured result payload.
* The fallback behavior is documented and explicit.

## Report-Back Requirements

Report back with:

* the exact finalization path implemented
* how annotations are inserted into the PDF
* any library/license/runtime assumptions
* any changes to worker artifacts or error codes
* whether the fallback path was kept intact and how it is triggered

