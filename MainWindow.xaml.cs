using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DestNote;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0x5157;
    private readonly AppData _data;
    private readonly DataStore _store;
    private readonly bool _startHidden;
    private Forms.NotifyIcon? _trayIcon;
    private HwndSource? _hwndSource;
    private bool _hotkeyRegistered;
    private bool _hotkeyFailed;
    private bool _ready;
    private bool _firstLoaded;
    private bool _settingsOpen;
    private bool _loadingSettings = true;
    private bool _hasSavedPlacement;
    private bool _cardMode;
    private bool _switchingMode;
    private bool _quitRequested;
    private bool _trayHintShown;
    private Action? _toastAction;
    private DispatcherTimer? _toastTimer;

    public MainWindow(bool startHidden = false)
    {
        InitializeComponent();
        _data = App.Data;
        _store = App.Store;
        _startHidden = startHidden;
        _cardMode = _data.Settings.CardMode;

        CardSurface.FullModeRequested += (_, _) => SetCardMode(false);
        CardSurface.PinRequested += (_, _) => TogglePin();
        CardSurface.MinimizeRequested += (_, _) => HideToTray();
        CardSurface.CloseRequested += (_, _) => QuitApp();

        ApplyModeVisibility();
        ApplyPlacement();
        Topmost = _data.Settings.AlwaysOnTop;
        UpdatePinVisual();
        CreateTrayIcon();
        _store.Saved += Store_Saved;
        Loaded += MainWindow_Loaded;
        LocationChanged += (_, _) => SavePlacementSoon();
        SizeChanged += (_, _) => SavePlacementSoon();
        StateChanged += (_, _) =>
        {
            if (_ready && !_quitRequested && WindowState == WindowState.Minimized)
                HideToTray();
        };
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        DateText.Text = DateTime.Today.ToString("M月d日 · dddd",
            System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
    }

    private IntPtr WindowHandle => new WindowInteropHelper(this).Handle;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = WindowHandle;
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);
        _hotkeyRegistered = NativeMethods.RegisterHotKey(handle, HotkeyId,
            NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat,
            (uint)Forms.Keys.N);
        _hotkeyFailed = !_hotkeyRegistered;
        ApplyTrayOnlyStyle(handle);
    }

    private void ApplyTrayOnlyStyle(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GwlExstyle);
        var desired = (style | NativeMethods.WsExToolWindow) & ~NativeMethods.WsExAppWindow;
        if (style != desired)
            NativeMethods.SetWindowLong(handle, NativeMethods.GwlExstyle, desired);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_firstLoaded) return;
        _firstLoaded = true;
        if (!_hasSavedPlacement)
            CenterOnScreen();
        ApplyTrayOnlyStyle(WindowHandle);
        _ready = true;

        if (_cardMode)
        {
            CardSurface.RefreshAll();
            CardSurface.ShowNotes(false);
        }
        else if (_data.Settings.LastTab == "bills")
        {
            BillsTab.IsChecked = true;
            SwitchTab("bills", false);
        }
        else
        {
            SwitchTab("notes", false);
        }

        if (_startHidden)
        {
            Hide();
            try
            {
                _trayIcon?.ShowBalloonTip(2600, "轻笺在托盘里", "按 Ctrl + Alt + N 随时写下一件小事。",
                    Forms.ToolTipIcon.None);
                _trayHintShown = true;
            }
            catch
            {
                // ignored
            }
        }
        else
        {
            Activate();
            if (!_cardMode && _data.Settings.LastTab != "bills")
                NotesPage.FocusQuickAdd();
        }

        if (_hotkeyFailed)
            ShowToast("全局快捷键被占用，可从托盘图标打开", null, 4600);
    }

    private void ApplyPlacement()
    {
        var area = SystemParameters.WorkArea;
        var minWidth = _cardMode ? 300 : 370;
        var minHeight = _cardMode ? 340 : 520;
        var maxWidth = Math.Max(minWidth, Math.Min(_cardMode ? 620 : 900, area.Width - 20));
        var maxHeight = Math.Max(minHeight, Math.Min(_cardMode ? 760 : 1000, area.Height - 20));
        var storedWidth = _cardMode ? _data.Settings.CardWindowWidth : _data.Settings.WindowWidth;
        var storedHeight = _cardMode ? _data.Settings.CardWindowHeight : _data.Settings.WindowHeight;
        var width = Math.Clamp(storedWidth, minWidth, maxWidth);
        var height = Math.Clamp(storedHeight, minHeight, maxHeight);
        MinWidth = minWidth;
        MinHeight = minHeight;
        Width = width;
        Height = height;

        var vx = SystemParameters.VirtualScreenLeft;
        var vy = SystemParameters.VirtualScreenTop;
        var vw = SystemParameters.VirtualScreenWidth;
        var vh = SystemParameters.VirtualScreenHeight;
        var left = _data.Settings.WindowLeft;
        var top = _data.Settings.WindowTop;
        _hasSavedPlacement = left > vx - width + 140 && left < vx + vw - 140 &&
                             top > vy - 1 && top < vy + vh - 120;
        WindowStartupLocation = _hasSavedPlacement ? WindowStartupLocation.Manual : WindowStartupLocation.CenterScreen;

        if (_hasSavedPlacement)
        {
            Left = left;
            Top = top;
        }
    }

    private void CenterOnScreen()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + Math.Max(0, (area.Width - ActualWidth) / 2);
        Top = area.Top + Math.Max(0, (area.Height - ActualHeight) / 2);
    }

    private void ApplyModeVisibility()
    {
        FullSurface.Visibility = _cardMode ? Visibility.Collapsed : Visibility.Visible;
        CardSurface.Visibility = _cardMode ? Visibility.Visible : Visibility.Collapsed;
        TapeDecor.Visibility = _cardMode ? Visibility.Collapsed : Visibility.Visible;
        MinWidth = _cardMode ? 300 : 370;
        MinHeight = _cardMode ? 340 : 520;
    }

    private void CaptureModeSize()
    {
        if (WindowState != WindowState.Normal || ActualWidth <= 0 || ActualHeight <= 0) return;
        if (_cardMode)
        {
            _data.Settings.CardWindowWidth = ActualWidth;
            _data.Settings.CardWindowHeight = ActualHeight;
        }
        else
        {
            _data.Settings.WindowWidth = ActualWidth;
            _data.Settings.WindowHeight = ActualHeight;
        }
    }

    private void SetCardMode(bool card)
    {
        if (_cardMode == card) return;

        CaptureModeSize();
        if (_settingsOpen) CloseSettings(false);

        var centerX = Left + ActualWidth / 2;
        var centerY = Top + ActualHeight / 2;
        var targetWidth = card ? _data.Settings.CardWindowWidth : _data.Settings.WindowWidth;
        var targetHeight = card ? _data.Settings.CardWindowHeight : _data.Settings.WindowHeight;
        _switchingMode = true;
        _cardMode = card;
        _data.Settings.CardMode = card;

        ApplyModeVisibility();
        var area = SystemParameters.WorkArea;
        var minWidth = card ? 300 : 370;
        var minHeight = card ? 340 : 520;
        var maxWidth = Math.Max(minWidth, Math.Min(card ? 620 : 900, area.Width - 20));
        var maxHeight = Math.Max(minHeight, Math.Min(card ? 760 : 1000, area.Height - 20));
        var width = Math.Clamp(targetWidth, minWidth, maxWidth);
        var height = Math.Clamp(targetHeight, minHeight, maxHeight);
        Width = width;
        Height = height;
        _switchingMode = false;

        var vx = SystemParameters.VirtualScreenLeft;
        var vy = SystemParameters.VirtualScreenTop;
        var vw = SystemParameters.VirtualScreenWidth;
        var vh = SystemParameters.VirtualScreenHeight;
        Left = Math.Clamp(centerX - width / 2, vx, Math.Max(vx, vx + vw - width));
        Top = Math.Clamp(centerY - height / 2, vy, Math.Max(vy, vy + vh - height));

        _store.QueueSave();
        if (card)
        {
            CardSurface.RefreshAll();
            CardSurface.ShowNotes(false);
        }
        else
        {
            NotesPage.RefreshView();
            BillsPage.RefreshAll(false);
            var tab = _data.Settings.LastTab == "bills" ? "bills" : "notes";
            if (tab == "bills") BillsTab.IsChecked = true;
            else NotesTab.IsChecked = true;
            SwitchTab(tab, false);
        }
    }

    private void SavePlacementSoon()
    {
        if (!_ready || _switchingMode || WindowState != WindowState.Normal || !IsVisible) return;
        if (Left < -10000 || Top < -10000 || ActualWidth <= 0 || ActualHeight <= 0) return;
        _data.Settings.WindowLeft = Left;
        _data.Settings.WindowTop = Top;
        if (_cardMode)
        {
            _data.Settings.CardWindowWidth = ActualWidth;
            _data.Settings.CardWindowHeight = ActualHeight;
        }
        else
        {
            _data.Settings.WindowWidth = ActualWidth;
            _data.Settings.WindowHeight = ActualHeight;
        }
        _store.QueueSave();
    }

    private void SaveWindowState()
    {
        if (Left > -10000 && Top > -10000 && ActualWidth > 0 && ActualHeight > 0)
        {
            _data.Settings.WindowLeft = Left;
            _data.Settings.WindowTop = Top;
            if (_cardMode)
            {
                _data.Settings.CardWindowWidth = ActualWidth;
                _data.Settings.CardWindowHeight = ActualHeight;
            }
            else
            {
                _data.Settings.WindowWidth = ActualWidth;
                _data.Settings.WindowHeight = ActualHeight;
            }
        }
        _store.SaveNow();
    }

    public void HideToTray()
    {
        if (_trayIcon is null)
        {
            QuitApp();
            return;
        }
        if (!IsVisible) return;

        CloseSettings(false);
        SaveWindowState();
        Hide();

        if (!_trayHintShown)
        {
            _trayHintShown = true;
            try
            {
                _trayIcon.ShowBalloonTip(2600, "轻笺已收进托盘",
                    "程序仍在后台运行，按 Ctrl + Alt + N 可随时打开。", Forms.ToolTipIcon.None);
            }
            catch
            {
                // ignored
            }
        }
    }

    public void QuitApp()
    {
        if (_quitRequested) return;
        _quitRequested = true;
        Close();
    }

    public void PrepareForShutdown() => _quitRequested = true;

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_quitRequested && _trayIcon is not null)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _ready = false;
        SaveWindowState();

        _toastTimer?.Stop();
        if (_hotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(WindowHandle, HotkeyId);
            _hotkeyRegistered = false;
        }
        _hwndSource?.RemoveHook(WndProc);
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _store.SaveNow();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _settingsOpen)
        {
            CloseSettings();
            e.Handled = true;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null) return;
        if (e.ClickCount == 2) return;
        try { DragMove(); } catch { /* ignored */ }
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

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        var tab = (sender as FrameworkElement)?.Tag?.ToString() ?? "notes";
        SwitchTab(tab, true);
    }

    private void SwitchTab(string tab, bool animate)
    {
        if (_settingsOpen) CloseSettings(false);
        var showNotes = tab != "bills";
        var target = showNotes ? (FrameworkElement)NotesPage : BillsPage;
        var other = showNotes ? (FrameworkElement)BillsPage : NotesPage;

        other.Visibility = Visibility.Collapsed;
        target.Visibility = Visibility.Visible;
        _data.Settings.LastTab = showNotes ? "notes" : "bills";
        _store.QueueSave();
        TabHintText.Text = showNotes ? "写下来，就轻一点" : "每一笔，都有去向";

        if (!showNotes)
            BillsPage.RefreshAll(false);

        if (animate)
            AnimateIn(target);

        if (showNotes && IsVisible && IsActive)
            NotesPage.FocusQuickAdd();
    }

    private static void AnimateIn(FrameworkElement target)
    {
        target.BeginAnimation(OpacityProperty, null);
        target.Opacity = 0;
        target.RenderTransform = new TranslateTransform(0, 5);
        target.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        ((TranslateTransform)target.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(5, 0, TimeSpan.FromMilliseconds(190))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void PinButton_Click(object sender, RoutedEventArgs e) => TogglePin();

    private void TogglePin()
    {
        _data.Settings.AlwaysOnTop = !_data.Settings.AlwaysOnTop;
        Topmost = _data.Settings.AlwaysOnTop;
        UpdatePinVisual();
        _store.QueueSave();
        if (_settingsOpen)
        {
            _loadingSettings = true;
            PinSwitch.IsChecked = _data.Settings.AlwaysOnTop;
            _loadingSettings = false;
        }
        if (!_cardMode)
            ShowToast(_data.Settings.AlwaysOnTop ? "已钉在桌面，始终置顶" : "已取消置顶");
    }

    private void CardModeSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        if (CardModeSwitch.IsChecked == true)
            SetCardMode(true);
    }

    private void UpdatePinVisual()
    {
        if (_data.Settings.AlwaysOnTop)
        {
            PinButton.SetResourceReference(BackgroundProperty, "AccentSoftBrush");
            PinButton.SetResourceReference(ForegroundProperty, "AccentDeepBrush");
            PinButton.ToolTip = "已钉在桌面（点击取消）";
        }
        else
        {
            PinButton.Background = Brushes.Transparent;
            PinButton.SetResourceReference(ForegroundProperty, "MutedBrush");
            PinButton.ToolTip = "钉在桌面";
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsOpen) CloseSettings();
        else OpenSettings();
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e) => CloseSettings();

    private void OpenSettings()
    {
        _settingsOpen = true;
        UpdateSettingsUi();
        SettingsOverlay.Visibility = Visibility.Visible;
        SettingsOverlay.Opacity = 0;
        SettingsOverlay.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        SettingsButton.SetResourceReference(BackgroundProperty, "AccentSoftBrush");
        SettingsButton.SetResourceReference(ForegroundProperty, "AccentDeepBrush");
    }

    private void CloseSettings(bool animate = true)
    {
        if (!_settingsOpen) return;
        _settingsOpen = false;
        SettingsButton.Background = Brushes.Transparent;
        SettingsButton.SetResourceReference(ForegroundProperty, "MutedBrush");

        if (!animate)
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
        fade.Completed += (_, _) => SettingsOverlay.Visibility = Visibility.Collapsed;
        SettingsOverlay.BeginAnimation(OpacityProperty, fade);
    }

    private void UpdateSettingsUi()
    {
        _loadingSettings = true;
        PinSwitch.IsChecked = _data.Settings.AlwaysOnTop;
        StartupSwitch.IsChecked = StartupRegistration.IsEnabled();
        CardModeSwitch.IsChecked = _cardMode;
        DataPathText.Text = _store.DataDirectory;
        DataPathText.ToolTip = _store.DataDirectory;
        var completed = NotesPage.CompletedCount;
        ClearDoneButton.Content = completed == 0 ? "没有已完成的便签" : $"清空已完成的便签（{completed}）";
        ClearDoneButton.IsEnabled = completed > 0;
        BuildThemeSwatches();
        _loadingSettings = false;
    }

    private void BuildThemeSwatches()
    {
        ThemePanel.Children.Clear();
        foreach (var palette in PaperPalette.All)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("ColorSwatch"),
                Background = ThemeManager.Brush(palette.Paper),
                GroupName = "PaperTheme",
                Tag = palette.Id,
                ToolTip = palette.Name,
                IsChecked = string.Equals(palette.Id, _data.Settings.PaperTheme, StringComparison.OrdinalIgnoreCase)
            };
            System.Windows.Automation.AutomationProperties.SetName(swatch, palette.Name + "纸张");
            swatch.Checked += ThemeSwatch_Checked;
            ThemePanel.Children.Add(swatch);
        }
    }

    private void ThemeSwatch_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        if ((sender as FrameworkElement)?.Tag is not string id) return;
        _data.Settings.PaperTheme = id;
        ThemeManager.Apply(id);
        _store.QueueSave();
        UpdatePinVisual();
        BillsPage.OnThemeChanged();
        CardSurface.OnThemeChanged();
        ShowToast($"纸张换成了{PaperPalette.Get(id).Name}色");
    }

    private void PinSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        var top = PinSwitch.IsChecked == true;
        if (_data.Settings.AlwaysOnTop == top) return;
        _data.Settings.AlwaysOnTop = top;
        Topmost = top;
        UpdatePinVisual();
        _store.QueueSave();
    }

    private void StartupSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        var enabled = StartupSwitch.IsChecked == true;
        if (StartupRegistration.SetEnabled(enabled, out var error))
        {
            _data.Settings.StartWithWindows = enabled;
            _store.QueueSave();
            ShowToast(enabled ? "已设置开机静默启动" : "已取消开机启动");
        }
        else
        {
            _loadingSettings = true;
            StartupSwitch.IsChecked = !enabled;
            _loadingSettings = false;
            ShowToast("设置开机启动失败：" + (error ?? "未知错误"), null, 5000);
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => HideToTray();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => HideToTray();

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _store.OpenDataFolder();
        }
        catch (Exception ex)
        {
            _store.LogError("open data folder", ex);
            ShowToast("无法打开数据文件夹");
        }
    }

    private void BackupData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "备份轻笺数据",
            Filter = "轻笺数据 (*.json)|*.json|所有文件 (*.*)|*.*",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = $"轻笺备份_{DateTime.Now:yyyyMMdd_HHmm}.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _store.BackupTo(dialog.FileName);
            ShowToast("数据已备份");
        }
        catch (Exception ex)
        {
            _store.LogError("backup", ex);
            ShowToast("备份失败，请换个位置再试");
        }
    }

    private void ExportAllBills_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出全部账单",
            Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"轻笺账单_{DateTime.Now:yyyyMMdd}.csv"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _store.ExportBillsCsv(dialog.FileName);
            ShowToast("全部账单已导出");
        }
        catch (Exception ex)
        {
            _store.LogError("export all bills", ex);
            ShowToast("导出失败，请换个位置再试");
        }
    }

    private void ClearDone_Click(object sender, RoutedEventArgs e)
    {
        var completed = NotesPage.CompletedCount;
        if (completed == 0) return;
        var result = System.Windows.MessageBox.Show(this,
            $"确定清空 {completed} 张已完成的便签吗？此操作不可撤销。",
            "轻笺", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;
        var removed = NotesPage.ClearCompleted();
        UpdateSettingsUi();
        ShowToast($"已清空 {removed} 张完成便签");
    }

    private void Store_Saved(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Store_Saved(sender, e));
            return;
        }
        SaveStatusText.Text = "已保存 " + DateTime.Now.ToString("HH:mm");
    }

    public void ShowToast(string message, Action? undo = null, int durationMs = 3800)
    {
        _toastTimer?.Stop();
        ToastText.Text = message;
        _toastAction = undo;
        ToastUndoButton.Visibility = undo is null ? Visibility.Collapsed : Visibility.Visible;
        ToastHost.BeginAnimation(OpacityProperty, null);
        ToastHost.Visibility = Visibility.Visible;
        ToastHost.Opacity = 0;
        ToastHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
        _toastTimer.Tick += (_, _) => HideToast();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer?.Stop();
        _toastTimer = null;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
        fade.Completed += (_, _) =>
        {
            ToastHost.Visibility = Visibility.Collapsed;
            ToastHost.Opacity = 0;
        };
        ToastHost.BeginAnimation(OpacityProperty, fade);
    }

    private void ToastUndo_Click(object sender, RoutedEventArgs e)
    {
        var action = _toastAction;
        _toastAction = null;
        HideToast();
        action?.Invoke();
    }

    private void CreateTrayIcon()
    {
        try
        {
            Drawing.Icon icon;
            var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (resource?.Stream is not null)
            {
                using var stream = resource.Stream;
                icon = new Drawing.Icon(stream);
            }
            else
            {
                icon = Drawing.SystemIcons.Application;
            }

            var menu = new Forms.ContextMenuStrip
            {
                ShowImageMargin = false
            };
            var open = new Forms.ToolStripMenuItem("打开轻笺");
            open.Font = new Drawing.Font(open.Font, Drawing.FontStyle.Bold);
            open.Click += (_, _) => ShowFromTray(false);
            var quick = new Forms.ToolStripMenuItem("快速写便签");
            quick.Click += (_, _) => ShowFromTray(true);
            var exit = new Forms.ToolStripMenuItem("退出");
            exit.Click += (_, _) => Dispatcher.BeginInvoke(QuitApp);
            menu.Items.Add(open);
            menu.Items.Add(quick);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(exit);

            _trayIcon = new Forms.NotifyIcon
            {
                Icon = icon,
                Text = "轻笺 · 桌面便签与账本",
                ContextMenuStrip = menu,
                Visible = true
            };
            _trayIcon.DoubleClick += (_, _) => ShowFromTray(false);
        }
        catch (Exception ex)
        {
            _store.LogError("tray icon", ex);
        }
    }

    private void ShowFromTray(bool quickNote)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ShowFromTray(quickNote));
            return;
        }

        if (!IsVisible) Show();
        ShowInTaskbar = false;
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        ApplyTrayOnlyStyle(WindowHandle);
        Activate();
        NativeMethods.SetForegroundWindow(WindowHandle);

        if (quickNote)
        {
            CloseSettings(false);
            if (_cardMode)
            {
                CardSurface.ShowNotes(false);
                CardSurface.FocusNotesInput();
            }
            else
            {
                if (NotesTab.IsChecked != true)
                    NotesTab.IsChecked = true;
                else
                    SwitchTab("notes", false);
                NotesPage.FocusQuickAdd();
            }
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == App.ShowMessageId && App.ShowMessageId != 0)
        {
            ShowFromTray(false);
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            ShowFromTray(true);
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WmNchittest)
        {
            var hit = ResizeHitTest(lParam);
            if (hit != 0)
            {
                handled = true;
                return new IntPtr(hit);
            }
        }

        return IntPtr.Zero;
    }

    private int ResizeHitTest(IntPtr lParam)
    {
        var screenPoint = new Point(
            unchecked((short)((long)lParam & 0xFFFF)),
            unchecked((short)(((long)lParam >> 16) & 0xFFFF)));
        var point = Paper.PointFromScreen(screenPoint);

        const double edge = 7;
        if (point.X < -edge || point.Y < -edge ||
            point.X > Paper.ActualWidth + edge || point.Y > Paper.ActualHeight + edge)
            return 0;

        var left = point.X <= edge;
        var right = point.X >= Paper.ActualWidth - edge;
        var top = point.Y <= edge;
        var bottom = point.Y >= Paper.ActualHeight - edge;

        if (left && top) return NativeMethods.Httopleft;
        if (right && top) return NativeMethods.Httopright;
        if (left && bottom) return NativeMethods.Htbottomleft;
        if (right && bottom) return NativeMethods.Htbottomright;
        if (left) return NativeMethods.Htleft;
        if (right) return NativeMethods.Htright;
        if (top) return NativeMethods.Httop;
        if (bottom) return NativeMethods.Htbottom;
        return 0;
    }
}
