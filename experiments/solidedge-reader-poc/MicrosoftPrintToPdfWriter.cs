using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace SolidedgeReaderPoc;

internal sealed class MicrosoftPrintToPdfWriter : IAnnotatedPdfWriter
{
    public void WritePdf(string outputPdfPath, IReadOnlyList<string> emfFiles, IReadOnlyList<SheetAnnotation> annotations)
    {
        if (string.IsNullOrWhiteSpace(outputPdfPath))
            throw new ArgumentException("An output PDF path is required.", nameof(outputPdfPath));

        if (emfFiles.Count == 0)
            throw new ArgumentException("At least one EMF file is required to create a PDF.", nameof(emfFiles));

        if (emfFiles.Count != annotations.Count)
            throw new ArgumentException("Each EMF file must have a matching annotation payload.", nameof(annotations));

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new DraftPdfFinalizationException("PDF finalization requires a Windows host.");

        string? outputDirectory = Path.GetDirectoryName(outputPdfPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("The output PDF path must include a directory.", nameof(outputPdfPath));

        Directory.CreateDirectory(outputDirectory);

        if (File.Exists(outputPdfPath))
            File.Delete(outputPdfPath);

        using var document = new PdfDocument();
        document.Info.Title = Path.GetFileName(outputPdfPath);
        document.Info.Creator = "SolidedgeReaderPoc";

        for (int pageIndex = 0; pageIndex < emfFiles.Count; pageIndex++)
        {
            string emfPath = emfFiles[pageIndex];
            SheetAnnotation annotation = annotations[pageIndex];

            using Bitmap pageBitmap = RenderPageBitmap(emfPath, annotation);
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(pageBitmap.Width * 72.0 / pageBitmap.HorizontalResolution);
            page.Height = XUnit.FromPoint(pageBitmap.Height * 72.0 / pageBitmap.VerticalResolution);

            using XGraphics gfx = XGraphics.FromPdfPage(page);
            using var imageStream = new MemoryStream();
            pageBitmap.Save(imageStream, ImageFormat.Png);
            imageStream.Position = 0;
            using XImage xImage = XImage.FromStream(imageStream);
            gfx.DrawImage(xImage, 0, 0, page.Width, page.Height);
        }

        document.Save(outputPdfPath);
    }

    private static Bitmap RenderPageBitmap(string emfPath, SheetAnnotation annotation)
    {
        using var metadataImage = Image.FromFile(emfPath);
        int dpiX = metadataImage.HorizontalResolution > 0 ? (int)Math.Round(metadataImage.HorizontalResolution) : 300;
        int dpiY = metadataImage.VerticalResolution > 0 ? (int)Math.Round(metadataImage.VerticalResolution) : 300;

        int pageWidth = Math.Max(1, (int)Math.Ceiling(metadataImage.Width * dpiX / metadataImage.HorizontalResolution));
        int pageHeight = Math.Max(1, (int)Math.Ceiling(metadataImage.Height * dpiY / metadataImage.VerticalResolution));
        if (pageWidth <= 0) pageWidth = 2480;
        if (pageHeight <= 0) pageHeight = 3508;

        var bitmap = new Bitmap(pageWidth, pageHeight, PixelFormat.Format32bppArgb);
        bitmap.SetResolution(dpiX, dpiY);

        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            RenderPage(graphics, new Rectangle(0, 0, pageWidth, pageHeight), bitmap, emfPath, annotation);
        }

        return bitmap;
    }

    private static void RenderPage(Graphics graphics, Rectangle pageBounds, Bitmap pageBitmap, string emfPath, SheetAnnotation annotation)
    {
        graphics.Clear(Color.White);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using var image = Image.FromFile(emfPath);
        RectangleF fitted = FitToBounds(image.Width, image.Height, pageBounds);
        graphics.DrawImage(image, fitted);

        PageLayoutAnalysis layout = BuildPageLayout(emfPath, pageBitmap, fitted);
        DrawOverlayAnnotations(graphics, fitted, pageBounds, annotation, layout);
    }

    private static PageLayoutAnalysis BuildPageLayout(string emfPath, Bitmap pageBitmap, RectangleF drawingBounds)
    {
        var occupancy = new OccupancyGrid(pageBitmap);
        RectangleF occupiedBounds = FindOccupiedBounds(pageBitmap);
        IReadOnlyList<TextAnchor> textAnchors = ExtractTextAnchors(emfPath, drawingBounds);
        return new PageLayoutAnalysis(occupancy, occupiedBounds, textAnchors);
    }

