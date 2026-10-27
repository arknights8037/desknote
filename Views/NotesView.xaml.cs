using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace DestNote.Views;

public partial class NotesView : UserControl
{
    private readonly AppData _data;
    private readonly DataStore _store;
    private readonly ListCollectionView _view;
    private readonly Dictionary<Guid, string> _editOriginals = new();
    private bool _initialized;
    private string _filter = "all";

    public NotesView()
    {
        InitializeComponent();
        _data = App.Data;
        _store = App.Store;

        _view = new ListCollectionView(_data.Notes)
        {
            Filter = FilterNote
        };
        _view.SortDescriptions.Add(new SortDescription(nameof(NoteItem.IsDone), System.ComponentModel.ListSortDirection.Ascending));
        _view.SortDescriptions.Add(new SortDescription(nameof(NoteItem.CreatedAt), System.ComponentModel.ListSortDirection.Descending));
        NotesList.ItemsSource = _view;

        _initialized = true;
        Refresh();
        _data.Notes.CollectionChanged += (_, _) => UpdateChrome();
    }

    public int CompletedCount => _data.Notes.Count(x => x.IsDone);

    public void RefreshView() => Refresh();

    private bool FilterNote(object item)
    {
        if (item is not NoteItem note) return false;
        return _filter switch
        {
            "open" => !note.IsDone,
            "done" => note.IsDone,
            _ => true
        };
    }

    private void Refresh()
    {
        if (!_initialized) return;
        foreach (var note in _data.Notes)
            note.RefreshDay();
        _view.Refresh();
        UpdateChrome();
    }

    private void UpdateChrome()
    {
        var total = _data.Notes.Count;
        var done = _data.Notes.Count(x => x.IsDone);
        var open = total - done;
        CountBadge.Visibility = total == 0 ? Visibility.Collapsed : Visibility.Visible;
        CountBadgeText.Text = open == 0 ? "都完成了" : $"{open} 件待办";
        SublineText.Text = total == 0 ? "轻一点，慢慢来" : $"已完成 {done} / {total}";
        FilterHintText.Text = _filter switch
        {
            "open" => "只看待办",
            "done" => "回看已完成",
            _ => "最近在前"
        };

        var empty = _view.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty)
        {
            switch (_filter)
            {
                case "open":
                    EmptyGlyph.Text = "\uE73E";
                    EmptyTitle.Text = "待办清空了";
                    EmptyHint.Text = "给自己倒杯水，休息一下";
                    break;
                case "done":
                    EmptyGlyph.Text = "\uE823";
                    EmptyTitle.Text = "还没有完成的便签";
                    EmptyHint.Text = "完成的事会收在这里";
                    break;
                default:
                    EmptyGlyph.Text = "\uE70B";
                    EmptyTitle.Text = "纸条还是空的";
                    EmptyHint.Text = "在上面写下一件小事，按 Enter 收好";
                    break;
            }
        }
    }

    public void FocusQuickAdd()
    {
        Dispatcher.BeginInvoke(() =>
        {
            QuickInput.Focus();
            Keyboard.Focus(QuickInput);
            QuickInput.CaretIndex = QuickInput.Text.Length;
        }, DispatcherPriority.Input);
    }

    public int ClearCompleted()
    {
        var removed = _data.Notes.Where(x => x.IsDone).ToList();
        if (removed.Count == 0) return 0;
        foreach (var note in removed)
            _data.Notes.Remove(note);
        _store.QueueSave();
        Refresh();
        return removed.Count;
    }

    private void QuickInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        QuickPlaceholder.Visibility = string.IsNullOrEmpty(QuickInput.Text) ? Visibility.Visible : Visibility.Collapsed;
        AddNoteButton.IsEnabled = !string.IsNullOrWhiteSpace(QuickInput.Text);
    }

    private void QuickInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            AddNote();
        }
    }

    private void AddNote_Click(object sender, RoutedEventArgs e) => AddNote();

    private void AddNote()
    {
        var text = QuickInput.Text.Trim();
        if (text.Length == 0) return;

        if (_filter == "done")
            OpenFilter.IsChecked = true;

        _data.Notes.Add(new NoteItem
        {
            Text = text,
            CreatedAt = DateTime.Now
        });
        QuickInput.Clear();
        _store.QueueSave();
        Refresh();
        NotesScroll.ScrollToTop();
        FocusQuickAdd();
    }

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _filter = (sender as FrameworkElement)?.Tag?.ToString() ?? "all";
        Refresh();
    }

    private void ToggleNote_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NoteItem note) return;
        note.IsDone = !note.IsDone;
        _store.QueueSave();
        Refresh();
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

    private void EditNoteMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NoteItem note)
            StartEditing(note);
    }

    private void DeleteNote(NoteItem note)
    {
        var index = _data.Notes.IndexOf(note);
        if (index < 0) return;
        _data.Notes.RemoveAt(index);
        _store.QueueSave();
        Refresh();
        App.Ui?.ShowToast("便签已删除", () =>
        {
            _data.Notes.Insert(Math.Min(index, _data.Notes.Count), note);
            _store.QueueSave();
            Refresh();
        });
    }

    private void NoteText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if ((sender as FrameworkElement)?.DataContext is not NoteItem note) return;
        e.Handled = true;
        StartEditing(note);
    }

    private void StartEditing(NoteItem note)
    {
        if (note.IsDone) return;
        foreach (var other in _data.Notes.Where(x => x.IsEditing && x != note).ToList())
            other.IsEditing = false;
        _editOriginals[note.Id] = note.Text;
        note.IsEditing = true;
    }

    private void EditBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NoteItem note) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CommitEditing(note, true);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_editOriginals.TryGetValue(note.Id, out var original))
                note.Text = original;
            note.IsEditing = false;
            _editOriginals.Remove(note.Id);
            Refresh();
        }
    }

    private void EditBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NoteItem note || !note.IsEditing) return;
        CommitEditing(note, true);
    }

    private void CommitEditing(NoteItem note, bool allowRemove)
    {
        if (!note.IsEditing) return;
        var text = note.Text.Trim();
        if (text.Length == 0 && allowRemove)
        {
            note.IsEditing = false;
            _editOriginals.Remove(note.Id);
            DeleteNote(note);
            return;
        }

        note.Text = text;
        note.IsEditing = false;
        _editOriginals.Remove(note.Id);
        _store.QueueSave();
        Refresh();
    }

    private void EditBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox box || !box.IsVisible) return;
        Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            Keyboard.Focus(box);
            box.SelectAll();
        }, DispatcherPriority.Input);
    }
}
