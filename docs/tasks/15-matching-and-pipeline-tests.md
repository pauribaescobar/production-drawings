# 15 - Matching and Pipeline Tests

## Goal

Add automated tests and fixtures that validate the order parsing, ZIP inspection, DFT matching, and end-to-end orchestration behavior using real sample inputs.

## Why this exists

The new preflight and orchestration logic needs regression coverage so the matching rules do not drift as the pipeline evolves.

## Scope

* create tests for PDF parsing and ZIP matching
* add fixtures for representative sample PDFs and ZIPs
* validate case-insensitive DFT matching
* validate that STP files are ignored
* validate the handling of missing DFTs
* validate the happy path for the end-to-end route as far as possible without the Windows host

## Fixtures

Use real or representative samples stored in a dedicated test fixtures folder.

The fixtures should cover:

* a basic order with one matching DFT
* an order with multiple lines
* a ZIP with extra STP files
* a ZIP with missing DFTs
* repeated lines that should remain visible in the output

## Acceptance criteria

* the matching logic is covered by tests
* the pipeline can be exercised with local fixtures
* the tests document the expected mapping behavior
* the preflight output and the orchestration assumptions are protected against regressions

## Notes

* Prefer the same test harness that already fits the web app and Python package structure.
* Keep the fixtures small and easy to understand.
* If a full end-to-end Windows worker run cannot be tested locally, isolate the maximum possible coverage in unit/integration tests around the matching and orchestration seams.

