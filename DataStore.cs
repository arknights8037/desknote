using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace DestNote;

public sealed class DataStore : IDisposable
{
    private readonly string _dataDirectory;
    private readonly string _dataPath;
    private readonly string _backupPath;
    private readonly string _dailyBackupDirectory;
    private readonly DispatcherTimer _saveTimer;
    private readonly object _fileLock = new();
    private bool _disposed;

    public DataStore(string? dataDirectory = null)
    {
        _dataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DestNote");
        _dataPath = Path.Combine(_dataDirectory, "data.json");
        _backupPath = Path.Combine(_dataDirectory, "data.bak.json");
        _dailyBackupDirectory = Path.Combine(_dataDirectory, "backups");

        Directory.CreateDirectory(_dataDirectory);
        Directory.CreateDirectory(_dailyBackupDirectory);

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(280)
        };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
        };
    }

    public string DataDirectory => _dataDirectory;
    public string DataFilePath => _dataPath;
    public AppData Data { get; set; } = new();
    public string? LoadWarning { get; private set; }
    public DateTime? LastSavedAt { get; private set; }

    public event EventHandler? Saved;

    private static JsonSerializerOptions JsonOptions => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AppData Load()
    {
        AppData? data = null;
        if (File.Exists(_dataPath))
        {
            try
            {
                data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(_dataPath, Encoding.UTF8), JsonOptions);
            }
            catch (Exception ex)
            {
                LoadWarning = "主数据文件读取失败，已尝试使用上次自动备份。";
                TrySaveCorruptCopy();
                LogError("load primary", ex);
            }
        }

        if (data is null && File.Exists(_backupPath))
        {
            try
            {
                data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(_backupPath, Encoding.UTF8), JsonOptions);
                if (data is not null)
                    LoadWarning = (LoadWarning ?? "主数据文件不存在。") + " 已从自动备份恢复。";
            }
            catch (Exception ex)
            {
                LoadWarning = "数据文件与自动备份都无法读取，已创建新数据。";
                LogError("load backup", ex);
            }
        }

        data ??= new AppData();
        data.Notes ??= new();
        data.Bills ??= new();
        data.Plans ??= new();
        data.Settings ??= new();

        Data = data;
        foreach (var note in data.Notes)
        {
            if (note.Id == Guid.Empty) note.Id = Guid.NewGuid();
            note.IsEditing = false;
        }

        foreach (var bill in data.Bills)
        {
            if (bill.Id == Guid.Empty) bill.Id = Guid.NewGuid();
            if (bill.Amount < 0) bill.Amount = Math.Abs(bill.Amount);
            bill.Date = bill.Date.Date;
            if (string.IsNullOrWhiteSpace(bill.Category)) bill.Category = "其他";
        }

        foreach (var group in data.Plans)
        {
            if (group.Id == Guid.Empty) group.Id = Guid.NewGuid();
            group.Items ??= new();
            foreach (var item in group.Items)
            {
                if (item.Id == Guid.Empty) item.Id = Guid.NewGuid();
            }
        }

        return data;
    }

    public void QueueSave()
    {
        if (_disposed) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        if (_disposed) return;
        _saveTimer.Stop();

        lock (_fileLock)
        {
            try
            {
                var json = JsonSerializer.Serialize(Data, JsonOptions);
                var tempPath = _dataPath + ".tmp";
                File.WriteAllText(tempPath, json, new UTF8Encoding(false));

                if (File.Exists(_dataPath))
                {
                    if (File.Exists(_backupPath))
                        File.Delete(_backupPath);
                    File.Replace(tempPath, _dataPath, _backupPath, true);
                }
                else
                {
                    File.Move(tempPath, _dataPath);
                }

                LastSavedAt = DateTime.Now;
                EnsureDailyBackup();
                Saved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                LogError("save", ex);
                try
                {
                    if (File.Exists(_dataPath + ".tmp"))
                        File.Delete(_dataPath + ".tmp");
                }
                catch
                {
                    // ignored
                }
            }
        }
    }

    public void Flush() => SaveNow();

    public void BackupTo(string destinationPath)
    {
        SaveNow();
        lock (_fileLock)
        {
            File.Copy(_dataPath, destinationPath, true);
        }
    }

    public void ExportBillsCsv(string destinationPath, IEnumerable<BillItem>? bills = null, DateTime? from = null, DateTime? to = null)
    {
        var query = (bills ?? Data.Bills).AsEnumerable();
        if (from is not null) query = query.Where(x => x.Date.Date >= from.Value.Date);
        if (to is not null) query = query.Where(x => x.Date.Date <= to.Value.Date);
        var rows = query.OrderBy(x => x.Date).ThenBy(x => x.CreatedAt).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("日期,金额,类别,备注,录入时间");
        foreach (var bill in rows)
        {
            sb.Append(bill.Date.ToString("yyyy-MM-dd")).Append(',');
            sb.Append(bill.Amount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(Csv(bill.Category)).Append(',');
            sb.Append(Csv(bill.Note)).Append(',');
            sb.AppendLine(bill.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        File.WriteAllText(destinationPath, sb.ToString(), new UTF8Encoding(true));
    }

    public void OpenDataFolder()
    {
        Directory.CreateDirectory(_dataDirectory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{_dataDirectory}\"",
            UseShellExecute = true
        });
    }

    private static string Csv(string? text)
    {
        var value = text ?? "";
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    private void EnsureDailyBackup()
    {
        try
        {
            if (!File.Exists(_dataPath)) return;
            var target = Path.Combine(_dailyBackupDirectory, $"data-{DateTime.Today:yyyyMMdd}.json");
            if (!File.Exists(target))
            {
                File.Copy(_dataPath, target, true);
                var old = Directory.GetFiles(_dailyBackupDirectory, "data-*.json")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Skip(30)
                    .ToList();
                foreach (var file in old)
                {
                    try { File.Delete(file); } catch { /* ignored */ }
                }
            }
        }
        catch (Exception ex)
        {
            LogError("daily backup", ex);
        }
    }

    private void TrySaveCorruptCopy()
    {
        try
        {
            var target = Path.Combine(_dataDirectory, $"data.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(_dataPath, target, true);
        }
        catch
        {
            // ignored
        }
    }

    public void LogError(string context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            File.AppendAllText(Path.Combine(_dataDirectory, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
            // ignored
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _saveTimer.Stop();
        SaveNow();
    }
}

public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DestNote";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, false);
            var value = key?.GetValue(ValueName) as string;
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(value) || exe is null) return false;
            var registeredExe = value.Trim();
            if (registeredExe.StartsWith('"'))
            {
                var closing = registeredExe.IndexOf('"', 1);
                registeredExe = closing > 0 ? registeredExe[1..closing] : registeredExe.Trim('"');
            }
            else
            {
                registeredExe = registeredExe.Split(' ')[0];
            }
            return string.Equals(registeredExe, exe, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled, out string? error)
    {
        error = null;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exe))
                    throw new InvalidOperationException("无法确定程序路径。");
                key.SetValue(ValueName, $"\"{exe}\" --startup", Microsoft.Win32.RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
