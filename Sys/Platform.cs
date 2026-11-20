using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace DuckPond.Sys;

public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "DuckPond";

    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return k?.GetValue(Name) is string v && v.Contains("DuckPond");
    }

    public static void Apply(bool enable)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k == null) return;
            if (enable)
            {
                var exe = Environment.ProcessPath ?? "";
                if (!string.IsNullOrEmpty(exe)) k.SetValue(Name, $"\"{exe}\" --autostart");
            }
            else if (k.GetValue(Name) != null)
            {
                k.DeleteValue(Name, false);
            }
        }
        catch { }
    }
}

/// <summary>检测前台是否全屏应用（游戏 / 演示 / 全屏视频），用于避免硬打断。</summary>
public static class Fullscreen
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder sb, int max);

    public struct RECT { public int Left, Top, Right, Bottom; }

    public static bool IsForegroundFullscreen()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            var cls = new System.Text.StringBuilder(256);
            GetClassName(hwnd, cls, 256);
            var name = cls.ToString();
            if (name is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;

            if (!GetWindowRect(hwnd, out var r)) return false;
            var screen = WinForms.Screen.FromHandle(hwnd).Bounds;
            var coversW = (r.Right - r.Left) >= screen.Width - 2;
            var coversH = (r.Bottom - r.Top) >= screen.Height - 2;
            return coversW && coversH;
        }
        catch { return false; }
    }
}

/// <summary>全局热键：Ctrl+Alt+R 立即休息 / Ctrl+Alt+P 开始或暂停 / Ctrl+Alt+D 打开面板。</summary>
public class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource? _source;
    public event Action<int>? Pressed;

    public HotkeyManager()
    {
        var p = new HwndSourceParameters("DuckPondHotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
        RegisterHotKey(_source.Handle, 1, MOD_CONTROL | MOD_ALT, 0x52); // R
        RegisterHotKey(_source.Handle, 2, MOD_CONTROL | MOD_ALT, 0x50); // P
        RegisterHotKey(_source.Handle, 3, MOD_CONTROL | MOD_ALT, 0x44); // D
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY) { Pressed?.Invoke(wParam.ToInt32()); handled = true; }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        try
        {
            if (_source == null) return;
            for (var i = 1; i <= 3; i++) UnregisterHotKey(_source.Handle, i);
            _source.RemoveHook(WndProc);
            _source.Dispose();
        }
        catch { }
    }
}
