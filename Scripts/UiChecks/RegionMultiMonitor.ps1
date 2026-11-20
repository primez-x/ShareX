# Runtime check: region selection on a desktop with two or more monitors.
# Each monitor gets its own selection window, the cursor moves freely between monitors, and the window under the cursor takes the keyboard.
# Starts region capture with a test hotkey (Ctrl+Alt+Shift+F9). Checks a selection on a monitor that is not the start monitor, Esc from that monitor, and a drag over a monitor edge.
# The captures contain screen content, so the script deletes the captures afterwards.
# Report: %LOCALAPPDATA%\Temp\sharex-e2e\ui\RegionMultiMonitor.txt, screenshots RegionMultiMonitor-<monitor>.png
$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SxInput {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool GetClipCursor(out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    public static string Foreground() { IntPtr h = GetForegroundWindow(); uint pid; GetWindowThreadProcessId(h, out pid); return h + " pid " + pid + " root " + GetAncestor(h, 2); }
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    public static void MoveTo(int x, int y) { SetCursorPos(x, y); mouse_event(1, 0, 0, 0, UIntPtr.Zero); }
    public static void Drag(int x1, int y1, int x2, int y2) {
        MoveTo(x1, y1); System.Threading.Thread.Sleep(200);
        mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        for (int i = 1; i <= 30; i++) { MoveTo(x1 + (x2 - x1) * i / 30, y1 + (y2 - y1) * i / 30); System.Threading.Thread.Sleep(15); }
        System.Threading.Thread.Sleep(150);
        mouse_event(4, 0, 0, 0, UIntPtr.Zero);
    }
    // Ctrl+Alt+Shift+F9: a hotkey that no other app uses.
    public static void Hotkey() {
        byte[] keys = { 0x11, 0x12, 0x10, 0x78 };
        foreach (byte k in keys) { keybd_event(k, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(30); }
        Array.Reverse(keys);
        foreach (byte k in keys) { keybd_event(k, 0, 2, UIntPtr.Zero); System.Threading.Thread.Sleep(30); }
    }
    public static void Escape() { keybd_event(0x1B, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(50); keybd_event(0x1B, 0, 2, UIntPtr.Zero); }
}
'@
# Per-monitor DPI awareness: every coordinate below is a physical pixel, the same as ShareX uses.
[void][SxInput]::SetProcessDpiAwarenessContext([IntPtr]-4)
Import-Module (Join-Path $PSScriptRoot 'ShareXUi.psm1') -Force
Add-Type -AssemblyName System.Windows.Forms, System.Drawing

$screens = @([System.Windows.Forms.Screen]::AllScreens | Sort-Object { -not $_.Primary })
if ($screens.Count -lt 2) { 'SKIPPED: this check needs two or more monitors.'; return }
$start = $screens[0].Bounds
$other = $screens[1].Bounds
$virtual = [System.Windows.Forms.SystemInformation]::VirtualScreen

$report = New-Object System.Collections.Generic.List[string]
function Check([string]$Name, [bool]$Ok, [string]$Detail) { $report.Add(("{0} {1} :: {2}" -f $(if ($Ok) { 'PASS' } else { 'FAIL' }), $Name, $Detail)) }
function Get-Log { (Get-ChildItem (Join-Path $Profile 'Logs') -Filter *.txt -Recurse -ErrorAction SilentlyContinue | Get-Content -Raw) -join "`n" }
function Get-Captures { @(Get-ChildItem (Join-Path $Profile 'Screenshots') -Recurse -File -ErrorAction SilentlyContinue) }
function Get-Overlays($Process) { @(Get-SxWindows $Process | Where-Object { $_.Title -eq 'Region capture' }) }
function Wait-Overlays($Process, [int]$Count) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $script:FirstSeconds = $null
    for ($t = 0; $t -lt 300; $t++) {
        $o = Get-Overlays $Process
        if ($o.Count -gt 0 -and $null -eq $script:FirstSeconds) { $script:FirstSeconds = [math]::Round($sw.Elapsed.TotalSeconds, 2) }
        if ($o.Count -ge $Count) { return $o }
        Start-Sleep -Milliseconds 50
    }
    Get-Overlays $Process
}
function Wait-Capture([int]$Before) {
    for ($t = 0; $t -lt 40; $t++) { $c = Get-Captures; if ($c.Count -gt $Before) { return $c | Sort-Object LastWriteTimeUtc | Select-Object -Last 1 }; Start-Sleep -Milliseconds 250 }
    $null
}
function Get-ImageSize($File) { $img = [System.Drawing.Image]::FromFile($File.FullName); try { "$($img.Width)x$($img.Height)" } finally { $img.Dispose() } }
function Start-RegionCapture {
    # Press the hotkey like a user, because Windows lets a process take the foreground only after the process receives the input.
    [SxInput]::MoveTo($start.X + $start.Width / 2, $start.Y + $start.Height / 2)
    [SxInput]::Hotkey()
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $overlays = Wait-Overlays $app $screens.Count
    $sw.Stop()
    Start-Sleep -Milliseconds 800
    $report.Add("INFO window timing :: seconds from hotkey until the first window showed: $script:FirstSeconds, until every window showed: $([math]::Round($sw.Elapsed.TotalSeconds, 2))")
    [pscustomobject]@{ Overlays = $overlays; First = $script:FirstSeconds; Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 2) }
}

Reset-SxApp -ConfigJson '{"ShowStartScreen": false, "CapturePanelRunAtStartup": false}'
[IO.File]::WriteAllText((Join-Path $Profile 'HotkeysConfig.json'), '{"Hotkeys": [{"HotkeyInfo": {"Hotkey": "F9, Shift, Control, Alt"}, "TaskSettings": {"Job": "RectangleRegion"}}]}')
$app = Start-SxApp @('-portable', '-silent')
try {
    for ($t = 0; $t -lt 60 -and (Get-Log) -notmatch 'Hotkey registered'; $t++) { Start-Sleep -Milliseconds 500 }
    Start-Sleep -Seconds 2
    # 1. Selection on the other monitor.
    $run = Start-RegionCapture
    $matched = @($screens | Where-Object { $b = $_.Bounds; $run.Overlays | Where-Object { $_.Left -eq $b.X -and $_.Top -eq $b.Y -and $_.Width -eq $b.Width -and $_.Height -eq $b.Height } })
    Check 'one selection window per monitor' ($run.Overlays.Count -eq $screens.Count -and $matched.Count -eq $screens.Count) `
        "windows: $(($run.Overlays | ForEach-Object { "$($_.Width)x$($_.Height)@$($_.Left),$($_.Top)" }) -join ', '); monitors: $(($screens | ForEach-Object { "$($_.Bounds.Width)x$($_.Bounds.Height)@$($_.Bounds.X),$($_.Bounds.Y)" }) -join ', ')"
    $startOverlay = $run.Overlays | Where-Object { $_.Left -eq $start.X -and $_.Top -eq $start.Y } | Select-Object -First 1
    $otherOverlay = $run.Overlays | Where-Object { $_.Left -eq $other.X -and $_.Top -eq $other.Y } | Select-Object -First 1
    Check 'start monitor window has the keyboard' ($startOverlay -and [SxInput]::GetForegroundWindow() -eq $startOverlay.Handle) "foreground $([SxInput]::Foreground()); window $($startOverlay.Handle); app pid $($app.Id)"

    $clip = New-Object SxInput+RECT
    [void][SxInput]::GetClipCursor([ref]$clip)
    Check 'cursor not clipped to one monitor' ($clip.L -eq $virtual.Left -and $clip.T -eq $virtual.Top -and $clip.R -eq $virtual.Right -and $clip.B -eq $virtual.Bottom) `
        "clip: $($clip.L),$($clip.T)-$($clip.R),$($clip.B); desktop: $($virtual.Left),$($virtual.Top)-$($virtual.Right),$($virtual.Bottom)"

    $tx = $other.X + [int]($other.Width / 2); $ty = $other.Y + [int]($other.Height / 2)
    [SxInput]::MoveTo($tx, $ty)
    Start-Sleep -Milliseconds 600
    $pos = New-Object SxInput+POINT
    [void][SxInput]::GetCursorPos([ref]$pos)
    Check 'cursor moves to the other monitor' ($pos.X -eq $tx -and $pos.Y -eq $ty) "target $tx,$ty; cursor $($pos.X),$($pos.Y)"
    Check 'other monitor window takes the keyboard' ($otherOverlay -and [SxInput]::GetForegroundWindow() -eq $otherOverlay.Handle) "foreground $([SxInput]::Foreground()); window $($otherOverlay.Handle)"
    foreach ($o in $run.Overlays) { [void](Save-SxWindow $o "RegionMultiMonitor-$($o.Left)_$($o.Top)") }

    $before = (Get-Captures).Count
    [SxInput]::Drag($other.X + 100, $other.Y + 100, $other.X + 500, $other.Y + 400)
    $file = Wait-Capture $before
    Check 'selection on the other monitor saves a 401x301 capture (end pixel included)' ($file -and (Get-ImageSize $file) -eq '401x301') "$(if ($file) { "$($file.Name) $(Get-ImageSize $file)" } else { 'no capture' })"
    Start-Sleep -Milliseconds 500
    Check 'every selection window closes' ((Get-Overlays $app).Count -eq 0) "open: $((Get-Overlays $app).Count)"

    # 2. Esc on the other monitor closes every window.
    $run = Start-RegionCapture
    [SxInput]::MoveTo($tx, $ty)
    Start-Sleep -Milliseconds 600
    $before = (Get-Captures).Count
    [SxInput]::Escape()
    Start-Sleep -Milliseconds 1500
    Check 'Esc on the other monitor closes every window' ((Get-Overlays $app).Count -eq 0) "open: $((Get-Overlays $app).Count)"
    Check 'Esc saves nothing' ((Get-Captures).Count -eq $before) "captures: $((Get-Captures).Count)"

    # 3. A drag over a monitor edge stays on the monitor where the drag started.
    $run = Start-RegionCapture
    $edge = $screens | Where-Object { $_.Bounds.X -lt $other.Right -and $_.Bounds.Right -gt $other.X -and $_.Bounds.Y -ge $other.Bottom } | Select-Object -First 1
    if ($edge) {
        $x = [math]::Max($other.X, $edge.Bounds.X) + 200
        $before = (Get-Captures).Count
        [SxInput]::Drag($x, $other.Bottom - 300, $x + 400, $other.Bottom + 300)
        $file = Wait-Capture $before
        Check 'drag over the bottom edge stops at the monitor edge (401x300)' ($file -and (Get-ImageSize $file) -eq '401x300') "$(if ($file) { "$($file.Name) $(Get-ImageSize $file)" } else { 'no capture' })"
    }
    else { Check 'drag over a monitor edge' $true 'SKIPPED: no monitor directly below the other monitor' }
    Check 'no exception in log' ((Get-Log) -notmatch 'Exception:') ''
}
finally {
    Get-Process ShareX -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\Temp\sharex-e2e\app\*' } | Stop-Process -Force
    Remove-Item -Recurse -Force (Join-Path $Profile 'Screenshots') -ErrorAction SilentlyContinue
}

$reportPath = Join-Path $Out 'RegionMultiMonitor.txt'
$report | Set-Content -Encoding utf8 $reportPath
$report
"Report: $reportPath"
