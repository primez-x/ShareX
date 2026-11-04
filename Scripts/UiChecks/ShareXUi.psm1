# Helpers to drive the ShareX Debug build in an isolated portable profile and inspect windows.
# Build first: dotnet build ShareX/ShareX.csproj. The profile lives in %LOCALAPPDATA%\Temp\sharex-e2e; screenshots go to its ui folder.
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class SxWin {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    public static List<IntPtr> For(uint pid) {
        var list = new List<IntPtr>();
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }
    public static string Title(IntPtr h) { var s = new System.Text.StringBuilder(256); GetWindowText(h, s, 256); return s.ToString(); }
}
'@
[void][SxWin]::SetProcessDPIAware()

$script:Root = Join-Path $env:LOCALAPPDATA 'Temp\sharex-e2e'
$script:App = Join-Path $script:Root 'app'
$script:Profile = Join-Path $script:App 'ShareX'
$script:Out = Join-Path $script:Root 'ui'

function Reset-SxApp([string]$Repo = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent), [string]$ConfigJson = '{"ShowStartScreen": false}') {
    if (Test-Path $script:Profile) { Remove-Item -Recurse -Force $script:Profile }
    New-Item -ItemType Directory -Force $script:App, $script:Profile, $script:Out | Out-Null
    Copy-Item (Join-Path $Repo 'ShareX\bin\Debug\*') $script:App -Recurse -Force
    # Pre-answer the one-time Print Screen question, so the question does not block unattended checks.
    # Mark Win+Shift+S as added, so the test copy does not take Win+Shift+S from the user. Checks that need the shortcut set the value to false.
    $config = $ConfigJson | ConvertFrom-Json
    $config | Add-Member -NotePropertyName PrintScreenKeyQuestionAsked -NotePropertyValue $true -Force
    if ($null -eq $config.SnippingToolHotkeyAdded) { $config | Add-Member -NotePropertyName SnippingToolHotkeyAdded -NotePropertyValue $true }
    [IO.File]::WriteAllText((Join-Path $script:Profile 'ApplicationConfig.json'), ($config | ConvertTo-Json -Depth 10))
    # No hotkeys by default: a running ShareX already owns Print Screen, and a clash opens a blocking warning. Checks that need hotkeys write their own.
    [IO.File]::WriteAllText((Join-Path $script:Profile 'HotkeysConfig.json'), '{"Hotkeys": []}')
}

function Start-SxApp([string[]]$Arguments = @('-portable')) {
    # -multi keeps the test app separate. Without -multi a running ShareX receives the arguments and runs them on the real profile.
    if ($Arguments -notcontains '-multi') { $Arguments += '-multi' }
    Start-Process -FilePath (Join-Path $script:App 'ShareX.exe') -ArgumentList $Arguments -WorkingDirectory $script:App -PassThru
}

function Get-SxWindows($Process) {
    foreach ($h in [SxWin]::For([uint32]$Process.Id)) {
        $r = New-Object SxWin+RECT
        [void][SxWin]::GetWindowRect($h, [ref]$r)
        [pscustomobject]@{ Handle = $h; Title = [SxWin]::Title($h); Left = $r.L; Top = $r.T; Width = $r.R - $r.L; Height = $r.B - $r.T }
    }
}

function Save-SxWindow($Window, [string]$Name) {
    $bmp = New-Object System.Drawing.Bitmap $Window.Width, $Window.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [void][SxWin]::PrintWindow($Window.Handle, $hdc, 2)
    $g.ReleaseHdc($hdc); $g.Dispose()
    $file = Join-Path $script:Out "$Name.png"
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    $file
}

function Get-SxButtons($Process) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $Process.Id)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Wait-SxButton($Process, [string]$Name, [int]$TimeoutSec = 20) {
    for ($t = 0; $t -lt $TimeoutSec * 2; $t++) {
        $b = Get-SxButtons $Process | Where-Object { $_.Current.Name -eq $Name } | Select-Object -First 1
        if ($b) { return $b }
        Start-Sleep -Milliseconds 500
    }
    $null
}

function Invoke-SxButton($Element) {
    $pattern = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { $pattern.Invoke() }
    else { $Element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
}

Export-ModuleMember -Function * -Variable Root, App, Profile, Out
