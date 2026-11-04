# Runtime check: Win+Shift+S starts ShareX region capture instead of Snipping Tool, and does not open Start.
# Some laptops have a screenshot key that sends Win+Shift+S. Windows keeps Win+Shift+S for Snipping Tool, so ShareX takes the keys with a keyboard hook.
# Also checks the one-time migration: an old config gets Win+Shift+S, and a config that already had the migration does not get Win+Shift+S again.
# Report: %LOCALAPPDATA%\Temp\sharex-e2e\ui\SnippingToolShortcut.txt
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ShareXUi.psm1') -Force
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class SxSnip {
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    public static uint ForegroundProcess() { uint pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid); return pid; }
}
'@
$report = New-Object System.Collections.Generic.List[string]
function Check([string]$Name, [bool]$Ok, [string]$Detail) { $report.Add(("{0} {1} :: {2}" -f $(if ($Ok) { 'PASS' } else { 'FAIL' }), $Name, $Detail)) }
function Get-Log { (Get-ChildItem (Join-Path $Profile 'Logs') -Recurse -File | Get-Content) -join "`n" }
function Stop-TestShareX { Get-Process ShareX -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\Temp\sharex-e2e\app\*' } | ForEach-Object { Stop-Process -Id $_.Id -Force; $_.WaitForExit(10000) | Out-Null } }
function Press-WinShiftS {
    # Same keys as the laptop screenshot key: Win down, Shift down, S down, then all up.
    foreach ($k in @(@(0x5B, 0x5B, 1), @(0xA0, 0x2A, 0), @(0x53, 0x1F, 0))) { [SxSnip]::keybd_event([byte]$k[0], [byte]$k[1], [uint32]$k[2], [UIntPtr]::Zero); Start-Sleep -Milliseconds 20 }
    foreach ($k in @(@(0x53, 0x1F, 2), @(0xA0, 0x2A, 2), @(0x5B, 0x5B, 3))) { [SxSnip]::keybd_event([byte]$k[0], [byte]$k[1], [uint32]$k[2], [UIntPtr]::Zero); Start-Sleep -Milliseconds 20 }
}
function Get-ForegroundName { $id = [SxSnip]::ForegroundProcess(); (Get-Process -Id $id -ErrorAction SilentlyContinue).ProcessName }

# A second keyboard hook, started before ShareX, records only the S key and the unassigned key 0xE8. Windows calls the newest hook first,
# so the S key reaches this hook only when ShareX lets the S key through to Windows. ShareX sends 0xE8 after ShareX takes the shortcut,
# so 0xE8 proves that this hook saw the keys.
$watcherReady = Join-Path $Out 'snip-watch.ready'
$watcherStop = Join-Path $Out 'snip-watch.stop'
$watcherOut = Join-Path $Out 'snip-watch.txt'
Remove-Item $watcherReady, $watcherStop, $watcherOut -ErrorAction SilentlyContinue
$watcher = Start-Job -ArgumentList $watcherReady, $watcherStop, $watcherOut -ScriptBlock {
    param($ready, $stop, $out)
    Add-Type @'
using System; using System.Collections.Generic; using System.IO; using System.Runtime.InteropServices; using System.Threading;
public static class SxSKeyWatch {
    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr h; public uint m; public IntPtr w, l; public uint t; public int x, y; }
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint tid);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int c, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool PeekMessage(out MSG m, IntPtr h, uint a, uint b, uint r);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string n);
    static HookProc proc; static List<string> log = new List<string>();
    static IntPtr Cb(int c, IntPtr w, IntPtr l) {
        int vk = Marshal.ReadInt32(l);
        if (vk == 0x53 || vk == 0xE8) log.Add((vk == 0x53 ? "S" : "E8") + (((int)w == 0x100 || (int)w == 0x104) ? " down" : " up"));
        return CallNextHookEx(IntPtr.Zero, c, w, l);
    }
    public static void Run(string ready, string stop, string output) {
        proc = Cb;
        IntPtr h = SetWindowsHookEx(13, proc, GetModuleHandle(null), 0);
        File.WriteAllText(ready, "ready");
        MSG m; DateTime end = DateTime.Now.AddSeconds(90);
        while (!File.Exists(stop) && DateTime.Now < end) { while (PeekMessage(out m, IntPtr.Zero, 0, 0, 1)) { } Thread.Sleep(5); }
        UnhookWindowsHookEx(h);
        File.WriteAllLines(output, log);
    }
}
'@
    [SxSKeyWatch]::Run($ready, $stop, $out)
}

