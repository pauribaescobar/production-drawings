import path from "node:path";

export type ParsedOrderRow = {
  rowId: string;
  lineNumber: number;
  referenceRaw: string;
  referenceKey: string;
  quantity: number;
  material: string;
  treatment?: string | null;
  dimensions: Record<string, string>;
  deliveryDate: string;
  businessIdentityKey: string;
  sourcePage?: number;
  rawRowText?: string;
};

export type ParsedOrder = {
  orderNumber: string;
  rows: ParsedOrderRow[];
  lines?: ParsedOrderRow[];
};

export type DftInventoryItem = {
  dftKey: string;
  pieceName: string;
  fileName: string;
  dftPath: string;
  relativePath: string;
};

export type BusinessIdentityGroup = {
  businessIdentityKey: string;
  referenceKey: string;
  referenceRaw: string;
  sourceRowIds: string[];
  sourceRows: ParsedOrderRow[];
  totalQuantity: number;
  quantityExpression: string;
  material: string;
  treatment?: string | null;
  dimensions: Record<string, string>;
  deliveryDates: string[];
  warnings: string[];
};

export type DftResolvedPayload = {
  dftKey: string;
  pieceName: string;
  fileName: string;
  dftPath: string;
  relativePath: string;
  identityGroups: BusinessIdentityGroup[];
  warnings: string[];
  conflictStatus: "none" | "multiple-identities" | "ambiguous-match";
};

export type IgnoredZipFileReport = {
  fileName: string;
  relativePath: string;
  reason: "stp" | "unsupported-file" | "duplicate-dft" | "unused-dft";
};

export type DuplicateDftReport = {
  dftKey: string;
  fileNames: string[];
  relativePaths: string[];
};

export type RepeatedReferenceReport = {
  referenceKey: string;
  referenceRaw: string;
  count: number;
  lineNumbers: number[];
  businessIdentityKeys: string[];
};

export type DftPreflightReport = {
  orderNumber: string;
  dftInventory: DftInventoryItem[];
  parsedRows: ParsedOrderRow[];
  resolvedDfts: DftResolvedPayload[];
  unmatchedPdfRows: ParsedOrderRow[];
  unmatchedDfts: DftInventoryItem[];
  duplicateDfts: DuplicateDftReport[];
  ignoredZipFiles: IgnoredZipFileReport[];
  repeatedReferences: RepeatedReferenceReport[];
  warnings: string[];
  blockingReasons: string[];
  readyForGeneration: boolean;
  parsedOrderLines: ParsedOrderRow[];
  matchedDfts: LegacyMatchedDftReport[];
  missingDfts: LegacyMissingDftReport[];
  extraFilesIgnored: IgnoredZipFileReport[];
  repeatedOrderLines: RepeatedReferenceReport[];
};

export type LegacyMatchedDftReport = {
  dftKey: string;
  dftFileName: string;
  dftPath: string;
  dftRelativePath: string;
  reference: string;
  referenceKey: string;
  businessIdentityKey: string;
  lineNumbers: number[];
  totalQuantity: number;
};

export type LegacyMissingDftReport = {
  dftKey: string;
  fileName: string;
  dftPath: string;
  relativePath: string;
  reason: "missing-from-pdf";
};

export type GenerationUnit = {
  dft: DftInventoryItem;
  identityGroup: BusinessIdentityGroup;
  sourceRowIds: string[];
};

export type DrawingMatchPlan = {
  report: DftPreflightReport;
  generationUnits: GenerationUnit[];
};

type ZipInventoryEntry = {
  path: string;
  relativePath: string;
  fileName: string;
  extension: string;
  stem: string;
};

function normalizeValue(value: string): string {
  return value.trim().toLowerCase();
}

function normalizeDimensions(dimensions: Record<string, string>): string {
  return Object.entries(dimensions)
    .map(([axis, value]) => `${normalizeValue(axis)}=${normalizeValue(value)}`)
    .sort()
    .join("|");
}

function buildReferenceKey(reference: string): string {
  return normalizeValue(reference);
}

