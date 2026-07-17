# 13 - Annotation Layout Implementation Prompt

Derived from `docs/tasks/13-annotation-layout-implementation.md`, `TECH_SPEC.md`, and the worker experiment. This prompt is intended for direct subagent delegation.

## Objective

Implement the concrete annotation layout defined in `TECH_SPEC.md` so the worker draws the business data in a fixed, readable block on the generated PDF.

## Scope

Implement the layout contract for the annotation block, including:

* overlaying values directly onto the drawing area, without a detached right-hand block
* the treatment line highlight
* dimension values anchored to the existing callouts
* quantity, material, delivery date, and order number as value-only overlays
* omission of the piece reference from the block

## Ownership / Files to Inspect First

* `TECH_SPEC.md`
* `docs/tasks/13-annotation-layout-implementation.md`
* `docs/tasks/10-annotation-and-pdf-final.md`
* `experiments/solidedge-reader-poc/README.md`
* `experiments/solidedge-reader-poc/SheetAnnotation.cs`
* `experiments/solidedge-reader-poc/MicrosoftPrintToPdfWriter.cs`
* `experiments/solidedge-reader-poc/ParsedOrderDocument.cs`
* `examples/example_result2.jpeg`
* `apps/web/lib/generate-final-pdf.ts`

## Critical Constraints

* Follow the ordering and visual rules in `TECH_SPEC.md`.
* The treatment line must stand out more than the rest of the block.
* The block must be legible in black and white.
* Do not invent extra annotations.
* Do not duplicate the piece reference in the block.
* Omit missing dimensions instead of inventing placeholders.
* Do not redraw the callout letters or add placeholder labels such as `QUANTITY` or `MATERIAL`.
* Keep the layout recognizably close to the product example.

## Acceptance Criteria

* The worker renders a concrete annotation block instead of placeholder text.
* The block includes treatment, dimensions, quantity, material, delivery date, and order number.
* The block is placed and styled according to the spec.
* The piece reference is absent from the annotation block.
* No placeholder field labels or repeated callout letters are drawn.
* The output is usable as the standard template for real drawings.

## Report-Back Requirements

Report back with:

* the final block structure and ordering
* how treatment is highlighted
* how missing dimensions are handled
* any changes needed in worker data shaping
* any mismatch between the example image and the implemented spec
