import {
  IconBuildingBank,
  IconCash,
  IconDatabase,
  IconHelpCircle,
  IconLayoutDashboard,
  IconPigMoney,
  IconUsersGroup,
  type Icon,
} from '@tabler/icons-react'
import type { Role } from '../api/types'

export interface MenuItem {
  label: string
  to: string
  /** Planned for a later phase (shows a badge, opens a placeholder). */
  phase?: number
  roles?: Role[]
  dividerBefore?: boolean
}

export interface MenuSection {
  label: string
  icon: Icon
  items: MenuItem[]
  /** Only shown when this app setting is 'true'. */
  setting?: string
  /** Direct link (no children). */
  to?: string
}

const soon = (label: string, phase: number, dividerBefore = false): MenuItem => ({
  label,
  to: `/soon/${encodeURIComponent(label)}`,
  phase,
  dividerBefore,
})

/** Navigation, mirroring the legacy MASTER / EMPLOYEE / LOAN / ACCOUNT / HELP sections. */
export const menu: MenuSection[] = [
  { label: 'Dashboard', icon: IconLayoutDashboard, to: '/', items: [] },
  {
    label: 'Loan',
    icon: IconCash,
    items: [
      { label: 'Customer (Member)', to: '/members' },
      { label: 'Loan Application', to: '/loans' },
      { label: 'Loan Approve', to: '/loan-approve', roles: ['ADMIN', 'MANAGER'] },
      { label: 'Loan Card / Signature Card', to: '/loan-cards' },
      { label: 'Loan Recovery', to: '/recovery', dividerBefore: true },
      { label: 'Loan Bulk Recovery', to: '/bulk-recovery' },
      { label: 'Loan Settlement', to: '/settlement' },
      soon('Loan Application Report', 6, true),
      soon('Loan Recovery Due Report', 6),
      soon('Loan Recovery Ledger', 6),
      soon('Loan Closer Report', 6),
    ],
  },
  {
    label: 'Employee',
    icon: IconUsersGroup,
    items: [
      { label: 'Advisor / Relationship Manager', to: '/employees' },
      { label: 'Daily Attendance', to: '/attendance' },
      { label: 'Advisor Rank Master', to: '/masters/advisor-ranks' },
      soon('Employee Wise Loan Report', 6, true),
    ],
  },
  {
    label: 'Saving',
    icon: IconPigMoney,
    setting: 'MODULE.SAVING.ENABLED',
    items: [{ label: 'Saving Accounts', to: '/savings' }],
  },
  {
    label: 'Account',
    icon: IconBuildingBank,
    items: [
      { label: 'Groups and Ledgers', to: '/ledgers', roles: ['ADMIN', 'MANAGER', 'ACCOUNTANT'] },
      { label: 'Ledger Transaction Journal', to: '/journal', roles: ['ADMIN', 'MANAGER', 'ACCOUNTANT'] },
      { label: 'Cash Book', to: '/cash-book', roles: ['ADMIN', 'MANAGER', 'ACCOUNTANT'], dividerBefore: true },
      { label: 'Ledger Statement', to: '/ledger-statement', roles: ['ADMIN', 'MANAGER', 'ACCOUNTANT'] },
      { label: 'Trial Balance', to: '/trial-balance', roles: ['ADMIN', 'MANAGER', 'ACCOUNTANT'] },
      { label: 'Profit and Loss', to: '/profit-loss', roles: ['ADMIN', 'MANAGER', 'ACCOUNTANT'] },
      { label: 'Balance Sheet', to: '/balance-sheet', roles: ['ADMIN', 'MANAGER', 'ACCOUNTANT'] },
    ],
  },
  {
    label: 'Master',
    icon: IconDatabase,
    items: [
      { label: 'Salutation', to: '/masters/salutations' },
      { label: 'Occupation', to: '/masters/occupations' },
      { label: 'Religion', to: '/masters/religions' },
      { label: 'Relation', to: '/masters/relations' },
      { label: 'Designation', to: '/masters/designations' },
      { label: 'Bank', to: '/masters/banks' },
      { label: 'Country', to: '/masters/countries', dividerBefore: true },
      { label: 'State', to: '/masters/states' },
      { label: 'City', to: '/masters/cities' },
      { label: 'District', to: '/masters/districts' },
      { label: 'Tehsil', to: '/masters/tehsils' },
      { label: 'Village', to: '/masters/villages' },
      { label: 'Khasra', to: '/masters/khasras' },
      { label: 'Center', to: '/masters/centers', dividerBefore: true },
      { label: 'Group', to: '/masters/groups' },
      { label: 'Vehicle', to: '/masters/vehicles' },
      { label: 'Dealers', to: '/masters/dealers' },
      { label: 'Loan Plan / EMI Setup', to: '/masters/loan-plans', dividerBefore: true },
      { label: 'Interest Rate Set', to: '/masters/interest-rates' },
      { label: 'Date Closing', to: '/day-closing', dividerBefore: true },
    ],
  },
  {
    label: 'Help',
    icon: IconHelpCircle,
    items: [
      { label: 'Change Password', to: '/help/change-password' },
      { label: 'Select Branch', to: '/help/select-branch' },
      { label: 'User Master', to: '/admin/users', roles: ['ADMIN'], dividerBefore: true },
      { label: 'Settings', to: '/admin/settings', roles: ['ADMIN'] },
    ],
  },
]
