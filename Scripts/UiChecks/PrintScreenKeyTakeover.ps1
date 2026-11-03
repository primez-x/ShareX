# Runtime check: Print Screen reaches ShareX right after "Use Print Screen for ShareX", without a restart.
# Turns the Windows Snipping Tool setting on for the test, then uses the Settings > Shortcuts button to turn the setting off.
# Leaves the Windows setting off (value 0) at the end. Stop any other ShareX that uses Print Screen first.
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ShareXUi.psm1') -Force
# Print Screen can have only one owner, so this check cannot run while another ShareX (not the test copy) is running.
$otherShareX = Get-Process ShareX -ErrorAction SilentlyContinue | Where-Object { $_.Path -notlike '*\Temp\sharex-e2e\app\*' }
if ($otherShareX) { "SKIPPED: close the running ShareX ($($otherShareX[0].Path)) first, because the running ShareX owns Print Screen."; return }
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class SxPrtSc { [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra); }
'@
$key = 'HKCU:\Control Panel\Keyboard'
function Press-PrintScreen { [SxPrtSc]::keybd_event(0x2C, 0x37, 1, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [SxPrtSc]::keybd_event(0x2C, 0x37, 3, [UIntPtr]::Zero) }
function Count-Triggers { @(Get-ChildItem (Join-Path $Profile 'Logs') -Recurse -File | Get-Content | Select-String 'Hotkey triggered').Count }

Get-Process ShareX -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\Temp\sharex-e2e\app\*' } | ForEach-Object { Stop-Process -Id $_.Id -Force; $_.WaitForExit(10000) | Out-Null }
Set-ItemProperty $key -Name PrintScreenKeyForSnippingEnabled -Value 1 -Type DWord
try {
    Reset-SxApp -ConfigJson '{"ShowStartScreen": false}'
    [IO.File]::WriteAllText((Join-Path $Profile 'HotkeysConfig.json'), '{"Hotkeys": [{"HotkeyInfo": {"Hotkey": "PrintScreen"}, "TaskSettings": {"Job": "RectangleRegion"}}]}')
    $p = Start-SxApp @('-portable', '-silent', '-multi')
    Invoke-SxButton (Wait-SxButton $p 'Settings' 30)
    Start-Sleep -Seconds 3
    $win = Get-SxWindows $p | Where-Object { $_.Title -eq 'ShareX - Settings' } | Select-Object -First 1
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($win.Handle)
    $item = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Shortcuts')))
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Seconds 1
    Invoke-SxButton (Wait-SxButton $p 'Use Print Screen for ShareX' 5)
    Start-Sleep -Seconds 1
    $root.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Seconds 2
    "Windows setting after the button: $((Get-ItemProperty $key).PrintScreenKeyForSnippingEnabled)"
    Press-PrintScreen; Start-Sleep -Seconds 2; [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Seconds 1
    "Print Screen reaches ShareX without a restart: $((Count-Triggers) -ge 1)"
    Stop-Process -Id $p.Id -Force; [void]$p.WaitForExit(10000)
}
finally {
    Set-ItemProperty $key -Name PrintScreenKeyForSnippingEnabled -Value 0 -Type DWord
}
