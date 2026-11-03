# More menu, Capture options dropdown and the Image history window on the shared gallery.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Import-Module (Join-Path $PSScriptRoot 'ShareXUi.psm1') -Force
$A = [System.Windows.Automation.AutomationElement]
function Get-MenuItems($p) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)),
        (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)))
    @($A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond) | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
}
Get-Process ShareX -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\Temp\sharex-e2e\app\*' } | ForEach-Object { Stop-Process -Id $_.Id -Force; $_.WaitForExit(10000) | Out-Null }
Reset-SxApp -ConfigJson '{"ShowStartScreen": false, "CapturePanelRunAtStartup": false}'
& python -I (Join-Path $PSScriptRoot 'seed_library.py') $Profile 40
$p = Start-SxApp @('-portable')
[void](Wait-SxButton $p 'New capture' 30)
Start-Sleep -Seconds 3

Invoke-SxButton (Wait-SxButton $p 'More')
Start-Sleep -Seconds 2
"More menu: $((Get-MenuItems $p) -join ', ')"
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1

Invoke-SxButton (Wait-SxButton $p 'Capture options')
Start-Sleep -Seconds 2
"Capture options: $((Get-MenuItems $p) -join ', ')"
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1

$second = Start-SxApp @('-portable', '-OpenImageHistory')
[void]$second.WaitForExit(15000)
Start-Sleep -Seconds 5
$ih = Get-SxWindows $p | Where-Object { $_.Title -like '*Image history*' } | Select-Object -First 1
"Image history window: $($ih -ne $null)"
if ($ih) {
    $cards = @(Get-SxButtons $p | Where-Object { $_.Current.Name -like 'Capture 0*' }).Count
    "Image history saved: $(Save-SxWindow $ih 'image-history')"
}
$log = Get-ChildItem (Join-Path $Profile 'Logs') -Filter *.txt -Recurse | Get-Content -Raw
"Exception in log: $($log -match 'Exception:')"
Stop-Process -Id $p.Id -Force; [void]$p.WaitForExit(15000)
