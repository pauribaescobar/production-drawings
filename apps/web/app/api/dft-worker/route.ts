import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { randomUUID } from "node:crypto";
import { NextResponse } from "next/server";
import { invokeWindowsDftWorker } from "@/lib/windows-dft-worker";

export const runtime = "nodejs";

export async function POST(request: Request) {
  let tempDir = "";

  try {
    let formData: FormData;

    try {
      formData = await request.formData();
    } catch {
      return NextResponse.json(
        { error: "Invalid multipart form data." },
        { status: 400 },
      );
    }

    const dft = formData.get("dft");
    const parsedOrder = formData.get("parsedOrder");
    const jobIdField = formData.get("jobId");

    if (!(dft instanceof File)) {
      return NextResponse.json({ error: "Missing DFT file." }, { status: 400 });
    }

    if (!(parsedOrder instanceof File)) {
      return NextResponse.json({ error: "Missing parsed order JSON file." }, { status: 400 });
    }

    const jobId =
      typeof jobIdField === "string" && jobIdField.trim().length > 0
        ? jobIdField.trim()
        : randomUUID().replace(/-/g, "");

    tempDir = await mkdtemp(path.join(os.tmpdir(), "production-drawings-dft-"));

    const inputPath = path.join(tempDir, path.basename(dft.name || "input.dft"));
    const parsedOrderPath = path.join(
      tempDir,
      path.basename(parsedOrder.name || "parsed-order.json"),
    );
    const outputDirectory = path.join(tempDir, "artifacts");

    await mkdir(outputDirectory, { recursive: true });
    await writeFile(inputPath, Buffer.from(await dft.arrayBuffer()));
    await writeFile(parsedOrderPath, Buffer.from(await parsedOrder.arrayBuffer()));

    const result = await invokeWindowsDftWorker({
      inputPath,
      outputDirectory,
      parsedOrderPath,
      jobId,
    });

    return NextResponse.json(result, {
      status: result.status === "succeeded" ? 200 : 422,
    });
  } catch (error) {
    const details = error instanceof Error ? error.message : "Unknown error.";

    return NextResponse.json(
      {
        error: "Failed to invoke the Windows DFT worker.",
        details,
      },
      { status: 500 },
    );
  } finally {
    if (tempDir) {
      await rm(tempDir, { recursive: true, force: true });
    }
  }
}
