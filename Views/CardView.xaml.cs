using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DestNote.Views;

public partial class CardView : UserControl
{
    private static readonly CultureInfo Zh = CultureInfo.GetCultureInfo("zh-CN");
    private static readonly string[] CommandNames = { "/move", "/collect", "/done", "/help" };
    private readonly AppData _data;
    private readonly DataStore _store;
    private readonly ListCollectionView _notesView;
    private int _page = -1;
    private string _category = "餐饮";
    private Guid? _editingBillId;
    private TextBox? _billNoteEditor;
    private bool _finishingBillEdit;
    private bool _commandHintShown;

    public event EventHandler? FullModeRequested;
    public event EventHandler? PinRequested;
    public event EventHandler? MinimizeRequested;
    public event EventHandler? CloseRequested;

    public CardView()
    {
        InitializeComponent();
        _data = App.Data;
        _store = App.Store;

        _notesView = new ListCollectionView(_data.Notes);
        _notesView.SortDescriptions.Add(new SortDescription(nameof(NoteItem.SortBucket), ListSortDirection.Ascending));
        _notesView.SortDescriptions.Add(new SortDescription(nameof(NoteItem.CreatedAt), ListSortDirection.Ascending));
        NotesList.ItemsSource = _notesView;
        DataObject.AddPastingHandler(AmountInput, AmountPaste);
        UpdateCategoryVisual();
        ShowPage(0, false, false);
        RefreshBills();

        Loaded += (_, _) =>
        {
            ScrollNotesToEnd();
            ScrollBillsToEnd();
        };
    }

    public int PageIndex => _page;

    public void RefreshAll()
    {
        _notesView.Refresh();
        RefreshBills();
        PlanPage.RefreshAll();
        if (_page == 0) ScrollNotesToEnd();
        else if (_page == 1) ScrollBillsToEnd();
    }

    public void ShowNotes(bool focus)
    {
        ShowPage(0, true, focus);
    }

    public void ShowBills(bool focus)
    {
        ShowPage(1, true, focus);
    }

    public void FocusActiveInput()
    {
        if (_page == 0) FocusNotesInput();
        else if (_page == 1) Dispatcher.BeginInvoke(() => AmountInput.Focus(), DispatcherPriority.Input);
    }

    public void FocusNotesInput()
    {
        Dispatcher.BeginInvoke(() =>
        {
            NoteInput.Focus();
            Keyboard.Focus(NoteInput);
            NoteInput.CaretIndex = NoteInput.Text.Length;
        }, DispatcherPriority.Input);
    }

    public void OnThemeChanged()
    {
        RefreshBills();
        PlanPage.OnThemeChanged();
    }

    private void ShowPage(int index, bool animate, bool focus)
    {
        index = Math.Clamp(index, 0, 2);
        var old = _page;
        _page = index;

        NotesPage.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        BillsPage.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        PlanPage.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        PrevButton.IsEnabled = index > 0;
        NextButton.IsEnabled = index < 2;
        PageDot1.SetResourceReference(Shape.FillProperty, index == 0 ? "AccentBrush" : "PaperLineBrush");
        PageDot2.SetResourceReference(Shape.FillProperty, index == 1 ? "AccentBrush" : "PaperLineBrush");
        PageDot3.SetResourceReference(Shape.FillProperty, index == 2 ? "AccentBrush" : "PaperLineBrush");

        if (index == 1)
            RefreshBills();
        if (index == 2)
            PlanPage.RefreshAll();

        var target = index == 0 ? (FrameworkElement)NotesPage : index == 1 ? BillsPage : PlanPage;
        if (animate && old != index)
            AnimateIn(target, index > old ? 8 : -8);

        if (index == 0) ScrollNotesToEnd();
        else if (index == 1) ScrollBillsToEnd();

        if (focus)
            FocusActiveInput();
    }

