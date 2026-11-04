# Builds publish\AlienHelper.exe from src\AlienHelper (single self-contained exe, no .NET runtime needed on the target).
# -Out picks another folder, e.g. "publish-dev" while the app in publish\ is running.
# -Channel prod|test; by default test on the test branches (see channel.ps1), and then the exe is AlienHelperTest.exe.
param([string]$Out = 'publish', [string]$Channel)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\channel.ps1"
"channel: $Channel ($AppKey.exe)"
# Prefer a user-local SDK (dotnet-install.ps1), else whatever dotnet is on PATH.
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
& $dotnet publish "$PSScriptRoot\src\AlienHelper\AlienHelper.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:Channel=$Channel `
    -o "$PSScriptRoot\$Out"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
# the plugin API for plugin authors (the exe has its own copy inside): <out>\sdk
& $dotnet build "$PSScriptRoot\src\AlienHelper.PluginApi\AlienHelper.PluginApi.csproj" -c Release -o "$PSScriptRoot\$Out\sdk" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "building the plugin API failed ($LASTEXITCODE)" }
# ship the license notices next to the exe
Copy-Item "$PSScriptRoot\THIRD-PARTY-NOTICES.txt" "$PSScriptRoot\$Out\" -Force
New-Item -ItemType Directory -Force "$PSScriptRoot\$Out\licenses" | Out-Null
Copy-Item "$PSScriptRoot\licenses\*" "$PSScriptRoot\$Out\licenses\" -Force