    private static void DrawOverlayAnnotations(
        Graphics graphics,
        RectangleF drawingBounds,
        RectangleF pageBounds,
        SheetAnnotation annotation,
        PageLayoutAnalysis layout)
    {
        using var titleBrush = new SolidBrush(Color.FromArgb(30, 30, 30));
        using var highlightBrush = new SolidBrush(Color.FromArgb(140, 255, 245, 90));

        float valueSize = 11.0f;
        float treatmentSize = 15.0f;
        float dimensionSize = 10.5f;

        var reserved = new List<RectangleF>();
        RectangleF treatmentZone = CreateTreatmentZone(drawingBounds);
        RectangleF treatmentRect = RectangleF.Empty;

        if (!string.IsNullOrWhiteSpace(annotation.Treatment))
        {
            string treatmentText = annotation.Treatment.Trim().ToUpperInvariant();
            treatmentRect = PlaceTreatment(
                graphics,
                treatmentText,
                treatmentZone,
                layout.Occupancy,
                reserved,
                pageBounds,
                treatmentSize);

            DrawHighlightedText(
                graphics,
                treatmentText,
                treatmentRect,
                titleBrush,
                highlightBrush,
                "Segoe UI",
                treatmentSize,
                FontStyle.Bold,
                StringAlignment.Center);

            reserved.Add(Inflate(treatmentRect, 3f));
        }

        IReadOnlyList<DimensionAnnotation> dimensions = annotation.Dimensions
            .Where(dimension => !string.IsNullOrWhiteSpace(dimension.Axis) && !string.IsNullOrWhiteSpace(dimension.Value))
            .ToArray();

        foreach (DimensionAnnotation dimension in dimensions)
        {
            TextAnchor? anchor = FindDimensionAnchor(layout.TextAnchors, dimension.Axis);
            string valueText = dimension.Value.Trim();
            RectangleF dimensionRect = PlaceDimensionValue(
                graphics,
                valueText,
                anchor,
                drawingBounds,
                pageBounds,
                layout.Occupancy,
                reserved,
                dimensionSize);

            DrawInlineValue(
                graphics,
                valueText,
                dimensionRect,
                titleBrush,
                "Segoe UI",
                dimensionSize,
                FontStyle.Bold,
                StringAlignment.Near);

            reserved.Add(Inflate(dimensionRect, 2f));
        }

        SummaryZones summaryZones = CreateSummaryZones(drawingBounds, pageBounds, valueSize, treatmentRect);

        DrawSummaryValueInZone(
            graphics,
            summaryZones.QuantityZone,
            annotation.Quantity,
            titleBrush,
            valueSize,
            StringAlignment.Center);

        DrawSummaryValueInZone(
            graphics,
            summaryZones.MaterialZone,
            annotation.Material,
            titleBrush,
            valueSize,
            StringAlignment.Center);

        DrawSummaryValueInZone(
            graphics,
            summaryZones.DeliveryDateZone,
            annotation.DeliveryDate,
            titleBrush,
            valueSize,
            StringAlignment.Near);

        DrawSummaryValueInZone(
            graphics,
            summaryZones.OrderNumberZone,
            annotation.OrderNumber,
            titleBrush,
            valueSize,
            StringAlignment.Near);
    }

    private static void DrawHighlightedText(
        Graphics graphics,
        string text,
        RectangleF bounds,
        Brush textBrush,
        Brush highlightBrush,
        string fontFamily,
        float maxFontSize,
        FontStyle style,
        StringAlignment alignment)
    {
        using Font font = CreateBestFitFont(graphics, text, fontFamily, maxFontSize, Math.Max(9f, maxFontSize - 6f), style, bounds.Width);
        SizeF measured = graphics.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic);
        float highlightWidth = Math.Min(bounds.Width, measured.Width + 18f);
        float highlightHeight = Math.Max(bounds.Height, measured.Height + 10f);
        float highlightX = bounds.Left + Math.Max(0f, (bounds.Width - highlightWidth) / 2f);
        float highlightY = bounds.Top + Math.Max(0f, (bounds.Height - highlightHeight) / 2f);

        graphics.FillRectangle(highlightBrush, new RectangleF(highlightX, highlightY, highlightWidth, highlightHeight));

        var format = new StringFormat
        {
            Alignment = alignment,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoClip | StringFormatFlags.NoWrap,
        };

