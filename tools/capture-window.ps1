# Captures a top-level window to a PNG using PrintWindow with PW_RENDERFULLCONTENT,
# which is the flag that also captures DirectComposition / WinUI content.
#
# Note: a system acrylic/Mica backdrop samples the desktop behind the window and is
# NOT reproduced by PrintWindow, so the captured background will look flat. Layout,
# typography and the card's own layers are still faithful.

param(
    [string]$WindowTitle = 'GitHub Goal',
    [string]$WindowClass = 'WinUIDesktopWin32WindowClass',
    [string]$OutPath = "$env:TEMP\widget.png",
    [string]$BackdropColor = '#2A2D34'
)

Add-Type -AssemblyName System.Drawing

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class Win32Capture
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

# Match on class + title: FindWindow with a null class does not reliably match
# WinUI's desktop window.
$hwnd = [Win32Capture]::FindWindow($WindowClass, $WindowTitle)
if ($hwnd -eq [IntPtr]::Zero) {
    $hwnd = [Win32Capture]::FindWindow($WindowClass, $null)
}
if ($hwnd -eq [IntPtr]::Zero) {
    Write-Error "Window '$WindowTitle' (class '$WindowClass') not found."
    exit 1
}

[Win32Capture]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 400

$rect = New-Object Win32Capture+RECT
[Win32Capture]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left
$h = $rect.Bottom - $rect.Top
Write-Output "window ${w}x${h} at ($($rect.Left),$($rect.Top))"

$bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
# 2 = PW_RENDERFULLCONTENT
$ok = [Win32Capture]::PrintWindow($hwnd, $hdc, 2)
$g.ReleaseHdc($hdc)
$g.Dispose()
Write-Output "PrintWindow ok=$ok"

# Composite over a flat colour so the glass layers are judgeable.
$out = New-Object System.Drawing.Bitmap($w, $h)
$og = [System.Drawing.Graphics]::FromImage($out)
$og.Clear([System.Drawing.ColorTranslator]::FromHtml($BackdropColor))
$og.DrawImage($bmp, 0, 0)
$og.Dispose()

$out.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$out.Dispose(); $bmp.Dispose()

Write-Output $OutPath
