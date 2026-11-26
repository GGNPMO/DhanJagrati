export interface Member {
  id: number
  memberNo: string
  fullName: string
  phone: string
  village: string
  groupName: string
  branch: string
  status: string
  joinedOn: string
  dateOfBirth: string | null
  guardianName: string | null
  monthlyIncome: number | null
  kycStatus: string
}

export interface Loan {
  id: number
  loanNo: string
  memberId: number
  memberName: string
  product: string
  sanctionedAmount: number
  outstanding: number
  installment: number
  nextDue: string
  relationshipManager: string
  status: string
}

export interface LoanApplication {
  id: number
  applicationNo: string
  memberId: number
  memberName: string
  product: string
  amountRequested: number
  tenureMonths: number
  appliedOn: string
  purpose: string
  status: string
  relationshipManager: string
}

export interface Collection {
  id: number
  receiptNo: string
  loanNo: string
  memberName: string
  paidOn: string
  amount: number
  mode: string
  reference: string
}

export interface DashboardSummary {
  activeMembers: number
  activeLoans: number
  portfolioOutstanding: number
  collectionsThisMonth: number
  overdueAccounts: number
  collectionTarget: number | null
  collectionAchieved: number
  collectionTrend: { day: string; amount: number }[]
  dueLoans: Loan[]
}

export interface BranchInfo {
  id: number
  name: string
}

export interface LoanProductOption {
  id: number
  code: string
  name: string
  minAmount: number
  maxAmount: number
  minInstallments: number
  maxInstallments: number
  frequency: string
}

export interface AccountGroupOption {
  id: number
  code: string
  name: string
  accountClass: string
  parentId: number | null
}

export interface LedgerOption {
  id: number
  code: string
  name: string
  ledgerType: string | null
  accountClass: string
  groupName: string
  isGroupLedger: boolean
  isCash: boolean
  isBank: boolean
  editable: boolean
  visibleInBranch: boolean
  isDefaultGroup: boolean
  loadToBranch: boolean
  applySanction: boolean
  isActive: boolean
}

export interface ReportData {
  type: string
  title: string
  subtitle: string
  branch: string
  dateFrom: string
  dateTo: string
  generatedAt: string
  metrics: { label: string; value: string; tone: string }[]
  columns: { key: string; label: string; alignment: string }[]
  rows: Record<string, string>[]
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`/api${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  })

  const body: unknown = await response.json().catch(() => null)
  if (!response.ok) {
    if (body && typeof body === 'object' && 'errors' in body) {
      const errors = Object.values((body as { errors: Record<string, string[]> }).errors).flat()
      throw new Error(errors.join(' ') || 'The request could not be completed.')
    }
    if (body && typeof body === 'object' && 'message' in body && typeof body.message === 'string') {
      throw new Error(body.message)
    }
    throw new Error(`Request failed (${response.status}).`)
  }

  return body as T
}

export const api = {
  get: <T>(path: string) => request<T>(path),
  post: <T = unknown>(path: string, data: unknown) =>
    request<T>(path, { method: 'POST', body: JSON.stringify(data) }),
}
