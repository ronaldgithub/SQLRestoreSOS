using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SQLRestoreSOS.Core.BackupChain;

namespace SQLRestoreSOS.App.ViewModels;

/// <summary>One row of the timeline grid: a discovered backup plus its chain status and position on the time axis.</summary>
public sealed partial class BackupTimelineRowViewModel : ObservableObject
{
    private const string DateFormat = "yyyy-MM-dd HH:mm:ss";
    private const double MinBarFraction = 0.01;

    public BackupTimelineRowViewModel(BackupChainRow row, DateTime axisStart, DateTime axisEnd)
    {
        Entry = row.Entry;

        (StatusGlyph, StatusBrush) = row.Status switch
        {
            BackupChainStatus.Restorable => ("✓", (IBrush)Brushes.LimeGreen),
            BackupChainStatus.Broken => ("✗", Brushes.OrangeRed),
            _ => ("–", Brushes.Gray)
        };

        TypeLabel = Entry.Type switch
        {
            BackupType.Full => "Full",
            BackupType.Differential => "Differential",
            _ => "Log"
        };

        Start = Entry.BackupStartDate.ToString(DateFormat);
        Finish = Entry.BackupFinishDate.ToString(DateFormat);
        StopAtFrom = row.StopAtFrom?.ToString(DateFormat) ?? string.Empty;
        StopAtTo = row.StopAtTo?.ToString(DateFormat) ?? string.Empty;
        FileName = Path.GetFileName(Entry.FilePath);
        FullPath = Entry.FilePath;
        Note = row.Note;

        var span = (axisEnd - axisStart).TotalSeconds;
        if (span <= 0)
        {
            Offset = new GridLength(0, GridUnitType.Star);
            Bar = new GridLength(1, GridUnitType.Star);
            Remainder = new GridLength(0, GridUnitType.Star);
            return;
        }

        var left = Math.Clamp((Entry.BackupStartDate - axisStart).TotalSeconds / span, 0, 1);
        var width = Math.Clamp((Entry.BackupFinishDate - Entry.BackupStartDate).TotalSeconds / span, MinBarFraction, 1);
        left = Math.Min(left, 1 - width);
        Offset = new GridLength(left, GridUnitType.Star);
        Bar = new GridLength(width, GridUnitType.Star);
        Remainder = new GridLength(Math.Max(0, 1 - left - width), GridUnitType.Star);
    }

    public BackupFileEntry Entry { get; }
    public string StatusGlyph { get; }
    public IBrush StatusBrush { get; }
    public string TypeLabel { get; }
    public string Start { get; }
    public string Finish { get; }
    public string StopAtFrom { get; }
    public string StopAtTo { get; }
    public string FileName { get; }
    public string FullPath { get; }
    public string Note { get; }

    // Proportional bar on the shared time axis: Offset | Bar | Remainder as star-sized columns.
    public GridLength Offset { get; }
    public GridLength Bar { get; }
    public GridLength Remainder { get; }

    public IBrush BarBrush => TypeLabel switch
    {
        "Full" => Brushes.SteelBlue,
        "Differential" => Brushes.MediumPurple,
        _ => Brushes.Goldenrod
    };

    /// <summary>"1", "2", ... = position in the restore sequence for the chosen point in time; empty = not applied.</summary>
    [ObservableProperty] private string _stepLabel = string.Empty;

    [ObservableProperty] private double _rowOpacity = 1.0;
}

/// <summary>
/// The "bigger dialog" shown after Discover backup chain: all backups in time order with chain status, plus a
/// slider that chooses the point in time and marks which files a restore to that point would apply.
/// </summary>
public partial class BackupTimelineDialogViewModel : ViewModelBase
{
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    private readonly Func<Task<BackupChainTimeline?>>? _refreshAsync;
    private IReadOnlyList<BackupChainRow> _analyzed = [];
    private DateTime _sliderStart;
    private bool _updatingText;

    /// <param name="refreshAsync">Re-runs backup discovery; null disables the Refresh button.</param>
    public BackupTimelineDialogViewModel(
        string databaseName,
        BackupChainTimeline timeline,
        DateTime? initialPointInTime,
        Func<Task<BackupChainTimeline?>>? refreshAsync = null)
    {
        DatabaseName = databaseName;
        _refreshAsync = refreshAsync;
        Load(timeline, initialPointInTime, followLatest: initialPointInTime is null);
    }

    public string DatabaseName { get; }
    public bool CanRefresh => _refreshAsync is not null;

    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _axisStartText = string.Empty;
    [ObservableProperty] private string _axisEndText = string.Empty;
    [ObservableProperty] private string _sliderStartText = string.Empty;
    [ObservableProperty] private string _sliderEndText = string.Empty;
    [ObservableProperty] private double _sliderMaximum = 1;
    [ObservableProperty] private string _refreshStatus = string.Empty;
    [ObservableProperty] private bool _isRefreshing;

    public ObservableCollection<BackupTimelineRowViewModel> Rows { get; } = new();

