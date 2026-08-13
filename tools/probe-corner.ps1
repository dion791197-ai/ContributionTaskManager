# Answers one question: is the widget's corner actually see-through?
#
# A magnified screenshot cannot tell you this — the app is dark and so is the
# desktop behind it, so an opaque black corner and a transparent one look the
# same. This samples the same screen pixels with the widget up and with it
# closed; if they match, that pixel is genuinely not being painted.

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
public class CornerProbe {
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr p);
 delegate bool EnumProc(IntPtr h, IntPtr p);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
 public static List<IntPtr> ForPid(uint pid){ var l=new List<IntPtr>();
   EnumWindows((h,p)=>{ uint q; GetWindowThreadProcessId(h,out q); if(q==pid) l.Add(h); return true; },IntPtr.Zero); return l; }
}
"@

$rect = $null
foreach ($h in [CornerProbe]::ForPid([uint32]$proc.Id)) {
    $sb = New-Object System.Text.StringBuilder 256
    [void][CornerProbe]::GetClassName($h, $sb, 256)
    if ($sb.ToString() -like 'WinUIDesktop*') {
        $rect = New-Object CornerProbe+RECT
        [void][CornerProbe]::GetWindowRect($h, [ref]$rect)
        break
    }
}
if ($null -eq $rect) { throw 'No WinUI window found.' }

Add-Type -AssemblyName System.Drawing
function Grab($x, $y, $n) {
    $b = New-Object System.Drawing.Bitmap($n, $n)
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size($n, $n)))
    $g.Dispose()
    return $b
}

$n = 20
$with = Grab $rect.L $rect.T $n
Get-Process GitHubGoal -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
$without = Grab $rect.L $rect.T $n

Write-Output "widget at $($rect.L),$($rect.T)"
Write-Output ''
Write-Output 'offset   widget    desktop   see-through'
foreach ($pt in @(@(0,0), @(1,1), @(2,2), @(4,4), @(8,8), @(16,16))) {
    $a = $with.GetPixel($pt[0], $pt[1])
    $b = $without.GetPixel($pt[0], $pt[1])
    '({0,2},{1,2})  #{2:X2}{3:X2}{4:X2}   #{5:X2}{6:X2}{7:X2}   {8}' -f `
        $pt[0], $pt[1], $a.R, $a.G, $a.B, $b.R, $b.G, $b.B, ($a.ToArgb() -eq $b.ToArgb())
}

$with.Dispose()
$without.Dispose()
