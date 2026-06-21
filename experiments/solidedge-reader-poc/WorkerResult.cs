using System.Collections.Generic;

namespace SolidedgeReaderPoc;

internal sealed class WorkerResult
{
    public WorkerResult(
        string status,
        string jobId,
        string inputPath,
        string outputDirectory,
        int sheetCount,
        IReadOnlyList<string> artifacts,
        string? errorCode,
        string? errorMessage)
    {
        Status = status;
        JobId = jobId;
        InputPath = inputPath;
        OutputDirectory = outputDirectory;
        SheetCount = sheetCount;
        Artifacts = artifacts;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public string Status { get; }

    public string JobId { get; }

    public string InputPath { get; }

    public string OutputDirectory { get; }

    public int SheetCount { get; }

    public IReadOnlyList<string> Artifacts { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public static WorkerResult Success(
        string jobId,
        string inputPath,
        string outputDirectory,
        int sheetCount,
        IReadOnlyList<string> artifacts)
    {
        return new WorkerResult("succeeded", jobId, inputPath, outputDirectory, sheetCount, artifacts, null, null);
    }

    public static WorkerResult Failure(
        string jobId,
        string inputPath,
        string outputDirectory,
        string errorCode,
        string errorMessage)
    {
        return new WorkerResult("failed", jobId, inputPath, outputDirectory, 0, new string[0], errorCode, errorMessage);
    }
}
