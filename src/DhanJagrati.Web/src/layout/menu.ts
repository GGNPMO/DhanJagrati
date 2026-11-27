import type { Role } from '../api/types'

export interface MenuItem {
  label: string
  /** Route; items without a route are planned for a later phase. */
  to?: string
  phase?: number
  roles?: Role[]
  /** Only shown when this app setting is 'true'. */
  setting?: string
  dividerBefore?: boolean
}

export interface MenuSection {
  label: string
  items: MenuItem[]
  setting?: string
}

const soon = (label: string, phase: number, dividerBefore = false): MenuItem => ({
  label,
  to: `/soon/${encodeURIComponent(label)}`,
  phase,
  dividerBefore,
})

/** Top-level menu, mirroring the legacy MASTER / EMPLOYEE / LOAN / ACCOUNT / HELP sections. */
export const menu: MenuSection[] = [
  {
    label: 'Master',
    items: [
      { label: 'Salutation Master', to: '/masters/salutations' },
      { label: 'Occupation Master', to: '/masters/occupations' },
      { label: 'Religion Master', to: '/masters/religions' },
      { label: 'Relation Master', to: '/masters/relations' },
      { label: 'Designation Master', to: '/masters/designations' },
      { label: 'Bank Master', to: '/masters/banks' },
      { label: 'Country Master', to: '/masters/countries', dividerBefore: true },
      { label: 'State Master', to: '/masters/states' },
      { label: 'City Master', to: '/masters/cities' },
      { label: 'District Master', to: '/masters/districts' },
      { label: 'Tehsil Master', to: '/masters/tehsils' },
      { label: 'Village Master', to: '/masters/villages' },
      { label: 'Khasra Master', to: '/masters/khasras' },
      { label: 'Center Master', to: '/masters/centers', dividerBefore: true },
      { label: 'Group Master', to: '/masters/groups' },
      { label: 'Vehicle Master', to: '/masters/vehicles' },
      { label: 'Dealers Master', to: '/masters/dealers' },
      { label: 'Loan Plan / EMI Setup', to: '/masters/loan-plans', dividerBefore: true },
      { label: 'Interest Rate Set', to: '/masters/interest-rates' },
      { label: 'Daily Attendance', to: '/attendance', dividerBefore: true },
      { label: 'Date Closing by User', to: '/day-closing' },
    ],
  },
  {
    label: 'Employee',
    items: [
      { label: 'Advisor / Relationship Manager', to: '/employees' },
      { label: 'Advisor Rank Master', to: '/masters/advisor-ranks' },
      soon('Employee Wise Loan Report', 6, true),
    ],
  },
  {
    label: 'Loan',
    items: [
      { label: 'Customer (Member) Entry', to: '/members' },
      soon('Loan Application', 3),
      soon('Loan Approve', 3),
      soon('Loan Recovery', 4, true),
      soon('Loan Bulk Recovery', 4),
      soon('Loan Settlement', 4),
      soon('Loan Application Report', 6, true),
      soon('Loan Recovery Due Report', 6),
      soon('Loan Recovery Ledger', 6),
      soon('Loan Closer Report', 6),
      soon('Loan Card / Signature Card', 3, true),
    ],
  },
  {
    label: 'Saving',
    setting: 'MODULE.SAVING.ENABLED',
    items: [soon('Saving Account Opening', 4), soon('Deposit / Withdrawal', 4), soon('Saving Statement', 4)],
  },
  {
    label: 'Account',
    items: [
      soon('Add Groups and Ledgers', 5),
      soon('Ledger Transaction Journal', 5),
      soon('Cash Book', 5, true),
      soon('Trial Balance', 5),
      soon('Profit and Loss Statement', 5),
      soon('Balance Sheet', 5),
    ],
  },
  {
    label: 'Help',
    items: [
      { label: 'Change Password', to: '/help/change-password' },
      { label: 'Select Branch', to: '/help/select-branch' },
      { label: 'User Master', to: '/admin/users', roles: ['ADMIN'], dividerBefore: true },
      { label: 'Settings', to: '/admin/settings', roles: ['ADMIN'] },
    ],
  },
]
