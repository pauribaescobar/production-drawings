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

            RenderPage(graphics, new Rectangle(0, 0, pageWidth, pageHeight), emfPath, annotation);
        }

        return bitmap;
    }

    private static void RenderPage(Graphics graphics, Rectangle pageBounds, string emfPath, SheetAnnotation annotation)
    {
        graphics.Clear(Color.White);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using var image = Image.FromFile(emfPath);
        RectangleF fitted = FitToBounds(image.Width, image.Height, pageBounds);
        graphics.DrawImage(image, fitted);
        DrawOverlayAnnotations(graphics, fitted, annotation);
    }

    private static void DrawOverlayAnnotations(Graphics graphics, RectangleF drawingBounds, SheetAnnotation annotation)
    {
        using var titleBrush = new SolidBrush(Color.FromArgb(30, 30, 30));
        using var mutedBrush = new SolidBrush(Color.FromArgb(90, 90, 90));
        using var highlightBrush = new SolidBrush(Color.FromArgb(140, 255, 245, 90));

        float labelSize = 8.5f;
        float valueSize = 12.5f;
        float treatmentSize = 15.5f;
        float dimensionSize = 11.0f;

        var treatmentRect = new RectangleF(
            drawingBounds.Left + drawingBounds.Width * 0.63f,
            drawingBounds.Top + drawingBounds.Height * 0.03f,
            drawingBounds.Width * 0.26f,
            54f);

        if (!string.IsNullOrWhiteSpace(annotation.Treatment))
        {
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
        }

        IReadOnlyList<DimensionAnnotation> dimensions = annotation.Dimensions
            .Where(dimension => !string.IsNullOrWhiteSpace(dimension.Axis) && !string.IsNullOrWhiteSpace(dimension.Value))
            .Take(3)
            .ToArray();

        float dimensionX = drawingBounds.Left + drawingBounds.Width * 0.68f;
        float dimensionY = drawingBounds.Top + drawingBounds.Height * 0.15f;
        float dimensionRowHeight = 38f;

        foreach (DimensionAnnotation dimension in dimensions)
        {
            var dimensionRect = new RectangleF(
                dimensionX,
                dimensionY,
                drawingBounds.Width * 0.18f,
                dimensionRowHeight);

            DrawInlineValue(
                graphics,
                $"{dimension.Axis.Trim().ToUpperInvariant()} {dimension.Value.Trim()}",
                dimensionRect,
                titleBrush,
                "Segoe UI",
                dimensionSize,
                FontStyle.Bold,
                StringAlignment.Near);

            dimensionY += dimensionRowHeight * 1.12f;
        }

        float summaryTop = drawingBounds.Top + drawingBounds.Height * 0.56f;
        float summaryRowHeight = 58f;

        DrawStackedField(
            graphics,
            new RectangleF(drawingBounds.Left + drawingBounds.Width * 0.41f, summaryTop, drawingBounds.Width * 0.12f, summaryRowHeight),
            "Quantity",
            annotation.Quantity,
            mutedBrush,
            titleBrush,
            "Segoe UI",
            labelSize,
            valueSize);

        DrawStackedField(
            graphics,
            new RectangleF(drawingBounds.Left + drawingBounds.Width * 0.58f, summaryTop, drawingBounds.Width * 0.20f, summaryRowHeight),
            "Material",
            annotation.Material,
            mutedBrush,
            titleBrush,
            "Segoe UI",
            labelSize,
            valueSize);

        DrawStackedField(
            graphics,
            new RectangleF(drawingBounds.Left + drawingBounds.Width * 0.39f, drawingBounds.Top + drawingBounds.Height * 0.68f, drawingBounds.Width * 0.35f, summaryRowHeight),
            "Delivery date",
            annotation.DeliveryDate,
            mutedBrush,
            titleBrush,
            "Segoe UI",
            labelSize,
            valueSize);

        DrawStackedField(
            graphics,
            new RectangleF(drawingBounds.Left + drawingBounds.Width * 0.39f, drawingBounds.Top + drawingBounds.Height * 0.77f, drawingBounds.Width * 0.40f, summaryRowHeight),
            "Order number",
            annotation.OrderNumber,
            mutedBrush,
            titleBrush,
            "Segoe UI",
            labelSize,
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

    private static void DrawStackedField(
        Graphics graphics,
        RectangleF bounds,
        string label,
        string value,
        Brush labelBrush,
        Brush valueBrush,
        string fontFamily,
        float labelMaxFontSize,
        float valueMaxFontSize)
    {
        float labelHeight = Math.Max(18f, bounds.Height * 0.40f);
        var labelRect = new RectangleF(bounds.Left, bounds.Top, bounds.Width, labelHeight);
        DrawInlineValue(
            graphics,
            label.ToUpperInvariant(),
            labelRect,
            labelBrush,
            fontFamily,
            labelMaxFontSize,
            FontStyle.Bold,
            StringAlignment.Near);

        var valueRect = new RectangleF(bounds.Left, bounds.Top + labelHeight - 1f, bounds.Width, Math.Max(18f, bounds.Height - labelHeight + 1f));
        DrawInlineValue(
            graphics,
            value.Trim(),
            valueRect,
            valueBrush,
            fontFamily,
            valueMaxFontSize,
            FontStyle.Bold,
            StringAlignment.Near);
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
}
