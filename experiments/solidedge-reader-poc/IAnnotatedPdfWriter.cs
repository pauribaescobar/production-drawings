using System.Collections.Generic;

namespace SolidedgeReaderPoc;

internal interface IAnnotatedPdfWriter
{
    void WritePdf(string outputPdfPath, IReadOnlyList<string> emfFiles, IReadOnlyList<SheetAnnotation> annotations);
}
