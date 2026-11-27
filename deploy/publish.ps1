<#
.SYNOPSIS
  Builds a release package of Dhan Jagrati: API, web UI and database scripts.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File deploy\publish.ps1
  # output: publish\api, publish\web, publish\db
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\publish')
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$Output = [IO.Path]::GetFullPath($Output)

Write-Host "==> Cleaning $Output"
if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Output | Out-Null

Write-Host '==> Unit tests'
dotnet test (Join-Path $root 'tests\DhanJagrati.Core.Tests') --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed.' }

Write-Host '==> Publishing API'
dotnet publish (Join-Path $root 'src\DhanJagrati.Api\DhanJagrati.Api.csproj') --configuration Release --output (Join-Path $Output 'api')
if ($LASTEXITCODE -ne 0) { throw 'API publish failed.' }
# Never ship a developer connection string: production reads ConnectionStrings__DhanJagrati from the environment.
$settings = Join-Path $Output 'api\appsettings.json'
$json = Get-Content $settings -Raw | ConvertFrom-Json
if ($json.PSObject.Properties.Name -contains 'ConnectionStrings') { $json.PSObject.Properties.Remove('ConnectionStrings') }
$json | ConvertTo-Json -Depth 10 | Set-Content $settings -Encoding utf8
Remove-Item (Join-Path $Output 'api\appsettings.Development.json') -ErrorAction SilentlyContinue

Write-Host '==> Building web UI'
Push-Location (Join-Path $root 'src\DhanJagrati.Web')
try {
    npm ci
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Web build failed.' }
} finally { Pop-Location }
Copy-Item (Join-Path $root 'src\DhanJagrati.Web\dist') (Join-Path $Output 'web') -Recurse

Write-Host '==> Database scripts'
New-Item -ItemType Directory -Force -Path (Join-Path $Output 'db\patches') | Out-Null
Copy-Item (Join-Path $root 'scheema\dhanjagrati.sql') (Join-Path $Output 'db')
Copy-Item (Join-Path $root 'scheema\dhanjagrati_patches\*.sql') (Join-Path $Output 'db\patches')

Write-Host "==> Done: $Output"
