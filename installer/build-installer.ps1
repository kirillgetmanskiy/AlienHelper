# Builds installer\app\AlienHelper.exe (its own folder, so a running copy in publish\ doesn't block it)
# and then installer\Output\AlienHelper-Setup-<version>.exe.
# Needs Inno Setup 6: winget install JRSoftware.InnoSetup
#
# -SignCmd signs the exe and the installer (required for the Microsoft Store); $f stands for the file, e.g.
#   -SignCmd 'signtool.exe sign /fd sha256 /tr http://timestamp.digicert.com /td sha256 /a $f'
# Silent install (the Store asks for it): AlienHelper-Setup-<version>.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
param([string]$SignCmd)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot

& "$root\build.ps1" -Out 'installer\app'

if ($SignCmd) {
    $cmd = $SignCmd.Replace('$f', "`"$PSScriptRoot\app\AlienHelper.exe`"")
    & cmd.exe /c $cmd
    if ($LASTEXITCODE -ne 0) { throw "signing AlienHelper.exe failed ($LASTEXITCODE)" }
}

$version = ([xml](Get-Content "$root\src\AlienHelper\AlienHelper.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it: winget install JRSoftware.InnoSetup' }

$isccArgs = @("/DAppVersion=$version")
if ($SignCmd) { $isccArgs += '/DSign', "/Ssigntool=$SignCmd" }
& $iscc @isccArgs "$PSScriptRoot\AlienHelper.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }
