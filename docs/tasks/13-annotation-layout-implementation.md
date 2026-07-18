# 13 - Annotation Layout Implementation

## Goal

Implement the concrete annotation layout defined in `TECH_SPEC.md` so the worker overlays the business data directly on top of the original drawing in a fixed, readable way.

## Why this exists

The worker now has a final PDF path, but the annotations need a real layout contract instead of placeholder or generic text.
The current output should be treated as the negative reference (`final.pdf`) and the annotated target as the positive reference (`final correcto.pdf`).

## Inputs

* `TECH_SPEC.md`
* `docs/tasks/10-annotation-and-pdf-final.md`
* `examples/example_result2.jpeg`
* `experiments/solidedge-reader-poc/`

## Scope

* overlay the annotation text on top of the original plan, not as a detached side panel
* place the text inside the drawing area at the same relative position shown in the reference mock
* keep the original drawing visible and do not add a new right-hand rectangle or separate page block
* add text only, never cover or replace existing drawing content
* never move or reflow existing plan content to make room for annotations
* dimension callouts in the source drawing are blue; the layout should attach each extracted value next to its matching callout
* when a dimension letter appears multiple times, anchor to the instance with the highest `x` coordinate, which is the one closest to the intended center-right annotation area
* do not redraw the callout letter itself; only render the resolved value next to the existing callout in the drawing
* highlight the treatment line with a yellow marker-style background
* render dimensions as inline value overlays adjacent to their matching callouts
* render quantity, material, delivery date, and order number as value-only overlays, without drawing placeholder labels like `QUANTITY`, `MATERIAL`, `DELIVERY DATE`, or `ORDER NUMBER`
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
* keep the text compact and aligned with the existing drawing composition
* locate the dimension labels already present in the PDF and write the corresponding values next to them
* if a value would overlap existing drawing text, shift the annotation into the nearest free area without covering the source drawing
* do not render empty placeholder boxes, blank lines, or field labels under the values
* if a dimension is missing, omit that row
* do not invent extra annotations

## Placement rules

Use relative zones per field type instead of one generic free-space search:

* `treatment`
  * upper-right free area of the drawing
  * tight yellow highlight around the value
  * never cover the drawing border or title block

* `dimensions`
  * next to the matching blue callout
  * if the same letter appears multiple times, choose the instance with the highest `x` coordinate
  * render only the resolved value, not the callout letter

* `quantity`
  * lower-right free region above the title block
  * its own dedicated zone

* `material`
  * dedicated zone in the right-side summary column, separated from `quantity`

* `delivery date`
  * dedicated zone below `quantity` and `material`

* `order number`
  * dedicated zone below `delivery date`
  * keep it aligned with the summary column, but not overlapping the other fields

## Acceptance criteria

* the worker overlays concrete annotation text instead of generic placeholder text
* the layout matches the annotated example closely enough to be recognizable
* the piece reference is not duplicated in the block
* no annotation text obscures the original drawing text
* dimensions are rendered as values attached to their matching labels rather than as a detached table
* no placeholder labels or rows are drawn under the annotation values
* each summary field occupies its own relative zone and does not overlap the others
* the block is usable as the standard template for real drawings
