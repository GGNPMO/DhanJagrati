export interface EmployeeDetail {
  id: number
  employeeCode: string
  branchId: number
  branchName: string
  joiningDate: string
  introducerCode: string | null
  introducerName: string | null
  introducerRankNo: number | null
  initialRankId: number | null
  currentRankId: number | null
  memberNo: string | null
  employeeName: string
  guardianName: string | null
  dateOfBirth: string | null
  gender: string | null
  addressLine: string | null
  cityId: number | null
  stateId: number | null
  countryId: number | null
  pinCode: string | null
  mobileNo: string | null
  phoneNo: string | null
  email: string | null
  passportNo: string | null
  nomineeName: string | null
  nomineeDob: string | null
  nomineeRelationId: number | null
  joiningFee: number
  joiningFeeMode: string | null
  joiningFeeRefNo: string | null
  isActive: boolean
}

export interface EmployeeListItem {
  id: number
  employeeCode: string
  employeeName: string
  rankCode: string | null
  mobileNo: string | null
  joiningDate: string
  introducerCode: string | null
  branchName: string
  isActive: boolean
}

export interface EmployeeSummary {
  id: number
  employeeCode: string
  employeeName: string
  branchId: number
  branchName: string
  rankNo: number | null
  rankCode: string | null
  isActive: boolean
}

export interface ChainLink {
  level: number
  advisorCode: string
  advisorRank: string | null
  advisorName: string
}

export interface AttendanceRow {
  employeeId: number
  employeeCode: string
  employeeName: string
  status: string | null
  inTime: string | null
  outTime: string | null
  remarks: string | null
}
