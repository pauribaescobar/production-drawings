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
        DrawOverlayAnnotations(graphics, fitted, annotation, layout);
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
        SheetAnnotation annotation,
        PageLayoutAnalysis layout)
    {
        using var titleBrush = new SolidBrush(Color.FromArgb(30, 30, 30));
        using var highlightBrush = new SolidBrush(Color.FromArgb(140, 255, 245, 90));

        float valueSize = 11.0f;
        float treatmentSize = 15.0f;
        float dimensionSize = 10.5f;

        var reserved = new List<RectangleF>();
        float rightAnchor = Math.Max(
            drawingBounds.Left + drawingBounds.Width * 0.66f,
            layout.OccupiedBounds.Right + 12f);

        if (rightAnchor > drawingBounds.Right - drawingBounds.Width * 0.16f)
            rightAnchor = drawingBounds.Right - drawingBounds.Width * 0.16f;

        float topAnchor = Math.Max(drawingBounds.Top + 8f, layout.OccupiedBounds.Top + 8f);
        RectangleF treatmentRect = RectangleF.Empty;

        var treatmentSizeF = MeasureSingleLine(graphics, annotation.Treatment?.Trim().ToUpperInvariant() ?? string.Empty, "Segoe UI", treatmentSize, FontStyle.Bold);
        var treatmentPreferred = new RectangleF(rightAnchor, topAnchor, treatmentSizeF.Width + 16f, treatmentSizeF.Height + 12f);

        if (!string.IsNullOrWhiteSpace(annotation.Treatment))
        {
            treatmentRect = FindFreePlacement(
                treatmentPreferred,
                new SizeF(treatmentPreferred.Width, treatmentPreferred.Height),
                layout.Occupancy,
                reserved,
                drawingBounds);

            DrawHighlightedText(
                graphics,
                annotation.Treatment.Trim().ToUpperInvariant(),
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
            .Take(3)
            .ToArray();

        foreach (DimensionAnnotation dimension in dimensions)
        {
            TextAnchor? anchor = FindDimensionAnchor(layout.TextAnchors, dimension.Axis);
            string valueText = dimension.Value.Trim();
            SizeF desiredSize = MeasureSingleLine(graphics, valueText, "Segoe UI", dimensionSize, FontStyle.Bold);

            RectangleF preferredRect = anchor is null
                ? new RectangleF(rightAnchor, topAnchor + 42f, desiredSize.Width + 10f, desiredSize.Height + 8f)
                : new RectangleF(
                    anchor.Bounds.Right + 6f,
                    anchor.Bounds.Top - 2f,
                    desiredSize.Width + 10f,
                    desiredSize.Height + 8f);

            RectangleF dimensionRect = FindFreePlacement(
                preferredRect,
                new SizeF(preferredRect.Width, preferredRect.Height),
                layout.Occupancy,
                reserved,
                drawingBounds);

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

        float summaryTop = Math.Max(
            drawingBounds.Top + drawingBounds.Height * 0.46f,
            treatmentRect.IsEmpty ? drawingBounds.Top + drawingBounds.Height * 0.46f : treatmentRect.Bottom + 14f);
        float summaryLeft = rightAnchor;
        float summaryRowGap = 10f;
        float summaryColumnGap = 18f;

        RectangleF quantityRect = PlaceInlineValueOverlay(
            graphics,
            layout,
            reserved,
            drawingBounds,
            summaryLeft,
            summaryTop,
            annotation.Quantity,
            titleBrush,
            valueSize);

        RectangleF materialRect = PlaceInlineValueOverlay(
            graphics,
            layout,
            reserved,
            drawingBounds,
            Math.Min(drawingBounds.Right - drawingBounds.Width * 0.18f, quantityRect.Right + summaryColumnGap),
            summaryTop,
            annotation.Material,
            titleBrush,
            valueSize);

        float secondRowTop = Math.Max(quantityRect.Bottom, materialRect.Bottom) + summaryRowGap;

        PlaceInlineValueOverlay(
            graphics,
            layout,
            reserved,
            drawingBounds,
            summaryLeft,
            secondRowTop,
            annotation.DeliveryDate,
            titleBrush,
            valueSize);

        PlaceInlineValueOverlay(
            graphics,
            layout,
            reserved,
            drawingBounds,
            Math.Min(drawingBounds.Right - drawingBounds.Width * 0.18f, summaryLeft + quantityRect.Width + summaryColumnGap),
            secondRowTop,
            annotation.OrderNumber,
            titleBrush,
            valueSize);
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

    private static RectangleF PlaceInlineValueOverlay(
        Graphics graphics,
        PageLayoutAnalysis layout,
        List<RectangleF> reserved,
        RectangleF pageBounds,
        float left,
        float top,
        string value,
        Brush valueBrush,
        float valueMaxFontSize)
    {
        string normalizedValue = value.Trim();
        if (string.IsNullOrWhiteSpace(normalizedValue))
            return RectangleF.Empty;

        SizeF valueSize = MeasureSingleLine(graphics, normalizedValue, "Segoe UI", valueMaxFontSize, FontStyle.Bold);
        SizeF desiredSize = new SizeF(valueSize.Width + 8f, valueSize.Height + 6f);
        RectangleF preferred = new RectangleF(left, top, desiredSize.Width, desiredSize.Height);
        RectangleF placed = FindFreePlacement(preferred, desiredSize, layout.Occupancy, reserved, pageBounds);

        DrawInlineValue(
            graphics,
            normalizedValue,
            placed,
            valueBrush,
            "Segoe UI",
            valueMaxFontSize,
            FontStyle.Bold,
            StringAlignment.Near);

        reserved.Add(Inflate(placed, 2f));
        return placed;
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

    private static SizeF MeasureSingleLine(Graphics graphics, string text, string fontFamily, float maxFontSize, FontStyle style)
    {
        using Font font = CreateBestFitFont(graphics, text, fontFamily, maxFontSize, Math.Max(8f, maxFontSize - 4f), style, 2000f);
        SizeF measured = graphics.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic);
        return measured;
    }

    private static RectangleF FindFreePlacement(
        RectangleF preferred,
        SizeF size,
        OccupancyGrid occupancy,
        IReadOnlyList<RectangleF> reserved,
        RectangleF pageBounds)
    {
        foreach (PointF offset in GenerateOffsets(96f, 8f))
        {
            RectangleF candidate = new RectangleF(preferred.Left + offset.X, preferred.Top + offset.Y, size.Width, size.Height);
            if (!IsInsidePage(candidate, pageBounds))
                continue;

            if (occupancy.Intersects(candidate) || IntersectsAny(candidate, reserved))
                continue;

            return candidate;
        }

        return ClampToPage(preferred, pageBounds);
    }

    private static IEnumerable<PointF> GenerateOffsets(float maxDistance, float step)
    {
        yield return PointF.Empty;

        for (float radius = step; radius <= maxDistance; radius += step)
        {
            for (float dx = -radius; dx <= radius; dx += step)
            {
                float dy = radius - Math.Abs(dx);

                if (dy < 0f)
                    continue;

                yield return new PointF(dx, -dy);
                if (dy > 0f)
                    yield return new PointF(dx, dy);
            }
        }
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

            if (best is null || anchor.Bounds.Right > best.Value.Bounds.Right)
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