Stop-TestShareX
try {
    for ($i = 0; $i -lt 100 -and -not (Test-Path $watcherReady); $i++) { Start-Sleep -Milliseconds 100 }
    if (-not (Test-Path $watcherReady)) { throw 'The key watcher did not start.' }

    # Phase 1: a config from before Win+Shift+S existed. The migration adds Win+Shift+S for region capture.
    Reset-SxApp -ConfigJson '{"ShowStartScreen": false, "SnippingToolHotkeyAdded": false}'
    [IO.File]::WriteAllText((Join-Path $Profile 'HotkeysConfig.json'), '{"Hotkeys": [{"HotkeyInfo": {"Hotkey": "F9, Control, Alt"}, "TaskSettings": {"Job": "RectangleRegion"}}]}')
    $started = Get-Date
    $p = Start-SxApp @('-portable', '-silent', '-multi')
    [void](Wait-SxButton $p 'Settings' 30)
    Start-Sleep -Seconds 1

    $log = Get-Log
    Check 'migration adds Win+Shift+S' ($log -match 'Hotkey registered: Hotkey: Shift \+ Win \+ S, Description: Capture region') 'log shows the registered hotkey'
    Check 'Win+Shift+S uses the keyboard hook' ($log -match 'Windows reserves this hotkey') 'log shows the hook path'

    Press-WinShiftS
    Start-Sleep -Milliseconds 1500
    $foreground = Get-ForegroundName
    $log = Get-Log
    Check 'Win+Shift+S starts ShareX region capture' ($log -match 'Hotkey triggered\. Hotkey: Shift \+ Win \+ S') 'log shows the hotkey trigger'
    # Start opens when Win goes up, before this point. If Start opened, Start would be in front instead of ShareX.
    Check 'region capture is in front, Start does not open' ($foreground -eq 'ShareX') "foreground process: $foreground"
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Seconds 1
    $snip = @(Get-CimInstance Win32_Process -Filter "Name='SnippingTool.exe'" | Where-Object { $_.CreationDate -gt $started })
    Check 'Snipping Tool does not start' ($snip.Count -eq 0) "new Snipping Tool processes: $($snip.Count)"
    Stop-TestShareX

    # Phase 2: the migration already ran and the user removed Win+Shift+S. ShareX does not add Win+Shift+S again.
    Reset-SxApp -ConfigJson '{"ShowStartScreen": false, "SnippingToolHotkeyAdded": true}'
    [IO.File]::WriteAllText((Join-Path $Profile 'HotkeysConfig.json'), '{"Hotkeys": [{"HotkeyInfo": {"Hotkey": "F9, Control, Alt"}, "TaskSettings": {"Job": "RectangleRegion"}}]}')
    $p = Start-SxApp @('-portable', '-silent', '-multi')
    [void](Wait-SxButton $p 'Settings' 30)
    Start-Sleep -Seconds 1
    $log = Get-Log
    Check 'removed Win+Shift+S stays removed' ($log -notmatch 'Win \+ S' -and $log -match 'Hotkey registered: Hotkey: Ctrl \+ Alt \+ F9') 'log shows only Ctrl + Alt + F9'
    Stop-TestShareX
}
finally {
    Stop-TestShareX
    New-Item -ItemType File $watcherStop -Force | Out-Null
    [void](Wait-Job $watcher -Timeout 15)
    Remove-Job $watcher -Force
}

$keys = @(Get-Content $watcherOut -ErrorAction SilentlyContinue)
$sKeys = @($keys | Where-Object { $_ -like 'S *' })
Check 'the key watcher saw the keys ShareX sends' (@($keys | Where-Object { $_ -like 'E8 *' }).Count -ge 2) "0xE8 events: $(@($keys | Where-Object { $_ -like 'E8 *' }).Count)"
Check 'Windows does not see the S key' ($sKeys.Count -eq 0) "S key events after ShareX: $($sKeys.Count)"
Remove-Item $watcherReady, $watcherStop, $watcherOut -ErrorAction SilentlyContinue

$reportPath = Join-Path $Out 'SnippingToolShortcut.txt'
$report | Set-Content $reportPath
$report
"Report: $reportPath"
