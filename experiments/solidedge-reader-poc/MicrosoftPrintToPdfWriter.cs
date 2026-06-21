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

        int margin = Math.Max(24, pageBounds.Width / 40);
        int gap = Math.Max(18, pageBounds.Width / 60);
        int annotationWidth = Math.Min(Math.Max(pageBounds.Width / 3, 280), Math.Max(320, pageBounds.Width / 2));
        int drawingWidth = Math.Max(1, pageBounds.Width - margin * 2 - annotationWidth - gap);

        var drawingBounds = new Rectangle(
            pageBounds.Left + margin,
            pageBounds.Top + margin,
            drawingWidth,
            pageBounds.Height - margin * 2);

        var annotationBounds = new Rectangle(
            drawingBounds.Right + gap,
            pageBounds.Top + margin,
            Math.Max(1, pageBounds.Right - margin - (drawingBounds.Right + gap)),
            pageBounds.Height - margin * 2);

        using var image = Image.FromFile(emfPath);
        RectangleF fitted = FitToBounds(image.Width, image.Height, drawingBounds);
        graphics.DrawImage(image, fitted);

        using var drawingBorderPen = new Pen(Color.FromArgb(160, 160, 160), 1);
        graphics.DrawRectangle(drawingBorderPen, drawingBounds);

        DrawAnnotationPanel(graphics, annotationBounds, annotation);
    }

    private static void DrawAnnotationPanel(Graphics graphics, Rectangle bounds, SheetAnnotation annotation)
    {
        using var backgroundBrush = new SolidBrush(Color.White);
        using var borderPen = new Pen(Color.FromArgb(100, 100, 100), 1.2f);
        using var titleBrush = new SolidBrush(Color.FromArgb(35, 35, 35));
        using var bodyBrush = new SolidBrush(Color.FromArgb(50, 50, 50));
        using var mutedBrush = new SolidBrush(Color.FromArgb(90, 90, 90));
        using var highlightBrush = new SolidBrush(Color.FromArgb(255, 249, 233, 128));
        using var highlightBorderPen = new Pen(Color.FromArgb(175, 145, 0), 1.2f);
        using var valueBadgeBorder = new Pen(Color.FromArgb(75, 75, 75), 1.1f);
        using var boxBorderPen = new Pen(Color.FromArgb(130, 130, 130), 1.1f);

        using var sectionLabelFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var fieldLabelFont = new Font("Segoe UI", 7.8f, FontStyle.Bold);
        using var bodyFont = new Font("Segoe UI", 11f, FontStyle.Regular);
        using var strongFont = new Font("Segoe UI", 12.5f, FontStyle.Bold);
        using var treatmentFont = new Font("Segoe UI", 14.5f, FontStyle.Bold);
        using var dimensionValueFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);

        graphics.FillRectangle(backgroundBrush, bounds);
        graphics.DrawRectangle(borderPen, bounds);

        int inset = Math.Max(12, bounds.Width / 24);
        int x = bounds.Left + inset;
        int y = bounds.Top + inset;
        int contentWidth = Math.Max(1, bounds.Width - inset * 2);

        if (!string.IsNullOrWhiteSpace(annotation.Treatment))
        {
            DrawSectionLabel(graphics, x, y, contentWidth, "Treatment", sectionLabelFont, mutedBrush);
            y += (int)Math.Ceiling(sectionLabelFont.GetHeight(graphics)) + 5;

            int bandHeight = Math.Max(40, (int)Math.Ceiling(treatmentFont.GetHeight(graphics)) + 16);
            var bandRect = new Rectangle(x, y, contentWidth, bandHeight);
            graphics.FillRectangle(highlightBrush, bandRect);
            graphics.DrawRectangle(highlightBorderPen, bandRect);

            var treatmentFormat = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoClip,
            };

            var treatmentTextRect = new RectangleF(bandRect.Left + 10, bandRect.Top + 2, bandRect.Width - 20, bandRect.Height - 4);
            graphics.DrawString(annotation.Treatment.Trim(), treatmentFont, titleBrush, treatmentTextRect, treatmentFormat);
            y = bandRect.Bottom + 14;
        }

        if (annotation.Dimensions.Count > 0)
        {
            DrawSectionLabel(graphics, x, y, contentWidth, "Dimensions", sectionLabelFont, mutedBrush);
            y += (int)Math.Ceiling(sectionLabelFont.GetHeight(graphics)) + 5;

            foreach (DimensionAnnotation dimension in annotation.Dimensions)
            {
                y = DrawDimensionRow(
                    graphics,
                    new Rectangle(x, y, contentWidth, 34),
                    dimension,
                    fieldLabelFont,
                    dimensionValueFont,
                    bodyBrush,
                    titleBrush,
                    valueBadgeBorder);
                y += 8;
            }
        }

        y += 2;

        int summaryWidth = contentWidth;
        int summaryGap = 8;
        int columnWidth = Math.Max(1, (summaryWidth - summaryGap) / 2);
        int fieldHeight = 66;

        DrawQuantityField(
            graphics,
            new Rectangle(x, y, columnWidth, fieldHeight),
            annotation.Quantity,
            fieldLabelFont,
            strongFont,
            bodyBrush,
            boxBorderPen,
            valueBadgeBorder);

        DrawValueField(
            graphics,
            new Rectangle(x + columnWidth + summaryGap, y, summaryWidth - columnWidth - summaryGap, fieldHeight),
            "Material",
            annotation.Material,
            fieldLabelFont,
            strongFont,
            bodyBrush,
            boxBorderPen);

        y += fieldHeight + 10;

        DrawValueField(
            graphics,
            new Rectangle(x, y, summaryWidth, 60),
            "Delivery date",
            annotation.DeliveryDate,
            fieldLabelFont,
            strongFont,
            bodyBrush,
            boxBorderPen);

        y += 70;

        DrawValueField(
            graphics,
            new Rectangle(x, y, summaryWidth, 60),
            "Order number",
            annotation.OrderNumber,
            fieldLabelFont,
            strongFont,
            bodyBrush,
            boxBorderPen);
    }

    private static void DrawSectionLabel(Graphics graphics, int x, int y, int width, string label, Font font, Brush brush)
    {
        var format = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            FormatFlags = StringFormatFlags.NoClip,
        };

        graphics.DrawString(label.ToUpperInvariant(), font, brush, new RectangleF(x, y, width, font.GetHeight(graphics) + 4), format);
    }

    private static int DrawDimensionRow(
        Graphics graphics,
        Rectangle bounds,
        DimensionAnnotation dimension,
        Font labelFont,
        Font valueFont,
        Brush bodyBrush,
        Brush titleBrush,
        Pen valueBorderPen)
    {
        int axisSize = Math.Min(28, bounds.Height);
        int circleSize = Math.Min(Math.Max(42, bounds.Width / 4), bounds.Height);

        var axisBounds = new Rectangle(bounds.Left, bounds.Top + 2, axisSize, axisSize);
        graphics.DrawEllipse(valueBorderPen, axisBounds);

        var centerFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoClip,
        };
        graphics.DrawString(dimension.Axis, labelFont, titleBrush, axisBounds, centerFormat);

        var valueText = dimension.Value.Trim();
        SizeF valueSize = graphics.MeasureString(valueText, valueFont, bounds.Width, StringFormat.GenericTypographic);
        int valueWidth = Math.Max(circleSize, (int)Math.Ceiling(valueSize.Width) + 20);
        int valueHeight = Math.Max(circleSize, (int)Math.Ceiling(valueSize.Height) + 16);

        var valueBounds = new Rectangle(bounds.Left + axisSize + 10, bounds.Top, valueWidth, valueHeight);
        graphics.DrawEllipse(valueBorderPen, valueBounds);
        graphics.DrawString(valueText, valueFont, bodyBrush, valueBounds, centerFormat);

        return Math.Max(bounds.Bottom, valueBounds.Bottom);
    }

    private static void DrawQuantityField(
        Graphics graphics,
        Rectangle bounds,
        string value,
        Font labelFont,
        Font valueFont,
        Brush bodyBrush,
        Pen boxBorderPen,
        Pen badgeBorderPen)
    {
        DrawFieldBox(graphics, bounds, "Quantity", labelFont, bodyBrush, boxBorderPen);

        var badgeBounds = new Rectangle(
            bounds.Left + 14,
            bounds.Top + 28,
            Math.Min(58, bounds.Width - 28),
            Math.Min(58, bounds.Height - 34));

        graphics.DrawEllipse(badgeBorderPen, badgeBounds);
        graphics.DrawString(
            value.Trim(),
            valueFont,
            bodyBrush,
            badgeBounds,
            new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoClip,
            });
    }

    private static void DrawValueField(
        Graphics graphics,
        Rectangle bounds,
        string label,
        string value,
        Font labelFont,
        Font valueFont,
        Brush bodyBrush,
        Pen boxBorderPen)
    {
        DrawFieldBox(graphics, bounds, label, labelFont, bodyBrush, boxBorderPen);

        var valueFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoClip,
        };

        RectangleF valueRect = new RectangleF(bounds.Left + 10, bounds.Top + 28, bounds.Width - 20, bounds.Height - 34);
        graphics.DrawString(value.Trim(), valueFont, bodyBrush, valueRect, valueFormat);
    }

    private static void DrawFieldBox(
        Graphics graphics,
        Rectangle bounds,
        string label,
        Font labelFont,
        Brush bodyBrush,
        Pen boxBorderPen)
    {
        graphics.DrawRectangle(boxBorderPen, bounds);

        var labelRect = new RectangleF(bounds.Left + 10, bounds.Top + 6, bounds.Width - 20, labelFont.GetHeight(graphics) + 6);
        graphics.DrawString(
            label.ToUpperInvariant(),
            labelFont,
            bodyBrush,
            labelRect,
            new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Near,
                FormatFlags = StringFormatFlags.NoClip,
            });
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
