# Rebuilds, relaunches, and reports the widget's real window geometry plus a
# magnified view of its four corners.
#
# The corner sheet is the point: at 1:1 a dark frame around a dark card is
# invisible, and every "the corners look fine" judgement made from a normal
# screenshot in this project turned out to be wrong.

param(
    [switch]$SkipBuild,
    [string]$OutPath = "$env:TEMP\corners.png"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'src\GitHubGoal\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\GitHubGoal.exe'

Get-Process GitHubGoal -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

if (-not $SkipBuild) {
    & "$env:USERPROFILE\.dotnet\dotnet.exe" build (Join-Path $root 'ContributionTaskManager.sln') -c Release -v q --nologo |
        Select-String 'error|Ошибок' | Select-Object -First 5
}

$proc = Start-Process $exe -PassThru
Start-Sleep -Seconds 6

Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;using System.Collections.Generic;
public class WidgetProbe {
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr p);
 delegate bool EnumProc(IntPtr h, IntPtr p);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h,int i);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
 public static List<IntPtr> ForPid(uint pid){ var l=new List<IntPtr>();
   EnumWindows((h,p)=>{ uint q; GetWindowThreadProcessId(h,out q); if(q==pid) l.Add(h); return true; },IntPtr.Zero); return l; }
}
"@

$hwnd = [IntPtr]::Zero
foreach ($h in [WidgetProbe]::ForPid([uint32]$proc.Id)) {
    $sb = New-Object System.Text.StringBuilder 256
    [void][WidgetProbe]::GetClassName($h, $sb, 256)
    if ($sb.ToString() -like 'WinUIDesktop*') { $hwnd = $h; break }
}

if ($hwnd -eq [IntPtr]::Zero) { throw 'No WinUI window found - the app probably threw before Activate().' }

$wr = New-Object WidgetProbe+RECT; [void][WidgetProbe]::GetWindowRect($hwnd, [ref]$wr)
$cr = New-Object WidgetProbe+RECT; [void][WidgetProbe]::GetClientRect($hwnd, [ref]$cr)
$style = [WidgetProbe]::GetWindowLong($hwnd, -16)
$ex = [WidgetProbe]::GetWindowLong($hwnd, -20)

$ww = $wr.R - $wr.L; $wh = $wr.B - $wr.T
$cw = $cr.R - $cr.L; $ch = $cr.B - $cr.T

Write-Output "visible      : $([WidgetProbe]::IsWindowVisible($hwnd))"
Write-Output "always-on-top: $((($ex -band 0x8) -ne 0))"
Write-Output "window rect  : ${ww}x${wh}"
Write-Output "client rect  : ${cw}x${ch}"
if ($ww -eq $cw -and $wh -eq $ch) {
    Write-Output "frame        : none - content fills the window"
} else {
    Write-Output "frame        : $((($ww-$cw)/2))px per side  <-- chrome still present"
}
Write-Output "WS_THICKFRAME: $((($style -band 0x00040000) -ne 0))"
Write-Output "WS_CAPTION   : $((($style -band 0x00C00000) -eq 0x00C00000))"

# --- magnified corner sheet ------------------------------------------------
Add-Type -AssemblyName System.Drawing
$n = 26; $z = 11; $gap = 20
$sheet = New-Object System.Drawing.Bitmap(($n*$z*2+$gap*3), ($n*$z*2+$gap*3))
$sg = [System.Drawing.Graphics]::FromImage($sheet)
$sg.Clear([System.Drawing.Color]::FromArgb(255,255,0,255))
$sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$sg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half

foreach ($c in @(
    @{x=$wr.L;       y=$wr.T;       dx=$gap;              dy=$gap},
    @{x=($wr.R-$n);  y=$wr.T;       dx=($gap*2+$n*$z);    dy=$gap},
    @{x=$wr.L;       y=($wr.B-$n);  dx=$gap;              dy=($gap*2+$n*$z)},
    @{x=($wr.R-$n);  y=($wr.B-$n);  dx=($gap*2+$n*$z);    dy=($gap*2+$n*$z)})) {
    $b = New-Object System.Drawing.Bitmap($n, $n)
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.CopyFromScreen([int]$c.x, [int]$c.y, 0, 0, (New-Object System.Drawing.Size($n,$n)))
    $g.Dispose()
    $sg.DrawImage($b, [int]$c.dx, [int]$c.dy, ($n*$z), ($n*$z))
    $b.Dispose()
}
$sg.Dispose()
$sheet.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()

Write-Output "corner sheet : $OutPath"
