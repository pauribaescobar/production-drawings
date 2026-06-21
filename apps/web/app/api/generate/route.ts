import { readFile } from "node:fs/promises";
import { NextResponse } from "next/server";
import {
  GenerationBlockedError,
  buildDftPreflightReport,
  buildGeneratedPdfJob,
} from "@/lib/generate-final-pdf";

export const runtime = "nodejs";

export async function POST(request: Request) {
  try {
    const url = new URL(request.url);
    let formData: FormData;

    try {
      formData = await request.formData();
    } catch {
      return NextResponse.json(
        { error: "Invalid multipart form data." },
        { status: 400 },
      );
    }

    const pdf = formData.get("pdf");
    const zip = formData.get("zip");
    const mode = String(formData.get("mode") ?? url.searchParams.get("mode") ?? "")
      .trim()
      .toLowerCase();
    const preflightRequested =
      mode === "preflight" ||
      mode === "preflight-only" ||
      formData.get("preflight") === "true" ||
      url.searchParams.get("preflight") === "true";

    if (!(pdf instanceof File)) {
      return NextResponse.json({ error: "Missing PDF file." }, { status: 400 });
    }

    if (!(zip instanceof File)) {
      return NextResponse.json({ error: "Missing ZIP file." }, { status: 400 });
    }

    if (preflightRequested) {
      const report = await buildDftPreflightReport(pdf, zip);

      return NextResponse.json(report, {
        status: 200,
        headers: {
          "X-Preflight": "true",
          "X-Ready-For-Generation": String(report.readyForGeneration),
        },
      });
    }

    const job = await buildGeneratedPdfJob(pdf, zip);

    try {
      const pdfBytes = await readFile(job.generatedPdfPath);

      return new NextResponse(pdfBytes, {
        status: 200,
        headers: {
          "Content-Type": "application/pdf",
          "Content-Disposition": 'inline; filename="generated.pdf"',
          "X-Job-Id": job.jobId,
          "X-Matched-Pieces": String(job.matchedPieces.length),
          "X-Generation-Units": String(job.generationUnits.length),
        },
      });
    } finally {
      await job.cleanup();
    }
  } catch (error) {
    if (error instanceof GenerationBlockedError) {
      return NextResponse.json(
        {
          error: "Generation blocked by preflight.",
          details: error.blockingReasons.join(" "),
        },
        { status: 422 },
      );
    }

    const execError = error as NodeJS.ErrnoException & { stderr?: string };
    const details = execError.stderr?.trim() || (error instanceof Error ? error.message : "Unknown error.");

    return NextResponse.json(
      { error: "Failed to generate the final PDF.", details },
      { status: 500 },
    );
  }
}
