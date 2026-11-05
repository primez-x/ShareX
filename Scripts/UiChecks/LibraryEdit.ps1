# Runtime check: Edit on a library card opens the editor on the same file. Done writes the edit back to that file,
# makes no new capture file, and the card and the selected-capture preview show the new image.
# The edit is a red image pasted from the clipboard (Ctrl+V, insert in the center), so the thumbnail must change.
# Report: %LOCALAPPDATA%\Temp\sharex-e2e\ui\LibraryEdit.txt, screenshots LibraryEdit-before.png and LibraryEdit-after.png
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ShareXUi.psm1') -Force
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class SxMouse {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    public static void LeftClick(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(150); mouse_event(2, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(60); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
}
'@
$report = New-Object System.Collections.Generic.List[string]
function Check([string]$Name, [bool]$Ok, [string]$Detail) { $report.Add(("{0} {1} :: {2}" -f $(if ($Ok) { 'PASS' } else { 'FAIL' }), $Name, $Detail)) }
function Find-Main($p) { Get-SxWindows $p | Where-Object { $_.Title -like 'ShareX 2*' } | Sort-Object { $_.Width * $_.Height } -Descending | Select-Object -First 1 }
function Get-CardImage($p, $main, [string]$Name, [string]$Shot) {
    $card = Wait-SxButton $p $Name 5
    $r = $card.Current.BoundingRectangle
    $file = Save-SxWindow $main $Shot
    $bmp = [System.Drawing.Bitmap]::FromFile($file)
    $crop = $bmp.Clone((New-Object System.Drawing.Rectangle ([int]($r.X - $main.Left)), ([int]($r.Y - $main.Top)), ([int]$r.Width), ([int]$r.Height)), $bmp.PixelFormat)
    $bmp.Dispose(); $crop
}
# The preview image sits above the Edit button in the right pane.
function Get-PreviewImage($p, $main, [string]$Shot) {
    $edit = (Wait-SxButton $p 'Edit' 5).Current.BoundingRectangle
    $file = Save-SxWindow $main $Shot
    $bmp = [System.Drawing.Bitmap]::FromFile($file)
    $x = [int]($edit.X - $main.Left); $w = [Math]::Min(400, $main.Width - $x - 10)
    $crop = $bmp.Clone((New-Object System.Drawing.Rectangle $x, 200, $w, 200), $bmp.PixelFormat)
    $bmp.Dispose(); $crop
}
function Get-Diff($a, $b) {
    $sum = 0; $n = 0
    for ($x = 0; $x -lt [Math]::Min($a.Width, $b.Width); $x += 4) { for ($y = 0; $y -lt [Math]::Min($a.Height, $b.Height); $y += 4) {
        $c1 = $a.GetPixel($x, $y); $c2 = $b.GetPixel($x, $y); $sum += [Math]::Abs($c1.R - $c2.R) + [Math]::Abs($c1.G - $c2.G) + [Math]::Abs($c1.B - $c2.B); $n++ } }
    [Math]::Round($sum / [Math]::Max(1, $n), 1)
}

Reset-SxApp -ConfigJson '{"ShowStartScreen": false, "CapturePanelRunAtStartup": false}'
& python -I (Join-Path $PSScriptRoot 'seed_library.py') $Profile 3
$cardName = 'Capture 003 screen.png'
$p = Start-SxApp @('-portable')
try {
    if (-not (Wait-SxButton $p $cardName 30)) { throw "card '$cardName' not found" }
    Start-Sleep -Seconds 3
    $main = Find-Main $p
    $filesBefore = @(Get-ChildItem (Join-Path $Profile 'Screenshots') -Recurse -File).Count
    $before = Get-CardImage $p $main $cardName 'LibraryEdit-before'
    $seedFile = Get-ChildItem (Join-Path $Profile 'Screenshots\seed') -File | Where-Object { $_.Name -like '*003*' } | Select-Object -First 1
    $writeBefore = $seedFile.LastWriteTimeUtc

    Invoke-SxButton (Wait-SxButton $p $cardName 5)
    Start-Sleep -Seconds 2
    $previewBefore = Get-PreviewImage $p $main 'LibraryEdit-selected'
    Invoke-SxButton (Wait-SxButton $p 'Edit' 5)
    $done = Wait-SxButton $p 'Done' 20
    Check 'Edit opens the editor in capture mode' ($null -ne $done) 'Done button found'
    if (-not $done) { throw 'editor Done button not found' }

    $red = New-Object System.Drawing.Bitmap 4000, 4000
    $g = [System.Drawing.Graphics]::FromImage($red); $g.Clear([System.Drawing.Color]::Red); $g.Dispose()
    [System.Windows.Forms.Clipboard]::SetImage($red); $red.Dispose()
    # A real click gives the editor the keyboard focus. Click the empty canvas area near the top left corner.
    $editor = Get-SxWindows $p | Where-Object { $_.Title -like '*Editor*' } | Select-Object -First 1
    [SxMouse]::LeftClick($editor.Left + 40, $editor.Top + [int]($editor.Height / 2))
    Start-Sleep -Milliseconds 800
    [System.Windows.Forms.SendKeys]::SendWait('^v')
    Start-Sleep -Seconds 1
    # The editor asks where to insert the pasted image.
    # The dialog buttons have no name of their own, so find the text and walk up to its button.
    $A = [System.Windows.Automation.AutomationElement]
    $textCond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)),
        (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'Insert image in the center')))
    $insert = $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $textCond)
    while ($insert -and $insert.Current.ControlType -ne [System.Windows.Automation.ControlType]::Button) {
        $insert = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($insert)
    }
    Check 'paste asks where to insert the image' ($null -ne $insert) ''
    if ($insert) { Invoke-SxButton $insert; Start-Sleep -Seconds 1 }
    Invoke-SxButton $done
    Start-Sleep -Seconds 4

    $seedFile.Refresh()
    $filesAfter = @(Get-ChildItem (Join-Path $Profile 'Screenshots') -Recurse -File).Count
    Check 'Done rewrites the same file' ($seedFile.LastWriteTimeUtc -gt $writeBefore) $seedFile.Name
    Check 'no new capture file' ($filesAfter -eq $filesBefore) "files before: $filesBefore, after: $filesAfter"
    $after = Get-CardImage $p (Find-Main $p) $cardName 'LibraryEdit-after'
    $diff = Get-Diff $before $after
    Check 'card thumbnail shows the edit' ($diff -gt 30) "mean pixel difference: $diff"
    $previewImage = Get-PreviewImage $p (Find-Main $p) 'LibraryEdit-after'
    $previewDiff = Get-Diff $previewBefore $previewImage
    Check 'selected preview shows the edit' ($previewDiff -gt 30) "mean pixel difference: $previewDiff"
    $log = (Get-ChildItem (Join-Path $Profile 'Logs') -Filter *.txt -Recurse | Get-Content -Raw) -join "`n"
    Check 'no exception in log' ($log -notmatch 'Exception:') ''
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    [System.Windows.Forms.Clipboard]::Clear()
}

$reportPath = Join-Path $Out 'LibraryEdit.txt'
$report | Set-Content $reportPath
$report
"Report: $reportPath"
