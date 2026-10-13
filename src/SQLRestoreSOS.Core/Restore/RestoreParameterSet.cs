namespace SQLRestoreSOS.Core.Restore;

/// <summary>
/// A named bag of proc parameter values (keyed by parameter name, no leading '@'). Deliberately
/// dictionary-backed rather than a fixed POCO: the Advanced tab is data-driven from
/// IRestoreEngine.DescribeParameters(), and each engine (sp_DatabaseRestore / sp_RestoreGene) has
/// its own distinct parameter list, so a single flexible bag lets both share one type.
/// A value of null means "leave at the stored procedure's own default" - RestoreScriptBuilder
/// omits null entries entirely so generated scripts stay short and readable.
/// </summary>
public sealed class RestoreParameterSet
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, object?> Values => _values;

    public void Set(string name, object? value) => _values[name] = value;

    public object? Get(string name) => _values.TryGetValue(name, out var v) ? v : null;

    public bool GetBool(string name, bool defaultValue = false) =>
        Get(name) switch
        {
            null => defaultValue,
            bool b => b,
            var v => Convert.ToBoolean(v)
        };

    public string? GetString(string name) => Get(name) as string;

    public int? GetInt(string name) =>
        Get(name) switch
        {
            null => null,
            int i => i,
            var v => Convert.ToInt32(v)
        };

    public DateTime? GetDateTime(string name) =>
        Get(name) switch
        {
            null => null,
            DateTime dt => dt,
            var v => Convert.ToDateTime(v)
        };

    public RestoreParameterSet Clone()
    {
        var clone = new RestoreParameterSet();
        foreach (var kv in _values)
        {
            clone._values[kv.Key] = kv.Value;
        }

        return clone;
    }
}
