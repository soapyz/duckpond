using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using DuckPond.Sys;
using DuckPond.UI;

namespace DuckPond.Core;

/// <summary>全局协调器：把计时、调度、鸭塘、遮罩、托盘、界面串起来。</summary>
public class Runtime : IDisposable
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public Store Store { get; }
    public PomodoroEngine Engine { get; } = new();
    public Scheduler Scheduler { get; }
    public StatsEngine Stats { get; }
    public AppSettings Settings => Store.Data.Settings;
    public string DataDir => Store.DataDir;

    public MainWindow? Main { get; private set; }
    public TrayService? Tray { get; private set; }
    public OverlayWindow? Overlay { get; private set; }
    public ToastWindow? Toast { get; private set; }

    private int _focusInCycle;
    private RestRule? _pendingRule;
    private DateTime _pendingAt = DateTime.MaxValue;
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromSeconds(10) };
    private DateTime _lastPush = DateTime.MinValue;
    private string _overlayLabel = "休息一下";

    public Runtime(Store store)
    {
        Store = store;
        Stats = new StatsEngine(store);
        Scheduler = new Scheduler(store);
    }

    public void Start()
    {
        Engine.Finished += OnFinished;
        Engine.Tick += () => PushState(false);
        Scheduler.Fire += OnScheduleFire;
        Scheduler.PreAlert += OnPreAlert;
        Scheduler.Changed += () => PushState(true);
        _watch.Tick += (_, _) => { TryStartPending(); PushState(false); };
        _watch.Start();
        Engine.Start();
        Scheduler.Start();
        AutoStart.Apply(Settings.AutoStartApp);
        Tray = new TrayService(this);
        Tray.UpdateTip();
    }

    // ---------- 面板 ----------

    public void ShowMain()
    {
        if (Main == null)
        {
            Main = new MainWindow(this);
            Main.Closed += (_, _) => Main = null;
        }
        if (Main.WindowState == WindowState.Minimized) Main.WindowState = WindowState.Normal;
        Main.Show();
        Main.Activate();
        Main.Topmost = true;
        Main.Topmost = false;
        PushState(true);
    }

    public void HideMain() => Main?.Hide();

    // ---------- 计时动作 ----------

    public void StartFocus(string task, int? minutes = null)
    {
        if (Engine.Phase == Phase.Rest) EndRestEarly("switch");
        Engine.StartFocus(minutes ?? Settings.FocusMin, task ?? "");
        HideOverlay();
        Tray?.UpdateTip();
        PushState(true);
    }

    public void StartRest(int seconds, string source, bool overlay = true, string label = "休息一下")
    {
        if (Engine.Phase == Phase.Focus) Engine.Abort("switch");
        _overlayLabel = label;
        Engine.StartRest(seconds, source);
        if (overlay && Settings.OverlayDuringRest) ShowOverlay(label, seconds);
        Tray?.UpdateTip();
        PushState(true);
    }

    public void TogglePause()
    {
        Engine.Toggle();
        PushState(true);
    }

    public void AbortFocus(string reason = "手动结束")
    {
        if (Engine.Phase == Phase.Focus) Engine.Abort(reason);
        else if (Engine.Phase == Phase.Rest) EndRestEarly(reason);
    }

    public void EndRestEarly(string reason = "点击退出")
    {
        HideOverlay();
        if (Engine.Phase == Phase.Rest) Engine.Abort(reason);
        PushState(true);
    }

    public void SkipCurrent()
    {
        if (Engine.Phase == Phase.Idle) return;
        Engine.Skip();
    }

    // ---------- 遮罩 ----------

    private void ShowOverlay(string label, int seconds)
    {
        if (Overlay == null)
        {
            Overlay = new OverlayWindow();
            Overlay.ExitRequested += () => EndRestEarly("点击退出");
        }
        Overlay.ShowFor(label, seconds);
    }

    public void HideOverlay() => Overlay?.HideAnimated();

    public void PreviewOverlay()
    {
        ShowOverlay("遮罩预览 · 30 秒后自动结束", 30);
    }

    // ---------- 引擎结果 ----------

    private void OnFinished(SessionResult r)
    {
        var kind = r.Phase == Phase.Focus ? "focus" : "rest";
        var session = new FocusSession
        {
            Kind = kind,
            PlannedSec = r.PlannedSec,
            ActualSec = r.ElapsedSec,
            StartAt = r.StartedAt,
            EndAt = DateTime.Now,
            Status = r.Status switch
            {
                FinishStatus.Completed => "completed",
                FinishStatus.Skipped => "skipped",
                _ => "aborted"
            },
            Task = r.Task,
            Source = r.Source,
            AbortedBy = r.Reason
        };
        Store.Data.Sessions.Add(session);

        if (r.Phase == Phase.Focus && r.Status == FinishStatus.Completed)
        {
            var streak = Stats.Streak();
            var duck = Ducks.Hatch(Store, session, streak);
            _focusInCycle++;
            var species = Ducks.SpeciesNames[duck.Species];
            Send(new { type = "celebrate", payload = new { kind = "duck", species = duck.Species, text = $"孵出一只{species}" } });
            SendSound("quack");

            var newAch = Achievements.Evaluate(Store, Stats);
            foreach (var a in newAch)
            {
                Send(new { type = "toast", payload = new { text = $"解锁徽章 {a.Icon} {a.Name}" } });
                Notify($"解锁徽章 {a.Icon} {a.Name}");
                Send(new { type = "celebrate", payload = new { kind = "badge", text = $"{a.Icon} {a.Name}" } });
            }

            if (Settings.AutoStartNext)
            {
                var longBreak = _focusInCycle >= Math.Max(1, Settings.CyclesBeforeLong);
                if (longBreak) _focusInCycle = 0;
                var seconds = (longBreak ? Settings.LongBreakMin : Settings.ShortBreakMin) * 60;
                StartRest(seconds, "pomodoro", Settings.OverlayDuringRest, longBreak ? "长休息" : "短休息");
                return;
            }
            Engine.Reset();
        }
        else if (r.Phase == Phase.Focus)
        {
            Ducks.Escape(Store, session, r.Reason);
            SendSound("fail");
            Send(new { type = "toast", payload = new { text = "鸭子飞走了，下次坐住就好" } });
            Notify(r.Status == FinishStatus.Skipped ? "这次专注跳过了，鸭子没孵出来" : "鸭子飞走了，下次坐住就好");
            TryStartPending();
        }
        else // rest
        {
            HideOverlay();
            if (r.Status == FinishStatus.Completed)
            {
                SendSound("chime");
                Send(new { type = "toast", payload = new { text = "休息结束，回来吧" } });
                Notify("休息结束，回来吧");
                TryStartPending();
            }
        }

        Store.Save();
        Tray?.UpdateTip();
        PushState(true);
    }

    // ---------- 定时规则 ----------

    private void OnScheduleFire(RestRule rule, DateTime at)
    {
        if (Scheduler.Paused) return;
        if (Engine.Phase != Phase.Idle)
        {
            _pendingRule = rule;
            _pendingAt = DateTime.MaxValue;
            Send(new { type = "toast", payload = new { text = $"{rule.Label}：正在专注，本轮结束后休息" } });
            Notify($"{rule.Label}：正在专注，本轮结束后休息");
            return;
        }
        if (Settings.FullscreenGuard && Fullscreen.IsForegroundFullscreen())
        {
            _pendingRule = rule;
            _pendingAt = DateTime.Now.AddMinutes(Math.Max(1, Settings.PostponeMin));
            Send(new { type = "toast", payload = new { text = $"检测到全屏应用，{Settings.PostponeMin} 分钟后再提醒休息" } });
            Notify($"检测到全屏应用，{Settings.PostponeMin} 分钟后再提醒休息");
            return;
        }
        StartRest(rule.RestSec, "schedule", rule.UseOverlay, rule.Label);
    }

    private void OnPreAlert(RestRule rule, DateTime at, int secondsLeft)
    {
        if (Scheduler.Paused) return;
        Send(new { type = "toast", payload = new { text = $"{at:HH:mm} 该休息了，{rule.Label}" } });
        Notify($"{at:HH:mm} 该休息了 · {rule.Label}");
        SendSound("chime");
    }

    private void TryStartPending()
    {
        if (_pendingRule == null) return;
        if (Engine.Phase != Phase.Idle) return;
        if (DateTime.Now < _pendingAt) return;
        var rule = _pendingRule;
        _pendingRule = null;
        StartRest(rule.RestSec, "schedule", rule.UseOverlay, rule.Label);
    }

    public void SetReminderPaused(bool paused)
    {
        Scheduler.Paused = paused;
        Send(new { type = "toast", payload = new { text = paused ? "定时提醒已暂停" : "定时提醒已恢复" } });
        Notify(paused ? "定时提醒已暂停" : "定时提醒已恢复");
        PushState(true);
    }

    // ---------- 与界面通信 ----------

    public void Send(object payload)
    {
        if (Main == null) return;
        var json = payload is string s ? s : JsonSerializer.Serialize(payload, J);
        Main.Send(json);
    }

    public void SendSound(string kind) => Send(new { type = "sound", payload = new { kind } });

    public void Notify(string text)
    {
        try
        {
            if (Toast == null) Toast = new ToastWindow();
            Toast.ShowText(text);
        }
        catch (Exception ex) { Log(ex); }
    }

    public void PushState(bool force = false)
    {
        if (Main == null) return;
        var now = DateTime.Now;
        if (!force && (now - _lastPush).TotalMilliseconds < 700) return;
        _lastPush = now;
        Main.Send(BuildState());
    }

    private string BuildState()
    {
        var s = Settings;
        var now = DateTime.Now;
        var today = Stats.Day(now);
        var totals = Stats.Totals();
        var streak = Stats.Streak();
        var week = Stats.Week();
        var month = Stats.Month(now);
        var hours = Stats.Hours();
        var level = Ducks.PondLevel(totals.Ducks);
        var next = Scheduler.NextFire();

        var ducks = Store.Data.Ducks.Where(d => d.Alive).OrderBy(d => d.BornAt)
            .Select(d => new
            {
                id = d.Id, species = d.Species,
                name = Ducks.SpeciesNames.GetValueOrDefault(d.Species, "小黄鸭"),
                stage = Ducks.StageOf(d.FocusSec),
                stageName = Ducks.StageName(Ducks.StageOf(d.FocusSec)),
                focusSec = d.FocusSec,
                bornAt = d.BornAt.ToString("MM-dd HH:mm")
            }).ToList();

        var ghosts = Store.Data.Ducks.Where(d => !d.Alive).OrderByDescending(d => d.BornAt).Take(30)
            .Select(d => new { id = d.Id, reason = d.Reason, bornAt = d.BornAt.ToString("MM-dd HH:mm") }).ToList();

        var rules = Store.Data.Rules.Select(r => new
        {
            id = r.Id, mode = r.Mode, time = r.Time, weekdays = r.Weekdays,
            intervalMin = r.IntervalMin, restSec = r.RestSec, enabled = r.Enabled,
            label = r.Label, useOverlay = r.UseOverlay,
            desc = Scheduler.Describe(r)
        }).ToList();

        var unlocked = Store.Data.Achievements.Select(a => a.Key).ToHashSet();
        var achievements = Achievements.Catalog.Select(a => new
        {
            key = a.Key, name = a.Name, desc = a.Desc, icon = a.Icon, unlocked = unlocked.Contains(a.Key)
        }).ToList();

        var recent = Store.Data.Sessions
            .Where(x => x.StartAt >= DateTime.Today)
            .OrderByDescending(x => x.StartAt)
            .Take(10)
            .Select(x => new
            {
                kind = x.Kind,
                start = x.StartAt.ToString("HH:mm"),
                end = x.EndAt?.ToString("HH:mm") ?? "",
                sec = x.ActualSec,
                status = x.Status,
                task = x.Task
            }).ToList();

        object? nextRest = next == null ? null : new
        {
            label = next.Value.Rule.Label,
            time = next.Value.At.ToString("HH:mm"),
            inMin = Math.Max(0, (int)Math.Round((next.Value.At - now).TotalMinutes)),
            at = next.Value.At.ToString("yyyy-MM-dd HH:mm")
        };

        var state = new
        {
            type = "state",
            payload = new
            {
                now = now.ToString("HH:mm:ss"),
                phase = Engine.Phase.ToString().ToLowerInvariant(),
                running = Engine.Running,
                paused = Scheduler.Paused,
                remaining = Engine.RemainingSec,
                planned = Engine.PlannedSec,
                elapsed = Engine.ElapsedSec,
                progress = Engine.Progress,
                task = Engine.Task,
                cycle = _focusInCycle,
                overlayLabel = _overlayLabel,
                settings = new
                {
                    focusMin = s.FocusMin,
                    shortBreakMin = s.ShortBreakMin,
                    longBreakMin = s.LongBreakMin,
                    cyclesBeforeLong = s.CyclesBeforeLong,
                    autoStartNext = s.AutoStartNext,
                    overlayDuringRest = s.OverlayDuringRest,
                    overlayStrength = s.OverlayStrength,
                    duckSound = s.DuckSound,
                    autoStartApp = s.AutoStartApp,
                    fullscreenGuard = s.FullscreenGuard,
                    postponeMin = s.PostponeMin,
                    dailyGoalMin = s.DailyFocusGoalMin
                },
                today = new
                {
                    focusSec = today.FocusSec,
                    focusCount = today.FocusCount,
                    restCount = today.RestCount,
                    escapes = today.Escapes,
                    goalSec = s.DailyFocusGoalMin * 60
                },
                totals = new
                {
                    focusSec = totals.FocusSec,
                    ducks = totals.Ducks,
                    escapes = totals.Escapes,
                    restSec = totals.RestSec,
                    streak,
                    pondLevel = level,
                    pondName = Ducks.PondName(level),
                    pondNextAt = Ducks.PondNextAt(level)
                },
                ducks,
                ghosts,
                rules,
                nextRest,
                week,
                month,
                hours,
                achievements,
                recent,
                dataDir = Store.DataDir
            }
        };
        return JsonSerializer.Serialize(state, J);
    }

    // ---------- 界面命令 ----------

    public void OnWebMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var cmd = root.TryGetProperty("cmd", out var c) ? c.GetString() ?? "" : "";
            switch (cmd)
            {
                case "ready":
                    PushState(true);
                    break;
                case "startFocus":
                {
                    var task = root.TryGetProperty("task", out var t) ? t.GetString() ?? "" : "";
                    var min = IntOf(root, "minutes", Settings.FocusMin);
                    StartFocus(task, min);
                    break;
                }
                case "toggle":
                    TogglePause();
                    break;
                case "abort":
                    AbortFocus("手动结束");
                    break;
                case "skip":
                    SkipCurrent();
                    break;
                case "startRest":
                    StartRest(IntOf(root, "sec", 300), "manual", true, root.TryGetProperty("label", out var l) ? l.GetString() ?? "休息一下" : "休息一下");
                    break;
                case "endRest":
                    EndRestEarly("点击退出");
                    break;
                case "previewOverlay":
                    PreviewOverlay();
                    break;
                case "patchSettings":
                    PatchSettings(root);
                    break;
                case "addRule":
                    AddRule(root);
                    break;
                case "updateRule":
                    UpdateRule(root);
                    break;
                case "deleteRule":
                    DeleteRule(root);
                    break;
                case "toggleRule":
                    ToggleRule(root);
                    break;
                case "openDataFolder":
                    OpenDataFolder();
                    break;
                case "savePng":
                    SavePng(root);
                    break;
                case "pauseReminders":
                    SetReminderPaused(root.TryGetProperty("paused", out var p) && p.GetBoolean());
                    break;
            }
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    private static int IntOf(JsonElement root, string name, int fallback)
    {
        if (!root.TryGetProperty(name, out var v)) return fallback;
        return v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;
    }

    private void PatchSettings(JsonElement root)
    {
        if (!root.TryGetProperty("patch", out var patch)) return;
        var s = Settings;
        foreach (var p in patch.EnumerateObject())
        {
            switch (p.Name)
            {
                case "focusMin": s.FocusMin = Clamp(p.Value.GetInt32(), 1, 180); break;
                case "shortBreakMin": s.ShortBreakMin = Clamp(p.Value.GetInt32(), 1, 60); break;
                case "longBreakMin": s.LongBreakMin = Clamp(p.Value.GetInt32(), 1, 120); break;
                case "cyclesBeforeLong": s.CyclesBeforeLong = Clamp(p.Value.GetInt32(), 1, 12); break;
                case "autoStartNext": s.AutoStartNext = p.Value.GetBoolean(); break;
                case "overlayDuringRest": s.OverlayDuringRest = p.Value.GetBoolean(); break;
                case "overlayStrength": s.OverlayStrength = Math.Clamp(p.Value.GetDouble(), 0.1, 0.9); break;
                case "duckSound": s.DuckSound = p.Value.GetBoolean(); break;
                case "autoStartApp":
                    s.AutoStartApp = p.Value.GetBoolean();
                    AutoStart.Apply(s.AutoStartApp);
                    break;
                case "fullscreenGuard": s.FullscreenGuard = p.Value.GetBoolean(); break;
                case "postponeMin": s.PostponeMin = Clamp(p.Value.GetInt32(), 1, 120); break;
                case "dailyGoalMin": s.DailyFocusGoalMin = Clamp(p.Value.GetInt32(), 30, 960); break;
            }
        }
        Store.Save();
        PushState(true);
    }

    private void AddRule(JsonElement root)
    {
        if (!root.TryGetProperty("rule", out var el)) return;
        var rule = JsonSerializer.Deserialize<RestRule>(el.GetRawText(), J) ?? new RestRule();
        rule.Id = Guid.NewGuid().ToString("N");
        rule.Enabled = true;
        if (!Scheduler.TryParseTime(rule.Time, out _)) rule.Time = "10:15";
        rule.RestSec = Clamp(rule.RestSec, 60, 7200);
        rule.IntervalMin = Clamp(rule.IntervalMin, 5, 600);
        if (rule.Mode == "once" && rule.OnceAt == null) rule.OnceAt = DateTime.Now.AddMinutes(30);
        Store.Data.Rules.Add(rule);
        Store.Save();
        Scheduler.Rebuild();
        PushState(true);
    }

    private void UpdateRule(JsonElement root)
    {
        if (!root.TryGetProperty("rule", out var el)) return;
        var incoming = JsonSerializer.Deserialize<RestRule>(el.GetRawText(), J);
        if (incoming == null) return;
        var rule = Store.Data.Rules.FirstOrDefault(r => r.Id == incoming.Id);
        if (rule == null) return;
        rule.Mode = incoming.Mode;
        rule.Time = incoming.Time;
        rule.Weekdays = incoming.Weekdays;
        rule.IntervalMin = Clamp(incoming.IntervalMin, 5, 600);
        rule.RestSec = Clamp(incoming.RestSec, 60, 7200);
        rule.Label = incoming.Label;
        rule.UseOverlay = incoming.UseOverlay;
        rule.Enabled = incoming.Enabled;
        rule.OnceAt = incoming.OnceAt;
        Store.Save();
        Scheduler.Rebuild();
        PushState(true);
    }

    private void DeleteRule(JsonElement root)
    {
        var id = root.TryGetProperty("id", out var v) ? v.GetString() : null;
        if (id == null) return;
        Store.Data.Rules.RemoveAll(r => r.Id == id);
        Store.Save();
        Scheduler.Rebuild();
        PushState(true);
    }

    private void ToggleRule(JsonElement root)
    {
        var id = root.TryGetProperty("id", out var v) ? v.GetString() : null;
        var enabled = root.TryGetProperty("enabled", out var e) && e.GetBoolean();
        var rule = Store.Data.Rules.FirstOrDefault(r => r.Id == id);
        if (rule == null) return;
        rule.Enabled = enabled;
        Store.Save();
        Scheduler.Rebuild();
        PushState(true);
    }

    private void OpenDataFolder()
    {
        try { Process.Start("explorer.exe", Store.DataDir); } catch { }
    }

    private void SavePng(JsonElement root)
    {
        try
        {
            var dataUrl = root.TryGetProperty("dataUrl", out var d) ? d.GetString() ?? "" : "";
            var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "打卡" : "打卡";
            var idx = dataUrl.IndexOf(',');
            if (idx < 0) return;
            var bytes = Convert.FromBase64String(dataUrl[(idx + 1)..]);
            var dir = Path.Combine(Store.DataDir, "exports");
            Directory.CreateDirectory(dir);
            var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
            var path = Path.Combine(dir, $"{safe}-{DateTime.Now:yyyyMMdd-HHmm}.png");
            File.WriteAllBytes(path, bytes);
            Send(new { type = "toast", payload = new { text = "打卡图已保存到 exports 文件夹" } });
            Notify("打卡图已保存到 exports 文件夹");
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex) { Log(ex); }
    }

    public static void Log(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DuckPond");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { }
    }

    private static int Clamp(int v, int lo, int hi) => Math.Min(hi, Math.Max(lo, v));

    public void Dispose()
    {
        _watch.Stop();
        Scheduler.Dispose();
        Engine.Dispose();
        Tray?.Dispose();
        Store.SaveNow();
    }
}
