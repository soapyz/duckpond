using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DuckPond.Core;
using Microsoft.Web.WebView2.Core;

namespace DuckPond.UI;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private readonly Runtime _rt;
    private bool _ready;

    public MainWindow(Runtime rt)
    {
        InitializeComponent();
        _rt = rt;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var dark = 1;
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
        }
        catch { }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (!Directory.Exists(root)) root = Path.Combine(Environment.CurrentDirectory, "wwwroot");
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(_rt.DataDir, "WebView2"));
            await Web.EnsureCoreWebView2Async(env);

            var core = Web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.SetVirtualHostNameToFolderMapping("duckpond.local", root, CoreWebView2HostResourceAccessKind.Allow);
            core.WebMessageReceived += (_, args) =>
            {
                var json = args.WebMessageAsJson;
                Dispatcher.Invoke(() => _rt.OnWebMessage(json));
            };
            core.NavigationCompleted += (_, _) =>
            {
                _ready = true;
                Loading.Visibility = Visibility.Collapsed;
                _rt.PushState(true);
            };
            core.Navigate("https://duckpond.local/index.html");
        }
        catch (Exception ex)
        {
            Runtime.Log(ex);
            Loading.Text = "界面加载失败，详情见 %AppData%\\DuckPond\\error.log";
        }
    }

    public void Send(string json)
    {
        if (!_ready || Web.CoreWebView2 == null) return;
        try { Web.CoreWebView2.PostWebMessageAsJson(json); }
        catch (Exception ex) { Runtime.Log(ex); }
    }
}
