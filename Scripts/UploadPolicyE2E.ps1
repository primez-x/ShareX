# End-to-end check for UploadPolicy (uploads allowed only to OneDrive and Shared folder).
# Build first: dotnet build ShareX/ShareX.csproj
# Run:         powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/UploadPolicyE2E.ps1
# The script copies the Debug build to %LOCALAPPDATA%\Temp\sharex-e2e and runs the build in portable mode,
# so the real ShareX profile is not touched. A ShareX window opens for about 15 seconds for the screenshot.
# Output: %LOCALAPPDATA%\Temp\sharex-e2e\out\ (report.txt, sharex-log.txt, destination-settings.png).
param([string]$Repo = (Split-Path $PSScriptRoot -Parent))

$ErrorActionPreference = 'Stop'
$root = Join-Path $env:LOCALAPPDATA 'Temp\sharex-e2e'
$app = Join-Path $root 'app'
$out = Join-Path $root 'out'
$profileDir = Join-Path $app 'ShareX'
$exe = Join-Path $app 'ShareX.exe'

if (Test-Path $root) { Remove-Item -Recurse -Force $root }
New-Item -ItemType Directory -Force $app, $out, $profileDir | Out-Null
Copy-Item (Join-Path $Repo 'ShareX\bin\Debug\*') $app -Recurse

# Legacy settings: public hosts, upload after capture, URL shortener after upload.
$legacyApp = @'
{
  "DefaultTaskSettings": {
    "AfterCaptureJob": "CopyImageToClipboard, SaveImageToFile, UploadImageToHost",
    "AfterUploadJob": "CopyURLToClipboard, UseURLShortener, ShareURL",
    "ImageDestination": "Imgur",
    "ImageFileDestination": "Dropbox",
    "TextDestination": "Pastebin",
    "TextFileDestination": "Dropbox",
    "FileDestination": "Dropbox",
    "URLShortenerDestination": "BITLY",
    "UploadSettings": { "ClipboardUploadShortenURL": true, "ClipboardUploadShareURL": true },
    "AdvancedSettings": { "AutoShortenURLLength": 20 }
  },
  "ShowTray": true,
  "ShowStartScreen": false
}
'@
$legacyHotkeys = @'
{
  "Hotkeys": [
    {
      "TaskSettings": {
        "Description": "Legacy Imgur hotkey",
        "Job": "PrintScreen",
        "UseDefaultAfterCaptureJob": false,
        "AfterCaptureJob": "SaveImageToFile, UploadImageToHost",
        "UseDefaultDestinations": false,
        "ImageDestination": "Imgur",
        "FileDestination": "MediaFire"
      }
    }
  ]
}
'@
$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $profileDir 'ApplicationConfig.json'), $legacyApp, $utf8)
[IO.File]::WriteAllText((Join-Path $profileDir 'HotkeysConfig.json'), $legacyHotkeys, $utf8)

$png = Join-Path $out 'upload-test.png'
Add-Type -AssemblyName System.Drawing
if (-not (Test-Path $png)) {
    $bmp = New-Object System.Drawing.Bitmap 64, 64
    $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}
$nm = Join-Path $out 'shorten.json'
[IO.File]::WriteAllText($nm, '{"Action":"ShortenURL","URL":"https://example.com/a-long-url"}', $utf8)

function Invoke-ShareX([string[]]$arguments, [int]$timeoutSec = 90) {
    $p = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $app -PassThru
    if (-not $p.WaitForExit($timeoutSec * 1000)) {
        Stop-Process -Id $p.Id -Force
        return "TIMEOUT"
    }
    return "exit=$($p.ExitCode)"
}

