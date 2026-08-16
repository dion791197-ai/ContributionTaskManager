# Captures the widget window (assumed already running) to a PNG.
param([string]$OutPath = "$env:TEMP\widget-capture.png")

Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;using System.Collections.Generic;
public class CapProbe {
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

$proc = Get-Process GitHubGoal -ErrorAction Stop
$rect = $null
foreach ($h in [CapProbe]::ForPid([uint32]$proc.Id)) {
    $sb = New-Object System.Text.StringBuilder 256
    [void][CapProbe]::GetClassName($h, $sb, 256)
    if ($sb.ToString() -like 'WinUIDesktop*') {
        $rect = New-Object CapProbe+RECT
        [void][CapProbe]::GetWindowRect($h, [ref]$rect)
        break
    }
}
if ($null -eq $rect) { throw 'No WinUI window found.' }

Add-Type -AssemblyName System.Drawing
$w = $rect.R - $rect.L; $h2 = $rect.B - $rect.T
$b = New-Object System.Drawing.Bitmap($w, $h2)
$g = [System.Drawing.Graphics]::FromImage($b)
$g.CopyFromScreen($rect.L, $rect.T, 0, 0, (New-Object System.Drawing.Size($w, $h2)))
$g.Dispose()
$b.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$b.Dispose()
Write-Output "saved $OutPath ($w x $h2)"
