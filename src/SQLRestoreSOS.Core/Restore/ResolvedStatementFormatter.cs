using System.Text.RegularExpressions;

namespace SQLRestoreSOS.Core.Restore;

/// <summary>
/// Formats the raw "-WhatIf" dry-run text captured from SqlConnection.InfoMessage (see
/// IRestoreEngine.ResolveActualStatementsAsync) into a readable, commented script: one blank
/// line between statements, and a short "-- what is this" comment above each one inferred from
/// its own text (RESTORE DATABASE/LOG, FULL/DIFF/LOG path, WITH RECOVERY/STANDBY, etc.).
/// </summary>
public static class ResolvedStatementFormatter
{
    public static string Format(string rawResolvedText)
    {
        if (string.IsNullOrWhiteSpace(rawResolvedText))
        {
            return rawResolvedText;
        }

        var normalized = rawResolvedText.Replace("\r\n", "\n").Trim();
        var statements = SplitIntoStatements(normalized);

        if (statements.Count == 0)
        {
            return rawResolvedText;
        }

        var blocks = statements.Select(statement =>
            $"-- {DescribeStatement(statement)}\r\n{BreakIntoReadableLines(statement).TrimEnd().TrimEnd(';')};");
        return string.Join("\r\n\r\n", blocks);
    }

    /// <summary>How many individual statements <see cref="Format"/> would produce - lets the UI show "N statements resolved" without reformatting.</summary>
    public static int CountStatements(string rawResolvedText)
    {
        if (string.IsNullOrWhiteSpace(rawResolvedText))
        {
            return 0;
        }

        return SplitIntoStatements(rawResolvedText.Replace("\r\n", "\n").Trim()).Count;
    }

    /// <summary>
    /// sp_DatabaseRestore builds @sql as one long single-line string (FROM DISK/WITH/MOVE all run
    /// together, only a trailing CRLF at the very end) - break it at the natural clause boundaries
    /// so it reads like a hand-written script instead of requiring horizontal scrolling.
    /// </summary>
    private static string BreakIntoReadableLines(string statement)
    {
        var result = statement;
        result = Regex.Replace(result, @"\s+FROM\s+(DISK|URL)\s*=", "\r\nFROM $1 =", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"\s+WITH\s+", "\r\nWITH ", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @",\s*MOVE\s+", ",\r\n    MOVE ", RegexOptions.IgnoreCase);
        return result;
    }

    private static List<string> SplitIntoStatements(string normalized)
    {
        // Preferred case: each PRINTed statement arrived as its own InfoMessage, so blank lines
        // already separate them cleanly - this also keeps a statement's own internal line breaks
        // (if the server formatted one statement across multiple lines) intact as a single block.
        var blankLineBlocks = Regex.Split(normalized, @"\n\s*\n")
            .Select(b => b.Trim())
            .Where(b => b.Length > 0)
            .ToList();

        if (blankLineBlocks.Count > 1)
        {
            return blankLineBlocks;
        }

        // Fallback: no blank-line separation found - split right before each RESTORE keyword that
        // starts a new statement, in case multiple statements arrived concatenated together.
        var singleBlock = blankLineBlocks.Count == 1 ? blankLineBlocks[0] : normalized;
        var splitOnRestore = Regex.Split(singleBlock, @"(?=^RESTORE )", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        return splitOnRestore.Count > 0 ? splitOnRestore : [singleBlock];
    }

    private static string DescribeStatement(string statement)
    {
        var upper = statement.ToUpperInvariant();

        if (!upper.Contains("RESTORE "))
        {
            // A block with a MOVE clause but no RESTORE keyword is a malformed/truncated fragment
            // (observed in practice: sp_DatabaseRestore can print just the tail of a FULL restore
            // statement - the MOVE clauses - with its "RESTORE DATABASE ... WITH NORECOVERY,
            // REPLACE" prefix missing from the message stream entirely). Flag it loudly rather than
            // silently showing what looks like a complete, trustworthy script.
            return upper.Contains("MOVE ")
                ? "⚠ INCOMPLETE STATEMENT - missing its RESTORE prefix (known sp_DatabaseRestore output quirk; verify in SSMS before relying on this)"
                : "Status message";
        }

        var isLog = upper.TrimStart().StartsWith("RESTORE LOG", StringComparison.Ordinal);
        var hasFromDisk = upper.Contains("FROM DISK") || upper.Contains("FROM URL");

        if (!hasFromDisk)
        {
            if (upper.Contains("WITH RECOVERY"))
            {
                return "Bring the database online (RECOVERY) - restore chain complete";
            }

            return isLog ? "Apply transaction log backup" : "Restore database";
        }

        var mode = upper.Contains("WITH STANDBY") ? ", STANDBY (readable, more logs can follow)"
            : upper.Contains("NORECOVERY") ? ", NORECOVERY (more backups can still be applied)"
            : string.Empty;

        if (isLog)
        {
            return $"Apply transaction log backup{mode}";
        }

        if (upper.Contains(@"\FULL\") || upper.Contains("/FULL/"))
        {
            return $"Restore FULL backup{mode}";
        }

        if (upper.Contains(@"\DIFF\") || upper.Contains("/DIFF/"))
        {
            return $"Restore DIFFERENTIAL backup{mode}";
        }

        return $"Restore database from disk{mode}";
    }
}
