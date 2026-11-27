export interface MemberDetail {
  id: number
  memberNo: string
  branchId: number
  branchName: string
  fileNo: string | null
  aadhaarNo: string | null
  registrationDate: string
  salutationId: number | null
  memberName: string
  guardianRelationId: number | null
  guardianName: string | null
  motherName: string | null
  gender: string
  religionId: number | null
  dateOfBirth: string | null
  monthlyIncome: number | null
  maritalStatus: string | null
  isSeniorCitizen: boolean
  isHandicapped: boolean
  isSpecialCategory: boolean
  occupationId: number | null
  panNo: string | null
  bankId: number | null
  ifscCode: string | null
  bankAccountNo: string | null
  addressLine: string
  permanentAddress: string | null
  villageId: number | null
  cityId: number | null
  stateId: number | null
  countryId: number | null
  pinCode: string | null
  mobileNo: string | null
  phoneNo: string | null
  email: string | null
  passportNo: string | null
  nomineeName: string | null
  nomineeGuardianName: string | null
  nomineeDob: string | null
  nomineeRelationId: number | null
  refMemberNo: string | null
  kycAadhaar: boolean
  kycVoterId: boolean
  kycDrivingLicense: boolean
  kycOtherProof: boolean
  idProofNo: string | null
  groupId: number | null
  designationId: number | null
  hasPhoto: boolean
  hasSignature: boolean
  isPhotoVerified: boolean
  isActive: boolean
}

export interface MemberListItem {
  id: number
  memberNo: string
  fileNo: string | null
  memberName: string
  guardianName: string | null
  mobileNo: string | null
  aadhaarNo: string | null
  registrationDate: string
  isActive: boolean
}

export interface MemberSummary {
  id: number
  memberNo: string
  branchId: number
  memberName: string
  guardianName: string | null
  gender: string
  dateOfBirth: string | null
  addressLine: string
  cityId: number | null
  stateId: number | null
  countryId: number | null
  pinCode: string | null
  mobileNo: string | null
  email: string | null
  isActive: boolean
}
