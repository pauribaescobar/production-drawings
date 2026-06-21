namespace SolidedgeReaderPoc;

internal interface IDraftEmfExporter
{
    ExportResult ExportSheets(string inputPath, string outputDirectory);
}
