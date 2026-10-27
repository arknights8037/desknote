using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace DestNote;

internal static class SelfTest
{
    public static bool Run()
    {
        var log = new StringBuilder();
        var root = Path.Combine(Path.GetTempPath(), "DestNoteSelfTest-" + Guid.NewGuid().ToString("N"));
        var logPath = Path.Combine(Environment.CurrentDirectory, "selftest.log");
        var failures = 0;

        void Check(string name, bool condition, string? detail = null)
        {
            if (condition)
            {
                log.AppendLine("PASS  " + name);
            }
            else
            {
                failures++;
                log.AppendLine("FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : " :: " + detail));
            }
        }

        try
        {
            Directory.CreateDirectory(root);
            var data = new AppData();
            data.Notes.Add(new NoteItem
            {
                Text = "买牛奶",
                CreatedAt = new DateTime(2026, 10, 26, 9, 30, 0)
            });
            var done = new NoteItem
            {
                Text = "寄快递",
                CreatedAt = new DateTime(2026, 10, 25, 8, 0, 0)
            };
            done.IsDone = true;
            data.Notes.Add(done);
            data.Notes[0].DeferredUntil = new DateTime(2026, 11, 2);
            data.Notes[0].CollectedAt = new DateTime(2026, 10, 27, 10, 0, 0);

            var plan = new PlanGroup { Text = "搬家", CreatedAt = new DateTime(2026, 10, 20, 9, 0, 0) };
            plan.Items.Add(new PlanItem { Text = "找房子", CreatedAt = new DateTime(2026, 10, 20, 9, 5, 0) });
            data.Plans.Add(plan);

            data.Bills.Add(Bill("2026-10-19", 20m, "交通", "地铁月卡"));
            data.Bills.Add(Bill("2026-10-26", 12.5m, "餐饮", "午饭"));
            data.Bills.Add(Bill("2026-10-27", 7.5m, "餐饮", "咖啡"));
            data.Bills.Add(Bill("2026-10-28", 30m, "购物", "笔记本"));
            data.Bills.Add(Bill("2026-09-15", 20m, "娱乐", "电影"));
            data.Bills.Add(Bill("2026-11-02", 99m, "其他", "错误月份"));

            var store = new DataStore(root) { Data = data };
            store.SaveNow();
            Check("atomic save creates data.json", File.Exists(Path.Combine(root, "data.json")));
            Check("daily backup", Directory.GetFiles(Path.Combine(root, "backups"), "data-*.json").Length == 1);

            var loaded = new DataStore(root);
            var restored = loaded.Load();
            Check("round-trip note count", restored.Notes.Count == 2, restored.Notes.Count.ToString());
            Check("round-trip bill amount", restored.Bills.Any(x => x.Amount == 12.5m));
            Check("round-trip unicode", restored.Notes[0].Text == "买牛奶");
            Check("round-trip done state", restored.Notes.Any(x => x.IsDone && x.Text == "寄快递"));
            Check("round-trip deferred note", restored.Notes[0].DeferredUntil == new DateTime(2026, 11, 2));
            Check("round-trip collected note", restored.Notes[0].IsCollected);
            Check("round-trip plans", restored.Plans.Count == 1 && restored.Plans[0].Items.Count == 1
                && restored.Plans[0].Items[0].Text == "找房子");

            Check("expression plus", Eval("12+8", 20m, true));
            Check("expression precedence", Eval("1+2*3", 7m, true));
            Check("expression parentheses", Eval("(10+2)*3", 36m, true));
            Check("expression decimals", Eval("1.25+2.5", 3.75m, true));
            Check("expression full-width", Eval("12＋8", 20m, true));
            Check("expression currency and commas", Eval("¥1,200+300", 1500m, true));
            Check("expression division", Eval("8/4", 2m, true));
            Check("expression rounding", Eval("1.234+1", 2.23m, true));
            Check("plain amount", Eval("42", 42m, false));
            Check("expression incomplete rejected", !Eval("12+", 0m, true));
            Check("expression divide by zero rejected", !Eval("5/0", 0m, true));
            Check("expression negative rejected", !Eval("3-5", 0m, true));
            Check("expression plus accepted by input filter", ExpressionEvaluator.IsAllowedInput("12+8"));
            Check("expression letters rejected by input filter", !ExpressionEvaluator.IsAllowedInput("12a"));

            var week = StatsCalculator.BuildWeek(restored.Bills, new DateTime(2026, 10, 26));
            Check("week starts Monday", week.Start == new DateTime(2026, 10, 26), week.Start.ToString("O"));
            Check("week end Sunday", week.End == new DateTime(2026, 11, 1), week.End.ToString("O"));
            Check("week total", week.Total == 50m, week.Total.ToString(CultureInfo.InvariantCulture));
            Check("week count", week.Count == 3, week.Count.ToString());
            Check("week previous total", week.PreviousTotal == 20m, week.PreviousTotal.ToString(CultureInfo.InvariantCulture));
            Check("week top category", week.TopCategory == "购物", week.TopCategory);
            Check("week category shares total", Math.Abs(week.Categories.Sum(x => x.Share) - 1) < 0.0001);
            Check("week bars", week.Bars.Count == 7);

            var pastWeek = StatsCalculator.BuildWeek(restored.Bills, new DateTime(2026, 10, 19), new DateTime(2026, 10, 26));
            Check("past week uses full-week average", pastWeek.DailyAverage == 2.86, pastWeek.DailyAverage.ToString(CultureInfo.InvariantCulture));

            var month = StatsCalculator.BuildMonth(restored.Bills, new DateTime(2026, 10, 26), new DateTime(2026, 10, 26));
            Check("month start", month.Start == new DateTime(2026, 10, 1));
            Check("month end", month.End == new DateTime(2026, 10, 31));
            Check("month total", month.Total == 70m, month.Total.ToString(CultureInfo.InvariantCulture));
            Check("month excludes other months", month.Count == 4, month.Count.ToString());
            Check("month previous total", month.PreviousTotal == 20m);
            Check("month buckets", month.Bars.Count == 5, month.Bars.Count.ToString());

            var csvPath = Path.Combine(root, "export.csv");
            loaded.ExportBillsCsv(csvPath, restored.Bills, new DateTime(2026, 10, 26), new DateTime(2026, 11, 1));
            var csv = File.ReadAllText(csvPath);
            Check("csv period filter", csv.Contains("2026-10-26") && !csv.Contains("2026-09-15"));
            Check("csv utf8 bom", File.ReadAllBytes(csvPath).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));

            // Recovery path: second save creates data.bak.json, then corrupt primary.
            loaded.Data.Notes[0].Text = "买牛奶和面包";
            loaded.SaveNow();
            File.WriteAllText(Path.Combine(root, "data.json"), "{ broken json", Encoding.UTF8);
            var recovery = new DataStore(root);
            var recovered = recovery.Load();
            Check("corrupt primary falls back to backup", recovered.Notes[0].Text == "买牛奶", recovered.Notes[0].Text);
            Check("corruption warning present", !string.IsNullOrWhiteSpace(recovery.LoadWarning));

            var backupPath = Path.Combine(root, "manual-backup.json");
            recovery.BackupTo(backupPath);
            Check("manual backup writes valid json", IsValidJson(backupPath));

            loaded.Dispose();
        }
        catch (Exception ex)
        {
            failures++;
            log.AppendLine("FAIL  unexpected exception :: " + ex);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch
            {
                // ignored
            }

            log.AppendLine();
            log.AppendLine(failures == 0 ? "SELFTEST OK" : $"SELFTEST FAILED ({failures})");
            try
            {
                File.WriteAllText(logPath, log.ToString(), new UTF8Encoding(false));
            }
            catch
            {
                // ignored
            }
        }

        return failures == 0;
    }

    private static bool Eval(string input, decimal expected, bool expectedOperator)
    {
        if (!ExpressionEvaluator.TryEvaluate(input, out var amount, out var hasOperator))
            return false;
        return amount == expected && hasOperator == expectedOperator;
    }

    private static BillItem Bill(string date, decimal amount, string category, string note)
        => new()
        {
            Date = DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            Amount = amount,
            Category = category,
            Note = note,
            CreatedAt = DateTime.Now
        };

    private static bool IsValidJson(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("notes", out _);
        }
        catch
        {
            return false;
        }
    }
}
