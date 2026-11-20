namespace DuckPond.Core;

public static class Ducks
{
    public static readonly Dictionary<string, string> SpeciesNames = new()
    {
        ["classic"] = "小黄鸭",
        ["strawhat"] = "草帽鸭",
        ["swimring"] = "游泳圈鸭",
        ["sunglasses"] = "墨镜鸭",
        ["golden"] = "金鸭",
        ["marathon"] = "马拉松鸭",
        ["earlybird"] = "早起鸭",
        ["nightowl"] = "夜猫鸭",
        ["ghost"] = "逃走的鸭"
    };

    public static string SpeciesFor(int streak, DateTime born, int plannedSec)
    {
        if (plannedSec >= 90 * 60) return "marathon";
        if (streak >= 30) return "golden";
        if (streak >= 14) return "sunglasses";
        if (streak >= 7) return "swimring";
        if (streak >= 3) return "strawhat";
        if (born.Hour < 7) return "earlybird";
        if (born.Hour >= 23) return "nightowl";
        return "classic";
    }

    public static string StageOf(int focusSec) =>
        focusSec < 60 * 60 ? "duckling" : focusSec < 180 * 60 ? "young" : "adult";

    public static string StageName(string stage) => stage switch
    {
        "duckling" => "鸭苗",
        "young" => "小鸭",
        _ => "成年鸭"
    };

    public static Duck Hatch(Store store, FocusSession session, int streak)
    {
        var duck = new Duck
        {
            BornAt = DateTime.Now,
            FocusSec = session.ActualSec,
            SessionId = session.Id,
            Species = SpeciesFor(streak, DateTime.Now, session.PlannedSec)
        };
        store.Data.Ducks.Add(duck);
        return duck;
    }

    public static Duck Escape(Store store, FocusSession session, string reason)
    {
        var ghost = new Duck
        {
            BornAt = DateTime.Now,
            Alive = false,
            Species = "ghost",
            FocusSec = 0,
            SessionId = session.Id,
            Reason = string.IsNullOrWhiteSpace(reason) ? "中途离开" : reason
        };
        store.Data.Ducks.Add(ghost);
        return ghost;
    }

    /// <summary>0 小水洼 / 1 池塘 / 2 芦苇塘 / 3 湖</summary>
    public static int PondLevel(int aliveCount) =>
        aliveCount < 10 ? 0 : aliveCount < 30 ? 1 : aliveCount < 80 ? 2 : 3;

    public static string PondName(int level) => level switch
    {
        0 => "小水洼",
        1 => "池塘",
        2 => "芦苇塘",
        _ => "鸭湖"
    };

    public static int PondNextAt(int level) => level switch
    {
        0 => 10,
        1 => 30,
        2 => 80,
        _ => 0
    };
}

public static class Achievements
{
    public class Def
    {
        public string Key { get; init; } = "";
        public string Name { get; init; } = "";
        public string Desc { get; init; } = "";
        public string Icon { get; init; } = "🏅";
    }

    public static readonly List<Def> Catalog = new()
    {
        new() { Key = "first_duck", Name = "第一只鸭", Desc = "完成第一个专注", Icon = "🐣" },
        new() { Key = "ducks_10", Name = "鸭群初成", Desc = "鸭塘养到 10 只鸭", Icon = "🦆" },
        new() { Key = "ducks_30", Name = "鸭塘热闹", Desc = "鸭塘养到 30 只鸭", Icon = "🌊" },
        new() { Key = "focus_10h", Name = "十小时", Desc = "累计专注 10 小时", Icon = "⏳" },
        new() { Key = "focus_50h", Name = "五十小时", Desc = "累计专注 50 小时", Icon = "🏔️" },
        new() { Key = "streak_3", Name = "三日不断", Desc = "连续 3 天有专注", Icon = "🔥" },
        new() { Key = "streak_7", Name = "一周坚持", Desc = "连续 7 天有专注", Icon = "🎯" },
        new() { Key = "streak_14", Name = "半月不辍", Desc = "连续 14 天有专注", Icon = "💎" },
        new() { Key = "streak_30", Name = "满月", Desc = "连续 30 天有专注", Icon = "👑" },
        new() { Key = "clean_week", Name = "无逃鸭周", Desc = "一周内 ≥5 次专注且零逃鸭", Icon = "🛡️" }
    };

    public static List<Def> Evaluate(Store store, StatsEngine stats)
    {
        var unlocked = store.Data.Achievements.Select(a => a.Key).ToHashSet();
        var totals = stats.Totals();
        var streak = stats.Streak();
        var week = stats.Week();

        bool Ok(Def d) => d.Key switch
        {
            "first_duck" => totals.Ducks >= 1,
            "ducks_10" => totals.Ducks >= 10,
            "ducks_30" => totals.Ducks >= 30,
            "focus_10h" => totals.FocusSec >= 10 * 3600,
            "focus_50h" => totals.FocusSec >= 50 * 3600,
            "streak_3" => streak >= 3,
            "streak_7" => streak >= 7,
            "streak_14" => streak >= 14,
            "streak_30" => streak >= 30,
            "clean_week" => week.Sum(w => w.FocusCount) >= 5 && week.Sum(w => w.Escapes) == 0,
            _ => false
        };

        var newly = new List<Def>();
        foreach (var d in Catalog.Where(d => !unlocked.Contains(d.Key)))
        {
            if (!Ok(d)) continue;
            store.Data.Achievements.Add(new Achievement { Key = d.Key, UnlockedAt = DateTime.Now });
            newly.Add(d);
        }
        return newly;
    }
}
