using System;
using System.IO;

namespace SolidedgeReaderPoc;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 4 || !string.Equals(args[0], "export", StringComparison.OrdinalIgnoreCase))
        {
            WriteResult(WorkerResult.Failure(
                jobId: string.Empty,
                inputPath: string.Empty,
                outputDirectory: string.Empty,
                errorCode: "usage_error",
                errorMessage: "Usage: SolidedgeReaderPoc export <input.dft> <output-directory> <parsed-order.json> [job-id]"));
            return 1;
        }

        string inputPath = Path.GetFullPath(args[1]);
        string outputDirectory = Path.GetFullPath(args[2]);
        string parsedOrderPath = Path.GetFullPath(args[3]);
        string jobId = args.Length >= 5 && !string.IsNullOrWhiteSpace(args[4])
            ? args[4]
            : Guid.NewGuid().ToString("N");

        try
        {
            IDraftConversionPipeline pipeline = new DraftConversionPipeline();
            ExportResult result = pipeline.Convert(inputPath, outputDirectory, jobId, parsedOrderPath);

            WriteResult(WorkerResult.Success(jobId, result.InputPath, result.OutputDirectory, result.SheetCount, result.Artifacts));

            return 0;
        }
        catch (DraftPdfFinalizationException ex)
        {
            WriteResult(WorkerResult.Failure(jobId, inputPath, outputDirectory, "pdf_finalization_error", ex.Message));
            return 2;
        }
        catch (Exception ex)
        {
            WriteResult(WorkerResult.Failure(jobId, inputPath, outputDirectory, "export_error", ex.Message));
            return 2;
        }
    }

    private static void WriteResult(WorkerResult result)
    {
        Console.WriteLine(WorkerJson.Serialize(result));
    }
}
