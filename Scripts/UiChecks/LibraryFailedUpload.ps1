# A failed upload of a saved file shows as a failed task card, not as a plain history card.
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ShareXUi.psm1') -Force
Reset-SxApp -ConfigJson '{"ShowStartScreen": false, "CapturePanelRunAtStartup": false}'
& python -I (Join-Path $PSScriptRoot 'seed_library.py') $Profile 3
$file = Join-Path $Profile 'Screenshots\seed\Capture 002 screen.png'
$p = Start-SxApp @('-portable')
[void](Wait-SxButton $p 'New capture' 30)
Start-Sleep -Seconds 3
$second = Start-SxApp @("`"$file`"", '-portable')
[void]$second.WaitForExit(15000)
Start-Sleep -Seconds 6
$cards = @(Get-SxButtons $p | Where-Object { $_.Current.Name -eq 'Capture 002 screen.png' })
"Cards for the file: $($cards.Count)"
Invoke-SxButton $cards[0]
Start-Sleep -Seconds 1
"Selected card offers Show errors: $((Wait-SxButton $p 'Show errors...' 3) -ne $null)"
$main = Get-SxWindows $p | Where-Object { $_.Title -like 'ShareX 2*' } | Select-Object -First 1
[void](Save-SxWindow $main 'library-failed-task')
Stop-Process -Id $p.Id -Force; [void]$p.WaitForExit(15000)
