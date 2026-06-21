# 16 - DFT Linking and Business Identity Refactor Prompt

Derived from `docs/tasks/16-dft-linking-and-business-identity-refactor.md`, `TECH_SPEC.md`, `docs/contracts/pdf-parser-output.md`, and the current preflight/orchestration code. This prompt is intended for direct subagent delegation.

## Objective

Refactor the PDF parsing and DFT matching pipeline so the ZIP DFT inventory is the source of truth, `reference` is used only for linking rows to DFT candidates, and full business identity is used for uniqueness and quantity aggregation.

## Scope

Implement the refactor so the pipeline:

* inventories `.dft` files from the ZIP and uses that inventory as the primary match set
* links parsed PDF rows to DFTs through `reference`
* groups rows by full business identity
* aggregates quantities only inside each identity group
* surfaces repeated `reference` values and multi-identity cases explicitly
* updates the parser and web-layer contracts to support DFT-centric matching

## Ownership / Files to Inspect First

* `docs/tasks/16-dft-linking-and-business-identity-refactor.md`
* `TECH_SPEC.md`
* `docs/contracts/pdf-parser-output.md`
* `apps/web/lib/dft-preflight.ts`
* `apps/web/lib/generate-final-pdf.ts`
* `apps/web/app/api/generate/route.ts`
* `packages/python-processing/src/production_drawings/parser.py`
* `experiments/solidedge-reader-poc/ParsedOrderDocument.cs`
* `experiments/solidedge-reader-poc/SheetAnnotation.cs`
* `experiments/solidedge-reader-poc/DraftConversionPipeline.cs`

## Critical Constraints

* Use the ZIP DFT inventory as the source of truth.
* Treat `reference` as the linking key only.
* Use full business identity for uniqueness and aggregation.
* Matching by `reference` must be case-insensitive and explicitly normalized.
* Do not collapse rows that share a `reference` but differ in dimensions, material, or treatment.
* If one DFT maps to multiple business identity groups, make that outcome explicit in preflight and enforce a clear downstream decision.
* Do not hardcode a single narrow reference regexp as the only valid parse gate.
* Preserve provenance needed for debugging and regression analysis.
* Treat `Material` as the full material cell content, including qualifiers.
* Treat `Treatment` as the dedicated last column, not the tail of `Material`.
* Accept bare numeric `reference` values and other plausible shapes from the reference column.
* Keep page-end rows even when the delivery-date cell is empty.

## Extraction Strategy Clarification

The parser should:

* stay column-based instead of regexp-first
* use table structure to locate rows and columns
* preserve `Material` exactly as it appears in the material column
* read `Treatment` only from the final treatment column
* treat delivery date as optional during extraction, so a valid row is not dropped when that cell is blank at page end
* preserve a pending row across a page break when the page ends before `deliveryDate`; allow the next page's first detail block to complete that same row
* allow `reference` values without a forced letter suffix

## Acceptance Criteria

* PDF rows are extracted without depending on one narrow `reference` pattern.
* PDF rows are extracted robustly even when the delivery date is missing on the last page row.
* Rows that split across a page boundary are preserved as one logical row when the next page carries the missing delivery date/description.
* `reference` is used only for linking rows to DFT candidates.
* Uniqueness and quantity aggregation are based on full business identity.
* Same-`reference` rows with different business data are not silently collapsed.
* Multi-identity DFT cases are either supported explicitly or blocked explicitly.
* The preflight and generation contracts reflect the new DFT-first model.

## Final Output Shape

The public JSON should be piece-level only:

```typescript
type PreflightReport = {
  orderNumber: string
  matchedDfts: ResolvedPieceOutput[]
  missingDfts: MissingDftOutput[]
  readyForGeneration: boolean
}

type ResolvedPieceOutput = {
  dftFileName: string
  dftPath: string
  quantity: string
  material: string
  treatment?: string | null
  deliveryDate: string
  dimensions: Record<string, string>
}

type MissingDftOutput = {
  dftFileName: string
  dftPath: string
}
```

`quantity` must be the printable grouped expression, such as `"2 + 2"`, not a computed integer total.
One `ResolvedPieceOutput` entry represents one final resolved piece / DFT output unit.
No row-by-row intermediate lists should be exposed in the external JSON.

## Report-Back Requirements

Report back with:

* the new matching model and any new types or contract fields
* how ZIP inventory, linking, and grouping are separated
* whether multi-identity DFTs are split or blocked
* any parser changes needed to support broader reference shapes
* any contract docs that must be updated to stay consistent
