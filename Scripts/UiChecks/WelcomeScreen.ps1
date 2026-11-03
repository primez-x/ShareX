# Runtime check: every control on the first-run welcome screen has an accessible name, and "Get started" closes the screen.
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ShareXUi.psm1') -Force
$A = [System.Windows.Automation.AutomationElement]
Get-Process ShareX -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\Temp\sharex-e2e\app\*' } | ForEach-Object { Stop-Process -Id $_.Id -Force; $_.WaitForExit(10000) | Out-Null }
Reset-SxApp -ConfigJson '{"ShowStartScreen": true}'
$p = Start-SxApp @('-portable', '-silent', '-multi')
$start = Wait-SxButton $p 'Get started' 30
$win = Get-SxWindows $p | Where-Object { $_.Title -eq 'Welcome to ShareX' } | Select-Object -First 1
$controls = $A::FromHandle($win.Handle).FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
    Where-Object { $_.Current.ControlType.ProgrammaticName -match 'Button|ComboBox|CheckBox' -and $_.Current.Name -notin 'Minimize', 'Maximize', 'Close' }
"Controls: $(($controls | ForEach-Object { $_.Current.Name }) -join ' | ')"
"Unnamed or type-named controls: $(@($controls | Where-Object { -not $_.Current.Name -or $_.Current.Name -like 'Avalonia.*' }).Count)"
Invoke-SxButton $start
Start-Sleep -Seconds 2
"Welcome screen closed: $((Get-SxWindows $p | Where-Object { $_.Title -eq 'Welcome to ShareX' }) -eq $null)"
Stop-Process -Id $p.Id -Force; [void]$p.WaitForExit(10000)
