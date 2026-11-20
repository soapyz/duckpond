using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace DuckPond.UI;

/// <summary>右下角自绘提醒条，不依赖系统 toast 注册。</summary>
public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _life = new() { Interval = TimeSpan.FromSeconds(4) };

    public ToastWindow()
    {
        InitializeComponent();
        MouseLeftButtonUp += (_, _) => { HideAnimated(); };
        _life.Tick += (_, _) => { _life.Stop(); HideAnimated(); };
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }

    public void ShowText(string text)
    {
        Msg.Text = text;
        var wa = WinForms.Screen.PrimaryScreen!.WorkingArea;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        Left = (wa.Right - Width * dpi.DpiScaleX) / dpi.DpiScaleX - 24;
        Top = (wa.Bottom - Height * dpi.DpiScaleY) / dpi.DpiScaleY - 24;
        Opacity = 0;
        Show();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        _life.Stop();
        _life.Start();
    }

    private void HideAnimated()
    {
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => Hide();
        BeginAnimation(OpacityProperty, fade);
    }
}
