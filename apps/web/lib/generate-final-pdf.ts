import { existsSync } from "node:fs";
import {
  mkdir,
  mkdtemp,
  readdir,
  rm,
  writeFile,
  copyFile,
} from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { randomUUID } from "node:crypto";
import { invokeWindowsDftWorker } from "@/lib/windows-dft-worker";
import {
  analyzeDrawingMatches,
  type DftPreflightReport,
  type GenerationUnit,
  type ParsedOrder,
  type ParsedOrderRow,
} from "@/lib/dft-preflight";

const execFileAsync = promisify(execFile);

type WorkerPdfResult = {
  dftPath: string;
  outputDirectory: string;
  finalPdfPath: string;
};

export type ResolvedPieceOutput = {
  dftFileName: string;
  dftPath: string;
  quantity: string;
  material: string;
  treatment?: string | null;
  deliveryDate: string;
  dimensions: Record<string, string>;
};

export type MissingDftOutput = {
  dftFileName: string;
  dftPath: string;
};

export type PublicPreflightReport = {
  orderNumber: string;
  matchedDfts: ResolvedPieceOutput[];
  missingDfts: MissingDftOutput[];
  warnings: string[];
  readyForGeneration: boolean;
};

type ParsedOrderInputRow = {
  reference: string;
  quantity: number;
  material: string;
  treatment?: string | null;
  deliveryDate: string;
  dimensions: Record<string, string>;
};

type ParsedOrderInput = {
  orderNumber: string;
  rows: ParsedOrderInputRow[];
  lines?: ParsedOrderInputRow[];
};

export class GenerationBlockedError extends Error {
  constructor(public readonly blockingReasons: string[]) {
    super(
      blockingReasons.length === 1
        ? blockingReasons[0]
        : `Generation is blocked: ${blockingReasons.join(" | ")}`,
    );
    this.name = "GenerationBlockedError";
  }
}

export class MissingDrawingError extends GenerationBlockedError {
  constructor(missingReferences: string[]) {
    super(
      missingReferences.length === 1
        ? [`Missing DFT for reference ${missingReferences[0]}.`]
        : [`Missing DFT files for references: ${missingReferences.join(", ")}.`],
    );
    this.name = "MissingDrawingError";
  }
}

export type GeneratedPdfJob = {
  jobId: string;
  jobDir: string;
  orderPdfPath: string;
  drawingsZipPath: string;
  parsedOrderPaths: string[];
  generatedPdfPath: string;
  matchedPieces: GenerationUnit[];
  generationUnits: GenerationUnit[];
  cleanup: () => Promise<void>;
};

function findRepoRoot(startDir: string): string {
  let current = startDir;

  while (true) {
    if (existsSync(path.join(current, "packages", "python-processing", "src"))) {
      return current;
    }

    const parent = path.dirname(current);
    if (parent === current) {
      throw new Error("Repository root not found.");
    }

    current = parent;
  }
}

function sanitizeSegment(value: string): string {
  const sanitized = value
    .trim()
    .replace(/[^a-z0-9._-]+/gi, "-")
    .replace(/-+/g, "-")
    .replace(/^-|-$/g, "");

  return sanitized.length > 0 ? sanitized : "item";
}

async function runPythonCommand(repoRoot: string, args: string[]): Promise<string> {
  const pythonPath = path.join(repoRoot, "packages", "python-processing", "src");
  const { stdout } = await execFileAsync("python3", args, {
    env: {
      ...process.env,
      PYTHONPATH: pythonPath,
    },
    maxBuffer: 10 * 1024 * 1024,
  });

  return stdout;
}

async function parseOrderPdf(repoRoot: string, pdfPath: string): Promise<ParsedOrder> {
  const stdout = await runPythonCommand(repoRoot, [
    "-m",
    "production_drawings.cli",
    pdfPath,
    "--compact",
  ]);

  const parsed = JSON.parse(stdout.trim()) as ParsedOrderInput;
  const rows = parsed.rows.map((row, index) => {
    const referenceKey = row.reference.trim().toLowerCase();
    return {
      rowId: `row-${index + 1}`,
      lineNumber: index + 1,
      referenceRaw: row.reference,
      referenceKey,
      quantity: row.quantity,
      material: row.material,
      treatment: row.treatment ?? null,
      dimensions: row.dimensions,
      deliveryDate: row.deliveryDate,
      businessIdentityKey: [
        referenceKey,
        Object.entries(row.dimensions)
          .map(([axis, value]) => `${axis.trim().toLowerCase()}=${value.trim().toLowerCase()}`)
          .sort()
          .join("|"),
        row.material.trim().toLowerCase(),
        (row.treatment ?? "").trim().toLowerCase(),
      ].join("::"),
    } satisfies ParsedOrderRow;
  });

  return {
    orderNumber: parsed.orderNumber,
    rows,
    lines: rows,
  };
}

