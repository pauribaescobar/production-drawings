using System;
using System.Collections.Generic;
using System.IO;
using SolidEdgeCommunity.Reader;

namespace SolidedgeReaderPoc;

internal sealed class DraftEmfExporter : IDraftEmfExporter
{
    public ExportResult ExportSheets(string inputPath, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException("An input path is required.", nameof(inputPath));

        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("An output directory is required.", nameof(outputDirectory));

        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Draft file not found.", inputPath);

        if (!string.Equals(Path.GetExtension(inputPath), ".dft", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The input file must have a .dft extension.", nameof(inputPath));

        Directory.CreateDirectory(outputDirectory);

        var exportedFiles = new List<string>();

        using DraftDocument draftDocument = DraftDocument.Open(inputPath);

        int sheetIndex = 0;

        foreach (var sheet in draftDocument.Sheets)
        {
            sheetIndex++;
            string emfPath = Path.Combine(outputDirectory, $"sheet-{sheetIndex:000}.emf");

            // The reader package exposes the sheet-level EMF export path directly.
            sheet.SaveAsEmf(emfPath);
            exportedFiles.Add(emfPath);
        }

        return new ExportResult(
            inputPath,
            outputDirectory,
            sheetIndex,
            exportedFiles,
            Path.Combine(outputDirectory, "final.pdf"));
    }
}
