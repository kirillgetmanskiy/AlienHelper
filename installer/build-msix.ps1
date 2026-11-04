# Builds installer\Output\AlienHelper-<version>.msix (for the Microsoft Store or sideloading).
# Needs makeappx.exe: Windows SDK, or the NuGet package Microsoft.Windows.SDK.BuildTools
# (dotnet restore of installer\msix\tools.proj puts it into the NuGet cache).
#
# The Store signs the package itself, so -SignCmd is only for sideloading; $f stands for the file and the
# certificate subject must equal Identity/@Publisher in Package.appxmanifest, e.g.
#   -SignCmd 'signtool.exe sign /fd sha256 /f my.pfx /p <password> $f'
#
# -Channel prod|test: by default test on the test branches (see channel.ps1). The test package has its own identity
# (35916LobsterSoft.AlienHelperTest, "Alien Helper Test"), so it is for sideloading only and has to be signed.
#
# -Plugins: a folder whose subfolders are plugins to ship inside the package as Plugins\<id> (sold as license-only
# add-ons: the Store build loads one only while the user owns it). By default VideoCap's Store bundle next to this
# repository (..\AlienHelper-VideoCap\publish\store-bundle, built by its build-store.ps1 -Bundle), when it is there.
param([string]$SignCmd, [string]$Channel, [string]$Plugins)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
. "$root\channel.ps1"
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

& "$root\build.ps1" -Out 'installer\app' -Channel $Channel

# MSIX versions have four parts
$version = ([xml](Get-Content "$root\src\AlienHelper\AlienHelper.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$msixVersion = (($version.Split('.') + @('0', '0', '0', '0'))[0..3]) -join '.'

$layout = "$msix\layout"
if (Test-Path $layout) { Remove-Item -Recurse -Force $layout }
New-Item -ItemType Directory -Force "$layout\Assets" | Out-Null
Copy-Item "$PSScriptRoot\app\$AppKey.exe" $layout
Copy-Item "$root\src\AlienHelper\Assets\Msix\*.png" "$layout\Assets"
Copy-Item "$root\LICENSE" "$layout\LICENSE.txt"
Copy-Item "$root\NOTICE.md" "$layout\NOTICE.txt"
Copy-Item "$PSScriptRoot\app\THIRD-PARTY-NOTICES.txt" $layout
Copy-Item "$PSScriptRoot\app\licenses" $layout -Recurse
if (-not $Plugins) { $Plugins = Join-Path (Split-Path $root) 'AlienHelper-VideoCap\publish\store-bundle' }
if (Test-Path $Plugins) {
    foreach ($p in Get-ChildItem $Plugins -Directory) {
        if (-not (Test-Path "$($p.FullName)\plugin.json")) { throw "$($p.FullName): no plugin.json" }
        # the app shares its own WinRT runtime and projection with plugins (PluginLoadContext): no second copy
        $to = New-Item -ItemType Directory -Force "$layout\Plugins\$($p.Name)"
        Get-ChildItem $p.FullName | Where-Object Name -notin 'WinRT.Runtime.dll', 'Microsoft.Windows.SDK.NET.dll' |
            Copy-Item -Destination $to -Recurse
        Write-Host "bundled plugin $($p.Name) $((Get-Content "$($p.FullName)\plugin.json" -Raw | ConvertFrom-Json).version)"
    }
} else { Write-Host "no plugins to bundle ($Plugins)" }
$manifest = (Get-Content "$msix\Package.appxmanifest" -Raw).Replace('$VERSION$', $msixVersion)
# the test package: every AlienHelper name (identity, app id, exe, alias, startup task) and the shown name get "Test"
if ($Channel -eq 'test') { $manifest = $manifest.Replace('AlienHelper', 'AlienHelperTest').Replace('Alien Helper', 'Alien Helper Test') }
$manifest | Set-Content "$layout\AppxManifest.xml" -Encoding UTF8

# resources.pri: without it Windows ignores the size-specific logos (Assets\*.targetsize-*.png) and draws the 44 px one
# on a plate in the taskbar
$makepri = Join-Path (Split-Path $makeappx) 'makepri.exe'
$priConfig = "$msix\priconfig.xml"
& $makepri createconfig /cf $priConfig /dq en-US /pv 10.0.0 /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makepri createconfig failed ($LASTEXITCODE)" }
& $makepri new /pr $layout /cf $priConfig /mn "$layout\AppxManifest.xml" /of "$layout\resources.pri" /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makepri new failed ($LASTEXITCODE)" }
Remove-Item $priConfig

if ($SignCmd) {
    & cmd.exe /c $SignCmd.Replace('$f', "`"$layout\$AppKey.exe`"")
    if ($LASTEXITCODE -ne 0) { throw "signing AlienHelper.exe failed ($LASTEXITCODE)" }
}

New-Item -ItemType Directory -Force "$PSScriptRoot\Output" | Out-Null
$out = "$PSScriptRoot\Output\$AppKey-$version.msix"
& $makeappx pack /o /d $layout /p $out
if ($LASTEXITCODE -ne 0) { throw "makeappx failed ($LASTEXITCODE)" }

if ($SignCmd) {
    & cmd.exe /c $SignCmd.Replace('$f', "`"$out`"")
    if ($LASTEXITCODE -ne 0) { throw "signing the package failed ($LASTEXITCODE)" }
}
"wrote $out"
