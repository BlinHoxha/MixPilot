param([string]$BaseUrl = 'http://localhost:5290', [string]$AccessToken)
$ErrorActionPreference = 'Stop'
$headers = @{}
if ($AccessToken) { $headers.Authorization = "Bearer $AccessToken" }
$csv = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../samples/library.csv'))
$body = @{ csv = $csv; authorizationConfirmed = $true } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/imports/csv" -Headers $headers -ContentType 'application/json' -Body $body
