using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SolidedgeReaderPoc;

internal static class WorkerJson
{
    public static string Serialize(WorkerResult result)
    {
        var builder = new StringBuilder();
        builder.Append('{');
        AppendProperty(builder, "status", result.Status);
        builder.Append(',');
        AppendProperty(builder, "jobId", result.JobId);
        builder.Append(',');
        AppendProperty(builder, "inputPath", result.InputPath);
        builder.Append(',');
        AppendProperty(builder, "outputDirectory", result.OutputDirectory);
        builder.Append(',');
        AppendProperty(builder, "sheetCount", result.SheetCount.ToString(CultureInfo.InvariantCulture), isString: false);
        builder.Append(',');
        AppendArrayProperty(builder, "artifacts", result.Artifacts);
        builder.Append(',');
        AppendNullableProperty(builder, "errorCode", result.ErrorCode);
        builder.Append(',');
        AppendNullableProperty(builder, "errorMessage", result.ErrorMessage);
        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendProperty(StringBuilder builder, string name, string value, bool isString = true)
    {
        builder.Append('"').Append(Escape(name)).Append('"').Append(':');
        if (isString)
        {
            builder.Append('"').Append(Escape(value)).Append('"');
            return;
        }

        builder.Append(value);
    }

    private static void AppendNullableProperty(StringBuilder builder, string name, string? value)
    {
        builder.Append('"').Append(Escape(name)).Append('"').Append(':');
        if (value is null)
        {
            builder.Append("null");
            return;
        }

        builder.Append('"').Append(Escape(value)).Append('"');
    }

    private static void AppendArrayProperty(StringBuilder builder, string name, IReadOnlyList<string> values)
    {
        builder.Append('"').Append(Escape(name)).Append('"').Append(':');
        builder.Append('[');

        bool first = true;
        foreach (string value in values)
        {
            if (!first)
            {
                builder.Append(',');
            }

            builder.Append('"').Append(Escape(value)).Append('"');
            first = false;
        }

        builder.Append(']');
    }

    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length + 8);

        foreach (char character in value)
        {
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (character < 0x20)
                    {
                        builder.Append("\\u");
                        builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}