function buildBusinessIdentityKey(
  referenceKey: string,
  dimensions: Record<string, string>,
  material: string,
  treatment?: string | null,
): string {
  return [
    referenceKey,
    normalizeDimensions(dimensions),
    normalizeValue(material),
    normalizeValue(treatment ?? ""),
  ].join("::");
}

function getZipInventoryEntry(rootDir: string, filePath: string): ZipInventoryEntry {
  const fileName = path.basename(filePath);
  const extension = path.extname(fileName).toLowerCase();

  return {
    path: filePath,
    relativePath: path.relative(rootDir, filePath) || fileName,
    fileName,
    extension,
    stem: normalizeValue(path.basename(fileName, path.extname(fileName))),
  };
}

function buildDftInventory(entries: ZipInventoryEntry[]): {
  dftInventory: DftInventoryItem[];
  duplicateDftGroups: DuplicateDftReport[];
  ignoredZipFiles: IgnoredZipFileReport[];
} {
  const dftInventory: DftInventoryItem[] = [];
  const ignoredZipFiles: IgnoredZipFileReport[] = [];
  const dftGroups = new Map<string, DftInventoryItem[]>();

  for (const entry of entries) {
    if (entry.extension === ".stp") {
      ignoredZipFiles.push({
        fileName: entry.fileName,
        relativePath: entry.relativePath,
        reason: "stp",
      });
      continue;
    }

    if (entry.extension !== ".dft") {
      ignoredZipFiles.push({
        fileName: entry.fileName,
        relativePath: entry.relativePath,
        reason: "unsupported-file",
      });
      continue;
    }

    const dftItem: DftInventoryItem = {
      dftKey: entry.stem,
      pieceName: path.basename(entry.fileName, path.extname(entry.fileName)),
      fileName: entry.fileName,
      dftPath: entry.path,
      relativePath: entry.relativePath,
    };
    dftInventory.push(dftItem);

    const group = dftGroups.get(dftItem.dftKey) ?? [];
    group.push(dftItem);
    dftGroups.set(dftItem.dftKey, group);
  }

  const duplicateDftGroups = Array.from(dftGroups.entries())
    .filter(([, items]) => items.length > 1)
    .map(([dftKey, items]) => ({
      dftKey,
      fileNames: items.map((item) => item.fileName),
      relativePaths: items.map((item) => item.relativePath),
    }));

  return {
    dftInventory,
    duplicateDftGroups,
    ignoredZipFiles,
  };
}

function groupRowsByReference(rows: ParsedOrderRow[]): Map<string, ParsedOrderRow[]> {
  const grouped = new Map<string, ParsedOrderRow[]>();

  for (const row of rows) {
    const key = row.referenceKey;
    const existing = grouped.get(key) ?? [];
    existing.push(row);
    grouped.set(key, existing);
  }

  return grouped;
}

function buildRepeatedReferenceReports(rows: ParsedOrderRow[]): RepeatedReferenceReport[] {
  const grouped = new Map<
    string,
    {
      referenceRaw: string;
      lineNumbers: number[];
      businessIdentityKeys: Set<string>;
    }
  >();

  for (const row of rows) {
    const existing = grouped.get(row.referenceKey);
    if (existing) {
      existing.lineNumbers.push(row.lineNumber);
      existing.businessIdentityKeys.add(row.businessIdentityKey);
      continue;
    }

    grouped.set(row.referenceKey, {
      referenceRaw: row.referenceRaw,
      lineNumbers: [row.lineNumber],
      businessIdentityKeys: new Set([row.businessIdentityKey]),
    });
  }

  return Array.from(grouped.entries())
    .filter(([, group]) => group.lineNumbers.length > 1)
    .map(([referenceKey, group]) => ({
      referenceKey,
      referenceRaw: group.referenceRaw,
      count: group.lineNumbers.length,
      lineNumbers: group.lineNumbers,
      businessIdentityKeys: Array.from(group.businessIdentityKeys),
    }));
}

function buildUnmatchedPdfWarnings(rows: ParsedOrderRow[]): string[] {
  return rows.map(
    (row) => `La línea ${row.lineNumber} (${row.referenceRaw}) no tiene plano en el ZIP.`,
  );
}

