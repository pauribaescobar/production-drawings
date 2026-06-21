using System.Collections.Generic;
using System.Linq;

namespace SolidedgeReaderPoc;

internal sealed record ExportResult(
    string InputPath,
    string OutputDirectory,
    int SheetCount,
    IReadOnlyList<string> EmfFiles,
    string FinalPdfPath)
{
    public IReadOnlyList<string> Artifacts => EmfFiles.Concat(new[] { FinalPdfPath }).ToArray();
}
