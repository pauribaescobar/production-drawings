# BUSINESS_RULES.md

## Purpose

The system must generate production-ready drawings from customer orders.

The generated drawings must contain the same information that workshop operators currently write manually.

---

# Rule 1 - Input

Each order consists of:

* one PDF file
* one ZIP file containing drawings

The PDF contains the order information.

The ZIP contains the technical drawings.

---

# Rule 2 - Relevant Files

Only DFT files are relevant.

Example:

```text
4004B.dft
4081A.dft
14191P.dft
```

STP files must be ignored.

Example:

```text
4004B010503000040.stp
4081A06650945.stp
```

STP files have no business value in this process.

They must not participate in matching, grouping or output generation.

---

# Rule 3 - Piece Identity

A drawing reference alone does not uniquely identify a piece.

The identity of a manufacturable piece is:

```text
reference
+
dimensions
+
material
+
treatment
```

Two order lines represent the same piece only if all four elements match.

---

# Rule 4 - Different Dimensions

If two order lines share the same drawing reference but contain different dimensions, they must generate different drawing outputs.

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

must generate two different outputs.

---

# Rule 5 - Grouping

Order lines may be grouped only when all of the following are equal:

* reference
* dimensions
* material
* treatment

If any of those values differ, a new drawing output must be created.

---

# Rule 6 - Quantity Representation

Grouped quantities must preserve the original order-line structure.

Example:

Input:

```text
Line 1 -> quantity 2
Line 2 -> quantity 2
```

Output:

```text
2 + 2
```

Not:

```text
4
```

This behaviour is required because workshop operators track completion per order line.

---

# Rule 7 - Required Information

Every generated drawing must contain:

* quantity
* material
* delivery date
* order number

---

# Rule 8 - Optional Information

When available, the drawing must also contain:

* treatment
* dimensions

---

# Rule 9 - Treatments

Treatments must be clearly visible.

Examples:

```text
Pulido
Electropulido
```

Treatment information is considered critical for manufacturing.

---

# Rule 10 - Materials

Material information must always be displayed.

Examples:

```text
Inox 304
Inox 316L
F1
F14
Poliéster
```

---

# Rule 11 - Special Material

When the material is:

```text
Inox 316L
```

it must be visually highlighted with a fluorescent yellow underline.

This reflects the workshop's existing process and helps avoid manufacturing mistakes.

---

# Rule 12 - Dimensions

When dimensions exist in the order, they must be written onto the drawing.

Example:

```text
A = 20
B = 36
C = 50
```

---

# Rule 13 - Pieces Without Dimensions

Some pieces do not contain variable dimensions.

In these cases:

* no dimensions should be added
* the drawing should still contain all other applicable information

---

# Rule 14 - Dimension Totals

This rule is currently out of scope for the first implementation.

Example:

```text
A = 6
B = 34
C = 7
```

The first version will not apply the total-dimension logic.

---

# Rule 15 - Order Number

The order number must always be displayed.

Example:

```text
PC26-04523
```

The workshop uses this information to locate the originating order.

---

# Rule 16 - Delivery Date

The delivery date must always be displayed.

Example:

```text
09/06/2026
```

The workshop uses this information to prioritize manufacturing.

---

# Rule 17 - Output

For each unique piece identity:

```text
reference
+
dimensions
+
material
+
treatment
```

the system must generate exactly one drawing output.

---

# Rule 18 - Production Ready

The generated drawings must contain all information required for a workshop operator to manufacture the piece without consulting the original order PDF.
