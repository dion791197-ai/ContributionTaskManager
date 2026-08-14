# Renders the widget's true silhouette: every pixel that differs from the desktop
# behind it is painted, everything else is see-through.
#
# This is the only reliable way to judge the shape. On a dark desktop an opaque
# black margin and a transparent one are indistinguishable by eye, and the frame
# artefacts in this project were repeatedly missed because of exactly that.
#
# Output: white = painted by the app, black = desktop showing through.

param(
    [switch]$SkipBuild,
    [string]$OutPath = "$env:TEMP\silhouette.png",
    [int]$Margin = 12
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
public class Sil {
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr p);
 delegate bool EnumProc(IntPtr h, IntPtr p);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
 [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
 public static List<IntPtr> ForPid(uint pid){ var l=new List<IntPtr>();
   EnumWindows((h,p)=>{ uint q; GetWindowThreadProcessId(h,out q); if(q==pid) l.Add(h); return true; },IntPtr.Zero); return l; }
}
"@

$rect = $null; $hwnd = [IntPtr]::Zero
foreach ($h in [Sil]::ForPid([uint32]$proc.Id)) {
    $sb = New-Object System.Text.StringBuilder 256
    [void][Sil]::GetClassName($h, $sb, 256)
    if ($sb.ToString() -like 'WinUIDesktop*') {
        $hwnd = $h
        $rect = New-Object Sil+RECT
        [void][Sil]::GetWindowRect($h, [ref]$rect)
        break
    }
}
if ($null -eq $rect) { throw 'No WinUI window found.' }

$cr = New-Object Sil+RECT; [void][Sil]::GetClientRect($hwnd, [ref]$cr)
$origin = New-Object Sil+POINT; [void][Sil]::ClientToScreen($hwnd, [ref]$origin)

$w = ($rect.R - $rect.L) + $Margin * 2
$h2 = ($rect.B - $rect.T) + $Margin * 2
$x0 = $rect.L - $Margin
$y0 = $rect.T - $Margin

Add-Type -AssemblyName System.Drawing
function Shot($x, $y, $w, $h) {
    $b = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $g.Dispose()
    return $b
}

$with = Shot $x0 $y0 $w $h2
Get-Process GitHubGoal -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
$without = Shot $x0 $y0 $w $h2

$mask = New-Object System.Drawing.Bitmap($w, $h2)
$painted = 0
for ($y = 0; $y -lt $h2; $y++) {
    for ($x = 0; $x -lt $w; $x++) {
        if ($with.GetPixel($x, $y).ToArgb() -ne $without.GetPixel($x, $y).ToArgb()) {
            $mask.SetPixel($x, $y, [System.Drawing.Color]::White)
            $painted++
        } else {
            $mask.SetPixel($x, $y, [System.Drawing.Color]::Black)
        }
    }
}
$mask.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)

Write-Output "window  : $($rect.R-$rect.L)x$($rect.B-$rect.T) at $($rect.L),$($rect.T)"
Write-Output "client  : $($cr.R-$cr.L)x$($cr.B-$cr.T) at screen $($origin.X),$($origin.Y)"
Write-Output "inset   : left $($origin.X - $rect.L)  top $($origin.Y - $rect.T)"
Write-Output "painted : $painted px of $($w*$h2)"
Write-Output "mask    : $OutPath  (white = app, black = see-through)"

$with.Dispose(); $without.Dispose(); $mask.Dispose()
