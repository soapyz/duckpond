using System.Diagnostics;
using System.Windows.Threading;

namespace DuckPond.Core;

public enum Phase { Idle, Focus, Rest }
public enum FinishStatus { Completed, Aborted, Skipped }

public class SessionResult
{
    public Phase Phase { get; set; }
    public string Task { get; set; } = "";
    public string Source { get; set; } = "pomodoro";
    public int PlannedSec { get; set; }
    public int ElapsedSec { get; set; }
    public FinishStatus Status { get; set; }
    public string Reason { get; set; } = "";
    public DateTime StartedAt { get; set; }
}

/// <summary>番茄状态机：单调时钟计时，暂停不丢时间，到点自动结束并抛出结果。</summary>
public class PomodoroEngine : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly Stopwatch _sw = new();
    private int _accSec;

    public Phase Phase { get; private set; } = Phase.Idle;
    public bool Running { get; private set; }
    public int PlannedSec { get; private set; }
    public string Task { get; private set; } = "";
    public string Source { get; private set; } = "pomodoro";
    public DateTime StartedAt { get; private set; } = DateTime.Now;

    public event Action? Tick;
    public event Action<SessionResult>? Finished;

    public int ElapsedSec => _accSec + (Running ? (int)_sw.Elapsed.TotalSeconds : 0);
    public int RemainingSec => Math.Max(0, PlannedSec - ElapsedSec);
    public double Progress => PlannedSec <= 0 ? 0 : Math.Clamp((double)ElapsedSec / PlannedSec, 0, 1);

    public PomodoroEngine()
    {
        _timer.Tick += (_, _) =>
        {
            if (Phase != Phase.Idle)
            {
                if (Running && RemainingSec <= 0) { Finish(FinishStatus.Completed, ""); return; }
                Tick?.Invoke();
            }
        };
    }

    public void Start() => _timer.Start();

    public void StartFocus(int minutes, string task)
    {
        Phase = Phase.Focus;
        PlannedSec = Math.Max(60, minutes * 60);
        Task = task ?? "";
        Source = "pomodoro";
        StartedAt = DateTime.Now;
        _accSec = 0;
        _sw.Restart();
        Running = true;
        Tick?.Invoke();
    }

    public void StartRest(int seconds, string source)
    {
        Phase = Phase.Rest;
        PlannedSec = Math.Max(20, seconds);
        Task = "";
        Source = source;
        StartedAt = DateTime.Now;
        _accSec = 0;
        _sw.Restart();
        Running = true;
        Tick?.Invoke();
    }

    public void Pause()
    {
        if (Phase == Phase.Idle || !Running) return;
        _accSec += (int)_sw.Elapsed.TotalSeconds;
        _sw.Stop();
        Running = false;
        Tick?.Invoke();
    }

    public void Resume()
    {
        if (Phase == Phase.Idle || Running) return;
        _sw.Restart();
        Running = true;
        Tick?.Invoke();
    }

    public void Toggle()
    {
        if (Running) Pause(); else Resume();
    }

    public void Abort(string reason)
    {
        if (Phase == Phase.Idle) return;
        Finish(FinishStatus.Aborted, reason);
    }

    public void Skip()
    {
        if (Phase == Phase.Idle) return;
        Finish(FinishStatus.Skipped, "skip");
    }

    public void Reset()
    {
        Phase = Phase.Idle;
        Running = false;
        _sw.Reset();
        _accSec = 0;
        PlannedSec = 0;
        Tick?.Invoke();
    }

    private void Finish(FinishStatus status, string reason)
    {
        var result = new SessionResult
        {
            Phase = Phase,
            Task = Task,
            Source = Source,
            PlannedSec = PlannedSec,
            ElapsedSec = Math.Min(ElapsedSec, PlannedSec),
            Status = status,
            Reason = reason,
            StartedAt = StartedAt
        };
        Phase = Phase.Idle;
        Running = false;
        _sw.Reset();
        _accSec = 0;
        Finished?.Invoke(result);
        Tick?.Invoke();
    }

    public void Dispose() => _timer.Stop();
}
