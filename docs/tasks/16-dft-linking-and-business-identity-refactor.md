# 16 - DFT Linking and Business Identity Refactor

## Goal

Refactor the PDF parsing and DFT matching pipeline so that:

* the ZIP inventory of `.dft` files is the primary driver of matching
* `reference` is used only to link PDF rows to DFT candidates
* full business identity is used for uniqueness and quantity aggregation

Business identity is defined as:

* `reference`
* `dimensions`
* `material`
* `treatment`

The refactor must reduce false negatives caused by narrow PDF parsing assumptions and preserve repeated rows without collapsing distinct pieces that share the same `reference`.

Extraction clarification from sample pages:

* `Material` is the full material cell content, including qualifiers such as `CERTIFICADO`
* `Treatment` comes from its own dedicated last column, not from the tail of `Material`
* `reference` accepts bare numeric values, alphanumeric values, and values with hyphens such as `733-8`
* `reference` accepts other plausible shapes that appear in the reference column
* the parser must remain column-based, not regexp-first
* a row at page end must still be captured even if the delivery-date cell is empty
* if a row breaks across a page boundary, keep it pending when the first page ends before `deliveryDate` and let the next page's first detail block complete the same row

## Why this exists

The current pipeline is too restrictive in two places:

* the parser depends on a narrow row pattern and can miss valid PDF rows
* the matcher treats `reference` too much like a unique piece identifier, which does not reflect the business rules

The system needs a model where:

* DFT filenames define the set of drawing candidates
* PDF rows are extracted more robustly from table data
* rows link to DFTs through `reference`
* rows are grouped and aggregated only by full business identity
* if a PDF row references a DFT that is not present in the ZIP, the user must be warned clearly and generation must not proceed as if that piece existed

## Key design decision

One DFT may link to multiple business identity groups.

That is valid at the matching level because the DFT stem and the PDF `reference` column are only the linking mechanism. The implementation must make an explicit downstream decision between:

1. allowing one DFT to produce multiple generation units, one per business identity group
2. treating multiple business identity groups under one DFT as a blocking conflict for annotation or final generation

This decision must be encoded in the preflight output and enforced consistently in final generation.

## Scope

This task covers:

* inventorying `.dft` files from the uploaded ZIP and using them as the primary match set
* extracting PDF order rows more robustly from the table structure
* treating `reference` as a column value rather than a fixed narrow pattern
* supporting multiple plausible reference shapes without making any one shape the sole parse gate
* grouping matched rows by full business identity
* aggregating quantities only within a business identity group
* surfacing repeated `reference` values that represent multiple business identity groups
* updating the parser and web-layer contracts to support DFT-centric matching
* documenting and testing the new behavior

This task does not require:

* changing the Windows worker executable itself
* changing unrelated frontend flows
* changing unrelated parser behavior outside the order-table extraction and matching path

## Inputs

The refactored pipeline continues to accept:

* `pdf`: the uploaded order PDF
* `zip`: the uploaded ZIP containing drawings

## Required matching model

The implementation must separate these concepts:

### Linking key

`reference` is the linking key between:

* the PDF order table row
* the `.dft` file stem

Matching by `reference` must be case-insensitive and normalization must be explicit and documented.

### Uniqueness and aggregation key

Full business identity is the uniqueness key:

* `reference`
* `dimensions`
* `material`
* `treatment`

Rows with the same `reference` but different dimensions, material, or treatment must not be collapsed into one piece.

Important extraction note:

* `reference` remains the linking key only
* `Material` is the full material column value
* `Treatment` is sourced from the final column
* delivery date is optional at the row level during extraction and cannot be used as the sole gate for row validity

## Reference handling requirements

The parser must not hardcode a single narrow regexp as the only valid shape for `reference`.

It should support known shapes such as:

* bare numeric references
* alphanumeric references
* hyphenated references such as `733-8`
* 4 digits + 1 letter
* 5 digits + 2 letters + optional number
* 5 digits + 2 letters
* 5 digits + 1 letter
* other plausible variants that still appear in the `reference` column

The parser should use table extraction and column assignment first, and only use pattern checks as soft validation or classification.
It must not require a forced letter suffix on numeric references.

### Table extraction robustness

The parser must:

* keep page-end rows even when the delivery-date cell is empty
* treat the presence of the row structure as sufficient for capture
* avoid using the delivery date cell as a hard stop for row extraction
* preserve page-break continuations instead of turning them into new rows

