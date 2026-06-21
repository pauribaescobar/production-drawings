# Final Resolved Piece Output Contract

## Purpose

Define the public JSON contract returned by the preflight/matching pipeline.

This is the final piece-level output only. Internal row-level matching data exists inside the application, but it is not exposed in JSON.

## Contract

```typescript
interface PreflightReport {
  orderNumber: string
  matchedDfts: ResolvedPieceOutput[]
  missingDfts: MissingDftOutput[]
  readyForGeneration: boolean
}

interface ResolvedPieceOutput {
  dftFileName: string
  dftPath: string
  quantity: string
  material: string
  treatment?: string | null
  deliveryDate: string
  dimensions: Record<string, string>
}

interface MissingDftOutput {
  dftFileName: string
  dftPath: string
}
```

## Notes

* `matchedDfts` contains one entry per resolved piece output unit.
* If multiple source rows share the same business identity, they are merged into one entry.
* `quantity` is the printable grouped expression, such as `"2 + 2"`.
* The contract does not expose intermediate row-level fields such as `rowId`, `referenceKey`, `businessIdentityKey`, `rawRowText`, `quantityExpression`, `unmatchedPdfRows`, or `repeatedOrderLines`.
* `dftFileName` and `dftPath` identify which drawing downstream stages must modify.
* A row may begin on one page and complete on the next page when the delivery date and description land in the first detail block of the following page; the parser must preserve that as one logical row.

## Example

```json
{
  "orderNumber": "PC26-04523",
  "matchedDfts": [
    {
      "dftFileName": "4004B.dft",
      "dftPath": "/tmp/drawings/4004B.dft",
      "quantity": "2 + 2",
      "material": "AISI-304",
      "treatment": null,
      "deliveryDate": "29/06/26",
      "dimensions": {
        "A": "10,5",
        "B": "30",
        "C": "4"
      }
    }
  ],
  "missingDfts": [],
  "readyForGeneration": true
}
```
