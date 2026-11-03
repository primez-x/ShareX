# Runtime check: the one-time Print Screen question, the Settings > Shortcuts notice and the hotkey migration.
# Turns the Windows Snipping Tool setting on for the test, answers "No", and restores the previous setting at the end. Needs Windows 11.
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ShareXUi.psm1') -Force
Get-Process ShareX -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\Temp\sharex-e2e\app\*' } | ForEach-Object { Stop-Process -Id $_.Id -Force; $_.WaitForExit(10000) | Out-Null }

Reset-SxApp -ConfigJson '{"ShowStartScreen": false, "CapturePanelRunAtStartup": false}'
# Leave the question unanswered and use the old default hotkeys (Ctrl+Print Screen region, Print Screen full screen).
[IO.File]::WriteAllText((Join-Path $Profile 'ApplicationConfig.json'), '{"ShowStartScreen": false, "PrintScreenKeyQuestionAsked": false}')
[IO.File]::WriteAllText((Join-Path $Profile 'HotkeysConfig.json'), '{"Hotkeys": [
  {"HotkeyInfo": {"Hotkey": "PrintScreen, Control"}, "TaskSettings": {"Job": "RectangleRegion"}},
  {"HotkeyInfo": {"Hotkey": "PrintScreen"}, "TaskSettings": {"Job": "PrintScreen"}}]}')

$original = (Get-ItemProperty 'HKCU:\Control Panel\Keyboard' -Name PrintScreenKeyForSnippingEnabled -ErrorAction SilentlyContinue).PrintScreenKeyForSnippingEnabled
Set-ItemProperty 'HKCU:\Control Panel\Keyboard' -Name PrintScreenKeyForSnippingEnabled -Value 1 -Type DWord
try {
$before = (Get-ItemProperty 'HKCU:\Control Panel\Keyboard' -Name PrintScreenKeyForSnippingEnabled -ErrorAction SilentlyContinue).PrintScreenKeyForSnippingEnabled
$p = Start-SxApp @('-portable', '-silent')
$no = Wait-SxButton $p 'No' 30
"Question shown: $($no -ne $null)"
if ($no) {
    $dialog = Get-SxWindows $p | Where-Object { $_.Width -lt 900 -and $_.Height -lt 600 } | Select-Object -First 1
    if ($dialog) { "Saved $(Save-SxWindow $dialog 'printscreen-question')" }
    Invoke-SxButton $no
}
Start-Sleep -Seconds 1
$after = (Get-ItemProperty 'HKCU:\Control Panel\Keyboard' -Name PrintScreenKeyForSnippingEnabled -ErrorAction SilentlyContinue).PrintScreenKeyForSnippingEnabled
"Windows setting unchanged after No: $($before -eq $after)"

# Settings > Shortcuts shows the notice while Windows owns Print Screen.
Invoke-SxButton (Wait-SxButton $p 'Settings' 10)
Start-Sleep -Seconds 3
$win = Get-SxWindows $p | Where-Object { $_.Title -eq 'ShareX - Settings' } | Select-Object -First 1
$root = [System.Windows.Automation.AutomationElement]::FromHandle($win.Handle)
$itemCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Shortcuts')
$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $itemCond).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 1
"Shortcuts notice button: $((Wait-SxButton $p 'Use Print Screen for ShareX' 5) -ne $null)"
"Saved $(Save-SxWindow $win 'printscreen-shortcuts')"
# Each shortcut row has three buttons: task, edit, hotkey.
$names = @(Get-SxButtons $p | ForEach-Object { $_.Current.Name })
$region = [array]::IndexOf($names, 'Capture region')
$full = [array]::IndexOf($names, 'Capture entire screen')
"Region hotkey: $($names[$region + 2]); full screen hotkey: $($names[$full + 2])"
"Print Screen now selects a region: $($names[$region + 2] -eq 'Print Screen' -and $names[$full + 2] -eq 'Ctrl + Print Screen')"

Stop-Process -Id $p.Id -Force; [void]$p.WaitForExit(15000)
}
finally {
    if ($null -eq $original) { Remove-ItemProperty 'HKCU:\Control Panel\Keyboard' -Name PrintScreenKeyForSnippingEnabled -ErrorAction SilentlyContinue }
    else { Set-ItemProperty 'HKCU:\Control Panel\Keyboard' -Name PrintScreenKeyForSnippingEnabled -Value $original -Type DWord }
}
