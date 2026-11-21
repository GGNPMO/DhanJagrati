# Dhan Jagrati Microfinance Ltd

A small web-first microfinance operations workspace. The solution uses a .NET 10 Minimal API, a React 19 + TypeScript client, and SQL Server persistence based on `scheema\microfinance_schema.sql`.

## Prerequisites

- .NET 10 SDK
- Node.js 22 or later and npm
- SQL Server 2017 or later (the reports use `STRING_AGG`)
- Windows PowerShell for the commands below

## Database setup

The API requires a SQL Server connection string named `Microfinance`. Keep credentials out of tracked configuration and configure a local .NET User Secret:

```powershell
dotnet user-secrets set "ConnectionStrings:Microfinance" "Server=ARUN\SA;Database=DhanJagrati_MFI;User ID=YOUR_SQL_LOGIN;Password=YOUR_SQL_PASSWORD;Encrypt=True;TrustServerCertificate=True;" --project src\DhanJagrati.Api\DhanJagrati.Api.csproj
```

Replace the login and password placeholders with a SQL login that can create/use the database and execute the schema. `TrustServerCertificate=True` is for local development only; production should use a valid SQL Server TLS certificate and a managed secret.

For a fresh database, run the bootstrap schema once from the repository root (SQLCMD prompts for the SQL password when `-P` is omitted):

```powershell
sqlcmd -S "ARUN\SA" -d master -U YOUR_SQL_LOGIN -C -b -i scheema\microfinance_schema.sql
sqlcmd -S "ARUN\SA" -d DhanJagrati_MFI -U YOUR_SQL_LOGIN -C -b -i scheema\migrations\001_sql_api_support.sql
```

The bootstrap creates `DhanJagrati_MFI`, schemas, tables, procedures, and seed records. Do not rerun it against an initialized or populated database. For an existing installation of the base schema, run only the idempotent API-support migration. Back up existing data before applying schema changes.

## Run locally

Configure the User Secret above before starting the API. Open two terminals from the repository root. Start the API:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src\DhanJagrati.Api\DhanJagrati.Api.csproj --no-launch-profile -- --urls http://localhost:5072
```

In the second terminal, install the frontend dependencies and start Vite:

```powershell
Set-Location src\DhanJagrati.Web
npm ci
npm run dev
```

Open the Vite URL (normally `http://localhost:5173`). The Vite development server proxies `/api` requests to the API at `http://localhost:5072`.

Check SQL Server connectivity and that the API is responding:

```powershell
Invoke-RestMethod http://localhost:5072/api/health
```

Stop either development server with `Ctrl+C`.

## Build and preview locally

From the repository root, build both applications in Release mode:

```powershell
dotnet build src\DhanJagrati.Api\DhanJagrati.Api.csproj --configuration Release
Set-Location src\DhanJagrati.Web
npm ci
npm run build
npm run preview
```

Vite writes the production frontend to `src\DhanJagrati.Web\dist`. Its preview server is for local review only; it is not a production web server. The API can be published separately:

```powershell
Set-Location ..\..
dotnet publish src\DhanJagrati.Api\DhanJagrati.Api.csproj --configuration Release --output publish\api
```

## Deployment outline (Windows Server / IIS)

The frontend calls relative `/api` URLs. Deploy both parts behind the same HTTPS origin so browser requests remain same-origin:

1. Build the React app and publish the API using the commands above.
2. Host the contents of `src\DhanJagrati.Web\dist` as the IIS website's static files.
3. Install the .NET 10 ASP.NET Core Hosting Bundle on the server. Run the published API on a loopback-only address (for example, `http://127.0.0.1:5072`) using IIS hosting or an approved Windows service/process manager.
4. Configure IIS URL Rewrite with Application Request Routing (ARR), or an equivalent reverse proxy, to forward `/api/*` to the API. Enable HTTPS for the public site; do not expose the API's loopback listener publicly.
5. Verify the public site's `/api/health`, then exercise the member, loan, collection, and report flows in a non-production environment.

The current Vite proxy is development-only; it is not included in the deployed static files. The API's CORS allowlist is also limited to localhost development origins. A same-origin reverse proxy avoids requiring browser CORS, but any cross-origin deployment needs an explicit, environment-specific CORS policy before release.

## Production readiness

The API uses SQL Server for reads and writes; it does not fall back to in-memory demo data. The current app still lacks authentication and server-side branch authorization, and it has not been hardened for production financial operations. Before production, implement access control and branch isolation, auditing, monitored database migrations, backups, and production-grade secret/TLS management. Verify all financial workflows against a non-production database first.

## Included workflows

- Branch overview with portfolio, due accounts and recent receipts.
- Member directory and member registration.
- Loan application creation, approval/decline, disbursement and active-account views.
- Single collections with receipt history.
- Balanced general-ledger voucher entry.
- Cashbook summary/detail, trial balance, profit & loss, balance sheet, application, recovery, relationship-manager and closure reports.
- Report date filters, CSV export and print layout.