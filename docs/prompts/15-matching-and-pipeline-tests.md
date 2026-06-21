# 15 - Matching and Pipeline Tests Prompt

Derived from `docs/tasks/15-matching-and-pipeline-tests.md`, `docs/tasks/16-dft-linking-and-business-identity-refactor.md`, `docs/contracts/pdf-parser-output.md`, and `TECH_SPEC.md`. This prompt is intended for direct subagent delegation.

## Objective

Add automated tests and fixtures that validate order parsing, ZIP inspection, DFT matching, and the end-to-end orchestration behavior using real sample inputs.

## Scope

Add regression coverage for:

* PDF parsing output shape
* ZIP inventory inspection
* case-insensitive DFT matching
* ignoring STP files completely
* handling missing DFTs
* repeated lines staying visible in output
* the happy path through the web orchestration seam as far as local execution allows

## Ownership / Files to Inspect First

* `docs/tasks/15-matching-and-pipeline-tests.md`
* `docs/tasks/16-dft-linking-and-business-identity-refactor.md`
* `docs/contracts/pdf-parser-output.md`
* `TECH_SPEC.md`
* `apps/web/lib/dft-preflight.ts`
* `apps/web/lib/generate-final-pdf.ts`
* `apps/web/app/api/generate/route.ts`
* `packages/python-processing/src/production_drawings/parser.py`
* `packages/python-processing/src/production_drawings/merge.py`
* `samples/PC26-04214 EMBEIM.pdf`
* `samples/PC26-04499 EMBEIM.pdf`
* `samples/PC26-04523 EMBEIM.pdf`

## Critical Constraints

* Use real or representative fixtures in a dedicated test fixture location.
* Keep fixtures small and understandable.
* Keep STP files ignored at every matching layer.
* Make the tests describe the expected mapping behavior, not just implementation details.
* If a full Windows-worker end-to-end run is not locally possible, maximize coverage around the parser, preflight, and orchestration seams.
* Do not change unrelated UI behavior.

## Acceptance Criteria

* Matching logic is covered by automated tests.
* Local fixtures exercise the pipeline.
* Tests document the expected mapping and error behavior.
* Regression coverage protects the preflight output and orchestration assumptions.
* Repeated order lines remain visible in the expected output path.

## Report-Back Requirements

Report back with:

* the fixture set added or updated
* the test cases added and the behaviors they lock in
* any gaps that could not be covered without a Windows host
* any contract mismatches found while writing the tests

