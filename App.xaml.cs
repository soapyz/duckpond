using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DuckPond.Core;

namespace DuckPond;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private Thread? _showThread;

    public Store Store { get; private set; } = null!;
    public Runtime Runtime { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Runtime.Log(args.Exception);
            args.Handled = true;
        };

        var created = false;
        _mutex = new Mutex(true, @"Local\DuckPond.Instance", out created);
        if (!created)
        {
            try { EventWaitHandle.OpenExisting(@"Local\DuckPond.ShowWindow").Set(); } catch { }
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\DuckPond.ShowWindow");
        _showThread = new Thread(() =>
        {
            while (true)
            {
                _showEvent.WaitOne();
                Dispatcher.BeginInvoke(() => Runtime?.ShowMain());
            }
        }) { IsBackground = true };
        _showThread.Start();

        Store = new Store();
        Runtime = new Runtime(Store);
        Runtime.Start();
        Store.Data.LastOpenAt = DateTime.Now;
        Store.Save();

        var silent = e.Args.Any(a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase));
        if (!silent) Runtime.ShowMain();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Runtime?.Dispose(); } catch { }
        try { _showEvent?.Set(); _showEvent?.Dispose(); } catch { }
        base.OnExit(e);
    }
}
