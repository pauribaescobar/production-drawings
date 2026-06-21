namespace SolidedgeReaderPoc;

internal interface IDraftConversionPipeline
{
    ExportResult Convert(string inputPath, string outputDirectory, string jobId, string parsedOrderPath);
}
