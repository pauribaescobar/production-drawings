using System;

namespace SolidedgeReaderPoc;

internal sealed class DraftPdfFinalizationException : Exception
{
    public DraftPdfFinalizationException(string message)
        : base(message)
    {
    }

    public DraftPdfFinalizationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
