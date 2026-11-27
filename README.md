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
| 4 | Single & bulk recovery, settlement, Saving module screens | Planned |
| 5 | Ledgers, Journal, Cash Book, Trial Balance, P&L, Balance Sheet | Planned |
| 6 | Loan reports, Excel export, hardening | Planned |

## Database setup

Run once against a new SQL Server (creates database `DhanJagrati`), then apply the patches in order. Use `-b` so an error stops the script:

```powershell
sqlcmd -S <server> -d master -U <login> -C -b -i scheema\dhanjagrati.sql
sqlcmd -S <server> -d DhanJagrati -U <login> -C -b -i scheema\dhanjagrati_patches\001_emi_config_saving_interbranch.sql
```

Patches record themselves in `sec.SchemaVersion` and are skipped if already applied. The API requires patch 001.

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
dotnet test
dotnet build DhanJagrati.slnx -c Release
Set-Location src\DhanJagrati.Web; npm run build; npm run lint
```

## Key design points

- **Sessions:** cookie authentication (HttpOnly, SameSite=Strict). Branch and login date live in `sec.UserSession` on the server and are never taken from the browser. Back-dating is limited to Admin/Manager.
- **Masters:** one generic API (`/api/masters/{entity}`) and one React page driven by `MasterRegistry.cs` / `configs.tsx`. Center and Group are branch-scoped.
- **EMI calculation is configurable per loan plan** (`MANUAL`, `FLAT`, `REDUCING`, `FACTOR`, plus rounding and schedule split), because the legacy formula is not confirmed yet. Plans default to `MANUAL`. The legacy EPL-11 values fit `FACTOR = 0.111`. Each loan stores a snapshot of the method used.
- **Saving module:** separate `sav` schema, linked to loans only through nullable columns and ledger mappings; switched on with the `MODULE.SAVING.ENABLED` setting.
- **Inter-branch transfers:** post through `INTER BRANCH A/C` so each branch balances on its own (`acc.vw_UnbalancedVouchers` checks this per branch).

## Deployment outline (Windows Server / IIS)

Serve `src\DhanJagrati.Web\dist` as static files and reverse-proxy `/api/*` to the published API (`dotnet publish src\DhanJagrati.Api -c Release -o publish\api`) on a loopback address, all behind one HTTPS origin. Do not expose the API port publicly.
