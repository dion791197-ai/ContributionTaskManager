# Reports the first few pixel rows/columns inwards from each edge of the widget,
# and flags any that look like system chrome rather than the card.
#
# The widget is a dark card on a dark desktop, so a stray bright border line is
# nearly invisible in a screenshot but obvious in the numbers. A 1px #E3E3E3 line
# along the top survived several rounds of "looks fine to me".

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
public class EdgeChk {
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

$r = $null
foreach ($h in [EdgeChk]::ForPid([uint32]$proc.Id)) {
    $sb = New-Object System.Text.StringBuilder 256
    [void][EdgeChk]::GetClassName($h, $sb, 256)
    if ($sb.ToString() -like 'WinUIDesktop*') {
        $r = New-Object EdgeChk+RECT; [void][EdgeChk]::GetWindowRect($h, [ref]$r); break
    }
}
if ($null -eq $r) { throw 'No WinUI window found.' }

$w = $r.R - $r.L; $h2 = $r.B - $r.T
Add-Type -AssemblyName System.Drawing
$shot = New-Object System.Drawing.Bitmap($w, $h2)
$g = [System.Drawing.Graphics]::FromImage($shot)
$g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size($w, $h2)))
$g.Dispose()

Write-Output "widget ${w}x${h2} at $($r.L),$($r.T)"
Write-Output ''

# Sample away from the corners so the rounding does not confuse the reading.
$midX = [int]($w / 2); $midY = [int]($h2 / 2)
$edges = @(
    @{ Name = 'top';    Points = 0..4 | ForEach-Object { ,@($midX, $_) } },
    @{ Name = 'bottom'; Points = 0..4 | ForEach-Object { ,@($midX, ($h2 - 1 - $_)) } },
    @{ Name = 'left';   Points = 0..4 | ForEach-Object { ,@($_, $midY) } },
    @{ Name = 'right';  Points = 0..4 | ForEach-Object { ,@(($w - 1 - $_), $midY) } }
)

$suspect = 0
foreach ($e in $edges) {
    $line = "$($e.Name.PadRight(7)): "
    $i = 0
    foreach ($pt in $e.Points) {
        $p = $shot.GetPixel($pt[0], $pt[1])
        $lum = (0.299 * $p.R) + (0.587 * $p.G) + (0.114 * $p.B)
        $mark = ''
        # The card sits well below this; anything brighter at an edge is chrome.
        if ($i -le 1 -and $lum -gt 140) { $mark = '<!'; $suspect++ }
        $line += ('#{0:X2}{1:X2}{2:X2}{3} ' -f $p.R, $p.G, $p.B, $mark)
        $i++
    }
    Write-Output $line
}

$shot.Dispose()
Write-Output ''
if ($suspect -gt 0) {
    Write-Output "$suspect suspicious edge pixel(s) marked <! - likely a system border line"
} else {
    Write-Output 'no bright edge lines detected'
}
