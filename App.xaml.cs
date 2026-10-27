using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace DestNote;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;

    public static AppData Data { get; private set; } = new();
    public static DataStore Store { get; private set; } = null!;
    public static MainWindow? Ui { get; private set; }
    public static int ShowMessageId { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        SessionEnding += (_, _) =>
        {
            try
            {
                Ui?.PrepareForShutdown();
                Store?.SaveNow();
            }
            catch
            {
                // ignored
            }
        };

        if (e.Args.Any(x => string.Equals(x, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            var ok = SelfTest.Run();
            Shutdown(ok ? 0 : 1);
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                Store?.LogError("dispatcher", args.Exception);
                System.Windows.MessageBox.Show(
                    "轻笺遇到了一点问题，已写入错误日志。\n\n" + args.Exception.Message,
                    "轻笺", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch
            {
                // ignored
            }
            args.Handled = true;
        };

        var created = false;
        _singleInstanceMutex = new Mutex(true, @"Local\DestNote.SingleInstance.7E1A4F6B", out created);
        ShowMessageId = NativeMethods.RegisterWindowMessage("DestNote.ShowWindow");

        if (!created)
        {
            ActivateExistingInstance();
            Shutdown();
            return;
        }

        try
        {
            var dataDirectory = Environment.GetEnvironmentVariable("DESTNOTE_DATA_DIR");
            Store = new DataStore(string.IsNullOrWhiteSpace(dataDirectory) ? null : dataDirectory);
            Data = Store.Load();
            ThemeManager.Apply(Data.Settings.PaperTheme);
            var startupEnabled = StartupRegistration.IsEnabled();
            if (Data.Settings.StartWithWindows != startupEnabled)
            {
                Data.Settings.StartWithWindows = startupEnabled;
                Store.QueueSave();
            }

            var startHidden = e.Args.Any(x => string.Equals(x, "--startup", StringComparison.OrdinalIgnoreCase));
            Ui = new MainWindow(startHidden);
            MainWindow = Ui;
            Ui.Show();

            if (!string.IsNullOrWhiteSpace(Store.LoadWarning))
            {
                Ui.Dispatcher.BeginInvoke(() => Ui?.ShowToast(Store.LoadWarning, null, 5200),
                    DispatcherPriority.ApplicationIdle);
            }
        }
        catch (Exception ex)
        {
            try
            {
                System.Windows.MessageBox.Show("轻笺启动失败：\n" + ex.Message, "轻笺",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
                // ignored
            }
            Shutdown(1);
        }
    }

    private static void ActivateExistingInstance()
    {
        var message = NativeMethods.RegisterWindowMessage("DestNote.ShowWindow");
        for (var i = 0; i < 8; i++)
        {
            var handle = NativeMethods.FindWindow(null, "轻笺");
            if (handle != IntPtr.Zero)
            {
                NativeMethods.PostMessage(handle, message, IntPtr.Zero, IntPtr.Zero);
                NativeMethods.ShowWindow(handle, 9);
                NativeMethods.SetForegroundWindow(handle);
                return;
            }
            Thread.Sleep(150);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Store?.Flush();
            Store?.Dispose();
        }
        catch
        {
            // ignored
        }

        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch
        {
            // ignored
        }

        base.OnExit(e);
    }
}
