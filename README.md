# Dhan Jagrati Microfinance Foundation

Web replacement for the legacy desktop application (screens in `screen shot/`): **Master, Employee, Loan, Account and Help** sections, plus an optional Saving module.

- **API:** .NET 10 minimal API + Dapper (`src/DhanJagrati.Api`, `src/DhanJagrati.Data`, `src/DhanJagrati.Core`)
- **UI:** React 19 + TypeScript, Vite, Mantine 9, TanStack Query, React Hook Form + Zod (`src/DhanJagrati.Web`)
- **Database:** SQL Server 2017+ (`scheema/dhanjagrati.sql` + patches in `scheema/dhanjagrati_patches/`)

## Status

| Phase | Scope | State |
|---|---|---|
| 1 | Login, Change Password, Select Branch, all masters, loan plans with configurable EMI, day closing, settings | **Done** |
| 2 | Employee / Advisor (introducer chain), Customer (Member) with photo/signature, Daily Attendance, User Master | **Done** |
| 3 | Loan Application (calculator, deductions, guarantors, deposits), Loan Approve (schedule + disbursement voucher), Loan Card / Signature Card, dashboard, responsive redesign | **Done** |
| 4 | Single & bulk recovery (receipts, late fee, reversal), settlement (pre-close / re-open), Saving module (accounts, deposit/withdrawal, statement, recovery from saving) | **Done** |
| 5 | Groups & Ledgers (tree, opening balances), Ledger Transaction Journal (payment/receipt/journal/contra/branch transfer, cancel), Cash Book, Ledger Statement, Trial Balance, P&L, Balance Sheet (print + CSV) | **Done** |
| 6 | Loan reports (application, recovery due, recovery ledger, closure, employee-wise) with print + CSV; hardening (login rate limit, security headers, persistent keys, HTTPS-only cookies, proxy headers, code splitting); API + end-to-end tests; deployment kit | **Done** |

## Database setup

Run once against a new SQL Server (creates database `DhanJagrati`), then apply the patches in order. Use `-b` so an error stops the script:

```powershell
sqlcmd -S <server> -d master -U <login> -C -b -i scheema\dhanjagrati.sql
sqlcmd -S <server> -d DhanJagrati -U <login> -C -b -I -i scheema\dhanjagrati_patches\001_emi_config_saving_interbranch.sql
sqlcmd -S <server> -d DhanJagrati -U <login> -C -b -I -i scheema\dhanjagrati_patches\002_recovery_settlement.sql
```

Patches record themselves in `sec.SchemaVersion` and are skipped if already applied. The API requires all patches. Use `-I` (quoted identifiers on); tables with computed columns reject writes without it.

## Configuration

Connection string `ConnectionStrings:DhanJagrati`: keep credentials out of tracked files. Use user-secrets locally and the `ConnectionStrings__DhanJagrati` environment variable on servers:

```powershell
dotnet user-secrets set "ConnectionStrings:DhanJagrati" "Server=<server>;Database=DhanJagrati;User ID=<login>;Password=<password>;Encrypt=True;TrustServerCertificate=True;" --project src\DhanJagrati.Api
```

`TrustServerCertificate=True` is for local development only.

## First administrator (one-time)

The schema seeds the `SYSTEM` admin without a password. Set a temporary one from the server console (it is prompted for, never stored in config) and change it at first login:

```powershell
dotnet run --project src\DhanJagrati.Api -- create-admin --user SYSTEM
# published build:  dotnet DhanJagrati.Api.dll create-admin --user SYSTEM
```

The command refuses to overwrite an existing password; `--reset` is the recovery path for a locked-out or forgotten admin password.

## Run locally

```powershell
# Terminal 1 - API
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src\DhanJagrati.Api --no-launch-profile -- --urls http://localhost:5072

# Terminal 2 - UI (proxies /api to the API)
Set-Location src\DhanJagrati.Web
npm ci
npm run dev
```

Open http://localhost:5173 and sign in as `SYSTEM` with the temporary password. Health check: `Invoke-RestMethod http://localhost:5072/api/health`. OpenAPI document (Development): `http://localhost:5072/openapi/v1.json`.

## Tests and build

