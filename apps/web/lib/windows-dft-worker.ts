import { existsSync } from "node:fs";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import path from "node:path";

const execFileAsync = promisify(execFile);

export type WindowsDftWorkerResponse = {
  status: "succeeded" | "failed";
  jobId: string;
  inputPath: string;
  outputDirectory: string;
  sheetCount: number;
  artifacts: string[];
  errorCode?: string | null;
  errorMessage?: string | null;
};

export type WindowsDftWorkerInvocation = {
  inputPath: string;
  outputDirectory: string;
  parsedOrderPath: string;
  jobId: string;
};

function findRepoRoot(startDir: string): string {
  let current = startDir;

  while (true) {
    if (existsSync(path.join(current, "experiments", "solidedge-reader-poc"))) {
      return current;
    }

    const parent = path.dirname(current);
    if (parent === current) {
      throw new Error("Repository root not found.");
    }

    current = parent;
  }
}

function resolveWorkerCommand(repoRoot: string): string {
  const configuredCommand = process.env.DFT_WORKER_COMMAND?.trim();

  if (configuredCommand) {
    return configuredCommand;
  }

  return path.join(
    repoRoot,
    "experiments",
    "solidedge-reader-poc",
    "bin",
    "Release",
    "net48",
    "SolidedgeReaderPoc.exe",
  );
}

function parseWorkerResponse(stdout: string): WindowsDftWorkerResponse {
  try {
    return JSON.parse(stdout.trim()) as WindowsDftWorkerResponse;
  } catch (error) {
    const message = error instanceof Error ? error.message : "Unknown JSON parse error.";
    throw new Error(`The worker returned invalid JSON. ${message}`);
  }
}

export async function invokeWindowsDftWorker(
  invocation: WindowsDftWorkerInvocation,
): Promise<WindowsDftWorkerResponse> {
  const repoRoot = findRepoRoot(process.cwd());
  const workerCommand = resolveWorkerCommand(repoRoot);

  if (!existsSync(workerCommand)) {
    throw new Error(
      `Windows DFT worker not found at ${workerCommand}. Set DFT_WORKER_COMMAND to the executable path.`,
    );
  }

  try {
    const { stdout } = await execFileAsync(
      workerCommand,
      [
        "export",
        invocation.inputPath,
        invocation.outputDirectory,
        invocation.parsedOrderPath,
        invocation.jobId,
      ],
      {
        maxBuffer: 10 * 1024 * 1024,
        windowsHide: true,
      },
    );

    return parseWorkerResponse(stdout);
  } catch (error) {
    const execError = error as NodeJS.ErrnoException & { stdout?: string };

    if (execError.stdout) {
      return parseWorkerResponse(execError.stdout);
    }

    throw new Error(execError.message || "Failed to invoke the Windows DFT worker.");
  }
}
