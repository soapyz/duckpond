param(
  [Parameter(Mandatory = $true)][string]$Action,
  [int]$X = 0,
  [int]$Y = 0,
  [string]$Out = "",
  [string]$Value = ""
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class UiW32 {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, int d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr extra);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

$proc = Get-Process DuckPond -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { Write-Output "NO_WINDOW"; exit 1 }
$hwnd = $proc.MainWindowHandle

function EnsureFront {
  [UiW32]::ShowWindow($hwnd, 9) | Out-Null
  [UiW32]::SetWindowPos($hwnd, [IntPtr]::new(-1), 180, 60, 1100, 790, 0x0040) | Out-Null
  [UiW32]::SetForegroundWindow($hwnd) | Out-Null
  Start-Sleep -Milliseconds 350
}

function CaptureWindow([string]$path) {
  $r = New-Object UiW32+RECT
  [UiW32]::GetWindowRect($hwnd, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
  $bmp = [System.Drawing.Bitmap]::new($w, $h)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
  $g.Dispose()
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  Write-Output "shot=$path ${w}x${h}"
}

function CaptureFull([string]$path) {
  $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
  Add-Type -AssemblyName System.Windows.Forms
  $bmp = [System.Drawing.Bitmap]::new($b.Width, $b.Height)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
  $g.Dispose()
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  Write-Output "full=$path"
}

switch ($Action) {
  "shot" {
    EnsureFront
    CaptureWindow $Out
  }
  "full" {
    CaptureFull $Out
  }
  "click" {
    EnsureFront
    [UiW32]::SetCursorPos($X, $Y) | Out-Null
    Start-Sleep -Milliseconds 120
    [UiW32]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    [UiW32]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 700
    if ($Out) { CaptureWindow $Out }
    else { Write-Output "clicked $X,$Y" }
  }
  "wheel" {
    [UiW32]::SetCursorPos($X, $Y) | Out-Null
    Start-Sleep -Milliseconds 150
    [UiW32]::mouse_event(0x0800, 0, 0, [int]$Value, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 700
    Write-Output "wheel $Value"
  }
  "rawclick" {
    [UiW32]::SetCursorPos($X, $Y) | Out-Null
    Start-Sleep -Milliseconds 120
    [UiW32]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    [UiW32]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 600
    Write-Output "rawclick $X,$Y"
  }
  "key" {
    EnsureFront
    [UiW32]::keybd_event([byte][char]$Value, 0, 0, [IntPtr]::Zero)
    [UiW32]::keybd_event([byte][char]$Value, 0, 2, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 400
    Write-Output "key=$Value"
  }
  "sendkeys" {
    EnsureFront
    [System.Windows.Forms.SendKeys]::SendWait($Value)
    Start-Sleep -Milliseconds 500
    if ($Out) { CaptureWindow $Out }
    else { Write-Output "sendkeys=$Value" }
  }
  default { Write-Output "unknown action" }
}
