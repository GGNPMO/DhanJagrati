export interface LoanHeader {
  loanId: number
  loanNo: string
  branchId: number
  branchName: string
  status: string
  applicationDate: string
  approvedOn: string | null
  disbursedOn: string | null
  closedOn: string | null
  memberNo: string
  memberName: string
  guardianName: string | null
  planCode: string
  loanAmount: number
  interestRate: number
  noOfInstallments: number
  emiAmount: number
  emiMode: string
  employeeId: number
  employeeCode: string
  employeeName: string
  lateFeePerInstallment: number
  preCloseChargesPct: number
  savingAccountId: number | null
  savingAccountNo: string | null
}

export interface ScheduleState {
  installmentNo: number
  dueDate: string
  emiAmount: number
  principalAmount: number
  interestAmount: number
  principalPaid: number
  interestPaid: number
  lateFeePaid: number
  status: string
}

export interface RecoveryPrefill {
  loan: LoanHeader
  nextInstallmentNo: number | null
  nextDueDate: string | null
  followingDueDate: string | null
  nextInstallmentBalance: number
  overdueCount: number
  overdueAmount: number
  lateFeeSuggested: number
  totalOutstanding: number
  paidInstallments: number
  schedule: ScheduleState[]
}

export interface ReceiptLine {
  installmentNo: number
  transNo: string
  principal: number
  interest: number
  lateFee: number
}

export interface RecoveryReceipt {
  receiptNo: string
  voucherNo: string
  transDate: string
  loanId: number
  loanNo: string
  memberName: string
  paymentMode: string
  lines: ReceiptLine[]
  bounceCharges: number
  total: number
  loanClosed: boolean
  savingBalance: number | null
}

export interface ReceiptHistoryItem {
  receiptNo: string
  transDate: string
  paymentMode: string
  installments: string
  principal: number
  interest: number
  lateFee: number
  bounceCharges: number
  total: number
  isReversed: boolean
  voucherNo: string | null
  isBulk: boolean
}

export interface BulkDueItem {
  loanId: number
  loanNo: string
  memberNo: string
  memberName: string
  savingAccountNo: string | null
  employeeCode: string
  employeeName: string
  groupName: string | null
  emiAmount: number
  firstInstallmentNo: number
  firstDueDate: string
  dueCount: number
  dueAmount: number
}

export interface BulkResult {
  loanId: number
  loanNo: string | null
  success: boolean
  message: string
  receiptNo: string | null
}

export interface SettlementQuote {
  principalReceived: number
  interestReceived: number
  balancePrincipal: number
  interestDueTillDate: number
  interestWaived: number
  balanceInstallmentAmount: number
  lateFee: number
  amountToBeReceived: number
  preCloseCharges: number
  netCloseAmount: number
}

export interface LastSettlement {
  settlementId: number
  actionType: string
  actionDate: string
  netCloseAmount: number
  voucherNo: string | null
  remarks: string | null
}

export interface SettlementView {
  loan: LoanHeader
  quote: SettlementQuote
  suggestedLateFee: number
  openInstallments: number
  canClose: boolean
  canReopen: boolean
  last: LastSettlement | null
}

export const paymentModes = ['CASH', 'CHEQUE', 'NEFT', 'RTGS', 'IMPS', 'UPI'].map((v) => ({ value: v, label: v }))
