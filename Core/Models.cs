using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Windows.Threading;

namespace DuckPond.Core;

public class FocusSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "focus";      // focus | rest
    public int PlannedSec { get; set; }
    public int ActualSec { get; set; }
    public DateTime StartAt { get; set; } = DateTime.Now;
    public DateTime? EndAt { get; set; }
    public string Status { get; set; } = "completed"; // completed | aborted | skipped
    public string Task { get; set; } = "";
    public string Source { get; set; } = "pomodoro";  // pomodoro | schedule
    public string AbortedBy { get; set; } = "";
}

public class Duck
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Species { get; set; } = "classic";
    public DateTime BornAt { get; set; } = DateTime.Now;
    public int FocusSec { get; set; }
    public bool Alive { get; set; } = true;
    public string SessionId { get; set; } = "";
    public string Reason { get; set; } = "";
}

public class RestRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Mode { get; set; } = "daily";   // once | daily | weekly | interval
    public string Time { get; set; } = "10:15";   // HH:mm
    public int Weekdays { get; set; } = 127;      // bit0=周一 ... bit6=周日
    public int IntervalMin { get; set; } = 60;
    public int RestSec { get; set; } = 600;
    public bool Enabled { get; set; } = true;
    public string Label { get; set; } = "休息一下";
    public bool UseOverlay { get; set; } = true;
    public DateTime? OnceAt { get; set; }
    public DateTime? LastFired { get; set; }
}

public class Achievement
{
    public string Key { get; set; } = "";
    public DateTime UnlockedAt { get; set; } = DateTime.Now;
}

public class AppSettings
{
    public int FocusMin { get; set; } = 25;
    public int ShortBreakMin { get; set; } = 5;
    public int LongBreakMin { get; set; } = 15;
    public int CyclesBeforeLong { get; set; } = 4;
    public bool AutoStartNext { get; set; } = true;
    public bool OverlayDuringRest { get; set; } = true;
    public double OverlayStrength { get; set; } = 0.55;
    public bool DuckSound { get; set; } = true;
    public bool AutoStartApp { get; set; } = true;
    public bool FullscreenGuard { get; set; } = true;
    public int PostponeMin { get; set; } = 10;
    public bool DailyDigest { get; set; } = true;
    public int DailyFocusGoalMin { get; set; } = 240;
}

public class DataFile
{
    public List<FocusSession> Sessions { get; set; } = new();
    public List<Duck> Ducks { get; set; } = new();
    public List<RestRule> Rules { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
    public List<Achievement> Achievements { get; set; } = new();
    public DateTime? LastOpenAt { get; set; }
}

/// <summary>本地 JSON 存储：原子写入 + 每日备份。单人自用，无需数据库引擎。</summary>
public class Store
{
    public string DataDir { get; }
    public string DataPath => Path.Combine(DataDir, "data.json");
    private string BackupDir => Path.Combine(DataDir, "backups");

    public DataFile Data { get; private set; } = new();

    private static readonly JsonSerializerOptions Opt = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly DispatcherTimer _debounce;
    private bool _dirty;

    public Store()
    {
        DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DuckPond");
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(BackupDir);
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); SaveNow(); };
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(DataPath))
            {
                var json = File.ReadAllText(DataPath);
                Data = JsonSerializer.Deserialize<DataFile>(json, Opt) ?? new DataFile();
            }
        }
        catch
        {
            var broken = Path.Combine(DataDir, $"data.broken-{DateTime.Now:yyyyMMddHHmmss}.json");
            try { File.Copy(DataPath, broken, true); } catch { }
            Data = new DataFile();
        }

        // 首次使用：给一条默认规则（对齐你提的 10:15 休 10 分钟）
        if (Data.Rules.Count == 0)
        {
            Data.Rules.Add(new RestRule
            {
                Mode = "daily", Time = "10:15", RestSec = 600, Label = "上午休息", UseOverlay = true
            });
            Data.Rules.Add(new RestRule
            {
                Mode = "daily", Time = "15:30", RestSec = 600, Label = "下午休息", UseOverlay = true
            });
        }
    }

    public void Save() { _dirty = true; _debounce.Stop(); _debounce.Start(); }

    public void SaveNow()
    {
        try
        {
            var tmp = DataPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Data, Opt));
            File.Move(tmp, DataPath, true);
            _dirty = false;
            BackupDaily();
        }
        catch { /* 磁盘异常不阻塞主流程 */ }
    }

    private void BackupDaily()
    {
        try
        {
            var target = Path.Combine(BackupDir, $"data-{DateTime.Now:yyyyMMdd}.json");
            if (!File.Exists(target) && File.Exists(DataPath))
                File.Copy(DataPath, target, true);
            // 只保留最近 14 份
            var files = new DirectoryInfo(BackupDir).GetFiles("data-*.json").OrderByDescending(f => f.LastWriteTime).ToList();
            foreach (var f in files.Skip(14)) f.Delete();
        }
        catch { }
    }
}