```powershell
dotnet test                                   # unit tests + API pipeline tests
dotnet build DhanJagrati.slnx -c Release
Set-Location src\DhanJagrati.Web; npm run build; npm run lint
```

**End-to-end test** (`tests/DhanJagrati.Api.Tests/LoanLifecycleTests.cs`): builds a throwaway database `DhanJagrati_Test_*` from the schema + patches, runs a full business day through the real API (login and forced password change, EMI setup, member, advisors, loan application, approval, recovery, reversal, settlement close/re-open, ledgers and vouchers, saving account, every report, day closing), checks that the trial balance and balance sheet balance, then drops the database. It never touches `DhanJagrati`. It is skipped unless `DJ_TEST_SQL` is set to a server connection whose login can `CREATE DATABASE`:

```powershell
$env:DJ_TEST_SQL = "Server=<server>;User ID=<login>;Password=<password>;Encrypt=True;TrustServerCertificate=True;"
dotnet test tests\DhanJagrati.Api.Tests
Remove-Item Env:DJ_TEST_SQL
```

## Key design points

- **Sessions:** cookie authentication (HttpOnly, SameSite=Strict). Branch and login date live in `sec.UserSession` on the server and are never taken from the browser. Back-dating is limited to Admin/Manager.
- **Masters:** one generic API (`/api/masters/{entity}`) and one React page driven by `MasterRegistry.cs` / `configs.tsx`. Center and Group are branch-scoped.
- **EMI calculation is configurable per loan plan** (`MANUAL`, `FLAT`, `REDUCING`, `FACTOR`, plus rounding and schedule split), because the legacy formula is not confirmed yet. Plans default to `MANUAL`. The legacy EPL-11 values fit `FACTOR = 0.111`. Each loan stores a snapshot of the method used.
- **Saving module:** separate `sav` schema, linked to loans only through nullable columns and ledger mappings; switched on with the `MODULE.SAVING.ENABLED` setting.
- **Inter-branch transfers:** post through `INTER BRANCH A/C` so each branch balances on its own (`acc.vw_UnbalancedVouchers` checks this per branch).

## Deployment (Windows Server / IIS)

1. **Package:** `powershell -ExecutionPolicy Bypass -File deploy\publish.ps1` runs the unit tests and produces `publish\api`, `publish\web` and `publish\db`. The packaged `appsettings.json` has the connection string removed.
2. **Server prerequisites:** .NET 10 ASP.NET Core Hosting Bundle, IIS with **URL Rewrite** and **Application Request Routing** (enable *proxy* in ARR server settings), SQL Server 2017+.
3. **Database:** run `publish\db\dhanjagrati.sql`, then every file in `publish\db\patches` in order (`sqlcmd ... -b -I`). Use a SQL login limited to the `DhanJagrati` database (`db_datareader`, `db_datawriter`, `EXECUTE`), not an owner or `sa`.
4. **API site:** an IIS site on `publish\api`, bound to **http://127.0.0.1:5072 only** (not reachable from the network). In its application pool (or the `web.config` `<environmentVariables>`), set:
   - `ConnectionStrings__DhanJagrati` = production connection string (with a trusted TLS certificate, no `TrustServerCertificate`)
   - optional `App__UploadPath` (member photos) and `App__KeysPath` (sign-in keys); default `App_Data\...` under the site. Give the app-pool identity modify rights there and **back them up**.
5. **First admin:** on the server, `dotnet publish\api\DhanJagrati.Api.dll create-admin --user SYSTEM` (with the same connection-string variable set), then change the password at first login.
6. **Web site:** the public HTTPS site on `publish\web`. Its `web.config` (from `src\DhanJagrati.Web\public`) proxies `/api/*` to the API, serves the SPA, and sets security headers and HSTS.
7. **Check:** `https://<site>/api/health` returns `{"status":"ok","database":true}`.

Built-in protections: per-account lockout (5 failures / 15 min) and per-IP login rate limit (10 / min), HTTPS-only `SameSite=Strict` session cookies with server-side sessions, `X-DJ-Client` header on all changes, security headers, forwarded-header support for real client IPs in the audit log, role checks on every endpoint, branch isolation from the session, and day-closing locks on postings.
