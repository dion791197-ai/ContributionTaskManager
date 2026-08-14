# Samples a cross-section through the widget's left edge and top edge, comparing
# each pixel with the same screen position when the widget is closed.
#
# "same as desktop" means that pixel is genuinely not painted. Anything else is
# chrome, however subtle - which is the thing that keeps looking like a frame.

param([switch]$SkipBuild)

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
public class EdgeProbe {
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr p);
 delegate bool EnumProc(IntPtr h, IntPtr p);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
 public static List<IntPtr> ForPid(uint pid){ var l=new List<IntPtr>();
   EnumWindows((h,p)=>{ uint q; GetWindowThreadProcessId(h,out q); if(q==pid) l.Add(h); return true; },IntPtr.Zero); return l; }
}
"@

$rect = $null
foreach ($h in [EdgeProbe]::ForPid([uint32]$proc.Id)) {
    $sb = New-Object System.Text.StringBuilder 256
    [void][EdgeProbe]::GetClassName($h, $sb, 256)
    if ($sb.ToString() -like 'WinUIDesktop*') {
        $rect = New-Object EdgeProbe+RECT
        [void][EdgeProbe]::GetWindowRect($h, [ref]$rect)
        break
    }
}
if ($null -eq $rect) { throw 'No WinUI window found.' }

$w = $rect.R - $rect.L
$h2 = $rect.B - $rect.T

Add-Type -AssemblyName System.Drawing
function Strip($x, $y, $len, $horizontal) {
    $bw = if ($horizontal) { $len } else { 1 }
    $bh = if ($horizontal) { 1 } else { $len }
    $b = New-Object System.Drawing.Bitmap($bw, $bh)
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size($bw, $bh)))
    $g.Dispose()
    return $b
}

$len = 16
$midY = $rect.T + [int]($h2 / 2)
$midX = $rect.L + [int]($w / 2)

$leftWith = Strip $rect.L $midY $len $true
$topWith  = Strip $midX $rect.T $len $false

Get-Process GitHubGoal -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

$leftWithout = Strip $rect.L $midY $len $true
$topWithout  = Strip $midX $rect.T $len $false

Write-Output "widget $($w)x$($h2) at $($rect.L),$($rect.T)"
Write-Output ''
Write-Output 'LEFT EDGE (inwards from window edge, through the vertical middle)'
Write-Output 'px   widget    desktop   see-through'
for ($i = 0; $i -lt $len; $i++) {
    $a = $leftWith.GetPixel($i, 0); $b = $leftWithout.GetPixel($i, 0)
    '{0,2}   #{1:X2}{2:X2}{3:X2}   #{4:X2}{5:X2}{6:X2}   {7}' -f $i, $a.R,$a.G,$a.B, $b.R,$b.G,$b.B, ($a.ToArgb() -eq $b.ToArgb())
}
Write-Output ''
Write-Output 'TOP EDGE (downwards from window edge, through the horizontal middle)'
Write-Output 'px   widget    desktop   see-through'
for ($i = 0; $i -lt $len; $i++) {
    $a = $topWith.GetPixel(0, $i); $b = $topWithout.GetPixel(0, $i)
    '{0,2}   #{1:X2}{2:X2}{3:X2}   #{4:X2}{5:X2}{6:X2}   {7}' -f $i, $a.R,$a.G,$a.B, $b.R,$b.G,$b.B, ($a.ToArgb() -eq $b.ToArgb())
}

$leftWith.Dispose(); $topWith.Dispose(); $leftWithout.Dispose(); $topWithout.Dispose()
