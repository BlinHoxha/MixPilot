param([string]$FrameworkRoot = (Join-Path $PSScriptRoot '../../Framework'))
$ErrorActionPreference = 'Stop'
$previousValue = $env:MIXPILOT_EXPORT_OPENAPI
Push-Location (Join-Path $PSScriptRoot '..')
try {
    $env:MIXPILOT_EXPORT_OPENAPI = '1'
    dotnet test tests/MixPilot.Tests --filter 'FullyQualifiedName~OpenApi_contains_shared_contracts' "-p:FrameworkRoot=$FrameworkRoot"
    if ($LASTEXITCODE -ne 0) { throw 'OpenAPI export failed.' }
} finally {
    $env:MIXPILOT_EXPORT_OPENAPI = $previousValue
    Pop-Location
}
