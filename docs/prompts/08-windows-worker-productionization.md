# 08 - Windows Worker Productionization Prompt

Derived from `docs/tasks/08-windows-worker-productionization.md`, `TECH_SPEC.md`, and the current worker experiment. This prompt is intended for direct subagent delegation.

## Objective

Turn the isolated `SolidEdgeCommunity.Reader` experiment into a production-ready Windows conversion worker that the web app can invoke through a private local boundary.

## Scope

Implement the worker boundary and worker-facing contract so the worker can:

* accept a single DFT input and parsed order JSON
* export the drawing intermediate
* produce the final PDF artifact
* return structured success or failure data
* remain private to the host and replaceable later

## Ownership / Files to Inspect First

* `TECH_SPEC.md`
* `docs/tasks/08-windows-worker-productionization.md`
* `docs/architecture/windows-self-hosted-public-app.md`
* `experiments/solidedge-reader-poc/README.md`
* `experiments/solidedge-reader-poc/Program.cs`
* `experiments/solidedge-reader-poc/DraftConversionPipeline.cs`
* `experiments/solidedge-reader-poc/DraftEmfExporter.cs`
* `experiments/solidedge-reader-poc/ParsedOrderDocument.cs`
* `experiments/solidedge-reader-poc/SheetAnnotation.cs`
* `experiments/solidedge-reader-poc/MicrosoftPrintToPdfWriter.cs`
* `experiments/solidedge-reader-poc/WorkerResult.cs`
* `experiments/solidedge-reader-poc/WorkerJson.cs`
* `apps/web/lib/windows-dft-worker.ts`
* `apps/web/app/api/dft-worker/route.ts`

## Critical Constraints

* Keep the worker Windows-only.
* Keep the worker private to the host.
* Keep the external contract stable for the web app.
* Do not expose a public API surface for the worker.
* Preserve the possibility of moving the worker out of process later.
* Align the worker output paths with the single-host workspace shape in `TECH_SPEC.md`.
* Do not change unrelated frontend behavior.
* Treat the current experiment as a seam to harden, not as a throwaway scaffold.

## Acceptance Criteria

* The worker can be invoked from the web app through the existing local boundary.
* The worker reads DFT input and parsed order JSON and writes artifacts to a known output directory.
* The worker returns structured JSON with success/failure status, output paths, and error details.
* The worker no longer behaves like a proof-of-concept only.
* The worker contract matches the deployment model described in `TECH_SPEC.md`.

## Report-Back Requirements

Report back with:

* the files changed
* the worker contract shape before and after
* any boundary or compatibility risks
* any assumptions that still need product confirmation
* whether the current local invocation path can remain stable for the next workstream

