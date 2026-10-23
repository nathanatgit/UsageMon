param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug', [switch]$Clean)
$ErrorActionPreference = 'Stop'
$environmentKeys = @('DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'APPDATA')
$previousEnvironment = @{}
foreach ($key in $environmentKeys) { $previousEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process') }
Push-Location $PSScriptRoot
try {
    $env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/cli'
    $env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools/packages'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:APPDATA = Join-Path $PSScriptRoot '.tools/appdata'
    New-Item -ItemType Directory -Force $env:APPDATA | Out-Null
    $localDotnet = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
    $dotnet = if (Test-Path $localDotnet) { $localDotnet } else { 'dotnet' }
    if ($Clean) {
        & $dotnet clean UsageMon.csproj -c $Configuration -p:Platform=x64
        if ($LASTEXITCODE -ne 0) { throw 'Clean failed.' }
    }
    & $dotnet restore UsageMon.csproj --configfile NuGet.Config -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw 'Package restore failed.' }
    & $dotnet build UsageMon.csproj --no-restore -c $Configuration -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
} finally {
    foreach ($key in $environmentKeys) { [Environment]::SetEnvironmentVariable($key, $previousEnvironment[$key], 'Process') }
    Pop-Location
}
