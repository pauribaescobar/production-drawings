# TECH_SPEC.md

## Objective

Build a web application capable of generating production-ready PDFs from:

* an order PDF
* a ZIP containing DFT drawings

The application must apply all business rules defined in `BUSINESS_RULES.md`.

---

# Technology Stack

## Frontend

```text
Next.js
React
TypeScript
```

Purpose:

* file upload
* processing status
* download generated PDFs

---

## Backend

```text
Next.js Route Handlers
TypeScript
```

Purpose:

* orchestrate processing pipeline
* parse uploaded files
* generate output

No separate Express server is required.

---

## Processing Layer

Python may be used for:

* PDF parsing
* CAD processing
* drawing rendering

The preferred implementation is TypeScript whenever possible for the web layer.

The Windows worker remains a .NET component.

---

# Input

## Order PDF

Example:

```text
PC26-04523 EMBEIM.pdf
```

Contains:

* references
* quantities
* materials
* treatments
* dimensions
* delivery dates

---

## Drawings ZIP

Example:

```text
PC26-04523.zip
```

Contains:

```text
4004B.dft
4081A.dft
14191P.dft
...
```

and

```text
4004B010503000040.stp
4081A06650945.stp
...
```

---

# Important

STP files are ignored completely.

They are not part of the processing pipeline.

They must not be parsed, analyzed or matched.

---

# Desired Output

The desired output is a single PDF document.

Example:

```text
PC26-04523-GENERATED.pdf
```

The PDF should contain all generated drawings for the order.

Each page represents one generated drawing.

---

# High-Level Architecture

```text
Upload PDF
       +
Upload ZIP

       ↓

Extract ZIP

       ↓

Read PDF

       ↓

Parse Order Lines

       ↓

Group Pieces

       ↓

Locate Corresponding DFT

       ↓

Render Drawing

       ↓

Apply Annotations

       ↓

Generate Final PDF

       ↓

Download
```

## Runtime Topology

The logical pipeline is the same in development and production, but the execution host can differ.

### Local development

```text
Mac dev machine
  ├─ Next.js UI
  ├─ Next.js API route handlers
  ├─ Python PDF parser
  └─ optional Windows conversion host for manual testing
```

### Remote deployment

```text
User browser
  ↓
Public web app on Windows
  ↓
Upload accepted + job workspace created
  ↓
Local parser/orchestrator
  ↓
Local Windows conversion worker
  ↓
EMF export / annotation / PDF generation
  ↓
Result returned to the web app
```

The production model in this repository is a single Windows host that runs the public web app, the parser/orchestrator, and the Windows conversion worker.

The public surface is only the web application. The worker is private to the host and is invoked over a local boundary.

### Public self-hosted deployment

If the whole system is deployed on a single Windows machine, the public surface should be the web application only.

```text
Internet
  ↓
Public web app on Windows
  ↓
Local Windows conversion worker
  ↓
EMF export / annotation / PDF generation
```

In this shape:

* the browser talks to the public web app
* the web app invokes the worker locally on the same machine
* the worker remains private to that host

The recommended internal boundary is `localhost` HTTP on a non-public port. If the implementation later uses a process call or local queue instead, the public contract should remain the same: the web app submits a job, the worker writes the final PDF into a known output path, and the web app returns that file to the browser.

This is the simplest deployment model if you want the complete system to live on one Windows box.

## Deployment Model

The first production target is a single Windows host that runs:

* the public web application
* the PDF parser/orchestrator
* the DFT conversion worker
* a shared job workspace on local disk

### Runtime contract

The web app and worker communicate through a narrow internal contract:

* input: `jobId`, uploaded order PDF path, drawings ZIP path, parsed order JSON path, and output directory
* output: generated PDF path, job status, and structured error details if processing fails

The parser/orchestrator owns the `ParsedOrder` JSON defined in `docs/contracts/pdf-parser-output.md`. The worker should consume that parsed data rather than reparsing the source PDF.

### Filesystem layout

The single-host deployment should keep all job artifacts on local disk under a predictable root, for example:

```text
C:\production-drawings\
  jobs\
    <jobId>\
      input\
        order.pdf
        drawings.zip
      parsed-order.json
      output\
        generated.pdf
      logs\
        worker.log
```

The exact root path can vary by environment, but the relative shape should stay stable.

### Request flow

```text
Browser uploads files
  ↓
Web app writes a job workspace
  ↓
Web app parses the order PDF
  ↓
Web app calls the local worker
  ↓
Worker reads the job workspace
  ↓
Worker renders drawings and writes generated.pdf
  ↓
Web app verifies the output and returns the PDF download
```

The worker should be implemented so it can later be moved out of process without changing the public web contract.

The first deployment implementation task is not the full cloud rollout; it is the wiring that makes the worker callable from the web app on the same Windows host.

---

# Core Components

## Order Parser

Responsible for extracting:

* reference
* quantity
* material
* treatment
* dimensions
* delivery date
* order number

Output:

```typescript
interface OrderLine {
  reference: string
  quantity: number
  material: string
  treatment?: string
  deliveryDate: string
  orderNumber: string
  dimensions: Record<string, string>
}
```

---

## Piece Grouper

Responsible for applying business grouping rules.

Identity:

