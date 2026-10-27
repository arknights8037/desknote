using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DestNote.Views;

public partial class PlanPage : UserControl
{
    private readonly AppData _data;
    private readonly DataStore _store;
    private Guid? _selectedGroupId;

    public PlanPage()
    {
        InitializeComponent();
        _data = App.Data;
        _store = App.Store;
        RefreshAll();
    }

    public void RefreshAll()
    {
        if (_selectedGroupId is null || _data.Plans.All(x => x.Id != _selectedGroupId))
            _selectedGroupId = _data.Plans.FirstOrDefault()?.Id;

        BuildGroups();
        BuildItems();
        RotateGroupButton.IsEnabled = SelectedGroup() is not null;
    }

    public void OnThemeChanged() => RefreshAll();

    private PlanGroup? SelectedGroup()
        => _selectedGroupId is Guid id ? _data.Plans.FirstOrDefault(x => x.Id == id) : null;

    private void BuildGroups()
    {
        GroupsPanel.Children.Clear();
        foreach (var group in _data.Plans.OrderBy(x => x.CreatedAt))
        {
            var selected = group.Id == _selectedGroupId;
            var row = new Border
            {
                Background = selected ? Res("AccentSoftBrush") : Brushes.Transparent,
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(1, 2, 3, 2),
                Margin = new Thickness(0, 1, 0, 1),
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var check = BuildCheck(group.IsDone);
            check.Width = 18;
            check.Height = 22;
            check.Click += (_, _) => ToggleGroup(group);
            System.Windows.Automation.AutomationProperties.SetName(check, group.IsDone ? "恢复事务" : "划去事务");
            grid.Children.Add(check);

            var text = new TextBlock
            {
                Text = group.Text,
                FontSize = 9.7,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = group.IsDone ? Res("FaintBrush") : Res("InkBrush"),
                TextDecorations = group.IsDone ? TextDecorations.Strikethrough : null,
                ToolTip = group.Text,
                Margin = new Thickness(1, 0, 0, 0)
            };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            row.Child = grid;
            if (group.IsDone) row.Opacity = 0.68;
            row.MouseLeftButtonDown += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null)
                    return;
                _selectedGroupId = group.Id;
                RefreshAll();
            };

            var menu = new ContextMenu();
            var rotate = new MenuItem { Header = "轮换" };
            rotate.Click += (_, _) => RotateGroup(group);
            var delete = new MenuItem { Header = "删除事务" };
            delete.Click += (_, _) => DeleteGroup(group);
            menu.Items.Add(rotate);
            menu.Items.Add(delete);
            row.ContextMenu = menu;

            GroupsPanel.Children.Add(row);
        }
    }

    private void BuildItems()
    {
        ItemsPanel.Children.Clear();
        var group = SelectedGroup();
        if (group is null) return;

        foreach (var item in group.Items.OrderBy(x => x.CreatedAt))
        {
            var row = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(1, 2, 1, 2),
                Margin = new Thickness(0, 1, 0, 1)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var check = BuildCheck(item.IsDone);
            check.Width = 18;
            check.Height = 22;
            check.Click += (_, _) => ToggleItem(group, item);
            System.Windows.Automation.AutomationProperties.SetName(check, item.IsDone ? "恢复事项" : "划去事项");
            grid.Children.Add(check);

            var text = new TextBlock
            {
                Text = item.Text,
                FontSize = 9.7,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = item.IsDone ? Res("FaintBrush") : Res("InkBrush"),
                TextDecorations = item.IsDone ? TextDecorations.Strikethrough : null,
                ToolTip = item.Text,
                Margin = new Thickness(1, 0, 2, 0)
            };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var rotate = MakeTinyAction("\uE72C", "轮换到末尾", () => RotateItem(group, item));
            Grid.SetColumn(rotate, 2);
            grid.Children.Add(rotate);

            var delete = MakeTinyAction("\uE711", "删除事项", () => DeleteItem(group, item));
            Grid.SetColumn(delete, 3);
            grid.Children.Add(delete);

            row.Child = grid;
            if (item.IsDone) row.Opacity = 0.68;
            row.MouseEnter += (_, _) =>
            {
                row.Background = Res("HoverBrush");
                rotate.Opacity = 0.8;
                delete.Opacity = 0.8;
            };
            row.MouseLeave += (_, _) =>
            {
                row.Background = Brushes.Transparent;
                rotate.Opacity = 0.16;
                delete.Opacity = 0.16;
            };

            var menu = new ContextMenu();
            var rotateItem = new MenuItem { Header = "轮换" };
            rotateItem.Click += (_, _) => RotateItem(group, item);
            var deleteItem = new MenuItem { Header = "删除事项" };
            deleteItem.Click += (_, _) => DeleteItem(group, item);
            menu.Items.Add(rotateItem);
            menu.Items.Add(deleteItem);
            row.ContextMenu = menu;

            ItemsPanel.Children.Add(row);
        }
    }

    private Button BuildCheck(bool isDone)
    {
        var visual = new Grid { Width = 15, Height = 16 };
        visual.Children.Add(new Ellipse
        {
            Width = 13,
            Height = 13,
            Stroke = isDone ? Res("AccentBrush") : Res("FaintBrush"),
            StrokeThickness = 1.1,
            Fill = isDone ? Res("AccentBrush") : Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (isDone)
        {
            visual.Children.Add(new TextBlock
            {
                Text = "\uE73E",
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 6.5,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        return new Button
        {
            Style = (Style)FindResource("IconButton"),
            Content = visual,
            Width = 18,
            Height = 22,
            Padding = new Thickness(0),
            Background = Brushes.Transparent
        };
    }

    private Button MakeTinyAction(string glyph, string tip, Action action)
    {
        var button = new Button
        {
            Style = (Style)FindResource("TinyIconButton"),
            Content = glyph,
            FontSize = 8,
            Width = 18,
            Height = 20,
            Opacity = 0.16,
            ToolTip = tip,
            Focusable = false
        };
        System.Windows.Automation.AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => action();
        return button;
    }

    private void ToggleGroup(PlanGroup group)
    {
        group.IsDone = !group.IsDone;
        group.DoneAt = group.IsDone ? DateTime.Now : null;
        _store.QueueSave();
        RefreshAll();
    }

    private void ToggleItem(PlanGroup group, PlanItem item)
    {
        item.IsDone = !item.IsDone;
        item.DoneAt = item.IsDone ? DateTime.Now : null;
        _store.QueueSave();
        RefreshAll();
    }

    private void RotateGroup_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGroup() is { } group)
            RotateGroup(group);
    }

    private void RotateGroup(PlanGroup group)
    {
        group.IsDone = false;
        group.DoneAt = null;
        group.Rounds++;
        var index = _data.Plans.IndexOf(group);
        if (index >= 0 && index < _data.Plans.Count - 1)
            _data.Plans.Move(index, _data.Plans.Count - 1);
        _store.QueueSave();
        RefreshAll();
    }

    private void RotateItem(PlanGroup group, PlanItem item)
    {
        item.IsDone = false;
        item.DoneAt = null;
        item.Rounds++;
        var index = group.Items.IndexOf(item);
        if (index >= 0 && index < group.Items.Count - 1)
            group.Items.Move(index, group.Items.Count - 1);
        _store.QueueSave();
        RefreshAll();
        ScrollItemsToEnd();
    }

    private void DeleteGroup(PlanGroup group)
    {
        var index = _data.Plans.IndexOf(group);
        if (index < 0) return;
        _data.Plans.RemoveAt(index);
        if (_selectedGroupId == group.Id)
            _selectedGroupId = _data.Plans.FirstOrDefault()?.Id;
        _store.QueueSave();
        RefreshAll();
        App.Ui?.ShowToast("已删除事务", () =>
        {
            _data.Plans.Insert(Math.Min(index, _data.Plans.Count), group);
            _selectedGroupId = group.Id;
            _store.QueueSave();
            RefreshAll();
        });
    }

    private void DeleteItem(PlanGroup group, PlanItem item)
    {
        var index = group.Items.IndexOf(item);
        if (index < 0) return;
        group.Items.RemoveAt(index);
        _store.QueueSave();
        RefreshAll();
        App.Ui?.ShowToast("已删除事项", () =>
        {
            group.Items.Insert(Math.Min(index, group.Items.Count), item);
            _selectedGroupId = group.Id;
            _store.QueueSave();
            RefreshAll();
        });
    }

    private void GroupInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateGroupPlaceholder();
        AddGroupButton.IsEnabled = !string.IsNullOrWhiteSpace(GroupInput.Text);
    }

    private void GroupInput_FocusChanged(object sender, KeyboardFocusChangedEventArgs e) => UpdateGroupPlaceholder();

    private void UpdateGroupPlaceholder()
    {
        GroupPlaceholder.Visibility = string.IsNullOrEmpty(GroupInput.Text) && !GroupInput.IsKeyboardFocusWithin
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void GroupInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        AddGroup();
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e) => AddGroup();

    private void AddGroup()
    {
        var text = GroupInput.Text.Trim();
        if (text.Length == 0) return;
        var group = new PlanGroup { Text = text, CreatedAt = DateTime.Now };
        _data.Plans.Add(group);
        _selectedGroupId = group.Id;
        _store.QueueSave();
        GroupInput.Clear();
        RefreshAll();
        Dispatcher.BeginInvoke(() => ItemInput.Focus(), DispatcherPriority.Input);
    }

    private void ItemInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateItemPlaceholder();
        AddItemButton.IsEnabled = !string.IsNullOrWhiteSpace(ItemInput.Text);
    }

    private void ItemInput_FocusChanged(object sender, KeyboardFocusChangedEventArgs e) => UpdateItemPlaceholder();

    private void UpdateItemPlaceholder()
    {
        ItemPlaceholder.Visibility = string.IsNullOrEmpty(ItemInput.Text) && !ItemInput.IsKeyboardFocusWithin
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ItemInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        AddItem();
    }

    private void AddItem_Click(object sender, RoutedEventArgs e) => AddItem();

    private void AddItem()
    {
        var text = ItemInput.Text.Trim();
        if (text.Length == 0) return;

        var group = SelectedGroup();
        if (group is null)
        {
            group = new PlanGroup { Text = "日常", CreatedAt = DateTime.Now };
            _data.Plans.Add(group);
            _selectedGroupId = group.Id;
        }

        group.Items.Add(new PlanItem { Text = text, CreatedAt = DateTime.Now });
        _store.QueueSave();
        ItemInput.Clear();
        RefreshAll();
        ScrollItemsToEnd();
        Dispatcher.BeginInvoke(() => ItemInput.Focus(), DispatcherPriority.Input);
    }

    private void ScrollItemsToEnd()
        => Dispatcher.BeginInvoke(() => ItemsScroll.ScrollToEnd(), DispatcherPriority.Loaded);

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T target) return target;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private Brush Res(string key) => (Brush)FindResource(key);
}
