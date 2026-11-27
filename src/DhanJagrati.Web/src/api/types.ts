export type Role = 'ADMIN' | 'MANAGER' | 'ACCOUNTANT' | 'OPERATOR' | 'FIELD_OFFICER'

export interface Me {
  userName: string
  displayName: string
  role: Role
  branchId: number
  branchCode: string
  branchName: string
  /** Login / business date, yyyy-MM-dd */
  businessDate: string
  today: string
  mustChangePassword: boolean
  companyName: string | null
  companyAddress: string | null
  settings: { key: string; value: string }[]
}

export interface BranchOption {
  branchId: number
  branchCode: string
  branchName: string
  isHeadOffice: boolean
}

export interface AppSetting {
  settingKey: string
  settingValue: string
  dataType: 'STRING' | 'INT' | 'DECIMAL' | 'BOOL' | 'DATE'
  description: string | null
}

export interface DayClosingRow {
  businessDate: string
  closedByName: string
  closedAt: string
  remarks: string | null
}

export interface ScheduleLine {
  installmentNo: number
  dueDate: string
  emiAmount: number
  principalAmount: number
  interestAmount: number
}

export interface EmiPreview {
  method: string
  rate: number
  emi: number
  installments: number
  totalRepayable: number
  totalInterest: number
  processingFee: number
  allowEmiOverride: boolean
  schedule: ScheduleLine[]
}

/** Rows returned by /api/masters/{entity}: camelCase columns + id. */
export type MasterRow = { id: number } & Record<string, unknown>
