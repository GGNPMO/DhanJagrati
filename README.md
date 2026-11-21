# Dhan Jagrati Microfinance Ltd

A small web-first microfinance operations workspace. The solution uses a .NET 10 Minimal API and a React 19 + TypeScript client, with a modular-monolith shape that can later be connected to the SQL Server schema in `scheema\microfinance_schema.sql`.

## Run locally

Open two terminals from the repository root:

```powershell
dotnet run --project src\DhanJagrati.Api\DhanJagrati.Api.csproj --urls http://localhost:5072
```

```powershell
Set-Location src\DhanJagrati.Web
npm install
npm run dev
```

Open the Vite URL (normally `http://localhost:5173`). The Vite development server proxies `/api` requests to the API at `http://localhost:5072`.

## Included workflows

- Branch overview with portfolio, due accounts and recent receipts.
- Member directory and member registration.
- Loan application creation, approval/decline, disbursement and active-account views.
- Single collections with receipt history.
- Balanced general-ledger voucher entry.
- Cashbook summary/detail, trial balance, profit & loss, balance sheet, application, recovery, relationship-manager and closure reports.
- Report date filters, CSV export and print layout.

## Data mode and production setup

The API currently runs in **explicit demo mode**. Seed records and transactions are held in memory: they reset when the API restarts, and this is not a production database connection. The UI labels this mode to avoid presenting sample data as live financial information. The API request/response shapes follow the member, loan, collections and GL concepts in the SQL Server schema, but SQL persistence, authentication, branch-level authorization, migrations and audit-user integration still need to be implemented before production use.

Do not connect this demo build to real member or financial data. For production, introduce a SQL Server repository behind the API and use versioned database migrations; do not repeatedly execute the bootstrap schema against an existing database.