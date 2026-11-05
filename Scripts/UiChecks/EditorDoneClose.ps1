# Runtime check: a full-screen capture opens the editor on the saved file; Done rewrites the file, Close keeps the original.
# Also checks the clipboard: the capture is on the clipboard while the editor is open. Done copies the result again; Close does not.
# The capture contains screen content, so the script deletes the capture and clears the clipboard afterwards.
# Report: %LOCALAPPDATA%\Temp\sharex-e2e\ui\EditorDoneClose-<Button>.txt
param([ValidateSet('Done', 'Close')][string]$Button = 'Done')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ShareXUi.psm1') -Force
Add-Type -AssemblyName System.Windows.Forms
$report = New-Object System.Collections.Generic.List[string]
function Check([string]$Name, [bool]$Ok, [string]$Detail) { $report.Add(("{0} {1} :: {2}" -f $(if ($Ok) { 'PASS' } else { 'FAIL' }), $Name, $Detail)) }
function Get-CopyCount { @((Get-ChildItem (Join-Path $Profile 'Logs') -Filter *.txt -Recurse | Get-Content) -match 'Image copied to clipboard\.').Count }

Reset-SxApp -ConfigJson '{"ShowStartScreen": false, "CapturePanelRunAtStartup": false}'
[System.Windows.Forms.Clipboard]::Clear()
$p = Start-SxApp @('-portable', '-silent', '-AutoClose', '-PrintScreen')
try {
    $target = Wait-SxButton $p $Button 20
    if (-not $target) { throw "button '$Button' not found" }
    $file = Get-ChildItem (Join-Path $Profile 'Screenshots') -Recurse -File | Select-Object -First 1
    Check 'capture saved before the editor closes' ($null -ne $file) "file: $($file.Name)"
    Check 'capture on the clipboard while the editor is open' ([System.Windows.Forms.Clipboard]::ContainsImage()) "copies in log: $(Get-CopyCount)"
    $before = $file.LastWriteTimeUtc
    Start-Sleep -Milliseconds 1200
    Invoke-SxButton $target
    if (-not $p.WaitForExit(30000)) { throw 'app did not auto-close' }
    $file.Refresh()
    $log = Get-ChildItem (Join-Path $Profile 'Logs') -Filter *.txt -Recurse | Get-Content -Raw
    $expectedCopies = if ($Button -eq 'Done') { 2 } else { 1 }
    Check "$Button copies $($expectedCopies - 1) more time(s)" ((Get-CopyCount) -eq $expectedCopies) "copies in log: $(Get-CopyCount)"
    Check 'capture file still exists' (Test-Path $file.FullName) $file.FullName
    Check "file rewritten after $Button" (($file.LastWriteTimeUtc -gt $before) -eq ($Button -eq 'Done')) "rewritten: $($file.LastWriteTimeUtc -gt $before)"
    Check 'no exception in log' ($log -notmatch 'Exception:') ''
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    [System.Windows.Forms.Clipboard]::Clear()
    Remove-Item -Recurse -Force (Join-Path $Profile 'Screenshots') -ErrorAction SilentlyContinue
}

$reportPath = Join-Path $Out "EditorDoneClose-$Button.txt"
$report | Set-Content $reportPath
$report
"Report: $reportPath"
