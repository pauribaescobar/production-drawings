using System;
using System.Collections.Generic;
using System.IO;

namespace SolidedgeReaderPoc;

internal sealed class DraftConversionPipeline : IDraftConversionPipeline
{
    private readonly IDraftEmfExporter emfExporter;
    private readonly IAnnotatedPdfWriter pdfWriter;

    public DraftConversionPipeline()
        : this(new DraftEmfExporter(), new MicrosoftPrintToPdfWriter())
    {
    }

    public DraftConversionPipeline(IDraftEmfExporter emfExporter, IAnnotatedPdfWriter pdfWriter)
    {
        this.emfExporter = emfExporter ?? throw new ArgumentNullException(nameof(emfExporter));
        this.pdfWriter = pdfWriter ?? throw new ArgumentNullException(nameof(pdfWriter));
    }

    public ExportResult Convert(string inputPath, string outputDirectory, string jobId, string parsedOrderPath)
    {
        ExportResult exportResult = emfExporter.ExportSheets(inputPath, outputDirectory);
        string finalPdfPath = Path.Combine(exportResult.OutputDirectory, "final.pdf");
        ParsedOrderDocument parsedOrder = ParsedOrderDocument.Load(parsedOrderPath);

        IReadOnlyList<SheetAnnotation> annotations = BuildAnnotations(
            jobId,
            exportResult.InputPath,
            parsedOrder,
            exportResult.SheetCount);
        pdfWriter.WritePdf(finalPdfPath, exportResult.EmfFiles, annotations);

        return new ExportResult(
            exportResult.InputPath,
            exportResult.OutputDirectory,
            exportResult.SheetCount,
            exportResult.EmfFiles,
            finalPdfPath);
    }

    private static IReadOnlyList<SheetAnnotation> BuildAnnotations(
        string jobId,
        string inputPath,
        ParsedOrderDocument parsedOrder,
        int sheetCount)
    {
        var annotations = new List<SheetAnnotation>(sheetCount);

        for (int sheetIndex = 1; sheetIndex <= sheetCount; sheetIndex++)
        {
            annotations.Add(SheetAnnotation.Create(jobId, inputPath, parsedOrder, sheetIndex));
        }

        return annotations;
    }
}
