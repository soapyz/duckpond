using System.Globalization;

namespace DuckPond.Core;

public class DayStat
{
    public string Date { get; set; } = "";
    public string Label { get; set; } = "";
    public int FocusSec { get; set; }
    public int FocusCount { get; set; }
    public int RestCount { get; set; }
    public int Ducks { get; set; }
    public int Escapes { get; set; }
}

public class DayCell
{
    public int Day { get; set; }
    public int FocusSec { get; set; }
    public int Ducks { get; set; }
    public int Escapes { get; set; }
}

public class MonthStat
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int TotalSec { get; set; }
    public int Ducks { get; set; }
    public int Escapes { get; set; }
    public List<DayCell> Days { get; set; } = new();
}

public class TotalsStat
{
    public int FocusSec { get; set; }
    public int Ducks { get; set; }
    public int Escapes { get; set; }
    public int RestSec { get; set; }
}

public class StatsEngine
{
    private readonly Store _store;
    private static readonly string[] WeekNames = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };

    public StatsEngine(Store store) => _store = store;

    private IEnumerable<FocusSession> SessionsOn(DateTime day)
    {
        var start = day.Date;
        var end = start.AddDays(1);
        return _store.Data.Sessions.Where(s => s.StartAt >= start && s.StartAt < end);
    }

    public DayStat Day(DateTime day)
    {
        var list = SessionsOn(day).ToList();
        var focus = list.Where(s => s.Kind == "focus" && s.Status == "completed").ToList();
        return new DayStat
        {
            Date = day.ToString("yyyy-MM-dd"),
            Label = WeekNames[(int)day.DayOfWeek],
            FocusSec = focus.Sum(s => s.ActualSec),
            FocusCount = focus.Count,
            RestCount = list.Count(s => s.Kind == "rest" && s.Status == "completed"),
            Ducks = focus.Count,
            Escapes = list.Count(s => s.Kind == "focus" && s.Status != "completed")
        };
    }

    public List<DayStat> Week()
    {
        var today = DateTime.Now.Date;
        return Enumerable.Range(0, 7).Select(i => Day(today.AddDays(-6 + i))).ToList();
    }

    public MonthStat Month(DateTime anchor)
    {
        var first = new DateTime(anchor.Year, anchor.Month, 1);
        var days = DateTime.DaysInMonth(anchor.Year, anchor.Month);
        var stat = new MonthStat { Year = anchor.Year, Month = anchor.Month };
        for (var d = 1; d <= days; d++)
        {
            var ds = Day(new DateTime(anchor.Year, anchor.Month, d));
            stat.Days.Add(new DayCell { Day = d, FocusSec = ds.FocusSec, Ducks = ds.Ducks, Escapes = ds.Escapes });
            stat.TotalSec += ds.FocusSec;
            stat.Ducks += ds.Ducks;
            stat.Escapes += ds.Escapes;
        }
        return stat;
    }

    public int[] Hours(int days = 30)
    {
        var arr = new int[24];
        var from = DateTime.Now.Date.AddDays(-days);
        foreach (var s in _store.Data.Sessions.Where(x => x.Kind == "focus" && x.Status == "completed" && x.StartAt >= from))
            arr[s.StartAt.Hour] += s.ActualSec;
        return arr;
    }

    public int Streak()
    {
        var today = DateTime.Now.Date;
        var probe = today;
        if (Day(today).FocusCount == 0) probe = today.AddDays(-1);
        var count = 0;
        for (var i = 0; i < 400; i++)
        {
            var d = probe.AddDays(-i);
            if (Day(d).FocusCount > 0) count++;
            else break;
        }
        return count;
    }

    public TotalsStat Totals()
    {
        var sessions = _store.Data.Sessions;
        return new TotalsStat
        {
            FocusSec = sessions.Where(s => s.Kind == "focus" && s.Status == "completed").Sum(s => s.ActualSec),
            RestSec = sessions.Where(s => s.Kind == "rest" && s.Status == "completed").Sum(s => s.ActualSec),
            Ducks = _store.Data.Ducks.Count(d => d.Alive),
            Escapes = _store.Data.Ducks.Count(d => !d.Alive)
        };
    }
}
