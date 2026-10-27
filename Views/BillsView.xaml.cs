using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DestNote.Views;

public partial class BillsView : UserControl
{
    private static readonly CultureInfo Zh = CultureInfo.GetCultureInfo("zh-CN");
    private readonly AppData _data;
    private readonly DataStore _store;
    private readonly List<RadioButton> _categoryButtons = new();
    private bool _initialized;
    private DateTime _billDate = DayClock.Today;
    private DateTime _calendarMonth = new(DayClock.Today.Year, DayClock.Today.Month, 1);
    private string _selectedCategory = "餐饮";
    private BillItem? _editing;
    private string _section = "details";

    public BillsView()
    {
        InitializeComponent();
        _data = App.Data;
        _store = App.Store;

        BuildCategoryChips();
        DatePopup.PlacementTarget = DateButton;
        DataObject.AddPastingHandler(AmountInput, AmountPaste);
        WeekSummary.Configure(false);
        MonthSummary.Configure(true);

        _initialized = true;
        UpdateDateButton();
        SetEditMode(false);
        RefreshAll(false);

        var initial = _data.Settings.LastBillSection;
        if (initial == "week") WeekTab.IsChecked = true;
        else if (initial == "month") MonthTab.IsChecked = true;
        else ShowSection("details");
    }

    public void OnThemeChanged()
    {
        RefreshTotals();
        BuildBillsList();
        WeekSummary.Refresh();
        MonthSummary.Refresh();
    }

    public void OnDayChanged()
    {
        if (_editing is null && _billDate.Date < DayClock.Today)
        {
            _billDate = DayClock.Today;
            _calendarMonth = new DateTime(_billDate.Year, _billDate.Month, 1);
            UpdateDateButton();
        }
        RefreshTotals();
        BuildBillsList();
        WeekSummary.OnDayChanged();
        MonthSummary.OnDayChanged();
    }

    public void RefreshAll(bool scrollTop)
    {
        RefreshTotals();
        BuildBillsList();
        WeekSummary.Refresh();
        MonthSummary.Refresh();
        if (scrollTop)
            Dispatcher.BeginInvoke(() => BillsScroll.ScrollToTop(), DispatcherPriority.Loaded);
    }

