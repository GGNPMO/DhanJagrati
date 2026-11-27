import { createBrowserRouter, Navigate } from 'react-router'
import { ProtectedLayout } from './layout/ProtectedLayout'
import { HomePage } from './pages/HomePage'
import { LoginPage } from './pages/LoginPage'

/** Route-level code splitting: each screen is downloaded the first time it is opened. */
const page = <T extends Record<string, unknown>>(load: () => Promise<T>, name: keyof T) => ({
  lazy: async () => ({ Component: (await load())[name] as React.ComponentType }),
})

export const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  {
    path: '/',
    element: <ProtectedLayout />,
    children: [
      { index: true, element: <HomePage /> },
      // Master / help / admin
      { path: 'masters/:entity', ...page(() => import('./masters/MasterRoute'), 'MasterRoute') },
      { path: 'day-closing', ...page(() => import('./pages/DayClosingPage'), 'DayClosingPage') },
      { path: 'help/change-password', ...page(() => import('./pages/ChangePasswordPage'), 'ChangePasswordPage') },
      { path: 'help/select-branch', ...page(() => import('./pages/SelectBranchPage'), 'SelectBranchPage') },
      { path: 'admin/settings', ...page(() => import('./pages/SettingsPage'), 'SettingsPage') },
      { path: 'admin/users', ...page(() => import('./pages/UsersPage'), 'UsersPage') },
      // Employee
      { path: 'employees', ...page(() => import('./employees/EmployeePage'), 'EmployeePage') },
      { path: 'employees/:id', ...page(() => import('./employees/EmployeePage'), 'EmployeePage') },
      { path: 'attendance', ...page(() => import('./employees/AttendancePage'), 'AttendancePage') },
      // Loan
      { path: 'members', ...page(() => import('./members/MemberPage'), 'MemberPage') },
      { path: 'members/:id', ...page(() => import('./members/MemberPage'), 'MemberPage') },
      { path: 'loans', ...page(() => import('./loans/LoanApplicationPage'), 'LoanApplicationPage') },
      { path: 'loans/:id', ...page(() => import('./loans/LoanApplicationPage'), 'LoanApplicationPage') },
      { path: 'loan-approve', ...page(() => import('./loans/LoanApprovePage'), 'LoanApprovePage') },
      { path: 'loan-cards', ...page(() => import('./loans/LoanCardPage'), 'LoanCardPage') },
      { path: 'loan-reports/:report', ...page(() => import('./loans/LoanReportPage'), 'LoanReportRoute') },
      { path: 'recovery', ...page(() => import('./recovery/RecoveryPage'), 'RecoveryPage') },
      { path: 'bulk-recovery', ...page(() => import('./recovery/BulkRecoveryPage'), 'BulkRecoveryPage') },
      { path: 'settlement', ...page(() => import('./recovery/SettlementPage'), 'SettlementPage') },
      // Saving
      { path: 'savings', ...page(() => import('./savings/SavingAccountsPage'), 'SavingAccountsPage') },
      { path: 'savings/:id', ...page(() => import('./savings/SavingAccountPage'), 'SavingAccountPage') },
      // Account
      { path: 'ledgers', ...page(() => import('./accounts/LedgersPage'), 'LedgersPage') },
      { path: 'journal', ...page(() => import('./accounts/VoucherPage'), 'VoucherPage') },
      { path: 'cash-book', ...page(() => import('./accounts/CashBookPage'), 'CashBookPage') },
      { path: 'trial-balance', ...page(() => import('./accounts/TrialBalancePage'), 'TrialBalancePage') },
      { path: 'profit-loss', ...page(() => import('./accounts/ProfitLossPage'), 'ProfitLossPage') },
      { path: 'balance-sheet', ...page(() => import('./accounts/BalanceSheetPage'), 'BalanceSheetPage') },
      { path: 'ledger-statement', ...page(() => import('./accounts/LedgerStatementPage'), 'LedgerStatementPage') },
      { path: 'soon/:feature', ...page(() => import('./pages/ComingSoonPage'), 'ComingSoonPage') },
      { path: '*', element: <Navigate to="/" replace /> },
    ],
  },
])