async function extractZipArchive(zipPath: string, extractDir: string): Promise<void> {
  await execFileAsync("python3", ["-m", "zipfile", "-e", zipPath, extractDir], {
    maxBuffer: 10 * 1024 * 1024,
  });
}

async function listFilesRecursive(rootDir: string): Promise<string[]> {
  const entries = await readdir(rootDir, { withFileTypes: true });
  const files: string[] = [];

  for (const entry of entries) {
    const entryPath = path.join(rootDir, entry.name);
    if (entry.isDirectory()) {
      files.push(...(await listFilesRecursive(entryPath)));
      continue;
    }

    if (entry.isFile()) {
      files.push(entryPath);
    }
  }

  return files;
}

type WorkerParsedOrderRow = {
  reference: string;
  quantity: number;
  material: string;
  treatment?: string | null;
  deliveryDate: string;
  dimensions: Record<string, string>;
};

type WorkerParsedOrder = {
  orderNumber: string;
  rows: WorkerParsedOrderRow[];
  lines: WorkerParsedOrderRow[];
};

function buildWorkerParsedOrder(orderNumber: string, unit: GenerationUnit): WorkerParsedOrder {
  const sourceRow = unit.identityGroup.sourceRows[0];
  const row: WorkerParsedOrderRow = {
    reference: unit.identityGroup.referenceRaw,
    quantity: unit.identityGroup.totalQuantity,
    material: unit.identityGroup.material,
    treatment: unit.identityGroup.treatment,
    dimensions: unit.identityGroup.dimensions,
    deliveryDate: unit.identityGroup.deliveryDates[0] ?? sourceRow?.deliveryDate ?? "",
  };

  return {
    orderNumber,
    rows: [row],
    lines: [row],
  };
}

function sanitizePreflightReport(report: DftPreflightReport): PublicPreflightReport {
  const matchedDfts: ResolvedPieceOutput[] = [];

  for (const resolvedDft of report.resolvedDfts) {
    for (const group of resolvedDft.identityGroups) {
      matchedDfts.push({
        dftFileName: resolvedDft.fileName,
        dftPath: resolvedDft.dftPath,
        quantity: group.sourceRows.map((row) => String(row.quantity)).join(" + "),
        material: group.material,
        treatment: group.treatment ?? null,
        deliveryDate: group.deliveryDates[0] ?? "",
        dimensions: group.dimensions,
      });
    }
  }

  return {
    orderNumber: report.orderNumber,
    matchedDfts,
    missingDfts: report.unmatchedDfts.map((dft) => ({
      dftFileName: dft.fileName,
      dftPath: dft.dftPath,
    })),
    warnings: report.warnings,
    readyForGeneration: report.readyForGeneration,
  };
}

async function runWorkerForDrawing(
  jobDir: string,
  unit: GenerationUnit,
  index: number,
  parsedOrderPath: string,
): Promise<WorkerPdfResult> {
  const outputDirectory = path.join(
    jobDir,
    "outputs",
    `${String(index + 1).padStart(2, "0")}-${sanitizeSegment(unit.dft.pieceName)}-${sanitizeSegment(unit.identityGroup.businessIdentityKey)}`,
  );

  await mkdir(outputDirectory, { recursive: true });

  const workerResult = await invokeWindowsDftWorker({
    inputPath: unit.dft.dftPath,
    outputDirectory,
    parsedOrderPath,
    jobId: `${path.basename(jobDir)}-${sanitizeSegment(unit.identityGroup.businessIdentityKey)}`,
  });

  if (workerResult.status !== "succeeded") {
    throw new Error(
      workerResult.errorMessage ??
        workerResult.errorCode ??
        `The Windows worker failed for ${unit.dft.fileName}.`,
    );
  }

  const finalPdfPath = workerResult.artifacts.find((artifact) =>
    artifact.toLowerCase().endsWith("final.pdf"),
  );

  if (!finalPdfPath) {
    throw new Error(`The Windows worker did not return a final PDF for ${unit.dft.fileName}.`);
  }

  return {
    dftPath: unit.dft.dftPath,
    outputDirectory,
    finalPdfPath,
  };
}

async function mergePdfFiles(repoRoot: string, outputPath: string, pdfPaths: string[]): Promise<void> {
  if (pdfPaths.length === 1) {
    await copyFile(pdfPaths[0], outputPath);
    return;
  }

  await runPythonCommand(repoRoot, [
    "-m",
    "production_drawings.merge",
    outputPath,
    ...pdfPaths,
  ]);
}

