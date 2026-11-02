param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$clickerCache = Join-Path $PSScriptRoot 'verification\build-cache'
$clickerEnvNames = @('DOTNET_CLI_HOME','DOTNET_CLI_TELEMETRY_OPTOUT','DOTNET_GENERATE_ASPNET_CERTIFICATE','DOTNET_ADD_GLOBAL_TOOLS_TO_PATH','NUGET_PACKAGES','APPDATA')
$clickerPrevious = @{}
foreach ($clickerEnvName in $clickerEnvNames) {
    $clickerPrevious[$clickerEnvName] = [Environment]::GetEnvironmentVariable($clickerEnvName, 'Process')
}
try {
    $env:DOTNET_CLI_HOME = Join-Path $clickerCache 'dotnet-home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    $env:NUGET_PACKAGES = Join-Path $clickerCache 'nuget'
    $env:APPDATA = Join-Path $clickerCache 'appdata'
    New-Item -ItemType Directory -Path $env:APPDATA -Force | Out-Null
    $clickerProject = Join-Path $PSScriptRoot 'TrayClicker.csproj'
    $clickerConfig = Join-Path $PSScriptRoot 'NuGet.Config'
    $clickerProperties = @('-p:PublishSingleFile=true','-p:EnableSingleFileAnalyzer=false','-p:SelfContained=false','-p:EnableRuntimePackDownload=false')
    & dotnet restore $clickerProject -r win-x64 --configfile $clickerConfig @clickerProperties
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось восстановить проект.' }
    & dotnet publish $clickerProject -c Release -r win-x64 --self-contained false --no-restore @clickerProperties -p:DebugType=None -p:DebugSymbols=false -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось собрать программу.' }
    Write-Host ('Готово: ' + (Join-Path $OutputDirectory 'TrayClicker.exe'))
}
finally {
    foreach ($clickerEnvName in $clickerEnvNames) {
        [Environment]::SetEnvironmentVariable($clickerEnvName, $clickerPrevious[$clickerEnvName], 'Process')
    }
}
