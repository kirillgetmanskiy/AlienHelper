# Builds publish\AlienHelper.exe from src\AlienHelper (single self-contained exe, no .NET runtime needed on the target).
# -Out picks another folder, e.g. "publish-dev" while the app in publish\ is running.
param([string]$Out = 'publish')
$ErrorActionPreference = 'Stop'
# Prefer a user-local SDK (dotnet-install.ps1), else whatever dotnet is on PATH.
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
& $dotnet publish "$PSScriptRoot\src\AlienHelper\AlienHelper.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none `
    -o "$PSScriptRoot\$Out"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
# ship the license notices next to the exe
Copy-Item "$PSScriptRoot\THIRD-PARTY-NOTICES.txt" "$PSScriptRoot\$Out\" -Force
New-Item -ItemType Directory -Force "$PSScriptRoot\$Out\licenses" | Out-Null
Copy-Item "$PSScriptRoot\licenses\*" "$PSScriptRoot\$Out\licenses\" -Force
