using System.Windows.Threading;

namespace DuckPond.Core;

/// <summary>定时休息调度：每天 / 每周指定几天 / 一次性 / 固定间隔，提前 30 秒预告。</summary>
public class Scheduler : IDisposable
{
    private readonly Store _store;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly Dictionary<string, DateTime> _due = new();
    private readonly HashSet<string> _alerted = new();

    public bool Paused { get; set; }

    public event Action<RestRule, DateTime>? Fire;
    public event Action<RestRule, DateTime, int>? PreAlert;
    public event Action? Changed;

    public Scheduler(Store store)
    {
        _store = store;
        _timer.Tick += (_, _) => Evaluate(DateTime.Now);
    }

    public void Start()
    {
        var now = DateTime.Now;
        foreach (var r in _store.Data.Rules)
            _due[r.Id] = NextAfter(r, now) ?? DateTime.MaxValue;
        _timer.Start();
        Changed?.Invoke();
    }

    public void Rebuild()
    {
        var now = DateTime.Now;
        _due.Clear();
        _alerted.Clear();
        foreach (var r in _store.Data.Rules)
            _due[r.Id] = NextAfter(r, now) ?? DateTime.MaxValue;
        Changed?.Invoke();
    }

    public void Dispose() => _timer.Stop();

    private void Evaluate(DateTime now)
    {
        foreach (var rule in _store.Data.Rules)
        {
            if (!rule.Enabled) continue;
            if (!_due.TryGetValue(rule.Id, out var dueAt))
            {
                _due[rule.Id] = NextAfter(rule, now) ?? DateTime.MaxValue;
                continue;
            }
            if (dueAt == DateTime.MaxValue) continue;

            var secondsLeft = (int)Math.Round((dueAt - now).TotalSeconds);
            if (secondsLeft is > 0 and <= 40 && !Paused && !_alerted.Contains(rule.Id + dueAt.Ticks))
            {
                _alerted.Add(rule.Id + dueAt.Ticks);
                PreAlert?.Invoke(rule, dueAt, secondsLeft);
            }

            if (dueAt <= now)
            {
                rule.LastFired = now;
                _due[rule.Id] = NextAfter(rule, now) ?? DateTime.MaxValue;
                if (!Paused)
                {
                    Fire?.Invoke(rule, dueAt);
                    Changed?.Invoke();
                }
            }
        }
    }

    /// <summary>下一次触发的规则与时间，供界面显示。</summary>
    public (RestRule Rule, DateTime At)? NextFire()
    {
        RestRule? best = null;
        DateTime bestAt = DateTime.MaxValue;
        foreach (var r in _store.Data.Rules)
        {
            if (!r.Enabled) continue;
            var at = _due.TryGetValue(r.Id, out var d) ? d : NextAfter(r, DateTime.Now) ?? DateTime.MaxValue;
            if (at < bestAt) { bestAt = at; best = r; }
        }
        return best == null || bestAt == DateTime.MaxValue ? null : (best, bestAt);
    }

    public DateTime? NextAfter(RestRule rule, DateTime from)
    {
        if (!rule.Enabled) return null;
        switch (rule.Mode)
        {
            case "once":
                return rule.OnceAt is { } t && t > from ? t : null;

            case "interval":
            {
                var anchor = rule.LastFired ?? from;
                var step = TimeSpan.FromMinutes(Math.Max(1, rule.IntervalMin));
                var at = anchor + step;
                if (at <= from) at = from + step;
                return at;
            }

            case "weekly":
            {
                if (!TryParseTime(rule.Time, out var hm)) return null;
                for (var i = 0; i < 8; i++)
                {
                    var day = from.Date.AddDays(i);
                    if ((rule.Weekdays & (1 << (((int)day.DayOfWeek + 6) % 7))) == 0) continue;
                    var at = day + hm;
                    if (at > from) return at;
                }
                return null;
            }

            default: // daily
            {
                if (!TryParseTime(rule.Time, out var hm)) return null;
                var today = from.Date + hm;
                return today > from ? today : today.AddDays(1);
            }
        }
    }

    public static bool TryParseTime(string text, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split(':');
        if (parts.Length < 2) return false;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return false;
        if (h is < 0 or > 23 || m is < 0 or > 59) return false;
        result = new TimeSpan(h, m, 0);
        return true;
    }

    public string Describe(RestRule r)
    {
        var days = new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
        return r.Mode switch
        {
            "interval" => $"每 {r.IntervalMin} 分钟",
            "once" => r.OnceAt?.ToString("MM-dd HH:mm") ?? "一次性",
            "weekly" => string.Join("", days.Where((_, i) => (r.Weekdays & (1 << i)) != 0).Select(d => d.Replace("周", ""))) + " " + r.Time,
            _ => $"每天 {r.Time}"
        };
    }
}
