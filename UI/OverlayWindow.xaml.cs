using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace DuckPond.UI;

/// <summary>休息遮罩：模糊桌面 + 倒计时，点击任意位置退出。</summary>
public partial class OverlayWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_SHOWWINDOW = 0x0040;

    private readonly DispatcherTimer _count = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _top = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private readonly Stopwatch _sw = new();
    private int _seconds;
    private bool _closing;

    public event Action? ExitRequested;

    public OverlayWindow()
    {
        InitializeComponent();
        Root.MouseLeftButtonUp += (_, _) => ExitRequested?.Invoke();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) ExitRequested?.Invoke(); };
        _count.Tick += (_, _) => UpdateCount();
        _top.Tick += (_, _) => KeepTop();
        SourceInitialized += (_, _) => FitToScreen();
    }

    public void ShowFor(string label, int seconds)
    {
        _closing = false;
        _seconds = Math.Max(20, seconds);
        TitleText.Text = label;
        SubText.Text = "站起来走走，看看远处";
        Backdrop.Source = ScreenBlur.Capture(0.55);
        _sw.Restart();

        Opacity = 0;
        Show();
        FitToScreen();
        KeepTop();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
        UpdateCount();
        _count.Start();
        _top.Start();
        Activate();
    }

    private void FitToScreen()
    {
        var vs = WinForms.SystemInformation.VirtualScreen;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, HWND_TOPMOST, vs.Left, vs.Top, vs.Width, vs.Height, SWP_SHOWWINDOW);
    }

    private void KeepTop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !IsVisible) return;
        var vs = WinForms.SystemInformation.VirtualScreen;
        SetWindowPos(hwnd, HWND_TOPMOST, vs.Left, vs.Top, vs.Width, vs.Height, SWP_SHOWWINDOW);
    }

    private void UpdateCount()
    {
        var left = Math.Max(0, _seconds - (int)_sw.Elapsed.TotalSeconds);
        CountText.Text = $"{left / 60:00}:{left % 60:00}";
        if (left <= 0) HideAnimated();
    }

    public void HideAnimated()
    {
        if (_closing) return;
        _closing = true;
        _count.Stop();
        _top.Stop();
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => { Hide(); Opacity = 0; };
        BeginAnimation(OpacityProperty, fade);
    }
}