$report = New-Object System.Collections.Generic.List[string]
$report.Add("Run 1 (legacy config, upload PNG): " + (Invoke-ShareX @("`"$png`"", '-portable', '-silent', '-AutoClose')))
$report.Add("Run 2 (native messaging ShortenURL): " + (Invoke-ShareX @('-portable', '-silent', '-AutoClose', '-NativeMessagingInput', "`"$nm`"")))

$appJson = Get-Content (Join-Path $profileDir 'ApplicationConfig.json') -Raw | ConvertFrom-Json
$hkJson = Get-Content (Join-Path $profileDir 'HotkeysConfig.json') -Raw | ConvertFrom-Json
$d = $appJson.DefaultTaskSettings
$h = ($hkJson.Hotkeys | Where-Object { $_.TaskSettings.Description -eq 'Legacy Imgur hotkey' }).TaskSettings
$log = Get-ChildItem (Join-Path $profileDir 'Logs') -Filter *.txt -Recurse | Get-Content -Raw

function Check([string]$name, [bool]$ok, [string]$detail) {
    $script:report.Add(("{0} {1} :: {2}" -f ($(if ($ok) { 'PASS' } else { 'FAIL' })), $name, $detail))
}

Check 'default image destination' ($d.ImageDestination -eq 'FileUploader' -and $d.ImageFileDestination -eq 'OneDrive') "$($d.ImageDestination)/$($d.ImageFileDestination)"
Check 'default text destination' ($d.TextDestination -eq 'FileUploader' -and $d.TextFileDestination -eq 'OneDrive') "$($d.TextDestination)/$($d.TextFileDestination)"
Check 'default file destination' ($d.FileDestination -eq 'OneDrive') "$($d.FileDestination)"
Check 'upload after capture removed' ($d.AfterCaptureJob -notmatch 'UploadImageToHost') "$($d.AfterCaptureJob)"
Check 'shortener/share after upload removed' ($d.AfterUploadJob -notmatch 'UseURLShortener|ShareURL') "$($d.AfterUploadJob)"
Check 'clipboard shorten/share settings gone' ($null -eq $d.UploadSettings.ClipboardUploadShortenURL -and $null -eq $d.UploadSettings.ClipboardUploadShareURL) "shorten=$($d.UploadSettings.ClipboardUploadShortenURL) share=$($d.UploadSettings.ClipboardUploadShareURL)"
Check 'auto shorten setting gone' ($null -eq $d.AdvancedSettings.AutoShortenURLLength) "$($d.AdvancedSettings.AutoShortenURLLength)"
Check 'hotkey image destination' ($h.ImageDestination -eq 'FileUploader' -and $h.FileDestination -eq 'OneDrive') "$($h.ImageDestination)/$($h.FileDestination)"
Check 'hotkey upload after capture removed' ($h.AfterCaptureJob -notmatch 'UploadImageToHost') "$($h.AfterCaptureJob)"
Check 'PNG upload went to OneDrive, not a public host' ($log -match 'OneDrive configuration is invalid') 'log contains OneDrive invalid-config message'
Check 'no public host in log' ($log -notmatch '(?i)https?://[^\s]*(imgur\.com|dropboxapi\.com|pastebin\.com|bit\.ly|bitly\.com)') 'no request URL to imgur/dropbox/pastebin/bitly logged'
Check 'no exception logged' ($log -notmatch 'Exception:') 'log has no exception'
Check 'URL shortener blocked' ($log -match 'is blocked\. This build uploads only to OneDrive or a shared folder\.') 'log contains policy block message'

# Run 3: upload again without auto close. OneDrive is not connected, so Destination settings opens.
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class Win {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    public static List<IntPtr> For(uint pid) {
        var list = new List<IntPtr>();
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }
}
'@

$p = Start-Process -FilePath $exe -ArgumentList @("`"$png`"", '-portable', '-silent') -WorkingDirectory $app -PassThru
Start-Sleep -Seconds 15
$i = 0
foreach ($h in [Win]::For([uint32]$p.Id)) {
    $r = New-Object Win+RECT
    [void][Win]::GetWindowRect($h, [ref]$r)
    $w = $r.R - $r.L; $hgt = $r.B - $r.T
    if ($w -lt 200 -or $hgt -lt 150) { continue }
    $bmp = New-Object System.Drawing.Bitmap $w, $hgt
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [void][Win]::PrintWindow($h, $hdc, 2)
    $g.ReleaseHdc($hdc); $g.Dispose()
    $file = Join-Path $out ("destination-settings{0}.png" -f $(if ($i++ -eq 0) { '' } else { "-$i" }))
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    $report.Add("Screenshot: $file ($w x $hgt)")
}
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }

$reportPath = Join-Path $out 'report.txt'
$report | Set-Content -Encoding utf8 $reportPath
Copy-Item (Get-ChildItem (Join-Path $profileDir 'Logs') -Filter *.txt -Recurse | Select-Object -First 1).FullName (Join-Path $out 'sharex-log.txt')
$report
"Report: $reportPath"
