using System.Globalization;
using System.Text;
using SQLRestoreSOS.Core.BackupChain.Engines;

namespace SQLRestoreSOS.Core.Restore;

/// <summary>
/// Pure, offline, no-ADO.NET T-SQL text generation. Given an ordered list of (name, kind, value)
/// tuples, builds a readable, copy/paste-ready "EXEC proc @Param = value, ..." call. Null values
/// are omitted entirely so the generated script only shows what differs from the proc's own
/// defaults - this is the single most important unit-test target in the whole app.
/// </summary>
public static class RestoreScriptBuilder
{
    public static string BuildExecCall(
        string procName,
        IEnumerable<(string Name, RestoreParameterKind Kind, object? Value)> orderedParams,
        string? headerComment = null)
    {
        var lines = new List<string>();

        foreach (var (name, kind, value) in orderedParams)
        {
            if (value is null)
            {
                continue;
            }

            lines.Add($"    @{name} = {FormatLiteral(kind, value)}");
        }

        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(headerComment))
        {
            sb.Append("-- ").Append(headerComment).Append('\n');
        }

        sb.Append("EXEC ").Append(procName);

        if (lines.Count == 0)
        {
            sb.Append(';');
            return sb.ToString();
        }

        sb.Append('\n');
        sb.Append(string.Join(",\n", lines));
        sb.Append(';');
        return sb.ToString();
    }

    private static string FormatLiteral(RestoreParameterKind kind, object value) => kind switch
    {
        RestoreParameterKind.Bit => Convert.ToBoolean(value) ? "1" : "0",
        RestoreParameterKind.YesNoChar => Convert.ToBoolean(value) ? "'Y'" : "'N'",
        RestoreParameterKind.Int => Convert.ToInt32(value).ToString(CultureInfo.InvariantCulture),
        RestoreParameterKind.DateTime => $"N'{((DateTime)value).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}'",
        RestoreParameterKind.String or RestoreParameterKind.Path => $"N'{EscapeSqlString(value.ToString()!)}'",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown parameter kind.")
    };

    private static string EscapeSqlString(string value) => value.Replace("'", "''");
}