    private void BuildCategoryChips()
    {
        CategoryGrid.Children.Clear();
        _categoryButtons.Clear();
        foreach (var category in CategoryCatalog.All)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new Border
            {
                Width = 5,
                Height = 5,
                CornerRadius = new CornerRadius(2.5),
                Background = ThemeManager.Brush(category.Color),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 3, 0)
            });
            content.Children.Add(new TextBlock
            {
                Text = category.Name,
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center
            });

            var chip = new RadioButton
            {
                GroupName = "BillCategory",
                Content = content,
                Tag = category.Name,
                Style = (Style)FindResource("CategoryChip"),
                ToolTip = category.Name
            };
            chip.Checked += CategoryChip_Checked;
            _categoryButtons.Add(chip);
            CategoryGrid.Children.Add(chip);
            if (category.Name == _selectedCategory)
                chip.IsChecked = true;
        }
    }

    private void CategoryChip_Checked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string category)
            _selectedCategory = category;
    }

    private void Section_Checked(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        var section = (sender as FrameworkElement)?.Tag?.ToString() ?? "details";
        ShowSection(section);
    }

    private void ShowSection(string section)
    {
        _section = section;
        DetailsPage.Visibility = section == "details" ? Visibility.Visible : Visibility.Collapsed;
        WeekSummary.Visibility = section == "week" ? Visibility.Visible : Visibility.Collapsed;
        MonthSummary.Visibility = section == "month" ? Visibility.Visible : Visibility.Collapsed;

        if (section == "week") WeekSummary.Refresh();
        if (section == "month") MonthSummary.Refresh();

        _data.Settings.LastBillSection = section;
        _store.QueueSave();
    }

    private void RefreshTotals()
    {
        var today = DayClock.Today;
        var todayTotal = _data.Bills.Where(x => x.Date.Date == today).Sum(x => x.Amount);
        TodayTotalText.Text = Money(todayTotal);

        var week = StatsCalculator.BuildWeek(_data.Bills, today);
        var month = StatsCalculator.BuildMonth(_data.Bills, today);
        WeekMiniText.Text = $"本周 {Money(week.Total)} · {week.Count} 笔";
        MonthMiniText.Text = $"本月 {Money(month.Total)} · {month.Count} 笔";
        BillCountText.Text = $"共 {_data.Bills.Count} 笔";
    }

    private void BuildBillsList()
    {
        BillsListPanel.Children.Clear();
        var groups = _data.Bills
            .GroupBy(x => x.Date.Date)
            .OrderByDescending(g => g.Key)
            .ToList();

        BillsEmptyState.Visibility = groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var first = true;
        foreach (var group in groups)
        {
            var items = group.OrderByDescending(x => x.CreatedAt).ToList();
            var header = BuildGroupHeader(group.Key, items, first);
            first = false;
            BillsListPanel.Children.Add(header);
            foreach (var bill in items)
                BillsListPanel.Children.Add(BuildBillRow(bill));
        }
    }

    private FrameworkElement BuildGroupHeader(DateTime day, IReadOnlyList<BillItem> bills, bool first)
    {
        var header = new Grid
        {
            Margin = new Thickness(0, first ? 4 : 12, 0, 2)
        };
        var label = new TextBlock
        {
            Text = DayLabel(day),
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Res("InkBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        var total = new TextBlock
        {
            Text = $"{bills.Count} 笔 · {Money(bills.Sum(x => x.Amount))}",
            FontSize = 9,
            Foreground = Res("MutedBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(label);
        header.Children.Add(total);
        return header;
    }

    private FrameworkElement BuildBillRow(BillItem bill)
    {
        var category = CategoryCatalog.Get(bill.Category);
        var row = new Border
        {
            BorderBrush = Res("PaperLineBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 6, 0, 6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var pill = new Border
        {
            Background = ThemeManager.Brush(category.SoftColor),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(5, 1.5, 5, 1.5),
            VerticalAlignment = VerticalAlignment.Center
        };
        pill.Child = new TextBlock
        {
            Text = category.Name,
            FontSize = 8.3,
            Foreground = ThemeManager.Brush(category.Color),
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetColumn(pill, 0);
        grid.Children.Add(pill);

        var note = new TextBlock
        {
            Text = bill.Note,
            FontSize = 11.6,
            Foreground = Res("InkBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 7, 0),
            ToolTip = string.IsNullOrWhiteSpace(bill.Note) ? null : bill.Note
        };
        Grid.SetColumn(note, 1);
        grid.Children.Add(note);

        var amount = new TextBlock
        {
            Text = Money(bill.Amount),
            FontFamily = (FontFamily)FindResource("NumberFont"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Res("InkBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 3, 0)
        };
        Grid.SetColumn(amount, 2);
        grid.Children.Add(amount);

        var edit = new Button
        {
            Style = (Style)FindResource("TinyIconButton"),
            Content = "\uE70F",
            FontSize = 9,
            ToolTip = "编辑",
            Opacity = 0.28,
            Focusable = false
        };
        System.Windows.Automation.AutomationProperties.SetName(edit, "编辑账单");
        edit.Click += (_, _) => BeginEdit(bill);
        Grid.SetColumn(edit, 3);
        grid.Children.Add(edit);

        var delete = new Button
        {
            Style = (Style)FindResource("TinyIconButton"),
            Content = "\uE74D",
            FontSize = 9,
            ToolTip = "删除",
            Opacity = 0.28,
            Focusable = false
        };
        System.Windows.Automation.AutomationProperties.SetName(delete, "删除账单");
        delete.Click += (_, _) => DeleteBill(bill);
        Grid.SetColumn(delete, 4);
        grid.Children.Add(delete);

        row.Child = grid;
        row.MouseEnter += (_, _) =>
        {
            edit.Opacity = 0.9;
            delete.Opacity = 0.9;
        };
        row.MouseLeave += (_, _) =>
        {
            edit.Opacity = 0.28;
            delete.Opacity = 0.28;
        };
        row.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                BeginEdit(bill);
                e.Handled = true;
            }
        };

        var menu = new ContextMenu();
        var editItem = new MenuItem { Header = "编辑这笔" };
        editItem.Click += (_, _) => BeginEdit(bill);
        var deleteItem = new MenuItem { Header = "删除这笔" };
        deleteItem.Click += (_, _) => DeleteBill(bill);
        menu.Items.Add(editItem);
        menu.Items.Add(deleteItem);
        row.ContextMenu = menu;

        return row;
    }

    private void AmountInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;
        var start = AmountInput.SelectionStart;
        var length = AmountInput.SelectionLength;
        var current = AmountInput.Text;
        var prospective = current.Remove(start, length).Insert(start, e.Text);
        if (!ExpressionEvaluator.IsAllowedInput(prospective))
            e.Handled = true;
    }

    private void AmountPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(typeof(string))) return;
        var text = (e.DataObject.GetData(typeof(string)) as string ?? "").Trim();
        var start = AmountInput.SelectionStart;
        var length = AmountInput.SelectionLength;
        var prospective = AmountInput.Text.Remove(start, length).Insert(start, text);
        if (!ExpressionEvaluator.IsAllowedInput(prospective))
            e.CancelCommand();
    }

    private void AmountInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        AmountPlaceholder.Visibility = string.IsNullOrEmpty(AmountInput.Text) ? Visibility.Visible : Visibility.Collapsed;
        UpdateAmountPreview();
        UpdateAddButtonState();
    }

    private void AmountInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SaveBill();
        }
    }

    private void NoteInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SaveBill();
        }
    }

    private void AddBill_Click(object sender, RoutedEventArgs e) => SaveBill();

    private bool TryGetAmount(out decimal amount)
        => ExpressionEvaluator.TryEvaluate(AmountInput.Text, out amount, out _);

    private void UpdateAddButtonState()
    {
        AddBillButton.IsEnabled = TryGetAmount(out _);
    }

    private void UpdateAmountPreview()
    {
        var valid = ExpressionEvaluator.TryEvaluate(AmountInput.Text, out var amount, out var hasOperator);
        if (valid && hasOperator)
        {
            AmountPreviewText.Text = "= " + Money(amount);
            AmountPreviewText.Visibility = Visibility.Visible;
        }
        else
        {
            AmountPreviewText.Visibility = Visibility.Collapsed;
        }
    }

    private void SaveBill()
    {
        if (!TryGetAmount(out var amount))
        {
            App.Ui?.ShowToast("请输入金额或算式");
            AmountInput.Focus();
            return;
        }

        amount = Math.Round(amount, 2);
        var note = NoteInput.Text.Trim();
        var wasEditing = _editing is not null;
        if (_editing is null)
        {
            _data.Bills.Add(new BillItem
            {
                Date = _billDate.Date,
                Amount = amount,
                Category = _selectedCategory,
                Note = note,
                CreatedAt = DateTime.Now
            });
        }
        else
        {
            _editing.Date = _billDate.Date;
            _editing.Amount = amount;
            _editing.Category = _selectedCategory;
            _editing.Note = note;
            _editing.UpdatedAt = DateTime.Now;
        }

        _store.QueueSave();
        App.Ui?.ShowToast(wasEditing ? "账单已更新" : $"已记一笔 · {Money(amount)}");

        _editing = null;
        AmountInput.Clear();
        NoteInput.Clear();
        if (wasEditing)
            _billDate = DayClock.Today;
        UpdateDateButton();
        SetEditMode(false);
        RefreshAll(true);
        AmountInput.Focus();
    }

    private void BeginEdit(BillItem bill)
    {
        if (_section != "details")
        {
            DetailsTab.IsChecked = true;
            ShowSection("details");
        }

        _editing = bill;
        _billDate = bill.Date.Date;
        UpdateDateButton();
        AmountInput.Text = bill.Amount.ToString("0.##", CultureInfo.InvariantCulture);
        SelectCategory(bill.Category);
        NoteInput.Text = bill.Note;
        SetEditMode(true);
        UpdateAddButtonState();
        Dispatcher.BeginInvoke(() =>
        {
            AmountInput.Focus();
            AmountInput.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void SelectCategory(string name)
    {
        _selectedCategory = CategoryCatalog.Get(name).Name;
        foreach (var button in _categoryButtons)
        {
            if (string.Equals(button.Tag?.ToString(), _selectedCategory, StringComparison.Ordinal))
            {
                button.IsChecked = true;
                break;
            }
        }
    }

    private void SetEditMode(bool editing)
    {
        AddBillButton.Content = editing ? "保存" : "记一笔";
        CancelEditButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        FormBorder.SetResourceReference(Border.BorderBrushProperty, editing ? "AccentBrush" : "PaperLineBrush");
        UpdateFormHint();
    }

    private void UpdateFormHint()
    {
        if (_editing is not null)
            FormHintText.Text = $"正在修改 {_billDate:M月d日} 的账单 · Enter 保存";
        else if (_billDate.Date == DayClock.Today)
            FormHintText.Text = "记录今天的开销 · Enter 保存";
        else
            FormHintText.Text = $"记录 {_billDate:M月d日} 的开销 · Enter 保存";
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e)
    {
        _editing = null;
        AmountInput.Clear();
        NoteInput.Clear();
        _billDate = DayClock.Today;
        UpdateDateButton();
        SetEditMode(false);
        UpdateAddButtonState();
        AmountInput.Focus();
    }

    private void DateButton_Click(object sender, RoutedEventArgs e)
    {
        _calendarMonth = new DateTime(_billDate.Year, _billDate.Month, 1);
        BuildCalendar();
        DatePopup.IsOpen = true;
    }

    private void QuickDate_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse((sender as FrameworkElement)?.Tag?.ToString(), out var days))
        {
            _billDate = DayClock.Today.AddDays(-days);
            _calendarMonth = new DateTime(_billDate.Year, _billDate.Month, 1);
            UpdateDateButton();
            DatePopup.IsOpen = false;
        }
    }

    private void CalendarPrev_Click(object sender, RoutedEventArgs e)
    {
        _calendarMonth = _calendarMonth.AddMonths(-1);
        BuildCalendar();
    }

    private void CalendarNext_Click(object sender, RoutedEventArgs e)
    {
        var currentMonth = new DateTime(DayClock.Today.Year, DayClock.Today.Month, 1);
        if (_calendarMonth >= currentMonth) return;
        _calendarMonth = _calendarMonth.AddMonths(1);
        BuildCalendar();
    }

    private void BuildCalendar()
    {
        CalendarMonthText.Text = _calendarMonth.ToString("yyyy年M月", Zh);
        var currentMonth = new DateTime(DayClock.Today.Year, DayClock.Today.Month, 1);
        CalendarNextButton.IsEnabled = _calendarMonth < currentMonth;
        CalendarDaysGrid.Children.Clear();

        var offset = ((int)_calendarMonth.DayOfWeek + 6) % 7;
        var start = _calendarMonth.AddDays(-offset);
        for (var i = 0; i < 42; i++)
        {
            var date = start.AddDays(i);
            var selected = date.Date == _billDate.Date;
            var today = date.Date == DayClock.Today;
            var inMonth = date.Month == _calendarMonth.Month;
            var future = date.Date > DayClock.Today;

            var button = new Button
            {
                Content = date.Day.ToString(CultureInfo.InvariantCulture),
                Tag = date.Date,
                Style = (Style)FindResource("CalendarDayButton"),
                IsEnabled = !future,
                ToolTip = date.ToString("yyyy年M月d日 dddd", Zh)
            };
            System.Windows.Automation.AutomationProperties.SetName(button,
                date.ToString("yyyy年M月d日", Zh));

            if (selected)
            {
                button.Background = Res("AccentBrush");
                button.BorderBrush = Res("AccentBrush");
                button.Foreground = Brushes.White;
                button.FontWeight = FontWeights.SemiBold;
            }
            else if (today)
            {
                button.BorderBrush = Res("AccentBrush");
                button.Foreground = Res("AccentDeepBrush");
                button.FontWeight = FontWeights.SemiBold;
            }
            else if (!inMonth)
            {
                button.Foreground = Res("FaintBrush");
                button.Opacity = 0.5;
            }

            button.Click += (_, _) =>
            {
                _billDate = date.Date;
                UpdateDateButton();
                DatePopup.IsOpen = false;
            };
            CalendarDaysGrid.Children.Add(button);
        }
    }

    private void UpdateDateButton()
    {
        DateButtonText.Text = RelDay(_billDate);
        DateButton.ToolTip = _billDate.ToString("yyyy年M月d日 dddd", Zh);
        UpdateFormHint();
    }

    private void DeleteBill(BillItem bill)
    {
        var index = _data.Bills.IndexOf(bill);
        if (index < 0) return;
        if (_editing == bill)
        {
            _editing = null;
            AmountInput.Clear();
            NoteInput.Clear();
            SetEditMode(false);
            UpdateAddButtonState();
        }

        _data.Bills.RemoveAt(index);
        _store.QueueSave();
        RefreshAll(false);
        App.Ui?.ShowToast($"已删除 · {Money(bill.Amount)}", () =>
        {
            _data.Bills.Insert(Math.Min(index, _data.Bills.Count), bill);
            _store.QueueSave();
            RefreshAll(false);
        });
    }

    private static string RelDay(DateTime day)
    {
        if (day.Date == DayClock.Today) return "今天";
        if (day.Date == DayClock.Today.AddDays(-1)) return "昨天";
        if (day.Year == DayClock.Today.Year) return day.ToString("M月d日");
        return day.ToString("yyyy年M月d日");
    }

    private static string DayLabel(DateTime day)
    {
        var prefix = day.Date == DayClock.Today ? "今天 · " :
            day.Date == DayClock.Today.AddDays(-1) ? "昨天 · " : "";
        var year = day.Year == DayClock.Today.Year ? "" : day.ToString("yyyy年");
        return $"{prefix}{year}{day.ToString("M月d日", Zh)} {day.ToString("dddd", Zh)}";
    }

    private static string Money(decimal amount)
        => "¥" + amount.ToString("#,##0.00", Zh);

    private Brush Res(string key) => (Brush)FindResource(key);
}
