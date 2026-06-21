# PROJECT_CONTEXT.md

## Context

This project aims to automate the generation of manufacturing drawings from customer orders.

The project is being developed for a metal fabrication workshop where operators currently perform this task manually.

The goal is to eliminate repetitive manual work and reduce human error.

---

## Current Workflow

When a customer places an order, the workshop receives:

1. A PDF containing the order information.
2. A ZIP file containing all associated technical drawings.

The operator must:

1. Open the order.
2. Review each line manually.
3. Identify the corresponding drawing.
4. Determine whether multiple lines represent:

   * the same piece
   * different variants of the same drawing
5. Duplicate drawings when necessary.
6. Manually write manufacturing information on each drawing.
7. Print the resulting drawings.
8. Deliver them to production.

---

## Input Files

### Order PDF

Example:

```text
PC26-04523 EMBEIM.pdf
```

The PDF contains information such as:

* part references
* quantities
* materials
* delivery dates
* treatments
* dimensions

---

### Drawings ZIP

For every order there is an associated ZIP file.

Example:

```text
PDF:
PC26-04523 EMBEIM.pdf

ZIP:
PC26-04523.zip
```

The ZIP name matches the order number.

The only difference is that the PDF contains the suffix:

```text
EMBEIM
```

while the ZIP does not.

---

### ZIP Contents

Inside the ZIP there are two file types:

```text
4004B.dft
4004B010503000040.stp

4081A.dft
4081A06650945.stp

...
```

---

## Important: DFT vs STP

### DFT Files

DFT files represent the technical drawings used by production.

These are the files that the application must process.

All business logic revolves around DFT files.

---

### STP Files

STP files are completely irrelevant to this project.

They happen to be included in the ZIP because they are part of the package delivered by the customer.

They are not used by the workshop during this process.

They are not required for matching.

They are not required for annotation.

They are not required for output generation.

The system must completely ignore STP files.

No analysis, parsing, matching or processing should ever be performed on STP files.

---

## Business Problem

A drawing reference does not necessarily represent a unique manufacturable piece.

Example:

```text
4004B

A=10.5
B=20
C=14
```

and

```text
4004B

A=10.5
B=20
C=12
```

share the same drawing reference but represent different physical pieces.

Therefore reference alone is not sufficient to identify a piece.

---

## Information Written On Drawings

Operators currently write the following information manually:

* quantities
* material
* delivery date
* order number
* treatment
* dimensions

The generated output must contain the same information.

---

## Production Usage

The printed drawings are used directly by workshop workers.

The drawing acts as a work order.

Workers need to immediately see:

* what to manufacture
* how many units
* material
* delivery date
* treatment
* dimensions

without having to consult the original PDF.

---

## Quantity Representation

The workshop prefers preserving quantity groups.

Example:

```text
2 + 2
```

instead of:

```text
4
```

because operators track completion per order line.

This behaviour must be preserved.

---

## Dimension Totals

Some drawings contain multiple dimensions belonging to the same axis.

Example:

```text
A = 6
B = 34
C = 7
```

This is a future rule. It is currently deferred from the first implementation.

---

## Project Goal

The desired workflow is:

```text
PDF + ZIP
     ↓
Upload
     ↓
Automatic processing
     ↓
Generated drawings
     ↓
Print
     ↓
Production
```

The user should only need to:

1. Upload the PDF.
2. Upload the ZIP.
3. Click Generate.
4. Print the resulting drawings.

---

## Success Criteria

The solution is successful if it can replace the current manual process while preserving all information required by production.

The generated drawings must be ready to print and use directly on the workshop floor.
