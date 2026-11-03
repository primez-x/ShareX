# Delete from the library: the file goes, the card goes, the history row stays.
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ShareXUi.psm1') -Force
Get-Process ShareX -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\Temp\sharex-e2e\app\*' } | ForEach-Object { Stop-Process -Id $_.Id -Force; $_.WaitForExit(10000) | Out-Null }
Reset-SxApp -ConfigJson '{"ShowStartScreen": false, "CapturePanelRunAtStartup": false}'
& python -I (Join-Path $PSScriptRoot 'seed_library.py') $Profile 10
$target = Join-Path $Profile 'Screenshots\seed\Capture 010 screen.png'
$p = Start-SxApp @('-portable')
[void](Wait-SxButton $p 'New capture' 30)
Start-Sleep -Seconds 3
$card = Wait-SxButton $p 'Capture 010 screen.png' 10
Invoke-SxButton $card
Start-Sleep -Seconds 1
# A modal dialog inside a UI Automation invoke blocks UI Automation, so press the Delete key on the focused card instead.
Add-Type -AssemblyName System.Windows.Forms
$card.SetFocus()
Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('{DELETE}')
$yes = Wait-SxButton $p 'Yes' 10
if (-not $yes) { Stop-Process -Id $p.Id -Force; throw 'Confirmation Yes button not found' }
Invoke-SxButton $yes
Start-Sleep -Seconds 2
"File deleted: $(-not (Test-Path $target))"
"Card removed: $(-not (Wait-SxButton $p 'Capture 010 screen.png' 2))"
Stop-Process -Id $p.Id -Force; [void]$p.WaitForExit(15000)
$rows = & python -I -c "import sqlite3,sys; print(sqlite3.connect(sys.argv[1]).execute('select count(*) from History where FileName=?', ('Capture 010 screen.png',)).fetchone()[0])" (Join-Path $Profile 'History.db')
"History row kept: $($rows -eq '1')"
