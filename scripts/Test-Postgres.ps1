param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\17\bin',
    [ValidateRange(1024, 65535)][int]$Port = 55434,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$TestFilter
)
$ErrorActionPreference = 'Stop'
if ($Port -eq 5432) { throw 'Port 5432 is reserved for the existing database; choose a test port.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = [IO.Path]::GetFullPath((Join-Path $repository '.local'))
$runRoot = [IO.Path]::GetFullPath((Join-Path $testRoot ('postgres-' + [Guid]::NewGuid().ToString('N'))))
if (-not $runRoot.StartsWith($testRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Temporary PostgreSQL path escaped the test workspace.'
}
foreach ($tool in @('initdb.exe', 'pg_ctl.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PostgresBin $tool) -PathType Leaf)) {
        throw "PostgreSQL 17 test binary $tool not found. Set -PostgresBin to the installation bin folder."
    }
}
New-Item -ItemType Directory -Path $runRoot | Out-Null
$dataPath = Join-Path $runRoot 'data'
$passwordPath = Join-Path $runRoot 'password'
$logPath = Join-Path $runRoot 'postgres.log'
$testPassword = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$previousConnection = [Environment]::GetEnvironmentVariable('MIXPILOT_TEST_POSTGRES', 'Process')
$startAttempted = $false
$testExitCode = 1
Push-Location $repository
try {
    [IO.File]::WriteAllText($passwordPath, $testPassword, [Text.UTF8Encoding]::new($false))
    & (Join-Path $PostgresBin 'initdb.exe') -D $dataPath -U mixpilot_test --encoding=UTF8 --locale=C --auth-host=scram-sha-256 --auth-local=scram-sha-256 --pwfile=$passwordPath
    if ($LASTEXITCODE -ne 0) { throw 'Isolated PostgreSQL initialization failed.' }
    # Do not pipe pg_ctl: the server inherits its output handles.
    $startAttempted = $true
    & (Join-Path $PostgresBin 'pg_ctl.exe') -D $dataPath -l $logPath -o "-h 127.0.0.1 -p $Port" -w start
    if ($LASTEXITCODE -ne 0) { throw 'Isolated PostgreSQL start failed. Check the local log and test port availability.' }
    $env:MIXPILOT_TEST_POSTGRES = "Host=127.0.0.1;Port=$Port;Database=postgres;Username=mixpilot_test;Password=$testPassword;Pooling=false"
    $testArguments = @('test', 'MixPilot.sln', '-c', $Configuration)
    if ($TestFilter) { $testArguments += @('--filter', $TestFilter) }
    & dotnet @testArguments
    $testExitCode = $LASTEXITCODE
}
finally {
    [Environment]::SetEnvironmentVariable('MIXPILOT_TEST_POSTGRES', $previousConnection, 'Process')
    try {
        if ($startAttempted) {
            & (Join-Path $PostgresBin 'pg_ctl.exe') -D $dataPath -m fast -w stop
            if ($LASTEXITCODE -ne 0) { Write-Warning 'Test cluster stop failed; inspect its local log.'; $testExitCode = 1 }
        }
    }
    finally {
        try {
            if (Test-Path -LiteralPath $passwordPath -PathType Leaf) { Remove-Item -LiteralPath $passwordPath }
        }
        finally { Pop-Location }
    }
}
# Keep only this run's data/logs in the ignored .local folder for diagnosis.
exit $testExitCode
