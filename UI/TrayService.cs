using DuckPond.Core;
using DuckPond.Sys;
using WinForms = System.Windows.Forms;

namespace DuckPond.UI;

/// <summary>托盘常驻、右键菜单、全局热键。</summary>
public class TrayService : IDisposable
{
    private readonly Runtime _rt;
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _pauseItem;
    private readonly WinForms.ToolStripMenuItem _autoItem;
    private readonly HotkeyManager _hotkeys;

    public TrayService(Runtime rt)
    {
        _rt = rt;
        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "鸭塘 DuckPond",
            Visible = true
        };

        var menu = new WinForms.ContextMenuStrip { ShowImageMargin = false };
        var open = new WinForms.ToolStripMenuItem("打开面板");
        open.Click += (_, _) => rt.ShowMain();
        var focus = new WinForms.ToolStripMenuItem($"开始专注 {rt.Settings.FocusMin} 分钟");
        focus.Click += (_, _) => rt.StartFocus("", rt.Settings.FocusMin);
        var rest = new WinForms.ToolStripMenuItem("现在休息 5 分钟");
        rest.Click += (_, _) => rt.StartRest(300, "tray", true, "休息一下");
        _pauseItem = new WinForms.ToolStripMenuItem("暂停定时提醒") { CheckOnClick = false };
        _pauseItem.Click += (_, _) => rt.SetReminderPaused(!rt.Scheduler.Paused);
        _autoItem = new WinForms.ToolStripMenuItem("开机自启") { CheckOnClick = false };
        _autoItem.Click += (_, _) =>
        {
            rt.Settings.AutoStartApp = !rt.Settings.AutoStartApp;
            AutoStart.Apply(rt.Settings.AutoStartApp);
            rt.Store.Save();
            Refresh();
            rt.PushState(true);
        };
        var quit = new WinForms.ToolStripMenuItem("退出鸭塘");
        quit.Click += (_, _) => System.Windows.Application.Current.Shutdown();

        menu.Items.Add(open);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(focus);
        menu.Items.Add(rest);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_autoItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(quit);
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => rt.ShowMain();

        _hotkeys = new HotkeyManager();
        _hotkeys.Pressed += id =>
        {
            switch (id)
            {
                case 1: rt.StartRest(300, "hotkey", true, "快捷键休息"); break;
                case 2:
                    if (rt.Engine.Phase == Phase.Idle) rt.StartFocus("", rt.Settings.FocusMin);
                    else rt.TogglePause();
                    break;
                case 3: rt.ShowMain(); break;
            }
        };
        Refresh();
    }

    public void Refresh()
    {
        _pauseItem.Checked = _rt.Scheduler.Paused;
        _pauseItem.Text = _rt.Scheduler.Paused ? "恢复定时提醒" : "暂停定时提醒";
        _autoItem.Checked = _rt.Settings.AutoStartApp;
        UpdateTip();
    }

    public void UpdateTip()
    {
        try
        {
            var today = _rt.Stats.Day(DateTime.Now);
            var h = today.FocusSec / 3600;
            var m = today.FocusSec % 3600 / 60;
            _icon.Text = $"鸭塘 · 今日 {(h > 0 ? h + "h" : "")}{m}m · {_rt.Stats.Totals().Ducks} 只鸭";
        }
        catch { }
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/duck.ico"))?.Stream;
            if (stream != null) return new System.Drawing.Icon(stream);
        }
        catch { }
        return System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _hotkeys.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
    }
}