function buildBusinessIdentityGroup(rows: ParsedOrderRow[]): BusinessIdentityGroup {
  const [firstRow] = rows;
  const deliveryDates = Array.from(new Set(rows.map((row) => row.deliveryDate.trim()).filter(Boolean)));
  const totalQuantity = rows.reduce((sum, row) => sum + row.quantity, 0);
  const quantityExpression =
    rows.length === 1
      ? String(totalQuantity)
      : `${rows.map((row) => String(row.quantity)).join(" + ")} = ${totalQuantity}`;
  const warnings: string[] = [];

  if (deliveryDates.length > 1) {
    warnings.push("Delivery dates differ inside this business identity group.");
  }

  return {
    businessIdentityKey: firstRow.businessIdentityKey,
    referenceKey: firstRow.referenceKey,
    referenceRaw: firstRow.referenceRaw,
    sourceRowIds: rows.map((row) => row.rowId),
    sourceRows: rows,
    totalQuantity,
    quantityExpression,
    material: firstRow.material,
    treatment: firstRow.treatment,
    dimensions: firstRow.dimensions,
    deliveryDates,
    warnings,
  };
}

function buildResolvedDftPayload(
  dft: DftInventoryItem,
  linkedRows: ParsedOrderRow[],
  hasDuplicateStem: boolean,
): DftResolvedPayload {
  const groupedRows = new Map<string, ParsedOrderRow[]>();

  for (const row of linkedRows) {
    const groupRows = groupedRows.get(row.businessIdentityKey) ?? [];
    groupRows.push(row);
    groupedRows.set(row.businessIdentityKey, groupRows);
  }

  const identityGroups = Array.from(groupedRows.values()).map((rows) => buildBusinessIdentityGroup(rows));
  const warnings: string[] = [];

  if (identityGroups.length === 0) {
    warnings.push("No PDF rows matched this DFT.");
  }

  if (identityGroups.length > 1) {
    warnings.push("This DFT maps to multiple business identity groups.");
  }

  if (hasDuplicateStem) {
    warnings.push("The ZIP contains duplicate DFT files with this stem.");
  }

  for (const group of identityGroups) {
    warnings.push(...group.warnings.map((warning) => `${group.referenceRaw}: ${warning}`));
  }

  return {
    dftKey: dft.dftKey,
    pieceName: dft.pieceName,
    fileName: dft.fileName,
    dftPath: dft.dftPath,
    relativePath: dft.relativePath,
    identityGroups,
    warnings,
    conflictStatus: hasDuplicateStem
      ? "ambiguous-match"
      : identityGroups.length > 1
        ? "multiple-identities"
        : "none",
  };
}

function buildGenerationUnits(resolvedDfts: DftResolvedPayload[], inventoryByPath: Map<string, DftInventoryItem>): GenerationUnit[] {
  const units: GenerationUnit[] = [];

  for (const resolved of resolvedDfts) {
    const dft = inventoryByPath.get(resolved.dftPath);
    if (!dft) {
      continue;
    }

    for (const identityGroup of resolved.identityGroups) {
      units.push({
        dft,
        identityGroup,
        sourceRowIds: identityGroup.sourceRowIds,
      });
    }
  }

  return units;
}

function buildLegacyMatchedReports(generationUnits: GenerationUnit[]): LegacyMatchedDftReport[] {
  return generationUnits.map((unit) => ({
    dftKey: unit.dft.dftKey,
    dftFileName: unit.dft.fileName,
    dftPath: unit.dft.dftPath,
    dftRelativePath: unit.dft.relativePath,
    reference: unit.identityGroup.referenceRaw,
    referenceKey: unit.identityGroup.referenceKey,
    businessIdentityKey: unit.identityGroup.businessIdentityKey,
    lineNumbers: unit.identityGroup.sourceRows.map((row) => row.lineNumber),
    totalQuantity: unit.identityGroup.totalQuantity,
  }));
}

function buildLegacyMissingReports(unmatchedDfts: DftInventoryItem[]): LegacyMissingDftReport[] {
  return unmatchedDfts.map((dft) => ({
    dftKey: dft.dftKey,
    fileName: dft.fileName,
    dftPath: dft.dftPath,
    relativePath: dft.relativePath,
    reason: "missing-from-pdf",
  }));
}

