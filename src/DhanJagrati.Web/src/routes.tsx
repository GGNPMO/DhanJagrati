import { createBrowserRouter, Navigate } from 'react-router'
import { BalanceSheetPage } from './accounts/BalanceSheetPage'
import { CashBookPage } from './accounts/CashBookPage'
import { LedgersPage } from './accounts/LedgersPage'
import { LedgerStatementPage } from './accounts/LedgerStatementPage'
import { ProfitLossPage } from './accounts/ProfitLossPage'
import { TrialBalancePage } from './accounts/TrialBalancePage'
import { VoucherPage } from './accounts/VoucherPage'
import { AttendancePage } from './employees/AttendancePage'
import { EmployeePage } from './employees/EmployeePage'
import { ProtectedLayout } from './layout/ProtectedLayout'
import { LoanApplicationPage } from './loans/LoanApplicationPage'
import { LoanApprovePage } from './loans/LoanApprovePage'
import { LoanCardPage } from './loans/LoanCardPage'
import { MasterRoute } from './masters/MasterRoute'
import { MemberPage } from './members/MemberPage'
import { BulkRecoveryPage } from './recovery/BulkRecoveryPage'
import { RecoveryPage } from './recovery/RecoveryPage'
import { SettlementPage } from './recovery/SettlementPage'
import { SavingAccountPage } from './savings/SavingAccountPage'
import { SavingAccountsPage } from './savings/SavingAccountsPage'
import { ChangePasswordPage } from './pages/ChangePasswordPage'
import { ComingSoonPage } from './pages/ComingSoonPage'
import { DayClosingPage } from './pages/DayClosingPage'
import { HomePage } from './pages/HomePage'
import { LoginPage } from './pages/LoginPage'
import { SelectBranchPage } from './pages/SelectBranchPage'
import { SettingsPage } from './pages/SettingsPage'
import { UsersPage } from './pages/UsersPage'

export const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  {
    path: '/',
    element: <ProtectedLayout />,
    children: [
      { index: true, element: <HomePage /> },
      { path: 'masters/:entity', element: <MasterRoute /> },
      { path: 'day-closing', element: <DayClosingPage /> },
      { path: 'members', element: <MemberPage /> },
      { path: 'members/:id', element: <MemberPage /> },
      { path: 'employees', element: <EmployeePage /> },
      { path: 'employees/:id', element: <EmployeePage /> },
      { path: 'attendance', element: <AttendancePage /> },
      { path: 'loans', element: <LoanApplicationPage /> },
      { path: 'loans/:id', element: <LoanApplicationPage /> },
      { path: 'loan-approve', element: <LoanApprovePage /> },
      { path: 'loan-cards', element: <LoanCardPage /> },
      { path: 'recovery', element: <RecoveryPage /> },
      { path: 'bulk-recovery', element: <BulkRecoveryPage /> },
      { path: 'settlement', element: <SettlementPage /> },
      { path: 'savings', element: <SavingAccountsPage /> },
      { path: 'savings/:id', element: <SavingAccountPage /> },
      { path: 'ledgers', element: <LedgersPage /> },
      { path: 'journal', element: <VoucherPage /> },
      { path: 'cash-book', element: <CashBookPage /> },
      { path: 'trial-balance', element: <TrialBalancePage /> },
      { path: 'profit-loss', element: <ProfitLossPage /> },
      { path: 'balance-sheet', element: <BalanceSheetPage /> },
      { path: 'ledger-statement', element: <LedgerStatementPage /> },
      { path: 'help/change-password', element: <ChangePasswordPage /> },
      { path: 'help/select-branch', element: <SelectBranchPage /> },
      { path: 'admin/settings', element: <SettingsPage /> },
      { path: 'admin/users', element: <UsersPage /> },
      { path: 'soon/:feature', element: <ComingSoonPage /> },
      { path: '*', element: <Navigate to="/" replace /> },
    ],
  },
])