## Contracts
The external contract should be piece-level only and printable directly.

### Final output

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

Quantity is a printable grouped expression, such as `"2 + 2"`, not a computed integer total.

One `ResolvedPieceOutput` entry represents one final resolved piece / DFT output unit.
If multiple source rows share the same business identity, they must be merged into one output entry before emission.

## Business rules

The implementation must enforce these rules:

1. Same `reference`, same dimensions, same material, same treatment:
   * same business identity
   * quantities aggregate

2. Same `reference`, different dimensions or material or treatment:
   * different business identities
   * do not aggregate together

3. Same DFT stem linked to multiple business identities:
   * surface explicitly in preflight
   * either split into multiple generation units or block generation, depending on downstream support

4. Delivery date is not part of the uniqueness key:
   * preserve it per source row
   * preserve the set of delivery dates per identity group
   * if delivery dates differ within one identity group, raise a warning or conflict according to downstream requirements

5. Missing DFT referenced by a PDF row:
   * surface the missing piece to the user
   * treat the job as incomplete for generation purposes
   * do not silently drop the referenced piece from the output

## Affected implementation areas

### `packages/python-processing/src/production_drawings/parser.py`

Required changes:

* replace narrow line-pair parsing with row-oriented table extraction
* locate the full order table more robustly
* treat `reference` as a column value, not as the proof that a row is valid
* emit parsed rows with both `referenceKey` and `businessIdentityKey`
* preserve source provenance needed for debugging and regression analysis

### `apps/web/lib/dft-preflight.ts`

Required changes:

* make ZIP `.dft` inventory the primary match set
* build lookup by DFT stem and lookup by parsed row `referenceKey`
* match DFTs to rows by `referenceKey`
* group matched rows by `businessIdentityKey`
* aggregate quantities only inside each business identity group
* report unmatched DFTs, unmatched PDF rows, duplicate DFTs, and multi-identity cases
* expose the downstream conflict decision in the report

### `apps/web/lib/generate-final-pdf.ts`

Required changes:

* consume the refactored DFT-centric match plan rather than a flat row-first match list
* write the resolved generation input needed by later stages
* fail fast on blocking identity conflicts before invoking the worker
* support either:
  * one worker execution per DFT with multiple identity groups, or
  * one worker execution per generation unit derived from a DFT

### Related contracts and documentation

Required changes:

* add or update contract docs for parsed PDF rows and DFT-resolved payloads
* keep this task self-contained so implementers do not need prior completed task docs to understand the target behavior

## Rollout phases

### Phase 1 - Establish new contracts

* define row-level and DFT-level types
* add a compatibility layer if existing code still expects the older parser output
* decide whether downstream supports one DFT with multiple identity groups

### Phase 2 - Refactor PDF extraction

* implement robust table extraction in the Python parser
* validate with representative fixtures that include multiple reference shapes
* ensure parser output preserves all rows that belong to the order table

### Phase 3 - Refactor matching and preflight

* switch matching to DFT-first inventory traversal
* group rows by business identity after linking by `reference`
* update preflight reporting to show identity groups and conflicts explicitly

### Phase 4 - Refactor final generation

* change generation to consume the new resolved payload
* block or split multi-identity DFTs according to the design decision
* keep missing or ambiguous cases visible and deterministic

### Phase 5 - Regression coverage and cleanup

* add fixtures and automated tests
* compare old and new outputs on representative samples
* remove obsolete row-first assumptions once the new path is verified

## Testing requirements

Add tests that cover at least:

* valid rows missed by the current narrow parser
* multiple `reference` shapes in one PDF
* repeated rows with the same full business identity and additive quantities
* repeated `reference` values with different dimensions, material, or treatment
* ZIP inventories with duplicate `.dft` stems
* DFTs present in the ZIP but missing matching PDF rows
* PDF rows with no corresponding DFT
* delivery-date variation within one business identity group

## Acceptance criteria

The task is complete when:

* every `.dft` in the ZIP is accounted for in preflight
* PDF rows are extracted without depending on one narrow `reference` regexp
* `reference` is used only for linking rows to DFT candidates
* uniqueness and quantity aggregation are based on full business identity
* same-`reference` rows with different dimensions, material, or treatment are not silently collapsed
* multi-identity DFT cases are either supported explicitly or blocked explicitly
* final generation consumes the refactored resolved payload rather than the old row-first match model