function buildBlockingReasons(report: Pick<DftPreflightReport, "duplicateDfts" | "unmatchedDfts" | "unmatchedPdfRows" | "resolvedDfts">): string[] {
  const reasons: string[] = [];

  if (report.duplicateDfts.length > 0) {
    reasons.push(
      `Duplicate DFT stems detected in the ZIP: ${report.duplicateDfts
        .map((group) => `${group.dftKey} (${group.fileNames.join(", ")})`)
        .join("; ")}`,
    );
  }

  if (report.unmatchedDfts.length > 0) {
    reasons.push(
      `DFT files without matching PDF rows: ${report.unmatchedDfts
        .map((dft) => dft.fileName)
        .join(", ")}`,
    );
  }

  if (report.unmatchedPdfRows.length > 0) {
    reasons.push(
      `PDF rows without matching DFT files: ${report.unmatchedPdfRows
        .map((row) => `${row.referenceRaw} (line ${row.lineNumber})`)
        .join(", ")}`,
    );
  }

  const ambiguousDfts = report.resolvedDfts.filter((dft) => dft.conflictStatus === "ambiguous-match");
  if (ambiguousDfts.length > 0) {
    reasons.push(
      `Ambiguous DFT matches require a ZIP cleanup: ${ambiguousDfts
        .map((dft) => dft.fileName)
        .join(", ")}`,
    );
  }

  return reasons;
}

export function analyzeDrawingMatches(
  order: ParsedOrder,
  rootDir: string,
  extractedFiles: string[],
): DrawingMatchPlan {
  const inventory = extractedFiles.map((filePath) => getZipInventoryEntry(rootDir, filePath));
  const { dftInventory, duplicateDftGroups, ignoredZipFiles } = buildDftInventory(inventory);
  const parsedRows = order.rows ?? order.lines ?? [];
  const rowsByReference = groupRowsByReference(parsedRows);
  const dftByKey = new Map<string, DftInventoryItem[]>();

  for (const dft of dftInventory) {
    const group = dftByKey.get(dft.dftKey) ?? [];
    group.push(dft);
    dftByKey.set(dft.dftKey, group);
  }

  const resolvedDfts = dftInventory.map((dft) => {
    const duplicateStem = (dftByKey.get(dft.dftKey)?.length ?? 0) > 1;
    const linkedRows = rowsByReference.get(dft.dftKey) ?? [];
    return buildResolvedDftPayload(dft, linkedRows, duplicateStem);
  });

  const unmatchedPdfRows = parsedRows.filter((row) => !dftByKey.has(row.referenceKey));
  const unmatchedDfts = resolvedDfts
    .filter((resolved) => resolved.identityGroups.length === 0)
    .map((resolved) => ({
      dftKey: resolved.dftKey,
      pieceName: resolved.pieceName,
      fileName: resolved.fileName,
      dftPath: resolved.dftPath,
      relativePath: resolved.relativePath,
    }));

  const repeatedReferences = buildRepeatedReferenceReports(parsedRows);
  const warnings = buildUnmatchedPdfWarnings(unmatchedPdfRows);
  const generationUnits = buildGenerationUnits(resolvedDfts, new Map(dftInventory.map((dft) => [dft.dftPath, dft])));
  const blockingReasons = buildBlockingReasons({
    duplicateDfts: duplicateDftGroups,
    unmatchedDfts,
    unmatchedPdfRows,
    resolvedDfts,
  });

  const report: DftPreflightReport = {
    orderNumber: order.orderNumber,
    dftInventory,
    parsedRows,
    resolvedDfts,
    unmatchedPdfRows,
    unmatchedDfts,
    duplicateDfts: duplicateDftGroups,
    ignoredZipFiles,
    repeatedReferences,
    warnings,
    blockingReasons,
    readyForGeneration: blockingReasons.length === 0,
    parsedOrderLines: parsedRows,
    matchedDfts: buildLegacyMatchedReports(generationUnits),
    missingDfts: buildLegacyMissingReports(unmatchedDfts),
    extraFilesIgnored: ignoredZipFiles,
    repeatedOrderLines: repeatedReferences,
  };

  return {
    report,
    generationUnits,
  };
}
