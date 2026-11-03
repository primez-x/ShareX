# Runtime check: hotkeys still work after the Settings window opens and closes.
# Binds Ctrl+Alt+F9 to region capture, opens and closes Settings, then presses Ctrl+Alt+F9.
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ShareXUi.psm1') -Force
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class SxKeys { [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra); }
'@
function Press-CtrlAltF9 {
    foreach ($k in @(@(0x11, 0x1D), @(0x12, 0x38), @(0x78, 0x43))) { [SxKeys]::keybd_event([byte]$k[0], [byte]$k[1], 0, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 80
    foreach ($k in @(@(0x78, 0x43), @(0x12, 0x38), @(0x11, 0x1D))) { [SxKeys]::keybd_event([byte]$k[0], [byte]$k[1], 2, [UIntPtr]::Zero) }
}
function Count-Triggers { @(Get-ChildItem (Join-Path $Profile 'Logs') -Recurse -File | Get-Content | Select-String 'Hotkey triggered').Count }

Get-Process ShareX -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\Temp\sharex-e2e\app\*' } | ForEach-Object { Stop-Process -Id $_.Id -Force; $_.WaitForExit(10000) | Out-Null }
Reset-SxApp -ConfigJson '{"ShowStartScreen": false}'
[IO.File]::WriteAllText((Join-Path $Profile 'HotkeysConfig.json'), '{"Hotkeys": [{"HotkeyInfo": {"Hotkey": "F9, Control, Alt"}, "TaskSettings": {"Job": "RectangleRegion"}}]}')
$p = Start-SxApp @('-portable', '-silent', '-multi')
[void](Wait-SxButton $p 'Settings' 30)

Press-CtrlAltF9; Start-Sleep -Seconds 2; [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Seconds 2
"Hotkey works before Settings: $((Count-Triggers) -eq 1)"

Invoke-SxButton (Wait-SxButton $p 'Settings')
Start-Sleep -Seconds 3
$win = Get-SxWindows $p | Where-Object { $_.Title -eq 'ShareX - Settings' } | Select-Object -First 1
[System.Windows.Automation.AutomationElement]::FromHandle($win.Handle).GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
Start-Sleep -Seconds 2

Press-CtrlAltF9; Start-Sleep -Seconds 2; [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Seconds 1
"Hotkey works after Settings closed: $((Count-Triggers) -eq 2)"
Stop-Process -Id $p.Id -Force; [void]$p.WaitForExit(10000)
