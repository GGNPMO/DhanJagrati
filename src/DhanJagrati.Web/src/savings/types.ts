export interface SavingProduct {
  productId: number
  productCode: string
  productName: string
  interestRate: number
  minOpeningDeposit: number
  minBalance: number
  allowWithdrawal: boolean
}

export interface SavingListItem {
  id: number
  accountNo: string
  memberNo: string
  memberName: string
  productCode: string
  currentBalance: number
  status: string
  openDate: string
}

export interface SavingAccountDetail {
  id: number
  accountNo: string
  branchId: number
  branchName: string
  memberNo: string
  memberName: string
  guardianName: string | null
  mobileNo: string | null
  jointMemberNo: string | null
  jointMemberName: string | null
  productId: number
  productCode: string
  productName: string
  openDate: string
  employeeCode: string | null
  employeeName: string | null
  nomineeName: string | null
  nomineeRelationId: number | null
  currentBalance: number
  minBalance: number
  allowWithdrawal: boolean
  status: string
  closedOn: string | null
}

export interface StatementLine {
  txnNo: string
  txnDate: string
  txnType: string
  paymentMode: string
  chequeNo: string | null
  narration: string | null
  withdrawal: number
  deposit: number
  balanceAfter: number
}

export interface SavingStatement {
  openingBalance: number
  lines: StatementLine[]
}