        graphics.DrawString(text, font, textBrush, new RectangleF(bounds.Left, bounds.Top, bounds.Width, highlightHeight), format);
    }

    private static void DrawInlineValue(
        Graphics graphics,
        string text,
        RectangleF bounds,
        Brush textBrush,
        string fontFamily,
        float maxFontSize,
        FontStyle style,
        StringAlignment alignment)
    {
        using Font font = CreateBestFitFont(graphics, text, fontFamily, maxFontSize, Math.Max(8f, maxFontSize - 4f), style, bounds.Width);

        var format = new StringFormat
        {
            Alignment = alignment,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoClip | StringFormatFlags.NoWrap,
        };

        graphics.DrawString(text, font, textBrush, bounds, format);
    }

    private static void DrawSummaryValueInZone(
        Graphics graphics,
        RectangleF zone,
        string value,
        Brush valueBrush,
        float valueMaxFontSize,
        StringAlignment alignment)
    {
        string normalizedValue = value.Trim();
        if (string.IsNullOrWhiteSpace(normalizedValue))
            return;

        RectangleF textBounds = new RectangleF(
            zone.Left + 2f,
            zone.Top,
            Math.Max(1f, zone.Width - 4f),
            zone.Height);

        DrawInlineValue(
            graphics,
            normalizedValue,
            textBounds,
            valueBrush,
            "Segoe UI",
            valueMaxFontSize,
            FontStyle.Bold,
            alignment);
    }

    private static RectangleF CreateTreatmentZone(RectangleF drawingBounds)
    {
        float zoneWidth = Math.Max(160f, drawingBounds.Width * 0.24f);
        float zoneHeight = Math.Max(44f, drawingBounds.Height * 0.10f);
        float left = drawingBounds.Right - Math.Max(12f, drawingBounds.Width * 0.03f) - zoneWidth;
        float top = drawingBounds.Top + Math.Max(10f, drawingBounds.Height * 0.03f);

        return new RectangleF(left, top, zoneWidth, zoneHeight);
    }

    private static SummaryZones CreateSummaryZones(
        RectangleF drawingBounds,
        RectangleF pageBounds,
        float valueMaxFontSize,
        RectangleF treatmentRect)
    {
        float rowHeight = Math.Max(20f, valueMaxFontSize + 8f);
        float rowGap = Math.Max(6f, drawingBounds.Height * 0.008f);
        float columnWidth = Math.Max(148f, drawingBounds.Width * 0.26f);
        float rightPadding = Math.Max(12f, drawingBounds.Width * 0.03f);

        float left = drawingBounds.Right - rightPadding - columnWidth;
        float desiredTop = drawingBounds.Top + (drawingBounds.Height * 0.56f);
        float stackHeight = (rowHeight * 3f) + (rowGap * 2f);
        float titleBlockReserve = Math.Max(94f, drawingBounds.Height * 0.11f);
        float bottomLimit = drawingBounds.Bottom - titleBlockReserve;
        float top = Math.Min(desiredTop, bottomLimit - stackHeight);
        top = Math.Max(top, drawingBounds.Top + (drawingBounds.Height * 0.44f));

        if (!treatmentRect.IsEmpty)
            top = Math.Max(top, treatmentRect.Bottom + Math.Max(18f, drawingBounds.Height * 0.01f));

        top = Math.Min(top, bottomLimit - stackHeight);

        float quantityWidth = Math.Max(56f, (columnWidth - rowGap) * 0.44f);
        float materialWidth = columnWidth - quantityWidth - rowGap;

        RectangleF quantityZone = new RectangleF(left, top, quantityWidth, rowHeight);
        RectangleF materialZone = new RectangleF(left + quantityWidth + rowGap, top, materialWidth, rowHeight);
        RectangleF deliveryDateZone = new RectangleF(left, top + rowHeight + rowGap, columnWidth, rowHeight);
        RectangleF orderNumberZone = new RectangleF(left, top + ((rowHeight + rowGap) * 2f), columnWidth, rowHeight);

        quantityZone = ClampToPage(quantityZone, pageBounds);
        materialZone = ClampToPage(materialZone, pageBounds);
        deliveryDateZone = ClampToPage(deliveryDateZone, pageBounds);
        orderNumberZone = ClampToPage(orderNumberZone, pageBounds);

        return new SummaryZones(quantityZone, materialZone, deliveryDateZone, orderNumberZone);
    }

    private static RectangleF PlaceTreatment(
        Graphics graphics,
        string text,
        RectangleF zone,
        OccupancyGrid occupancy,
        IReadOnlyList<RectangleF> reserved,
        RectangleF pageBounds,
        float maxFontSize)
    {
        SizeF measured = MeasureSingleLine(
            graphics,
            text,
            "Segoe UI",
            maxFontSize,
            FontStyle.Bold,
            Math.Max(1f, zone.Width - 16f));

        float width = Math.Min(zone.Width, measured.Width + 16f);
        float height = Math.Min(zone.Height, measured.Height + 12f);
        float left = Math.Max(zone.Left, zone.Right - width);

        foreach (float topOffset in new[] { 0f, 8f, 16f, 24f, 32f })
        {
            RectangleF candidate = new RectangleF(left, zone.Top + topOffset, width, height);
            if (candidate.Bottom > zone.Bottom)
                break;

            if (CanPlace(candidate, pageBounds, occupancy, reserved))
                return candidate;
        }

        RectangleF fallback = new RectangleF(left, zone.Top, width, height);
        return ClampToPage(fallback, pageBounds);
    }

    private static RectangleF PlaceDimensionValue(
        Graphics graphics,
        string text,
        TextAnchor? anchor,
        RectangleF drawingBounds,
        RectangleF pageBounds,
        OccupancyGrid occupancy,
        IReadOnlyList<RectangleF> reserved,
        float maxFontSize)
    {
        SizeF measured = MeasureSingleLine(graphics, text, "Segoe UI", maxFontSize, FontStyle.Bold);
        float width = measured.Width + 10f;
        float height = measured.Height + 8f;

        if (anchor is not null)
        {
            TextAnchor anchorValue = anchor!;

            RectangleF rightCandidate = new RectangleF(anchorValue.Bounds.Right + 6f, anchorValue.Bounds.Top - 2f, width, height);
            if (CanPlace(rightCandidate, pageBounds, occupancy, reserved))
                return rightCandidate;

            RectangleF leftCandidate = new RectangleF(anchorValue.Bounds.Left - 6f - width, anchorValue.Bounds.Top - 2f, width, height);
            if (CanPlace(leftCandidate, pageBounds, occupancy, reserved))
                return leftCandidate;

            RectangleF belowCandidate = new RectangleF(anchorValue.Bounds.Right + 4f, anchorValue.Bounds.Bottom + 2f, width, height);
            if (CanPlace(belowCandidate, pageBounds, occupancy, reserved))
                return belowCandidate;

            RectangleF aboveCandidate = new RectangleF(anchorValue.Bounds.Right + 4f, anchorValue.Bounds.Top - height - 2f, width, height);
            if (CanPlace(aboveCandidate, pageBounds, occupancy, reserved))
                return aboveCandidate;
        }

        float fallbackLeft = drawingBounds.Right - Math.Max(12f, drawingBounds.Width * 0.03f) - width;
        float fallbackTop = drawingBounds.Top + Math.Max(40f, drawingBounds.Height * 0.16f);
        RectangleF fallback = new RectangleF(fallbackLeft, fallbackTop, width, height);
        return ClampToPage(fallback, pageBounds);
    }

    private static bool CanPlace(
        RectangleF candidate,
        RectangleF pageBounds,
        OccupancyGrid occupancy,
        IReadOnlyList<RectangleF> reserved)
    {
        return IsInsidePage(candidate, pageBounds)
            && !occupancy.Intersects(candidate)
            && !IntersectsAny(candidate, reserved);
    }

    private static Font CreateBestFitFont(
        Graphics graphics,
        string text,
        string fontFamily,
        float maxFontSize,
        float minFontSize,
        FontStyle style,
        float maxWidth)
    {
        string value = text ?? string.Empty;
        float size = maxFontSize;

        while (size >= minFontSize)
        {
            Font candidate = new Font(fontFamily, size, style);
            SizeF measured = graphics.MeasureString(value, candidate, int.MaxValue, StringFormat.GenericTypographic);

            if (measured.Width <= maxWidth || size <= minFontSize)
                return candidate;

            candidate.Dispose();
            size -= 0.5f;
        }

        return new Font(fontFamily, minFontSize, style);
    }

    private static RectangleF FitToBounds(float sourceWidth, float sourceHeight, Rectangle destinationBounds)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
            return destinationBounds;

        float widthScale = destinationBounds.Width / sourceWidth;
        float heightScale = destinationBounds.Height / sourceHeight;
        float scale = Math.Min(widthScale, heightScale);

        float width = sourceWidth * scale;
        float height = sourceHeight * scale;

        float x = destinationBounds.Left + ((destinationBounds.Width - width) / 2);
        float y = destinationBounds.Top + ((destinationBounds.Height - height) / 2);

        return new RectangleF(x, y, width, height);
    }

    private static SizeF MeasureSingleLine(
        Graphics graphics,
        string text,
        string fontFamily,
        float maxFontSize,
        FontStyle style,
        float maxWidth = 2000f)
    {
        using Font font = CreateBestFitFont(graphics, text, fontFamily, maxFontSize, Math.Max(8f, maxFontSize - 4f), style, maxWidth);
        SizeF measured = graphics.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic);
        return measured;
    }

    private static bool IsInsidePage(RectangleF candidate, RectangleF pageBounds)
    {
        return candidate.Left >= pageBounds.Left
            && candidate.Top >= pageBounds.Top
            && candidate.Right <= pageBounds.Right
            && candidate.Bottom <= pageBounds.Bottom;
    }

    private static RectangleF ClampToPage(RectangleF candidate, RectangleF pageBounds)
    {
        float x = candidate.Left;
        float y = candidate.Top;

        if (x < pageBounds.Left)
            x = pageBounds.Left;
        else if (x + candidate.Width > pageBounds.Right)
            x = pageBounds.Right - candidate.Width;

        if (y < pageBounds.Top)
            y = pageBounds.Top;
        else if (y + candidate.Height > pageBounds.Bottom)
            y = pageBounds.Bottom - candidate.Height;

        return new RectangleF(x, y, candidate.Width, candidate.Height);
    }

    private static bool IntersectsAny(RectangleF candidate, IReadOnlyList<RectangleF> reserved)
    {
        foreach (RectangleF rectangle in reserved)
        {
            if (rectangle.IntersectsWith(candidate))
                return true;
        }

        return false;
    }

    private static RectangleF Inflate(RectangleF rectangle, float padding)
    {
        return new RectangleF(
            rectangle.Left - padding,
            rectangle.Top - padding,
            rectangle.Width + padding * 2f,
            rectangle.Height + padding * 2f);
    }

    private static RectangleF FindOccupiedBounds(Bitmap bitmap)
    {
        int minX = bitmap.Width;
        int minY = bitmap.Height;
        int maxX = -1;
        int maxY = -1;

        for (int y = 0; y < bitmap.Height; y += 2)
        {
            for (int x = 0; x < bitmap.Width; x += 2)
            {
                if (!IsInkPixel(bitmap.GetPixel(x, y)))
                    continue;

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < 0 || maxY < 0)
            return new RectangleF(0, 0, bitmap.Width, bitmap.Height);

        return RectangleF.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    private static bool IsInkPixel(Color color)
    {
        return color.A > 0 && (color.R < 245 || color.G < 245 || color.B < 245);
    }

    private static IReadOnlyList<TextAnchor> ExtractTextAnchors(string emfPath, RectangleF drawingBounds)
    {
        using var image = Image.FromFile(emfPath);
        if (image is not Metafile metafile)
            return Array.Empty<TextAnchor>();

        GraphicsUnit unit = GraphicsUnit.Pixel;
        RectangleF sourceBounds = image.GetBounds(ref unit);
        if (sourceBounds.Width <= 0f || sourceBounds.Height <= 0f)
            return Array.Empty<TextAnchor>();

        var anchors = new List<TextAnchor>();
        using var probeBitmap = new Bitmap(1, 1);
        using var probeGraphics = Graphics.FromImage(probeBitmap);

        Graphics.EnumerateMetafileProc callback = (recordType, flags, dataSize, data, callbackData) =>
        {
            if ((int)recordType != 0x401C || data == IntPtr.Zero || dataSize < 28)
                return true;

            byte[] bytes = new byte[dataSize];
            Marshal.Copy(data, bytes, 0, dataSize);

            int length = BitConverter.ToInt32(bytes, 8);
            int stringByteCount = length * 2;
            if (length <= 0 || bytes.Length < 28 + stringByteCount)
                return true;

            string text = Encoding.Unicode.GetString(bytes, 28, stringByteCount).Trim('\0', ' ', '\t', '\r', '\n');
            if (string.IsNullOrWhiteSpace(text))
                return true;

            float left = BitConverter.ToSingle(bytes, 12);
            float top = BitConverter.ToSingle(bytes, 16);
            float width = BitConverter.ToSingle(bytes, 20);
            float height = BitConverter.ToSingle(bytes, 24);

            RectangleF sourceRect = new RectangleF(left, top, width, height);
            RectangleF mappedRect = MapRectToPage(sourceRect, sourceBounds, drawingBounds);
            anchors.Add(new TextAnchor(NormalizeCalloutText(text), mappedRect));
            return true;
        };

        probeGraphics.EnumerateMetafile(metafile, new PointF(0f, 0f), callback);
        return anchors;
    }

    private static RectangleF MapRectToPage(RectangleF sourceRect, RectangleF sourceBounds, RectangleF destinationBounds)
    {
        float widthScale = sourceBounds.Width <= 0f ? 1f : destinationBounds.Width / sourceBounds.Width;
        float heightScale = sourceBounds.Height <= 0f ? 1f : destinationBounds.Height / sourceBounds.Height;

        float x = destinationBounds.Left + ((sourceRect.Left - sourceBounds.Left) * widthScale);
        float y = destinationBounds.Top + ((sourceRect.Top - sourceBounds.Top) * heightScale);
        float width = sourceRect.Width * widthScale;
        float height = sourceRect.Height * heightScale;

        return new RectangleF(x, y, width, height);
    }

    private static string NormalizeCalloutText(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (char character in text.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(character);
        }

        return builder.ToString();
    }

    private static TextAnchor? FindDimensionAnchor(IReadOnlyList<TextAnchor> anchors, string axis)
    {
        string normalizedAxis = NormalizeCalloutText(axis);
        if (string.IsNullOrWhiteSpace(normalizedAxis))
            return null;

        TextAnchor? best = null;

        foreach (TextAnchor anchor in anchors)
        {
            if (!string.Equals(anchor.Text, normalizedAxis, StringComparison.OrdinalIgnoreCase))
                continue;

            if (best is null || anchor.Bounds.Right > best.Bounds.Right)
                best = anchor;
        }

        return best;
    }

    private sealed record PageLayoutAnalysis(OccupancyGrid Occupancy, RectangleF OccupiedBounds, IReadOnlyList<TextAnchor> TextAnchors);

    private sealed record TextAnchor(string Text, RectangleF Bounds);

    private sealed class OccupancyGrid
    {
        private readonly bool[] occupiedCells;

        public OccupancyGrid(Bitmap bitmap, int cellSize = 6)
        {
            CellSize = Math.Max(2, cellSize);
            Columns = Math.Max(1, (int)Math.Ceiling(bitmap.Width / (double)CellSize));
            Rows = Math.Max(1, (int)Math.Ceiling(bitmap.Height / (double)CellSize));
            occupiedCells = new bool[Columns * Rows];

            for (int y = 0; y < bitmap.Height; y += 2)
            {
                for (int x = 0; x < bitmap.Width; x += 2)
                {
                    if (!IsInkPixel(bitmap.GetPixel(x, y)))
                        continue;

                    int column = Math.Min(Columns - 1, x / CellSize);
                    int row = Math.Min(Rows - 1, y / CellSize);
                    occupiedCells[(row * Columns) + column] = true;
                }
            }
        }

        public int CellSize { get; }

        public int Columns { get; }

        public int Rows { get; }

        public bool Intersects(RectangleF rectangle)
        {
            int minColumn = ClampCellIndex((int)Math.Floor(rectangle.Left / CellSize), Columns);
            int maxColumn = ClampCellIndex((int)Math.Ceiling(rectangle.Right / CellSize), Columns);
            int minRow = ClampCellIndex((int)Math.Floor(rectangle.Top / CellSize), Rows);
            int maxRow = ClampCellIndex((int)Math.Ceiling(rectangle.Bottom / CellSize), Rows);

            if (rectangle.Left < 0f || rectangle.Top < 0f)
                return true;

            for (int row = minRow; row <= maxRow; row++)
            {
                for (int column = minColumn; column <= maxColumn; column++)
                {
                    if (occupiedCells[(row * Columns) + column])
                        return true;
                }
            }

            return false;
        }

        private static int ClampCellIndex(int value, int limit)
        {
            if (value < 0)
                return 0;

            if (value >= limit)
                return limit - 1;

            return value;
        }
    }
}
