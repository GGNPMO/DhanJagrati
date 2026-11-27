export interface ScheduleRow {
  installmentNo: number
  dueDate: string
  emiAmount: number
  principalAmount: number
  interestAmount: number
  principalPaid?: number
  interestPaid?: number
  status?: string
}

export interface GuarantorRow {
  memberNo: string | null
  guarantorName: string
  guardianName: string | null
  addressLine: string | null
  mobileNo: string | null
  relationId: number | null
  idProofNo: string | null
}

export interface CollateralRow {
  depositAccountNo: string
  depositAmount: number
  maturityDate: string | null
  lienAmount: number | null
}

export interface LoanDetail {
  id: number
  loanNo: string
  applicationDate: string
  branchId: number
  branchName: string
  status: string
  memberNo: string
  memberName: string
  guardianName: string | null
  fileNo: string | null
  gender: string
  memberAddress: string
  memberMobile: string | null
  memberEmail: string | null
  pinCode: string | null
  cityName: string | null
  stateName: string | null
  planId: number
  planCode: string
  emiMode: string
  loanAmount: number
  interestRate: number
  tenureYears: number
  tenureMonths: number
  noOfInstallments: number
  emiAmount: number
  emiCalcMethod: string | null
  isEmiOverridden: boolean
  noOfUnits: number
  emiStartDate: string
  disbursementMode: string
  disbursementLedgerId: number | null
  disbursementLedgerName: string | null
  chequeNo: string | null
  chequeAmount: number | null
  remark: string | null
  employeeCode: string
  employeeName: string
  groupId: number | null
  processingFeePct: number
  processingFee: number
  gstAmount: number
  shareAmount: number
  insuranceAmount: number
  netDisbursedAmount: number
  savingAccountNo: string | null
  approvedOn: string | null
  disbursedOn: string | null
  disbursementVoucherNo: string | null
  closedOn: string | null
  guarantors: GuarantorRow[]
  collaterals: CollateralRow[]
}

export interface LoanListItem {
  id: number
  loanNo: string
  applicationDate: string
  memberNo: string
  memberName: string
  planCode: string
  loanAmount: number
  emiAmount: number
  employeeCode: string
  status: string
}

export interface LoanTerms {
  planId: number
  planCode: string
  emiMode: string
  method: string
  factor: number | null
  rate: number
  calculatedEmi: number
  emi: number
  isEmiOverridden: boolean
  installments: number
  tenureMonths: number
  totalRepayable: number
  totalInterest: number
  processingFeePct: number
  processingFee: number
  allowEmiOverride: boolean
  interestSplit: string
  firstDueDate: string
  schedule: ScheduleRow[]
}

export interface ApprovalListItem {
  loanId: number
  loanNo: string
  applicationDate: string
  memberNo: string
  memberName: string
  guardianName: string | null
  planCode: string
  loanAmount: number
  interestRate: number
  noOfInstallments: number
  emiAmount: number
  employeeCode: string
  employeeName: string
  status: string
  approvedOn: string | null
}

export interface ApprovalResult {
  loanId: number
  loanNo: string | null
  success: boolean
  message: string
}

export interface CardSearchItem {
  loanId: number
  loanNo: string
  fileNo: string | null
  memberName: string
  disbursedOn: string | null
  loanAmount: number
  prints: number
}

export interface LoanCard {
  loanId: number
  memberId: number
  loanNo: string
  fileNo: string | null
  memberNo: string
  memberName: string
  guardianName: string | null
  addressLine: string
  mobileNo: string | null
  gender: string
  age: number | null
  occupationName: string | null
  dateOfJoining: string
  applicationDate: string
  disbursedOn: string | null
  loanAmount: number
  interestRate: number
  emiAmount: number
  noOfInstallments: number
  emiMode: string
  emiStartDate: string
  planCode: string
  disbursementMode: string
  chequeNo: string | null
  employeeCode: string
  employeeName: string
  branchName: string
  hasPhoto: boolean
  hasSignature: boolean
  schedule: ScheduleRow[]
}
