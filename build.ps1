$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build-home'
dotnet publish (Join-Path $PSScriptRoot 'src\Kroshka.csproj') -c Release --self-contained false -o (Join-Path $PSScriptRoot 'app') -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