async function collectDrawingMatchPlan(
  repoRoot: string,
  pdfPath: string,
  zipPath: string,
  drawingsDir: string,
): Promise<{
  report: DftPreflightReport;
  generationUnits: GenerationUnit[];
}> {
  const parsedOrder = await parseOrderPdf(repoRoot, pdfPath);
  await extractZipArchive(zipPath, drawingsDir);

  const extractedFiles = await listFilesRecursive(drawingsDir);
  const matchPlan = analyzeDrawingMatches(parsedOrder, drawingsDir, extractedFiles);

  return {
    report: matchPlan.report,
    generationUnits: matchPlan.generationUnits,
  };
}

export async function buildDftPreflightReport(
  pdfFile: File,
  drawingsZip: File,
): Promise<PublicPreflightReport> {
  const repoRoot = findRepoRoot(process.cwd());
  const jobDir = await mkdtemp(path.join(os.tmpdir(), "production-drawings-preflight-"));

  const inputDir = path.join(jobDir, "input");
  const drawingsDir = path.join(jobDir, "drawings");

  await Promise.all([
    mkdir(inputDir, { recursive: true }),
    mkdir(drawingsDir, { recursive: true }),
  ]);

  const orderPdfPath = path.join(inputDir, path.basename(pdfFile.name || "order.pdf"));
  const drawingsZipPath = path.join(inputDir, path.basename(drawingsZip.name || "drawings.zip"));

  const cleanup = async (): Promise<void> => {
    await rm(jobDir, { recursive: true, force: true });
  };

  try {
    await writeFile(orderPdfPath, Buffer.from(await pdfFile.arrayBuffer()));
    await writeFile(drawingsZipPath, Buffer.from(await drawingsZip.arrayBuffer()));

    const { report } = await collectDrawingMatchPlan(repoRoot, orderPdfPath, drawingsZipPath, drawingsDir);

    return sanitizePreflightReport(report);
  } finally {
    await cleanup();
  }
}

export async function buildGeneratedPdfJob(
  pdfFile: File,
  drawingsZip: File,
): Promise<GeneratedPdfJob> {
  const repoRoot = findRepoRoot(process.cwd());
  const jobId = randomUUID().replace(/-/g, "");
  const jobDir = await mkdtemp(path.join(os.tmpdir(), "production-drawings-job-"));

  const inputDir = path.join(jobDir, "input");
  const parsedDir = path.join(jobDir, "parsed");
  const drawingsDir = path.join(jobDir, "drawings");
  const outputDir = path.join(jobDir, "output");

  await Promise.all([
    mkdir(inputDir, { recursive: true }),
    mkdir(parsedDir, { recursive: true }),
    mkdir(drawingsDir, { recursive: true }),
    mkdir(outputDir, { recursive: true }),
  ]);

  const orderPdfPath = path.join(inputDir, path.basename(pdfFile.name || "order.pdf"));
  const drawingsZipPath = path.join(inputDir, path.basename(drawingsZip.name || "drawings.zip"));
  const generatedPdfPath = path.join(outputDir, "generated.pdf");

  await writeFile(orderPdfPath, Buffer.from(await pdfFile.arrayBuffer()));
  await writeFile(drawingsZipPath, Buffer.from(await drawingsZip.arrayBuffer()));

  const cleanup = async (): Promise<void> => {
    await rm(jobDir, { recursive: true, force: true });
  };

  try {
    const { report, generationUnits } = await collectDrawingMatchPlan(
      repoRoot,
      orderPdfPath,
      drawingsZipPath,
      drawingsDir,
    );

    if (report.blockingReasons.length > 0) {
      throw new GenerationBlockedError(report.blockingReasons);
    }

    if (generationUnits.length === 0) {
      throw new Error("No matching DFT files were found in the uploaded ZIP.");
    }

    const workerResults: WorkerPdfResult[] = [];
    const parsedOrderPaths: string[] = [];

    for (const [index, unit] of generationUnits.entries()) {
      const parsedOrderPath = path.join(
        parsedDir,
        `${String(index + 1).padStart(2, "0")}-${sanitizeSegment(unit.dft.pieceName)}-${sanitizeSegment(unit.identityGroup.businessIdentityKey)}.json`,
      );
      parsedOrderPaths.push(parsedOrderPath);
      await writeFile(
        parsedOrderPath,
        JSON.stringify(buildWorkerParsedOrder(report.orderNumber, unit), null, 2),
      );
      workerResults.push(await runWorkerForDrawing(jobDir, unit, index, parsedOrderPath));
    }

    await mergePdfFiles(
      repoRoot,
      generatedPdfPath,
      workerResults.map((result) => result.finalPdfPath),
    );

    return {
      jobId,
      jobDir,
      orderPdfPath,
      drawingsZipPath,
      parsedOrderPaths,
      generatedPdfPath,
      matchedPieces: generationUnits,
      generationUnits,
      cleanup,
    };
  } catch (error) {
    await cleanup();
    throw error;
  }
}
