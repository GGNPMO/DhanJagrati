export interface AccountNature {
  natureId: number
  natureCode: string
  natureName: string
  reportHead: 'BS' | 'PL'
  normalBalance: 'D' | 'C'
}

export interface LedgerRow {
  id: number
  ledgerName: string
  natureId: number
  natureName: string
  isGroup: boolean
  parentLedgerId: number | null
  reportHead: 'BS' | 'PL'
  cashBankType: 'C' | 'B' | 'N'
  bankAccountNo: string | null
  ifscCode: string | null
  isEditable: boolean
  isVisibleInBranch: boolean
  isDefaultGroup: boolean
  applySanction: boolean
  isSystem: boolean
  isActive: boolean
  openingBalance: number | null
  openingBalType: 'D' | 'C' | null
  openingDate: string | null
  hasPostings: boolean
  mappingKeys: string | null
}

export interface VoucherListItem {
  id: number
  voucherNo: string
  voucherDate: string
  voucherType: string
  paymentMode: string
  amount: number
  narration: string | null
  debitLedgers: string | null
  creditLedgers: string | null
  sourceModule: string | null
  isCancelled: boolean
}

export interface VoucherDetail {
  id: number
  voucherNo: string
  voucherDate: string
  branchName: string
  voucherType: string
  paymentMode: string
  chequeNo: string | null
  chequeDate: string | null
  payeeName: string | null
  narration: string | null
  amount: number
  transferToBranchName: string | null
  sanctionNo: string | null
  sourceModule: string | null
  isCancelled: boolean
  cancelledAt: string | null
  createdByName: string | null
  createdAt: string
  lines: { lineNumber: number; branchName: string; ledgerName: string; debit: number; credit: number; narration: string | null }[]
}

export interface CashBook {
  totals: { openingBalance: number; totalReceipts: number; totalPayments: number; closingBalance: number }
  summary: { ledgerId: number; ledgerName: string; receipt: number; payment: number }[]
  detail: {
    srNo: number
    voucherDate: string
    voucherNo: string
    ledgerId: number
    accountHead: string
    particulars: string | null
    debit: number
    credit: number
    paymentMode: string
    chequeNo: string | null
  }[]
}

export interface TrialBalanceRow {
  srNo: number
  ledgerId: number
  ledgerName: string
  natureName: string
  groupName: string | null
  openingNet: number
  periodDebit: number
  periodCredit: number
  debit: number
  credit: number
}

export interface StatementRow {
  ledgerId: number | null
  ledgerName: string
  side: string
  amount: number
  groupName: string | null
}

export interface ProfitLoss {
  rows: StatementRow[]
  totals: { totalExpense: number; totalIncome: number; netProfit: number; netLoss: number }
}

export interface LedgerStatement {
  openingNet: number
  lines: {
    voucherDate: string
    voucherNo: string
    voucherType: string
    paymentMode: string
    chequeNo: string | null
    particulars: string | null
    debit: number
    credit: number
    runningBalance: number
  }[]
}
