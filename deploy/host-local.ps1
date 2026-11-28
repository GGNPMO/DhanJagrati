<#
.SYNOPSIS
  Hosts Dhan Jagrati from this PC and publishes it on the internet through a Cloudflare Tunnel.

.DESCRIPTION
  1. Builds the React UI and copies it into the API (one site, one port).
  2. Publishes the API in Release and starts it in Production mode on http://localhost:<Port>
     (in its own window, using the connection string from src\DhanJagrati.Api\appsettings.json
     or the ConnectionStrings__DhanJagrati environment variable).
  3. Starts cloudflared: a quick tunnel (random https://*.trycloudflare.com address) or, with
     -TunnelName, your named tunnel on your own domain.

  Stop: close the cloudflared window (Ctrl+C) and the API window.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File deploy\host-local.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File deploy\host-local.ps1 -SkipBuild -TunnelName dhanjagrati
#>
param(
    [int]$Port = 5072,
    [switch]$SkipBuild,
    [string]$TunnelName
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$web = Join-Path $root 'src\DhanJagrati.Web'
$apiProject = Join-Path $root 'src\DhanJagrati.Api'
$out = Join-Path $root 'publish\live'
$data = Join-Path $apiProject 'App_Data'   # member photos + sign-in keys (same folder as development)

$cloudflared = (Get-Command cloudflared -ErrorAction SilentlyContinue).Source
if (-not $cloudflared) { $cloudflared = 'C:\Program Files (x86)\cloudflared\cloudflared.exe' }
if (-not (Test-Path $cloudflared)) { throw 'cloudflared not found. Install it with: winget install --id Cloudflare.cloudflared' }

if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
    throw "Port $Port is already in use (is the development API still running? stop it with Ctrl+C), or pass -Port <other>."
}

if (-not $SkipBuild) {
    Write-Host '==> Building the web UI' -ForegroundColor Cyan
    Push-Location $web
    try {
        if (-not (Test-Path 'node_modules')) { npm ci; if ($LASTEXITCODE) { throw 'npm ci failed' } }
        npm run build; if ($LASTEXITCODE) { throw 'Web build failed' }
    } finally { Pop-Location }

    Write-Host '==> Copying the UI into the API (wwwroot)' -ForegroundColor Cyan
    robocopy (Join-Path $web 'dist') (Join-Path $apiProject 'wwwroot') /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw 'Copying dist to wwwroot failed' }

    Write-Host '==> Publishing the API (Release)' -ForegroundColor Cyan
    dotnet publish (Join-Path $apiProject 'DhanJagrati.Api.csproj') --configuration Release --output $out
    if ($LASTEXITCODE) { throw 'API publish failed' }
}

Write-Host "==> Starting the application on http://localhost:$Port (Production)" -ForegroundColor Cyan
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = "http://localhost:$Port"
$env:App__UploadPath = Join-Path $data 'uploads'
$env:App__KeysPath = Join-Path $data 'keys'
$api = Start-Process dotnet -ArgumentList "`"$(Join-Path $out 'DhanJagrati.Api.dll')`"" -WorkingDirectory $out -PassThru

$healthy = $false
for ($i = 0; $i -lt 60 -and -not $api.HasExited; $i++) {
    try {
        $health = Invoke-RestMethod "http://localhost:$Port/api/health" -TimeoutSec 3
        if ($health.database) { $healthy = $true; break }
    } catch { Start-Sleep -Seconds 1 }
}
if (-not $healthy) { throw 'The application did not become healthy (database not reachable?). Check the API window for errors.' }
Write-Host "    Application is up: http://localhost:$Port" -ForegroundColor Green

Write-Host '==> Opening the Cloudflare tunnel (Ctrl+C here to stop publishing)' -ForegroundColor Cyan
if ($TunnelName) {
    & $cloudflared tunnel run $TunnelName
} else {
    Write-Host '    Look for the https://....trycloudflare.com address below and open it in any browser.' -ForegroundColor Yellow
    & $cloudflared tunnel --url "http://localhost:$Port"
}
