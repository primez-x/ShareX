# Runtime check: a full-screen capture opens the editor on the saved file; Done rewrites the file, Close keeps the original.
# The capture contains screen content, so the script deletes the capture afterwards.
param([ValidateSet('Done', 'Close')][string]$Button = 'Done')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ShareXUi.psm1') -Force
Reset-SxApp -ConfigJson '{"ShowStartScreen": false, "CapturePanelRunAtStartup": false}'
$p = Start-SxApp @('-portable', '-silent', '-AutoClose', '-PrintScreen')
$target = Wait-SxButton $p $Button 20
if (-not $target) { Stop-Process -Id $p.Id -Force; "FAIL: button '$Button' not found"; return }
$file = Get-ChildItem (Join-Path $Profile 'Screenshots') -Recurse -File | Select-Object -First 1
"Capture saved before the editor closed: $($file -ne $null)"
$before = $file.LastWriteTimeUtc
Start-Sleep -Milliseconds 1200
Invoke-SxButton $target
if (-not $p.WaitForExit(30000)) { Stop-Process -Id $p.Id -Force; 'WARN: app did not auto-close' }
$file.Refresh()
$log = Get-ChildItem (Join-Path $Profile 'Logs') -Filter *.txt -Recurse | Get-Content -Raw
"Button: $Button"
"Capture file still exists: $(Test-Path $file.FullName)"
"File rewritten after button: $($file.LastWriteTimeUtc -gt $before)"
"Exception in log: $($log -match 'Exception:')"
Remove-Item -Recurse -Force (Join-Path $Profile 'Screenshots')
