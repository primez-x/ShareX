# Runtime check: library grid, search, filters, preview actions, live update.
param([ValidateSet('Dark', 'Light')][string]$Theme = 'Dark', [switch]$LiveUpdate)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ShareXUi.psm1') -Force
$A = [System.Windows.Automation.AutomationElement]

function Get-Cards($p) { @(Get-SxButtons $p | Where-Object { $_.Current.Name -match '\.(png|txt|jpg)$' } | ForEach-Object { $_.Current.Name }) }
function Find-Main($p) { Get-SxWindows $p | Where-Object { $_.Title -like 'ShareX 2*' -or $_.Title -eq 'ShareX' } | Sort-Object { $_.Width * $_.Height } -Descending | Select-Object -First 1 }

Reset-SxApp -ConfigJson ('{"ShowStartScreen": false, "CapturePanelRunAtStartup": false, "ThemeOptions": {"Theme": "' + $Theme + '"}}')
& python -I (Join-Path $PSScriptRoot 'seed_library.py') $Profile 130
$p = Start-SxApp @('-portable')
if (-not (Wait-SxButton $p 'New capture' 30)) { Stop-Process -Id $p.Id -Force; throw 'New capture not found' }
Start-Sleep -Seconds 4
$main = Find-Main $p
"Main window: '$($main.Title)' $($main.Width)x$($main.Height)"
$cards = Get-Cards $p
"Cards realized: $($cards.Count); first: $($cards | Select-Object -First 3)"
"Newest first: $($cards[0] -eq 'notes.txt' -or $cards[0] -like 'Capture 130*')"
"Missing file hidden: $(-not ($cards -contains 'gone.png'))"
"Toolbar names present: $(@('New capture','Capture options','More','Settings','All','Images','Videos','Other files' | Where-Object { -not (Wait-SxButton $p $_ 2) }).Count -eq 0)"
[void](Save-SxWindow $main "library-$($Theme.ToLower())")

# Select a card, then read the preview actions.
Invoke-SxButton (Wait-SxButton $p 'Capture 130 screen.png' 5)
Start-Sleep -Seconds 2
$actions = 'Edit', 'Open', 'Copy', 'Copy file path', 'Show in folder', 'Delete' | Where-Object { Wait-SxButton $p $_ 2 }
"Preview actions: $($actions -join ', ')"
[void](Save-SxWindow $main "library-$($Theme.ToLower())-selected")

# Search
$editCond = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)),
    (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'Search captures')))
$search = $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $editCond)
$search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('INVOICE')
Start-Sleep -Seconds 2
$found = Get-Cards $p
"Search 'INVOICE': $($found.Count) cards, all match: $(@($found | Where-Object { $_ -notmatch 'invoice' }).Count -eq 0)"
[void](Save-SxWindow $main "library-$($Theme.ToLower())-search")
$search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('')
Start-Sleep -Seconds 2

# Filter
Invoke-SxButton (Wait-SxButton $p 'Other files')
Start-Sleep -Seconds 2
"Filter 'Other files': $((Get-Cards $p) -join ', ')"
Invoke-SxButton (Wait-SxButton $p 'All')
Start-Sleep -Seconds 2

if ($LiveUpdate) {
    # Capture in the same app from Capture options > Full screen. The capture saves without the editor, so the card appears when the task ends.
    Invoke-SxButton (Wait-SxButton $p 'Capture options')
    Start-Sleep -Seconds 1
    $menuCond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)),
        (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'Full screen')))
    $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $menuCond).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 6
    $after = Get-Cards $p
    $new = $after | Where-Object { $_ -notmatch '^Capture \d{3}|notes\.txt' }
    "Live update: new card(s): $($new -join ', ')"
}
Stop-Process -Id $p.Id -Force; [void]$p.WaitForExit(15000)
Get-ChildItem (Join-Path $Profile 'Screenshots') -Directory | Where-Object { $_.Name -ne 'seed' } | Remove-Item -Recurse -Force
