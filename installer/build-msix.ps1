# Builds installer\Output\AlienHelper-<version>.msix (for the Microsoft Store or sideloading).
# Needs makeappx.exe: Windows SDK, or the NuGet package Microsoft.Windows.SDK.BuildTools
# (dotnet restore of installer\msix\tools.proj puts it into the NuGet cache).
#
# The Store signs the package itself, so -SignCmd is only for sideloading; $f stands for the file and the
# certificate subject must equal Identity/@Publisher in Package.appxmanifest, e.g.
#   -SignCmd 'signtool.exe sign /fd sha256 /f my.pfx /p <password> $f'
param([string]$SignCmd)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$msix = "$PSScriptRoot\msix"

$makeappx = @(
    Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue
    Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools\*\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue
) | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $makeappx) {
    $dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
    if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
    & $dotnet restore "$msix\tools.proj"
    if ($LASTEXITCODE -ne 0) { throw "restore of Microsoft.Windows.SDK.BuildTools failed ($LASTEXITCODE)" }
    $makeappx = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools\*\bin\*\x64\makeappx.exe" |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}

& "$root\build.ps1" -Out 'installer\app'

# MSIX versions have four parts
$version = ([xml](Get-Content "$root\src\AlienHelper\AlienHelper.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$msixVersion = (($version.Split('.') + @('0', '0', '0', '0'))[0..3]) -join '.'

$layout = "$msix\layout"
if (Test-Path $layout) { Remove-Item -Recurse -Force $layout }
New-Item -ItemType Directory -Force "$layout\Assets" | Out-Null
Copy-Item "$PSScriptRoot\app\AlienHelper.exe" $layout
Copy-Item "$root\src\AlienHelper\Assets\Msix\*.png" "$layout\Assets"
Copy-Item "$root\LICENSE" "$layout\LICENSE.txt"
Copy-Item "$root\NOTICE.md" "$layout\NOTICE.txt"
(Get-Content "$msix\Package.appxmanifest" -Raw).Replace('$VERSION$', $msixVersion) |
    Set-Content "$layout\AppxManifest.xml" -Encoding UTF8

if ($SignCmd) {
    & cmd.exe /c $SignCmd.Replace('$f', "`"$layout\AlienHelper.exe`"")
    if ($LASTEXITCODE -ne 0) { throw "signing AlienHelper.exe failed ($LASTEXITCODE)" }
}

New-Item -ItemType Directory -Force "$PSScriptRoot\Output" | Out-Null
$out = "$PSScriptRoot\Output\AlienHelper-$version.msix"
& $makeappx pack /o /d $layout /p $out
if ($LASTEXITCODE -ne 0) { throw "makeappx failed ($LASTEXITCODE)" }

if ($SignCmd) {
    & cmd.exe /c $SignCmd.Replace('$f', "`"$out`"")
    if ($LASTEXITCODE -ne 0) { throw "signing the package failed ($LASTEXITCODE)" }
}
"wrote $out"
