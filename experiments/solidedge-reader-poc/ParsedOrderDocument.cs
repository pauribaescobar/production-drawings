using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace SolidedgeReaderPoc;

internal sealed record ParsedOrderDocument(string OrderNumber, IReadOnlyList<ParsedOrderLineDocument> Lines)
{
    public static ParsedOrderDocument Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A parsed order JSON path is required.", nameof(path));

        if (!File.Exists(path))
            throw new FileNotFoundException("The parsed order JSON file was not found.", path);

        string json = File.ReadAllText(path);
        var serializer = new JavaScriptSerializer();
        serializer.MaxJsonLength = int.MaxValue;
        object? rootValue = serializer.DeserializeObject(json);
        var root = AsDictionary(rootValue, "root");

        string orderNumber = ReadRequiredString(root, "orderNumber");
        IReadOnlyList<ParsedOrderLineDocument> lines = ReadLines(root);

        return new ParsedOrderDocument(orderNumber, lines);
    }

    private static IReadOnlyList<ParsedOrderLineDocument> ReadLines(IReadOnlyDictionary<string, object?> root)
    {
        object? linesValue = ReadRequiredValue(root, "lines");
        var lines = AsList(linesValue, "lines");
        var parsedLines = new List<ParsedOrderLineDocument>(lines.Count);

        foreach (object? lineValue in lines)
        {
            var line = AsDictionary(lineValue, "lines[]");
            var dimensions = ReadDimensions(line);

            parsedLines.Add(new ParsedOrderLineDocument(
                ReadRequiredString(line, "reference"),
                ReadRequiredInt(line, "quantity"),
                ReadRequiredString(line, "material"),
                ReadOptionalString(line, "treatment"),
                ReadRequiredString(line, "deliveryDate"),
                dimensions));
        }

        return parsedLines;
    }

    private static IReadOnlyList<DimensionAnnotation> ReadDimensions(IReadOnlyDictionary<string, object?> line)
    {
        object? dimensionsValue = ReadOptionalValue(line, "dimensions");
        if (dimensionsValue is null)
            return Array.Empty<DimensionAnnotation>();

        var dimensions = AsDictionary(dimensionsValue, "dimensions");
        return dimensions
            .Select(pair => new DimensionAnnotation(pair.Key.Trim(), Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? string.Empty))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Axis) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToArray();
    }

    private static string ReadRequiredString(IReadOnlyDictionary<string, object?> values, string fieldName)
    {
        object? value = ReadRequiredValue(values, fieldName);
        string? text = Convert.ToString(value, CultureInfo.InvariantCulture);

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException($"The parsed order JSON field '{fieldName}' is missing or empty.");

        return text.Trim();
    }

    private static string? ReadOptionalString(IReadOnlyDictionary<string, object?> values, string fieldName)
    {
        object? value = ReadOptionalValue(values, fieldName);
        if (value is null)
            return null;

        string? text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static int ReadRequiredInt(IReadOnlyDictionary<string, object?> values, string fieldName)
    {
        object? value = ReadRequiredValue(values, fieldName);

        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"The parsed order JSON field '{fieldName}' is not a valid integer.", ex);
        }
    }

    private static object? ReadRequiredValue(IReadOnlyDictionary<string, object?> values, string fieldName)
    {
        if (!values.TryGetValue(fieldName, out object? value) || value is null)
            throw new InvalidDataException($"The parsed order JSON field '{fieldName}' is missing.");

        return value;
    }

    private static object? ReadOptionalValue(IReadOnlyDictionary<string, object?> values, string fieldName)
    {
        return values.TryGetValue(fieldName, out object? value) ? value : null;
    }

    private static Dictionary<string, object?> AsDictionary(object? value, string fieldName)
    {
        if (value is Dictionary<string, object?> typedDictionary)
            return typedDictionary;

        if (value is IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (DictionaryEntry entry in dictionary)
            {
                result[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty] = entry.Value;
            }

            return result;
        }

        throw new InvalidDataException($"The parsed order JSON field '{fieldName}' must be an object.");
    }

    private static IReadOnlyList<object?> AsList(object? value, string fieldName)
    {
        if (value is IEnumerable enumerable && value is not string)
        {
            var result = new List<object?>();

            foreach (object? item in enumerable)
            {
                result.Add(item);
            }

            return result;
        }

        throw new InvalidDataException($"The parsed order JSON field '{fieldName}' must be an array.");
    }
}

internal sealed record ParsedOrderLineDocument(
    string Reference,
    int Quantity,
    string Material,
    string? Treatment,
    string DeliveryDate,
    IReadOnlyList<DimensionAnnotation> Dimensions);

internal sealed record DimensionAnnotation(string Axis, string Value);