    private void Load(BackupChainTimeline timeline, DateTime? pointInTime, bool followLatest)
    {
        _analyzed = BackupChainAnalyzer.Analyze(timeline);
        Rows.Clear();

        if (_analyzed.Count > 0)
        {
            var axisStart = _analyzed.Min(r => r.Entry.BackupStartDate);
            var axisEnd = _analyzed.Max(r => r.Entry.BackupFinishDate);
            AxisStartText = axisStart.ToString("yyyy-MM-dd HH:mm");
            AxisEndText = axisEnd.ToString("yyyy-MM-dd HH:mm");

            foreach (var row in _analyzed)
            {
                Rows.Add(new BackupTimelineRowViewModel(row, axisStart, axisEnd));
            }

            // Earliest usable point: just after the earliest full finished (STOPAT at/before a full's finish
            // is rejected by SQL Server with Msg 4335).
            var earliestFull = _analyzed.Where(r => r.Entry.Type == BackupType.Full && r.IsRestorable)
                                        .Select(r => r.Entry.BackupFinishDate)
                                        .DefaultIfEmpty(axisStart)
                                        .Min();
            _sliderStart = CeilingToSecond(earliestFull).AddSeconds(1);
            var sliderEnd = axisEnd > _sliderStart ? axisEnd : _sliderStart;

            SliderStartText = _sliderStart.ToString(TimeFormat);
            SliderEndText = sliderEnd.ToString(TimeFormat);
            SliderMaximum = Math.Max(1, Math.Floor((sliderEnd - _sliderStart).TotalSeconds));

            var initial = !followLatest && pointInTime is { } p && p >= _sliderStart && p <= sliderEnd ? p : sliderEnd;
            SelectedSeconds = Math.Floor((initial - _sliderStart).TotalSeconds);
        }

        var broken = _analyzed.Count(r => r.Status == BackupChainStatus.Broken);
        Summary = $"{_analyzed.Count} backups found, {_analyzed.Count(r => r.IsRestorable)} restorable, {broken} with a broken link.";

        UpdateTargetFromSlider();
    }

    /// <summary>Re-scans the backup folders (new files may have arrived) and keeps the chosen point in time.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_refreshAsync is null || IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        RefreshStatus = "Refreshing...";
        try
        {
            var before = _analyzed.Select(r => r.Entry.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var wasAtLatest = Rows.Count == 0 || SelectedSeconds >= SliderMaximum;
            var keep = Rows.Count == 0 ? (DateTime?)null : TargetFromSlider;

            var timeline = await _refreshAsync();
            if (timeline is null)
            {
                RefreshStatus = "Refresh failed.";
                return;
            }

            Load(timeline, keep, followLatest: wasAtLatest);

            var added = _analyzed.Count(r => !before.Contains(r.Entry.FilePath));
            RefreshStatus = $"{DateTime.Now:HH:mm:ss}: " + (added == 0 ? "no new backups." : $"{added} new backup(s) found.");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>Slider position: whole seconds after the earliest usable point.</summary>
    [ObservableProperty] private double _selectedSeconds;

    /// <summary>Editable "yyyy-MM-dd HH:mm:ss" box kept in sync with the slider.</summary>
    [ObservableProperty] private string _targetText = string.Empty;

    [ObservableProperty] private string _planSummary = string.Empty;
    [ObservableProperty] private string? _problem;

    /// <summary>True when the user chose "Use" (either a point in time or latest); false when cancelled/closed.</summary>
    public bool Applied { get; private set; }

    /// <summary>The chosen point in time, or null for "restore to latest" (no STOPAT).</summary>
    public DateTime? SelectedPointInTime { get; private set; }

    public DateTime TargetFromSlider => _sliderStart.AddSeconds(Math.Round(SelectedSeconds));

    public event Action? RequestClose;

    partial void OnSelectedSecondsChanged(double value) => UpdateTargetFromSlider();

    partial void OnTargetTextChanged(string value)
    {
        if (_updatingText || Rows.Count == 0)
        {
            return;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            var seconds = Math.Clamp(Math.Round((parsed - _sliderStart).TotalSeconds), 0, SliderMaximum);
            SelectedSeconds = seconds; // fires OnSelectedSecondsChanged, which reformats the text
        }
    }

    private void UpdateTargetFromSlider()
    {
        if (Rows.Count == 0)
        {
            PlanSummary = "No backups found.";
            return;
        }

        var target = TargetFromSlider;

        _updatingText = true;
        TargetText = target.ToString(TimeFormat);
        _updatingText = false;

        MarkRows(BackupRestorePlanner.Plan(_analyzed, target), target);
    }

    private void MarkRows(BackupRestorePlan plan, DateTime target)
    {
        var order = plan.Steps.Select((e, i) => (e, i)).ToDictionary(x => x.e.FilePath, x => x.i + 1, StringComparer.OrdinalIgnoreCase);

        foreach (var row in Rows)
        {
            if (order.TryGetValue(row.Entry.FilePath, out var step))
            {
                row.StepLabel = step.ToString(CultureInfo.InvariantCulture);
                row.RowOpacity = 1.0;
            }
            else
            {
                row.StepLabel = string.Empty;
                row.RowOpacity = 0.4;
            }
        }

        Problem = plan.Problem;
        PlanSummary = plan.Problem is not null
            ? plan.Problem
            : $"Restore to {target.ToString(TimeFormat)}: " + string.Join(" → ", plan.Steps.Select(s => s.Type switch
            {
                BackupType.Full => $"Full {s.BackupFinishDate:HH:mm:ss}",
                BackupType.Differential => $"Diff {s.BackupFinishDate:HH:mm:ss}",
                _ => $"Log {s.BackupFinishDate:HH:mm:ss}"
            }));
    }

    [RelayCommand]
    private void UsePointInTime()
    {
        if (Problem is not null)
        {
            return;
        }

        SelectedPointInTime = TargetFromSlider;
        Applied = true;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void UseLatest()
    {
        SelectedPointInTime = null;
        Applied = true;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke();

    private static DateTime CeilingToSecond(DateTime value) =>
        value.Ticks % TimeSpan.TicksPerSecond == 0
            ? value
            : new DateTime((value.Ticks / TimeSpan.TicksPerSecond + 1) * TimeSpan.TicksPerSecond, value.Kind);
}
