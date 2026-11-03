# Runtime check: open Settings from the capture panel, screenshot each section, list accessible names.
param([ValidateSet('Dark', 'Light')][string]$Theme = 'Dark')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ShareXUi.psm1') -Force

Reset-SxApp -ConfigJson ('{"ShowStartScreen": false, "ThemeOptions": {"Theme": "' + $Theme + '"}}')
$p = Start-SxApp @('-portable', '-silent')
$settingsButton = Wait-SxButton $p 'Settings'
if (-not $settingsButton) { Stop-Process -Id $p.Id -Force; throw 'Capture panel Settings button not found' }
Invoke-SxButton $settingsButton
Start-Sleep -Seconds 3
$win = Get-SxWindows $p | Where-Object { $_.Title -eq 'ShareX - Settings' } | Select-Object -First 1
"Settings window found: $($win -ne $null) ($($win.Width)x$($win.Height))"

$root = [System.Windows.Automation.AutomationElement]::FromHandle($win.Handle)
$itemCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TreeItem)
$items = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCond)
"Nav items: $(($items | ForEach-Object { $_.Current.Name }) -join ', ')"

foreach ($item in $items) {
    $name = $item.Current.Name
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 800
    [void](Save-SxWindow $win ("settings-{0}-{1}" -f $Theme.ToLower(), ($name -replace '\W', '')))
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.IsOffscreen -eq $false -and $_.Current.ControlType.ProgrammaticName -match 'CheckBox|Button|ComboBox|Edit' }
    $unnamed = @($all | Where-Object { -not $_.Current.Name }).Count
    "[$name] controls: $(($all | ForEach-Object { $_.Current.Name } | Where-Object { $_ } | Select-Object -Unique) -join ' | ') ; unnamed: $unnamed"
}

if ($Theme -eq 'Dark') {
    $advanced = $items | Where-Object { $_.Current.Name -eq 'Advanced' }
    $advanced.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 800
    foreach ($b in 'All application settings...', 'All capture and task settings...', 'All shortcut settings...', 'Destination settings...') {
        $btn = Wait-SxButton $p $b 5
        if ($btn) { Invoke-SxButton $btn; Start-Sleep -Seconds 3 } else { "MISSING button: $b" }
    }
    $titles = (Get-SxWindows $p | ForEach-Object { $_.Title }) -join ' | '
    "Open windows after Advanced buttons: $titles"
    foreach ($w in Get-SxWindows $p | Where-Object { $_.Title -match 'Application settings|Task settings|Hotkey settings|Destination settings' }) {
        [void](Save-SxWindow $w ("old-" + ($w.Title -replace '\W', '')))
    }
}
Stop-Process -Id $p.Id -Force; [void]$p.WaitForExit(15000); Start-Sleep -Milliseconds 500
