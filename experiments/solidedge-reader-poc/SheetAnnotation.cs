using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;

namespace SolidedgeReaderPoc;

internal sealed record SheetAnnotation(
    int SheetIndex,
    string SourceFileName,
    string JobId,
    DateTimeOffset GeneratedAtUtc,
    string OrderNumber,
    string? Treatment,
    string Quantity,
    string Material,
    string DeliveryDate,
    IReadOnlyList<DimensionAnnotation> Dimensions)
{
    public static SheetAnnotation Create(string jobId, string inputPath, ParsedOrderDocument parsedOrder, int sheetIndex)
    {
        var sourceFileName = Path.GetFileName(inputPath);
        var pieceReference = Path.GetFileNameWithoutExtension(inputPath).Trim();
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var orderLine = parsedOrder.Lines.FirstOrDefault(line =>
            string.Equals(line.Reference.Trim(), pieceReference, StringComparison.OrdinalIgnoreCase));

        if (orderLine is null)
        {
            throw new InvalidOperationException(
                $"No parsed order line matched the DFT file name '{sourceFileName}'.");
        }

        return new SheetAnnotation(
            sheetIndex,
            sourceFileName,
            jobId,
            generatedAtUtc,
            parsedOrder.OrderNumber,
            orderLine.Treatment,
            orderLine.Quantity.ToString(CultureInfo.InvariantCulture),
            orderLine.Material,
            orderLine.DeliveryDate,
            orderLine.Dimensions
                .OrderBy(dimension => dimension.Axis, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }
}
