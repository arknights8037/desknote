using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows;

namespace DestNote;

public sealed class AppData
{
    public int Version { get; set; } = 1;
    public ObservableCollection<NoteItem> Notes { get; set; } = new();
    public ObservableCollection<BillItem> Bills { get; set; } = new();
    public ObservableCollection<PlanGroup> Plans { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}

public sealed class AppSettings
{
    public bool AlwaysOnTop { get; set; }
    public bool StartWithWindows { get; set; }
    public bool CardMode { get; set; } = true;
    public double CardWindowWidth { get; set; } = 332;
    public double CardWindowHeight { get; set; } = 380;
    public string PaperTheme { get; set; } = "cream";
    public string LastTab { get; set; } = "notes";
    public string LastBillSection { get; set; } = "details";
    public double WindowLeft { get; set; } = -1;
    public double WindowTop { get; set; } = -1;
    public double WindowWidth { get; set; } = 414;
    public double WindowHeight { get; set; } = 650;
}

public sealed class NoteItem : INotifyPropertyChanged
{
    private string _text = "";
    private bool _isDone;
    private bool _isEditing;
    private DateTime? _deferredUntil;
    private DateTime? _collectedAt;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            OnPropertyChanged();
        }
    }

    public bool IsDone
    {
        get => _isDone;
        set
        {
            if (_isDone == value) return;
            _isDone = value;
            DoneAt = value ? DateTime.Now : null;
            if (value && _collectedAt is not null)
            {
                _collectedAt = null;
                OnPropertyChanged(nameof(CollectedAt));
                OnPropertyChanged(nameof(IsCollected));
                OnPropertyChanged(nameof(SortBucket));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsOpen));
            OnPropertyChanged(nameof(DoneHint));
        }
    }

    public DateTime? DeferredUntil
    {
        get => _deferredUntil;
        set
        {
            if (_deferredUntil == value) return;
            _deferredUntil = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DeferLabel));
            OnPropertyChanged(nameof(SortBucket));
        }
    }

    public DateTime? CollectedAt
    {
        get => _collectedAt;
        set
        {
            if (_collectedAt == value) return;
            _collectedAt = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCollected));
            OnPropertyChanged(nameof(SortBucket));
        }
    }

    [JsonIgnore]
    public bool IsCollected => _collectedAt is not null;

    [JsonIgnore]
    public int SortBucket => _collectedAt is not null ? 0 :
        _deferredUntil is { } deferred && deferred.Date > DayClock.Today ? 2 : 1;

    [JsonIgnore]
    public string DeferLabel => _deferredUntil is { } deferred && deferred.Date > DayClock.Today
        ? deferred.ToString("M/d")
        : "";

    [JsonIgnore]
    public bool IsOpen => !IsDone;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? DoneAt { get; set; }

    [JsonIgnore]
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing == value) return;
            _isEditing = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public string CreatedHint
    {
        get
        {
            var span = DayClock.Today - DayClock.BusinessDayOf(CreatedAt);
            if (span.Days <= 0) return CreatedAt.ToString("今天 HH:mm", CultureInfo.GetCultureInfo("zh-CN"));
            if (span.Days == 1) return "昨天 " + CreatedAt.ToString("HH:mm");
            if (CreatedAt.Year == DayClock.Today.Year) return CreatedAt.ToString("M月d日 HH:mm");
            return CreatedAt.ToString("yyyy年M月d日");
        }
    }

    [JsonIgnore]
    public string DoneHint => DoneAt is null ? "未完成" : "完成于 " + DoneAt.Value.ToString("M月d日 HH:mm");

    public void RefreshDay()
    {
        OnPropertyChanged(nameof(SortBucket));
        OnPropertyChanged(nameof(DeferLabel));
        OnPropertyChanged(nameof(CreatedHint));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class PlanGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public bool IsDone { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? DoneAt { get; set; }
    public int Rounds { get; set; }
    public ObservableCollection<PlanItem> Items { get; set; } = new();
}

public sealed class PlanItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public bool IsDone { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? DoneAt { get; set; }
    public int Rounds { get; set; }
}

public sealed class BillItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Date { get; set; } = DayClock.Today;
    public decimal Amount { get; set; }
    public string Category { get; set; } = "餐饮";
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }

    [JsonIgnore]
    public string DisplayNote => string.IsNullOrWhiteSpace(Note) ? Category : Note;
}

public sealed class CategoryInfo
{
    public CategoryInfo(string name, string color, string softColor, string glyph)
    {
        Name = name;
        Color = color;
        SoftColor = softColor;
        Glyph = glyph;
    }

    public string Name { get; }
    public string Color { get; }
    public string SoftColor { get; }
    public string Glyph { get; }
}

public static class CategoryCatalog
{
    public static readonly IReadOnlyList<CategoryInfo> All = new[]
    {
        new CategoryInfo("餐饮", "#D87557", "#F8E6DE", "\uE8D4"),
        new CategoryInfo("交通", "#5D8FB3", "#E1EDF5", "\uE804"),
        new CategoryInfo("购物", "#B77FA3", "#F4E5EF", "\uE7BF"),
        new CategoryInfo("居住", "#7F9960", "#EAF0DF", "\uE80F"),
        new CategoryInfo("娱乐", "#D4A246", "#F8EFD9", "\uE8D6"),
        new CategoryInfo("医疗", "#5C9D96", "#E0F0ED", "\uE95E"),
        new CategoryInfo("学习", "#7B82C4", "#E9EAF7", "\uE82D"),
        new CategoryInfo("其他", "#968E82", "#EEEBE5", "\uE712")
    };

