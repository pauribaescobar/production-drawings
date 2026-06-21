# 13 - Annotation Layout Implementation

## Goal

Implement the concrete annotation layout defined in `TECH_SPEC.md` so the worker draws the business data in a fixed, readable block on the generated PDF.

## Why this exists

The worker now has a final PDF path, but the annotations need a real layout contract instead of placeholder or generic text.

## Inputs

* `TECH_SPEC.md`
* `docs/tasks/10-annotation-and-pdf-final.md`
* `examples/example_result2.jpeg`
* `experiments/solidedge-reader-poc/`

## Scope

* render the annotation block in the upper-right / right-hand area of the sheet
* highlight the treatment line with a yellow marker-style background
* render dimensions as labeled value pairs
* render quantity, material, delivery date, and order number in the defined order
* exclude the piece reference from the block because it comes from the DFT filename

## Required content

The block must include:

* treatment
* dimensions
* quantity
* material
* delivery date
* order number

## Visual rules

* treatment must stand out visually
* the block must be legible in black and white
* keep a clean box layout with spacing between rows
* if a dimension is missing, omit that row
* do not invent extra annotations

## Acceptance criteria

* the worker draws a concrete annotation block instead of generic placeholder text
* the layout matches the product example closely enough to be recognizable
* the piece reference is not duplicated in the block
* the block is usable as the standard template for real drawings

