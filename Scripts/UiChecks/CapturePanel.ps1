# Runtime check: capture panel buttons, position, themes, hiding during capture and while the editor is open.
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ShareXUi.psm1') -Force
Add-Type -AssemblyName System.Windows.Forms

function Find-Panel($p) { Get-SxWindows $p | Where-Object { $_.Title -like '*Capture panel*' } | Select-Object -First 1 }

# Dark theme run
# The panel hides while the editor is open, so this check turns on "Open the editor after capture".
Reset-SxApp -ConfigJson '{"ShowStartScreen": false, "DefaultTaskSettings": {"OpenEditorAfterCapture": true}}'
$p = Start-SxApp @('-portable', '-silent')
$region = Wait-SxButton $p 'Region'
if (-not $region) { Stop-Process -Id $p.Id -Force; throw 'Region button not found' }
Start-Sleep -Seconds 1
$names = (Get-SxButtons $p | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) -join ', '
"Panel buttons: $names"
$panel = Find-Panel $p
$wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$centerOffset = [math]::Abs(($panel.Left + $panel.Width / 2) - ($wa.Left + $wa.Width / 2))
"Panel rect: $($panel.Left),$($panel.Top) $($panel.Width)x$($panel.Height); work area top=$($wa.Top); center offset=$centerOffset px"
"Panel at top center: $($centerOffset -le 2 -and ($panel.Top - $wa.Top) -ge 0 -and ($panel.Top - $wa.Top) -le 30)"
$panelShot = Save-SxWindow $panel 'panel-dark'
"Saved $panelShot"

# Full screen capture from the panel; the editor opens on the saved file.
Invoke-SxButton (Wait-SxButton $p 'Full screen')
$close = Wait-SxButton $p 'Close' 30
$editorOpen = (Get-SxWindows $p | Where-Object { $_.Title -like '*Image Editor*' }) -ne $null
"Editor open: $editorOpen"
"Panel hidden while editor open: $((Find-Panel $p) -eq $null)"

$file = Get-ChildItem (Join-Path $Profile 'Screenshots') -Recurse -File | Select-Object -First 1
$vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
$cap = [System.Drawing.Bitmap]::FromFile($file.FullName)
$ref = [System.Drawing.Bitmap]::FromFile($panelShot)
$diff = 0; $n = 0
for ($x = 4; $x -lt $panel.Width - 4; $x += 6) {
    for ($y = 4; $y -lt $panel.Height - 4; $y += 6) {
        $a = $cap.GetPixel($panel.Left - $vs.Left + $x, $panel.Top - $vs.Top + $y); $b = $ref.GetPixel($x, $y)
        $diff += [math]::Abs($a.R - $b.R) + [math]::Abs($a.G - $b.G) + [math]::Abs($a.B - $b.B); $n++
    }
}
$cap.Dispose(); $ref.Dispose()
$meanDiff = [math]::Round($diff / $n / 3, 1)
"Mean pixel difference between the capture and the panel at the panel position: $meanDiff (near 0 would mean the panel was captured)"
"Panel not in capture: $($meanDiff -gt 15)"

# Close the editor; the panel returns.
$closeInTask = Get-SxButtons $p | Where-Object { $_.Current.Name -eq 'Close' -and $_.Current.HelpText -like '*original*' } | Select-Object -First 1
Invoke-SxButton $closeInTask
Start-Sleep -Seconds 3
"Panel back after editor closed: $((Find-Panel $p) -ne $null)"
Stop-Process -Id $p.Id -Force; [void]$p.WaitForExit(15000); Start-Sleep -Milliseconds 500
Remove-Item -Recurse -Force (Join-Path $Profile 'Screenshots')

# Light theme run
Reset-SxApp -ConfigJson '{"ShowStartScreen": false, "ThemeOptions": {"Theme": "Light"}}'
$p = Start-SxApp @('-portable', '-silent')
[void](Wait-SxButton $p 'Region')
Start-Sleep -Seconds 1
"Saved $(Save-SxWindow (Find-Panel $p) 'panel-light')"
Stop-Process -Id $p.Id -Force; [void]$p.WaitForExit(15000); Start-Sleep -Milliseconds 500
