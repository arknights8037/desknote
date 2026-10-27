using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DestNote.Views;

public partial class SummaryView : UserControl
{
    private static readonly CultureInfo Zh = CultureInfo.GetCultureInfo("zh-CN");
    private readonly AppData _data;
    private readonly DataStore _store;
    private bool _isMonth;
    private bool _initialized;
    private DateTime _anchor = DayClock.Today;
    private DateTime _lastStart = DayClock.Today;
    private DateTime _lastEnd = DayClock.Today;
    private bool _wasCurrent;

    public SummaryView()
    {
        InitializeComponent();
        _data = App.Data;
        _store = App.Store;
    }

    public void Configure(bool isMonth)
    {
        _isMonth = isMonth;
        _anchor = DayClock.Today;
        _initialized = true;
        BackCurrent.Content = isMonth ? "回到本月" : "回到本周";
        FootNote.Text = isMonth ? "按自然月统计 · 周段按日期划分" : "周从周一开始统计";
        Refresh();
    }

    public void Refresh()
    {
        if (!_initialized) return;

        var today = DayClock.Today;
        var summary = _isMonth
            ? StatsCalculator.BuildMonth(_data.Bills, _anchor)
            : StatsCalculator.BuildWeek(_data.Bills, _anchor);
        _lastStart = summary.Start;
        _lastEnd = summary.End;

        var currentStart = _isMonth
            ? new DateTime(today.Year, today.Month, 1)
            : StatsCalculator.StartOfWeek(today);
        var periodStart = _isMonth
            ? new DateTime(summary.Start.Year, summary.Start.Month, 1)
            : StatsCalculator.StartOfWeek(summary.Start);
        var isCurrent = periodStart == currentStart;

        PeriodCaption.Text = _isMonth
            ? summary.Start.ToString("yyyy年M月", Zh)
            : summary.RangeText;
        PeriodKindText.Text = isCurrent
            ? (_isMonth ? "本月支出" : "本周支出")
            : (_isMonth ? "月支出" : "周支出");
        BackCurrent.Visibility = isCurrent ? Visibility.Collapsed : Visibility.Visible;
        NextButton.IsEnabled = periodStart < currentStart;
        NextButton.Opacity = NextButton.IsEnabled ? 1 : 0.28;

        TotalText.Text = Money(summary.Total);
        CountText.Text = $"{summary.Count} 笔";
        AverageText.Text = $"日均 {Money((decimal)summary.DailyAverage)}";
        UpdateCompare(summary);

        ChartTitle.Text = _isMonth ? "每周段支出" : "每日支出";
        Chart.SetBars(summary.Bars);
        BuildCategories(summary);
        BuildInsight(summary);
        _wasCurrent = isCurrent;
    }

    public void OnDayChanged()
    {
        if (_wasCurrent)
            _anchor = DayClock.Today;
        Refresh();
    }

    private void UpdateCompare(PeriodSummary summary)
    {
        if (summary.ChangePercent is not double change)
        {
            CompareText.Text = "上期无记录";
            CompareText.Foreground = Res("FaintBrush");
            return;
        }

        var direction = change > 0 ? "+" : change < 0 ? "−" : "";
        CompareText.Text = $"较上期 {direction}{Math.Abs(change * 100):0.#}%";
        CompareText.Foreground = change > 0 ? Res("DangerBrush") :
            change < 0 ? Res("AccentBrush") : Res("FaintBrush");
    }

    private void BuildCategories(PeriodSummary summary)
    {
        CategoryPanel.Children.Clear();
        if (summary.Categories.Count == 0)
        {
            CategoryHintText.Text = "暂无记录";
            CategoryPanel.Children.Add(new TextBlock
            {
                Text = "这段时间还没有账单，记一笔就能看见花销去向。",
                FontSize = 9.3,
                Foreground = Res("MutedBrush"),
                Margin = new Thickness(0, 3, 0, 2)
            });
            return;
        }

        CategoryHintText.Text = $"{summary.Categories.Count} 个类别";
        foreach (var item in summary.Categories)
        {
            var category = CategoryCatalog.Get(item.Name);
            var brush = ThemeManager.Brush(category.Color);
            var grid = new Grid { Margin = new Thickness(0, 0, 10, 7) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = brush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            Grid.SetColumn(dot, 0);
            grid.Children.Add(dot);

            var name = new TextBlock
            {
                Text = category.Name,
                FontSize = 9.3,
                Foreground = Res("InkBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);

            var percent = new TextBlock
            {
                Text = $"{item.Share * 100:0.#}%",
                FontSize = 8.2,
                Foreground = Res("FaintBrush"),
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(percent, 2);
            grid.Children.Add(percent);

            var amount = new TextBlock
            {
                Text = Money(item.Amount),
                FontFamily = (FontFamily)FindResource("NumberFont"),
                FontSize = 9.3,
                FontWeight = FontWeights.SemiBold,
                Foreground = Res("InkBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(amount, 3);
            grid.Children.Add(amount);

            var bar = new ProgressBar
            {
                Height = 3.5,
                Maximum = 100,
                Value = Math.Clamp(item.Share * 100, 0, 100),
                Foreground = brush,
                Background = Res("PaperDeepBrush"),
                Margin = new Thickness(0, 4, 0, 0)
            };
            Grid.SetRow(bar, 1);
            Grid.SetColumn(bar, 0);
            Grid.SetColumnSpan(bar, 4);
            grid.Children.Add(bar);
            CategoryPanel.Children.Add(grid);
        }
    }

    private void BuildInsight(PeriodSummary summary)
    {
        if (summary.Count == 0)
        {
            InsightText.Text = "这段时间还没有记账。哪怕只是一杯咖啡，记下来也能看见钱去了哪里。";
            return;
        }

        var share = summary.Total <= 0 ? 0 : (double)(summary.TopCategoryAmount / summary.Total) * 100;
        if (summary.PeakAmount > 0 && !string.IsNullOrWhiteSpace(summary.PeakLabel))
        {
            InsightText.Text = $"{summary.PeakLabel} 花得最多，{Money(summary.PeakAmount)}；"
                               + $"{summary.TopCategory}占了大头（{share:0}%）。";
        }
        else
        {
            InsightText.Text = $"{summary.TopCategory}是这段时间的主要开销，占 {share:0}%。";
        }
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        _anchor = _isMonth ? _anchor.AddMonths(-1) : _anchor.AddDays(-7);
        Refresh();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (!NextButton.IsEnabled) return;
        _anchor = _isMonth ? _anchor.AddMonths(1) : _anchor.AddDays(7);
        Refresh();
    }

    private void BackCurrent_Click(object sender, RoutedEventArgs e)
    {
        _anchor = DayClock.Today;
        Refresh();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var defaultName = _isMonth
            ? $"轻笺账单_{_lastStart:yyyy-MM}.csv"
            : $"轻笺账单_{_lastStart:yyyyMMdd}-{_lastEnd:yyyyMMdd}.csv";
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出本期账单",
            Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = defaultName
        };
        if (dialog.ShowDialog(App.Ui) != true) return;

        try
        {
            _store.ExportBillsCsv(dialog.FileName, _data.Bills, _lastStart, _lastEnd);
            App.Ui?.ShowToast("本期账单已导出");
        }
        catch (Exception ex)
        {
            _store.LogError("export csv", ex);
            App.Ui?.ShowToast("导出失败，请换个位置再试");
        }
    }

    private static string Money(decimal amount)
        => "¥" + amount.ToString("#,##0.00", Zh);

    private Brush Res(string key) => (Brush)FindResource(key);
}