    public static CategoryInfo Get(string? name)
        => All.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal)) ?? All[^1];

    public static int IndexOf(string? name)
    {
        for (var i = 0; i < All.Count; i++)
            if (string.Equals(All[i].Name, name, StringComparison.Ordinal)) return i;
        return All.Count - 1;
    }
}

public sealed record CategoryTotal(string Name, decimal Amount, double Share);

public sealed record ChartBar(string Label, decimal Amount);

public sealed record PeriodSummary(
    DateTime Start,
    DateTime End,
    decimal Total,
    int Count,
    double DailyAverage,
    decimal PreviousTotal,
    IReadOnlyList<CategoryTotal> Categories,
    IReadOnlyList<ChartBar> Bars,
    string PeakLabel,
    decimal PeakAmount,
    string TopCategory,
    decimal TopCategoryAmount)
{
    public string RangeText => Start.Year == End.Year
        ? $"{Start:M月d日} – {End:M月d日}"
        : $"{Start:yyyy年M月d日} – {End:yyyy年M月d日}";

    public double? ChangePercent => PreviousTotal <= 0 ? null : (double)((Total - PreviousTotal) / PreviousTotal);
}

public static class StatsCalculator
{
    public static DateTime StartOfWeek(DateTime date)
    {
        var d = date.Date;
        var offset = ((int)d.DayOfWeek + 6) % 7;
        return d.AddDays(-offset);
    }

    public static PeriodSummary BuildWeek(IEnumerable<BillItem> bills, DateTime anchor, DateTime? asOf = null)
    {
        var start = StartOfWeek(anchor);
        var end = start.AddDays(6);
        var previousStart = start.AddDays(-7);
        var previousEnd = start.AddDays(-1);

        var inPeriod = InRange(bills, start, end).ToList();
        var previous = InRange(bills, previousStart, previousEnd).Sum(x => x.Amount);
        var bars = Enumerable.Range(0, 7).Select(i =>
        {
            var day = start.AddDays(i);
            var total = inPeriod.Where(x => x.Date.Date == day).Sum(x => x.Amount);
            return new ChartBar(day.ToString("ddd", CultureInfo.GetCultureInfo("zh-CN")).Replace("周", ""), total);
        }).ToList();

        var today = (asOf ?? DayClock.Today).Date;
        var elapsed = end.Date < today ? 7 : Math.Clamp((today - start).Days + 1, 1, 7);
        var peak = inPeriod.GroupBy(x => x.Date.Date)
            .Select(g => new { Date = g.Key, Amount = g.Sum(x => x.Amount) })
            .OrderByDescending(x => x.Amount).FirstOrDefault();

        return Build(start, end, inPeriod, previous, elapsed, bars,
            peak is null ? "" : WeekdayName(peak.Date) + " · " + peak.Date.ToString("M月d日"),
            peak?.Amount ?? 0);
    }

    public static PeriodSummary BuildMonth(IEnumerable<BillItem> bills, DateTime anchor, DateTime? asOf = null)
    {
        var start = new DateTime(anchor.Year, anchor.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var previousStart = start.AddMonths(-1);
        var previousEnd = start.AddDays(-1);

        var inPeriod = InRange(bills, start, end).ToList();
        var previous = InRange(bills, previousStart, previousEnd).Sum(x => x.Amount);

        var bars = new List<ChartBar>();
        var cursor = 1;
        while (cursor <= end.Day)
        {
            var last = Math.Min(cursor + 6, end.Day);
            var from = new DateTime(anchor.Year, anchor.Month, cursor);
            var to = new DateTime(anchor.Year, anchor.Month, last);
            var total = inPeriod.Where(x => x.Date.Date >= from && x.Date.Date <= to).Sum(x => x.Amount);
            bars.Add(new ChartBar($"{cursor}-{last}", total));
            cursor = last + 1;
        }

        var daysInMonth = end.Day;
        var today = (asOf ?? DayClock.Today).Date;
        var elapsed = end.Date < today ? daysInMonth : Math.Clamp(today.Day, 1, daysInMonth);
        var peak = inPeriod.GroupBy(x => x.Date.Date)
            .Select(g => new { Date = g.Key, Amount = g.Sum(x => x.Amount) })
            .OrderByDescending(x => x.Amount).FirstOrDefault();

        return Build(start, end, inPeriod, previous, elapsed, bars,
            peak is null ? "" : peak.Date.ToString("M月d日") + " " + WeekdayName(peak.Date),
            peak?.Amount ?? 0);
    }

    private static PeriodSummary Build(
        DateTime start,
        DateTime end,
        List<BillItem> inPeriod,
        decimal previous,
        int elapsedDays,
        List<ChartBar> bars,
        string peakLabel,
        decimal peakAmount)
    {
        var total = inPeriod.Sum(x => x.Amount);
        var categories = inPeriod.GroupBy(x => x.Category)
            .Select(g => new CategoryTotal(g.Key, g.Sum(x => x.Amount), total <= 0 ? 0 : (double)(g.Sum(x => x.Amount) / total)))
            .OrderByDescending(x => x.Amount)
            .ToList();
        var top = categories.FirstOrDefault();
        return new PeriodSummary(
            start, end, total, inPeriod.Count,
            Math.Round((double)total / Math.Max(1, elapsedDays), 2),
            previous,
            categories,
            bars,
            peakLabel,
            peakAmount,
            top?.Name ?? "",
            top?.Amount ?? 0);
    }

    public static IEnumerable<BillItem> InRange(IEnumerable<BillItem> bills, DateTime start, DateTime end)
        => bills.Where(x => x.Date.Date >= start.Date && x.Date.Date <= end.Date);

    private static string WeekdayName(DateTime date)
        => date.ToString("dddd", CultureInfo.GetCultureInfo("zh-CN"));
}