```text
reference
+
dimensions
+
material
+
treatment
```

Output:

```typescript
interface GroupedPiece {
  identity: string
  lines: OrderLine[]
}
```

---

## Drawing Locator

Responsible for finding the matching DFT.

Example:

```text
4004B -> 4004B.dft
```

---

## Drawing Renderer

Responsible for transforming a DFT into a renderable format.

This is currently the biggest technical uncertainty.

Preferred intermediate format for the first working path:

```text
DFT
 ↓
EMF
 ↓
Annotate
 ↓
PDF
```

EMF is the preferred intermediate because it is vector-based and can preserve drawing fidelity better than raster formats.

If the final pipeline can generate PDF directly after reading the DFT, that is acceptable, but the worker should still keep the rendering stage isolated as a replaceable component.

## PDF Finalization

The preferred implementation for final PDF generation is a managed .NET PDF library inside the Windows worker.

Current recommendation:

* use `Syncfusion.Pdf` for the PDF finalization and annotation stage
* keep `Microsoft Print to PDF` only as a fallback when the library path is unavailable

Why:

* it keeps the full pipeline inside the existing C# worker
* it avoids a machine-level dependency on the Windows print feature
* it allows annotations to be written directly into the PDF structure
* it keeps the output deterministic and easier to test in automation

The PDF finalizer should take the worker's intermediate drawing output and emit the final PDF as the primary artifact for the job.

---

# Open Technical Question

The project currently does not know how DFT files should be processed.

The first implementation task is to investigate:

* what DFT format is being used
* whether DFT files can be read programmatically
* whether DFT files can be rendered directly
* whether DFT files can be exported automatically
* whether Solid Edge automation is required

The preferred hypothesis is to start by exploring option A, but this decision is still pending validation.

Current evidence suggests that `.dft` is a Solid Edge Draft document stored as an OLE/Compound Document file, so a generic parser is unlikely to be enough. The next step is to validate export or rendering via Solid Edge automation or a Solid Edge-specific reader.

---

# Candidate Approaches

## Option A

```text
DFT
 ↓
Render
 ↓
Annotate
 ↓
PDF
```

Preferred option.

---

## Option B

```text
DFT
 ↓
DXF
 ↓
Annotate
 ↓
PDF
```

Fallback option.

---

## Option C

```text
DFT
 ↓
Solid Edge Automation
 ↓
PDF
 ↓
Annotate
```

Fallback option.

---

# Annotation Engine

Responsible for placing:

* quantity
* material
* treatment
* delivery date
* order number
* dimensions

onto the rendered drawing.

Preferred implementation:

* generate PDF annotations directly in the worker using the managed PDF library
* avoid post-processing the final PDF in a separate runtime unless later requirements force that split

## Annotation Layout

The worker should draw the business data in a fixed annotation block, not as scattered free text.

The lower reference block from the example sketch is out of scope for the first implementation because the DFT file name already carries the piece reference.

### Fixed block

Use a large annotation block that occupies the visible right-hand / upper-right area of the sheet, away from the main drawing.

The block should contain, in this order:

1. **Treatment**
   * shown at the top of the block
   * highlighted with a yellow marker-style background
   * uppercase text is acceptable if that matches the source data

2. **Dimensions**
   * rendered as labeled value pairs such as `A 20`, `B 36`, `C 50`
   * labels should remain visible even if the values are numeric only
   * if a dimension is missing, omit that row rather than inventing data

3. **Quantity**
   * rendered as a single number or quantity expression
   * should match the grouped quantity policy from the business rules

4. **Material**
   * rendered as plain text
   * this can be adjacent to or below quantity if space requires

5. **Delivery date**
   * rendered in a readable date format
   * should be visually distinct from the material/quantity row

6. **Order number**
   * rendered near the bottom of the block
   * this is the order-level identifier

### Visual treatment

* The treatment line must stand out visually more than the rest of the block.
* Dimension values may be emphasized with a circle, badge, or similar lightweight highlight if that fits the available space.
* The block should be legible when printed in black and white.
* Use a clean box layout with clear spacing between rows.

### Explicit exclusions

* Do not place the piece reference in the block if it can be derived from the DFT file name.
* Do not recreate the handwritten-looking lower panel from the example unless the business rules later require it.
* Do not invent additional annotations beyond the required business data.

---

# Quantity Formatting

Must follow business rules.

Example:

```text
2 + 2 + 1
```

Not:

```text
5
```

---

# Dimension Totals

This rule is currently deferred from the first implementation.

Example:

```text
A = 6
B = 34
C = 7
```

The first version will not generate a total-dimension annotation.

---

# Testing Strategy

## Phase 1

Validate DFT processing.

Goal:

Determine the viable rendering approach.

---

## Phase 2

Implement PDF parser.

Create tests using real order PDFs.

---

## Phase 3

Implement grouping logic.

Use TDD.

Business rules should be fully covered by automated tests.

---

## Phase 4

Implement annotation generation.

Validate against the workshop examples.

---

## Phase 5

Generate final PDF.

Validate with real workshop orders.

---

# Performance Goal

A typical order should be processed in a few seconds.

The user should be able to:

1. Upload files.
2. Click Generate.
3. Download the resulting PDF.

without noticeable waiting times.
