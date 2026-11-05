# Runtime check: a full-screen capture saves and shows the preview, and the editor stays closed.
# A left click on the preview opens the editor on the saved file. Done rewrites the same file; Close keeps the file.
# The profile starts with the old defaults (editor after capture, left click opens the file), so the check also proves the one-time migration.
# The capture contains screen content, so the script deletes the capture and clears the clipboard afterwards.
# Report: %LOCALAPPDATA%\Temp\sharex-e2e\ui\PreviewEdit-<Button>.txt, screenshots PreviewEdit-preview.png and PreviewEdit-editor.png
param([ValidateSet('Done', 'Close')][string]$Button = 'Done')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ShareXUi.psm1') -Force
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System.Runtime.InteropServices;
public static class SxMouse {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, System.UIntPtr extra);
    public static void LeftClick(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(150); mouse_event(2, 0, 0, 0, System.UIntPtr.Zero); System.Threading.Thread.Sleep(60); mouse_event(4, 0, 0, 0, System.UIntPtr.Zero); }
}
'@
$report = New-Object System.Collections.Generic.List[string]
function Check([string]$Name, [bool]$Ok, [string]$Detail) { $report.Add(("{0} {1} :: {2}" -f $(if ($Ok) { 'PASS' } else { 'FAIL' }), $Name, $Detail)) }
function Get-Log { (Get-ChildItem (Join-Path $Profile 'Logs') -Filter *.txt -Recurse | Get-Content -Raw) -join "`n" }
function Get-CopyCount { ([regex]::Matches((Get-Log), 'Image copied to clipboard\.')).Count }
function Get-Captures { @(Get-ChildItem (Join-Path $Profile 'Screenshots') -Recurse -File -ErrorAction SilentlyContinue) }

$seed = '{"ShowStartScreen": false, "CapturePanelRunAtStartup": false, "PreviewEditDefaultsApplied": false,
  "DefaultTaskSettings": {"OpenEditorAfterCapture": true, "GeneralSettings": {"ToastWindowLeftClickAction": "OpenUrl", "ToastWindowDuration": 15}}}'
Reset-SxApp -ConfigJson $seed
[System.Windows.Forms.Clipboard]::Clear()
$p = Start-SxApp @('-portable', '-silent', '-PrintScreen')
try {
    $file = $null
    for ($t = 0; $t -lt 40 -and -not $file; $t++) { Start-Sleep -Milliseconds 500; $file = Get-Captures | Select-Object -First 1 }
    Check 'capture saved' ($null -ne $file) "file: $($file.Name)"
    if (-not $file) { throw 'no capture file' }

    $preview = $null
    for ($t = 0; $t -lt 20 -and -not $preview; $t++) { Start-Sleep -Milliseconds 250; $preview = Get-SxWindows $p | Where-Object { $_.Width -gt 100 -and $_.Height -gt 100 } | Select-Object -First 1 }
    Check 'preview appears' ($null -ne $preview) $(if ($preview) { "window $($preview.Width)x$($preview.Height) at $($preview.Left),$($preview.Top)" } else { 'no window' })
    if (-not $preview) { throw 'no preview window' }
    $previewShot = Save-SxWindow $preview 'PreviewEdit-preview'
    Check 'editor stays closed after capture' ($null -eq (Wait-SxButton $p 'Done' 2)) 'no Done button'
    Check 'capture on the clipboard' ([System.Windows.Forms.Clipboard]::ContainsImage()) "copies in log: $(Get-CopyCount)"

    # Click the image, away from the action buttons in the middle of the preview.
    [SxMouse]::LeftClick($preview.Left + 60, $preview.Top + 60)
    $target = Wait-SxButton $p $Button 20
    Check 'click on the preview opens the editor' ($null -ne $target) "button '$Button' found: $($null -ne $target)"
    if (-not $target) { throw "button '$Button' not found" }
    $editor = Get-SxWindows $p | Sort-Object { $_.Width * $_.Height } -Descending | Select-Object -First 1
    $editorShot = Save-SxWindow $editor 'PreviewEdit-editor'

    $before = $file.LastWriteTimeUtc
    $copiesBefore = Get-CopyCount
    Start-Sleep -Milliseconds 1200
    Invoke-SxButton $target
    Start-Sleep -Seconds 3
    $file.Refresh()
    $expectedCopies = if ($Button -eq 'Done') { $copiesBefore + 1 } else { $copiesBefore }
    Check 'editor closed' ($null -eq (Wait-SxButton $p $Button 1)) ''
    Check 'still one capture file' ((Get-Captures).Count -eq 1) "files: $((Get-Captures).Count)"
    Check "file rewritten after $Button" (($file.LastWriteTimeUtc -gt $before) -eq ($Button -eq 'Done')) "rewritten: $($file.LastWriteTimeUtc -gt $before)"
    Check "$Button copies $($expectedCopies - $copiesBefore) more time(s)" ((Get-CopyCount) -eq $expectedCopies) "copies in log: $(Get-CopyCount)"
    Check 'no exception in log' ((Get-Log) -notmatch 'Exception:') ''
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    [System.Windows.Forms.Clipboard]::Clear()
    Remove-Item -Recurse -Force (Join-Path $Profile 'Screenshots') -ErrorAction SilentlyContinue
}

$reportPath = Join-Path $Out "PreviewEdit-$Button.txt"
$report | Set-Content $reportPath
$report
"Screenshots: $previewShot, $editorShot"
"Report: $reportPath"
