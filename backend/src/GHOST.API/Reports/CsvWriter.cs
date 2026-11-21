using System.Globalization;
using System.Text;

namespace GHOST.API.Reports;

/// <summary>
/// Minimal CSV export that Excel opens directly (UTF-8 with BOM so Arabic text is kept).
/// Cells that start with a formula character are prefixed so a product or staff name can never run as a formula.
/// </summary>
public static class CsvWriter
{
    public static byte[] Build(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', headers.Select(Cell)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(',', row.Select(Cell)));

        var preamble = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble();
        return preamble.Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public static string Cell(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        if (text.Length > 0 && value is string && "=+-@\t\r".Contains(text[0]))
            text = "'" + text;

        return text.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? "\"" + text.Replace("\"", "\"\"") + "\""
            : text;
    }
}