    private static void AnimateIn(FrameworkElement target, double offset)
    {
        target.BeginAnimation(OpacityProperty, null);
        target.Opacity = 0;
        target.RenderTransform = new TranslateTransform(offset, 0);
        target.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        ((TranslateTransform)target.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
            new System.Windows.Media.Animation.DoubleAnimation(offset, 0, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            });
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_page > 0) ShowPage(_page - 1, true, false);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_page < 2) ShowPage(_page + 1, true, false);
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null) return;
        try { Window.GetWindow(this)?.DragMove(); } catch { /* ignored */ }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T target) return target;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = MoreButton,
            Placement = PlacementMode.Bottom,
            StaysOpen = false
        };

        var full = new MenuItem { Header = "完整模式" };
        full.Click += (_, _) => FullModeRequested?.Invoke(this, EventArgs.Empty);
        var pin = new MenuItem { Header = _data.Settings.AlwaysOnTop ? "取消钉住" : "钉在桌面" };
        pin.Click += (_, _) => PinRequested?.Invoke(this, EventArgs.Empty);
        var minimize = new MenuItem { Header = "收进托盘" };
        minimize.Click += (_, _) => MinimizeRequested?.Invoke(this, EventArgs.Empty);
        var close = new MenuItem { Header = "退出" };
        close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(full);
        menu.Items.Add(pin);
        menu.Items.Add(minimize);
        menu.Items.Add(close);
        menu.IsOpen = true;
    }

    private void NoteInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateNotePlaceholder();
        UpdateCommandHint();
        AddNoteButton.IsEnabled = !string.IsNullOrWhiteSpace(NoteInput.Text);
    }

    private void Input_FocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        UpdateNotePlaceholder();
        UpdateAmountPlaceholder();
        UpdateBillNotePlaceholder();
    }

    private void UpdateNotePlaceholder()
    {
        NotePlaceholder.Visibility = string.IsNullOrEmpty(NoteInput.Text) && !NoteInput.IsKeyboardFocusWithin
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateAmountPlaceholder()
    {
        AmountPlaceholder.Visibility = string.IsNullOrEmpty(AmountInput.Text) && !AmountInput.IsKeyboardFocusWithin
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateBillNotePlaceholder()
    {
        BillNotePlaceholder.Visibility = string.IsNullOrEmpty(BillNoteInput.Text) && !BillNoteInput.IsKeyboardFocusWithin
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void NoteInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && NoteInput.Text.TrimStart().StartsWith('/'))
        {
            e.Handled = true;
            CompleteCommand();
            return;
        }
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (TryExecuteCommand(NoteInput.Text)) return;
        AddNote();
    }

    private void AddNote_Click(object sender, RoutedEventArgs e) => AddNote();

    private void AddNote()
    {
        var text = NoteInput.Text.Trim();
        if (text.Length == 0) return;
        if (TryExecuteCommand(text)) return;

        _data.Notes.Add(new NoteItem { Text = text, CreatedAt = DateTime.Now });
        _store.QueueSave();
        NoteInput.Clear();
        _notesView.Refresh();
        ScrollNotesToEnd();
        FocusNotesInput();
    }

    private void CompleteCommand()
    {
        var text = NoteInput.Text.TrimStart();
        var token = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "/";
        var match = CommandNames.FirstOrDefault(x => x.StartsWith(token, StringComparison.OrdinalIgnoreCase));
        if (match is null) return;
        NoteInput.Text = match + " ";
        NoteInput.CaretIndex = NoteInput.Text.Length;
        UpdateCommandHint();
    }

    private void UpdateCommandHint()
    {
        var text = NoteInput.Text.TrimStart();
        if (!text.StartsWith('/') && !text.StartsWith('／'))
        {
            CommandHintText.Visibility = Visibility.Collapsed;
            _commandHintShown = false;
            return;
        }

        var normalized = text.Replace('／', '/');
        var token = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "/";
        CommandHintText.Text = token switch
        {
            "/" => "/move  ·  /collect  ·  /done  ·  /help",
            _ when "/move".StartsWith(token, StringComparison.OrdinalIgnoreCase)
                => "/move 天数 [@s]  ·  把最新一条延后",
            _ when "/collect".StartsWith(token, StringComparison.OrdinalIgnoreCase)
                => "/collect [天数]  ·  收集以前的未完成",
            _ when "/done".StartsWith(token, StringComparison.OrdinalIgnoreCase)
                => "/done [@s]  ·  完成最新一条",
            _ when "/help".StartsWith(token, StringComparison.OrdinalIgnoreCase)
                => "/move 3 @s  ·  /collect 7  ·  /done",
            _ => "/move  ·  /collect  ·  /done  ·  /help"
        };
        CommandHintText.Visibility = Visibility.Visible;
        if (!_commandHintShown)
        {
            _commandHintShown = true;
            ScrollNotesToEnd();
        }
    }

    private bool TryExecuteCommand(string raw)
    {
        var text = raw.Trim();
        if (text.Length == 0) return false;
        text = text.Replace('／', '/');
        if (!text.StartsWith('/')) return false;

        var parts = text[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            ShowCommandHelp();
            return true;
        }

        var command = parts[0].ToLowerInvariant();
        switch (command)
        {
            case "move":
                ExecuteMove(parts);
                return true;
            case "collect":
                ExecuteCollect(parts);
                return true;
            case "done":
                ExecuteDone(parts);
                return true;
            case "help":
                ShowCommandHelp();
                return true;
            default:
                App.Ui?.ShowToast("未知指令，输入 /help");
                return true;
        }
    }

    private void ExecuteMove(string[] parts)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out var days) || days < 1 || days > 3650)
        {
            App.Ui?.ShowToast("/move 天数 [@s]，例如 /move 3");
            return;
        }
        if (parts.Length > 2 && !string.Equals(parts[2], "@s", StringComparison.OrdinalIgnoreCase))
        {
            App.Ui?.ShowToast("目标只支持 @s（最新一条）");
            return;
        }

        var note = LatestOpenNote();
        if (note is null)
        {
            App.Ui?.ShowToast("没有可移动的待办");
            return;
        }

        note.DeferredUntil = DateTime.Today.AddDays(days);
        note.CollectedAt = null;
        _store.QueueSave();
        _notesView.Refresh();
        NoteInput.Clear();
        FocusNotesInput();
        ScrollNotesToEnd();
        App.Ui?.ShowToast($"已延后 {days} 天");
    }

    private void ExecuteCollect(string[] parts)
    {
        var days = 0;
        if (parts.Length > 1 && (!int.TryParse(parts[1], out days) || days < 1 || days > 3650))
        {
            App.Ui?.ShowToast("/collect [天数]，例如 /collect 7");
            return;
        }

        var today = DateTime.Today;
        var cutoff = today.AddDays(-days);
        var notes = _data.Notes
            .Where(x => !x.IsDone && x.CreatedAt.Date < today && (days == 0 || x.CreatedAt.Date >= cutoff))
            .ToList();

        if (notes.Count == 0)
        {
            App.Ui?.ShowToast("没有可收集的旧事项");
            return;
        }

        foreach (var note in notes)
        {
            note.CollectedAt = DateTime.Now;
            note.DeferredUntil = null;
        }
        _store.QueueSave();
        _notesView.Refresh();
        NoteInput.Clear();
        ScrollNotesToTop();
        FocusNotesInput();
        App.Ui?.ShowToast($"已收集 {notes.Count} 条旧事项");
    }

    private void ExecuteDone(string[] parts)
    {
        if (parts.Length > 1 && !string.Equals(parts[1], "@s", StringComparison.OrdinalIgnoreCase))
        {
            App.Ui?.ShowToast("目标只支持 @s（最新一条）");
            return;
        }
        var note = LatestOpenNote();
        if (note is null)
        {
            App.Ui?.ShowToast("没有可完成的待办");
            return;
        }
        note.IsDone = true;
        _store.QueueSave();
        NoteInput.Clear();
        FocusNotesInput();
        App.Ui?.ShowToast("已完成最新一条");
    }

    private NoteItem? LatestOpenNote()
        => _data.Notes.Where(x => !x.IsDone).OrderByDescending(x => x.CreatedAt).FirstOrDefault();

    private void ShowCommandHelp()
    {
        App.Ui?.ShowToast("/move 3 @s · /collect 7 · /done · /help", null, 5200);
        UpdateCommandHint();
    }

    private void ToggleNote_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NoteItem note) return;
        note.IsDone = !note.IsDone;
        _store.QueueSave();
    }

    private void DeleteNote_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NoteItem note) return;
        DeleteNote(note);
    }

    private void DeleteNoteMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NoteItem note)
            DeleteNote(note);
    }

    private void DeleteNote(NoteItem note)
    {
        var index = _data.Notes.IndexOf(note);
        if (index < 0) return;
        _data.Notes.RemoveAt(index);
        _store.QueueSave();
        _notesView.Refresh();
        App.Ui?.ShowToast("已删除", () =>
        {
            _data.Notes.Insert(Math.Min(index, _data.Notes.Count), note);
            _store.QueueSave();
            _notesView.Refresh();
            ScrollNotesToEnd();
        });
    }

    private void ScrollNotesToEnd()
    {
        Dispatcher.BeginInvoke(() => NotesScroll.ScrollToEnd(), DispatcherPriority.Loaded);
    }

    private void ScrollNotesToTop()
    {
        Dispatcher.BeginInvoke(() => NotesScroll.ScrollToTop(), DispatcherPriority.Loaded);
    }

    private void ScrollBillsToEnd()
    {
        Dispatcher.BeginInvoke(() => BillsScroll.ScrollToEnd(), DispatcherPriority.Loaded);
    }

    private void AmountInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;
        var start = AmountInput.SelectionStart;
        var length = AmountInput.SelectionLength;
        var prospective = AmountInput.Text.Remove(start, length).Insert(start, e.Text);
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
        UpdateAmountPlaceholder();
        UpdateAmountPreview();
        AddBillButton.IsEnabled = TryAmount(out _);
    }

    private bool TryAmount(out decimal amount)
        => ExpressionEvaluator.TryEvaluate(AmountInput.Text, out amount, out _);

    private void UpdateAmountPreview()
    {
        var valid = ExpressionEvaluator.TryEvaluate(AmountInput.Text, out var amount, out var hasOperator);
        var show = valid && hasOperator;
        var wasVisible = AmountPreviewText.Visibility == Visibility.Visible;
        if (show)
        {
            AmountPreviewText.Text = "= " + amount.ToString("#,##0.00", Zh);
            AmountPreviewText.Visibility = Visibility.Visible;
            if (!wasVisible)
                ScrollBillsToEnd();
        }
        else
        {
            AmountPreviewText.Visibility = Visibility.Collapsed;
        }
    }

    private void AmountInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        AddBill();
    }

    private void BillNoteInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateBillNotePlaceholder();
    }

    private void BillNoteInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        AddBill();
    }

    private void AddBill_Click(object sender, RoutedEventArgs e) => AddBill();

    private void AddBill()
    {
        if (!TryAmount(out var amount)) return;
        _data.Bills.Add(new BillItem
        {
            Date = DateTime.Today,
            Amount = Math.Round(amount, 2),
            Category = _category,
            Note = BillNoteInput.Text.Trim(),
            CreatedAt = DateTime.Now
        });
        _store.QueueSave();
        AmountInput.Clear();
        BillNoteInput.Clear();
        RefreshBills();
        ScrollBillsToEnd();
        Dispatcher.BeginInvoke(() => AmountInput.Focus(), DispatcherPriority.Input);
    }

    private void DeleteBill(BillItem bill)
    {
        var index = _data.Bills.IndexOf(bill);
        if (index < 0) return;
        _data.Bills.RemoveAt(index);
        _store.QueueSave();
        RefreshBills();
        App.Ui?.ShowToast("已删除", () =>
        {
            _data.Bills.Insert(Math.Min(index, _data.Bills.Count), bill);
            _store.QueueSave();
            RefreshBills();
            ScrollBillsToEnd();
        });
    }

    private void RefreshBills()
    {
        var bills = _data.Bills
            .Where(x => x.Date.Date == DateTime.Today)
            .OrderBy(x => x.CreatedAt)
            .ToList();

        if (_editingBillId is Guid editingId && bills.All(x => x.Id != editingId))
            _editingBillId = null;

        BillsListPanel.Children.Clear();
        _billNoteEditor = null;
        foreach (var bill in bills)
            BillsListPanel.Children.Add(BuildBillRow(bill));

        var total = bills.Sum(x => x.Amount);
        TodayStatsText.Text = "今日 " + Money(total);
        TodayCountText.Text = $"{bills.Count} 笔";

        if (_editingBillId is not null && _billNoteEditor is { } editor)
        {
            Dispatcher.BeginInvoke(() =>
            {
                editor.Focus();
                editor.SelectAll();
                editor.BringIntoView();
            }, DispatcherPriority.Input);
        }
    }

    private FrameworkElement BuildBillRow(BillItem bill)
    {
        var category = CategoryCatalog.Get(bill.Category);
        var row = new Border
        {
            BorderBrush = Res("PaperLineBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 4, 0, 4),
            Background = Brushes.Transparent,
            Cursor = Cursors.Arrow
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Ellipse
        {
            Width = 5.5,
            Height = 5.5,
            Fill = ThemeManager.Brush(category.Color),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1, 0, 6, 0)
        };
        Grid.SetColumn(dot, 0);
        grid.Children.Add(dot);

        var isEditing = bill.Id == _editingBillId;
        if (isEditing)
        {
            var editor = new TextBox
            {
                Text = bill.Note,
                FontSize = 11.3,
                MaxLength = 80,
                Background = Res("CardStrongBrush"),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2, 1, 2, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "输入备注，回车保存"
            };
            System.Windows.Automation.AutomationProperties.SetName(editor, "账单备注");
            editor.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    FinishBillNoteEdit(true);
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    FinishBillNoteEdit(false);
                }
            };
            editor.LostKeyboardFocus += (_, _) =>
            {
                if (!_finishingBillEdit) FinishBillNoteEdit(true);
            };
            Grid.SetColumn(editor, 1);
            grid.Children.Add(editor);
            _billNoteEditor = editor;
        }
        else
        {
            var text = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(bill.Note) ? category.Name : bill.Note,
                FontSize = 11.3,
                Foreground = Res("InkBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = string.IsNullOrWhiteSpace(bill.Note)
                    ? category.Name
                    : $"{category.Name} · {bill.Note}"
            };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
        }

        var amount = new TextBlock
        {
            Text = Money(bill.Amount),
            FontFamily = (FontFamily)FindResource("NumberFont"),
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Res("InkBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 2, 0)
        };
        Grid.SetColumn(amount, 2);
        grid.Children.Add(amount);

        var edit = new Button
        {
            Style = (Style)FindResource("TinyIconButton"),
            Content = isEditing ? "\uE73E" : "\uE70F",
            FontSize = 8,
            ToolTip = isEditing ? "保存备注" : "修改备注",
            Opacity = isEditing ? 0.8 : 0.14,
            Focusable = false
        };
        System.Windows.Automation.AutomationProperties.SetName(edit, isEditing ? "保存备注" : "修改备注");
        edit.Click += (_, _) =>
        {
            if (isEditing) FinishBillNoteEdit(true);
            else BeginBillNoteEdit(bill);
        };
        Grid.SetColumn(edit, 3);
        grid.Children.Add(edit);

        var delete = new Button
        {
            Style = (Style)FindResource("TinyIconButton"),
            Content = "\uE711",
            FontSize = 8,
            ToolTip = "删除",
            Opacity = 0.14,
            Focusable = false
        };
        System.Windows.Automation.AutomationProperties.SetName(delete, "删除账单");
        delete.Click += (_, _) => DeleteBill(bill);
        Grid.SetColumn(delete, 4);
        grid.Children.Add(delete);

        row.Child = grid;
        row.MouseEnter += (_, _) =>
        {
            row.Background = Res("HoverBrush");
            edit.Opacity = 0.8;
            delete.Opacity = 0.75;
        };
        row.MouseLeave += (_, _) =>
        {
            row.Background = Brushes.Transparent;
            edit.Opacity = isEditing ? 0.8 : 0.14;
            delete.Opacity = 0.14;
        };
        if (!isEditing)
        {
            row.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount != 2) return;
                if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null)
                    return;
                BeginBillNoteEdit(bill);
                e.Handled = true;
            };
        }
        var menu = new ContextMenu();
        var noteItem = new MenuItem { Header = "修改备注" };
        noteItem.Click += (_, _) => BeginBillNoteEdit(bill);
        var deleteItem = new MenuItem { Header = "删除" };
        deleteItem.Click += (_, _) => DeleteBill(bill);
        menu.Items.Add(noteItem);
        menu.Items.Add(deleteItem);
        row.ContextMenu = menu;
        return row;
    }

    private void BeginBillNoteEdit(BillItem bill)
    {
        if (_editingBillId == bill.Id) return;
        _editingBillId = bill.Id;
        RefreshBills();
    }

    private void FinishBillNoteEdit(bool save)
    {
        if (_finishingBillEdit) return;
        var billId = _editingBillId;
        if (billId is null) return;
        var editor = _billNoteEditor;
        _finishingBillEdit = true;

        if (save && editor is not null)
        {
            var bill = _data.Bills.FirstOrDefault(x => x.Id == billId);
            if (bill is not null)
            {
                var note = editor.Text.Trim();
                if (!string.Equals(bill.Note, note, StringComparison.Ordinal))
                {
                    bill.Note = note;
                    bill.UpdatedAt = DateTime.Now;
                    _store.QueueSave();
                }
            }
        }

        _editingBillId = null;
        _billNoteEditor = null;
        _finishingBillEdit = false;
        RefreshBills();
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = CategoryButton,
            Placement = PlacementMode.Bottom,
            StaysOpen = false
        };
        foreach (var category in CategoryCatalog.All)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = ThemeManager.Brush(category.Color),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0)
            });
            content.Children.Add(new TextBlock { Text = category.Name, FontSize = 11 });
            var item = new MenuItem { Header = content, Tag = category.Name };
            System.Windows.Automation.AutomationProperties.SetName(item, category.Name);
            item.Click += (_, _) =>
            {
                _category = category.Name;
                UpdateCategoryVisual();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void UpdateCategoryVisual()
    {
        var category = CategoryCatalog.Get(_category);
        CategoryText.Text = category.Name;
        CategoryDot.Fill = ThemeManager.Brush(category.Color);
    }

    private static string Money(decimal amount) => "¥" + amount.ToString("#,##0.00", Zh);

    private Brush Res(string key) => (Brush)FindResource(key);
}
