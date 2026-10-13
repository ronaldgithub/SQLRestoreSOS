using CommunityToolkit.Mvvm.ComponentModel;
using SQLRestoreSOS.Core.BackupChain.Engines;
using SQLRestoreSOS.Core.Restore;

namespace SQLRestoreSOS.App.ViewModels;

/// <summary>
/// Advanced tab rows are data-driven from IRestoreEngine.DescribeParameters(): each descriptor
/// becomes a row of the CLR-appropriate type below, and editing a row's Value writes straight
/// back into the shared RestoreParameterSet (and triggers SQL preview regeneration).
/// </summary>
public abstract partial class ParameterEditorRow : ViewModelBase
{
    public required string Name { get; init; }

    public required string HelpText { get; init; }

    public required RestoreParameterGroup Group { get; init; }
}

/// <summary>A non-editable row used purely to render a group heading in the flat Advanced tab list.</summary>
public sealed class GroupHeaderRow : ParameterEditorRow
{
    public string Title => Group switch
    {
        RestoreParameterGroup.SourceAndTarget => "Source & Target",
        RestoreParameterGroup.BackupLocations => "Backup Locations",
        RestoreParameterGroup.PointInTimeAndChain => "Point-in-Time & Chain Control",
        RestoreParameterGroup.FilePlacement => "File Placement (MOVE)",
        RestoreParameterGroup.RecoveryBehavior => "Recovery Behavior",
        RestoreParameterGroup.ValidationAndSafety => "Validation & Safety",
        RestoreParameterGroup.PerformanceTuning => "Performance Tuning",
        RestoreParameterGroup.ExecutionAndDiagnostics => "Execution & Diagnostics",
        _ => Group.ToString()
    };
}

public sealed partial class BitParameterRow : ParameterEditorRow
{
    [ObservableProperty]
    private bool _value;

    public Action<string, bool>? OnChanged { get; init; }

    partial void OnValueChanged(bool value) => OnChanged?.Invoke(Name, value);
}

public sealed partial class IntParameterRow : ParameterEditorRow
{
    [ObservableProperty]
    private int? _value;

    public Action<string, int?>? OnChanged { get; init; }

    partial void OnValueChanged(int? value) => OnChanged?.Invoke(Name, value);
}

public sealed partial class StringParameterRow : ParameterEditorRow
{
    [ObservableProperty]
    private string? _value;

    public Action<string, string?>? OnChanged { get; init; }

    partial void OnValueChanged(string? value) => OnChanged?.Invoke(Name, value);
}

/// <summary>Same editing surface as String, kept as a distinct type so a future folder-picker button can target just these rows.</summary>
public sealed partial class PathParameterRow : ParameterEditorRow
{
    [ObservableProperty]
    private string? _value;

    public Action<string, string?>? OnChanged { get; init; }

    partial void OnValueChanged(string? value) => OnChanged?.Invoke(Name, value);
}

public sealed partial class DateTimeParameterRow : ParameterEditorRow
{
    [ObservableProperty]
    private DateTime? _value;

    public Action<string, DateTime?>? OnChanged { get; init; }

    partial void OnValueChanged(DateTime? value) => OnChanged?.Invoke(Name, value);
}

public static class ParameterEditorRowFactory
{
    /// <summary>Builds the flat, group-header-interspersed row list the Advanced tab binds to.</summary>
    public static List<ParameterEditorRow> Build(
        IReadOnlyList<RestoreParameterDescriptor> descriptors,
        RestoreParameterSet parameters,
        Action onAnyChange)
    {
        var rows = new List<ParameterEditorRow>();

        foreach (var group in Enum.GetValues<RestoreParameterGroup>())
        {
            var inGroup = descriptors.Where(d => d.Group == group).ToList();
            if (inGroup.Count == 0)
            {
                continue;
            }

            rows.Add(new GroupHeaderRow { Name = string.Empty, HelpText = string.Empty, Group = group });

            foreach (var d in inGroup)
            {
                rows.Add(BuildRow(d, parameters, onAnyChange));
            }
        }

        return rows;
    }

    private static ParameterEditorRow BuildRow(RestoreParameterDescriptor d, RestoreParameterSet parameters, Action onAnyChange)
    {
        void Set(string name, object? value)
        {
            parameters.Set(name, value);
            onAnyChange();
        }

        return d.Kind switch
        {
            RestoreParameterKind.Bit or RestoreParameterKind.YesNoChar => new BitParameterRow
            {
                Name = d.Name,
                HelpText = d.HelpText,
                Group = d.Group,
                Value = parameters.GetBool(d.Name, d.DefaultValue is bool b && b),
                OnChanged = (name, value) => Set(name, value)
            },
            RestoreParameterKind.Int => new IntParameterRow
            {
                Name = d.Name,
                HelpText = d.HelpText,
                Group = d.Group,
                Value = parameters.GetInt(d.Name),
                OnChanged = (name, value) => Set(name, value)
            },
            RestoreParameterKind.DateTime => new DateTimeParameterRow
            {
                Name = d.Name,
                HelpText = d.HelpText,
                Group = d.Group,
                Value = parameters.GetDateTime(d.Name),
                OnChanged = (name, value) => Set(name, value)
            },
            RestoreParameterKind.Path => new PathParameterRow
            {
                Name = d.Name,
                HelpText = d.HelpText,
                Group = d.Group,
                Value = parameters.GetString(d.Name),
                OnChanged = Set
            },
            _ => new StringParameterRow
            {
                Name = d.Name,
                HelpText = d.HelpText,
                Group = d.Group,
                Value = parameters.GetString(d.Name),
                OnChanged = Set
            }
        };
    }
}
