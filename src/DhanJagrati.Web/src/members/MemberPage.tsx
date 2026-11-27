import { Alert, Badge, Button, Checkbox, Grid, Group, Modal, SimpleGrid, Stack, Text, TextInput } from '@mantine/core'
import { PageHeader } from '../components/PageHeader'
import { useForm, useWatch } from 'react-hook-form'
import { useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { errorMessage } from '../api/client'
import { useMe } from '../auth/session'
import { CheckField, DateField, LookupField, NumberField, SelectField, TextField } from '../components/fields'
import { ageOn, fromNum, fromSel, fromStr, genderChoices, toNum, toSel, toStr } from '../components/formValues'
import { formatDate } from '../components/format'
import { RecordToolbar } from '../components/RecordToolbar'
import { SearchModal } from '../components/SearchModal'
import { Section } from '../components/Section'
import { useRecordController } from '../components/useRecordController'
import { MemberPhotoPanel } from './MemberPhotoPanel'
import type { MemberDetail, MemberListItem } from './types'

const maritalChoices = ['SINGLE', 'MARRIED', 'WIDOWED', 'DIVORCED'].map((v) => ({ value: v, label: v }))

type MemberForm = {
  fileNo: string
  aadhaarNo: string
  registrationDate: string | null
  salutationId: string | null
  memberName: string
  guardianRelationId: string | null
  guardianName: string
  motherName: string
  gender: string | null
  religionId: string | null
  dateOfBirth: string | null
  monthlyIncome: number | ''
  maritalStatus: string | null
  isSeniorCitizen: boolean
  isHandicapped: boolean
  isSpecialCategory: boolean
  occupationId: string | null
  panNo: string
  bankId: string | null
  ifscCode: string
  bankAccountNo: string
  addressLine: string
  permanentAddress: string
  villageId: string | null
  cityId: string | null
  stateId: string | null
  countryId: string | null
  pinCode: string
  mobileNo: string
  phoneNo: string
  email: string
  passportNo: string
  nomineeName: string
  nomineeGuardianName: string
  nomineeDob: string | null
  nomineeRelationId: string | null
  refMemberNo: string
  kycAadhaar: boolean
  kycVoterId: boolean
  kycDrivingLicense: boolean
  kycOtherProof: boolean
  idProofNo: string
  groupId: string | null
  designationId: string | null
  isActive: boolean
}

const textKeys = [
  'fileNo', 'aadhaarNo', 'memberName', 'guardianName', 'motherName', 'panNo', 'ifscCode', 'bankAccountNo', 'addressLine',
  'permanentAddress', 'pinCode', 'mobileNo', 'phoneNo', 'email', 'passportNo', 'nomineeName', 'nomineeGuardianName',
  'refMemberNo', 'idProofNo',
] as const
const selectKeys = [
  'salutationId', 'guardianRelationId', 'religionId', 'occupationId', 'bankId', 'villageId', 'cityId', 'stateId',
  'countryId', 'nomineeRelationId', 'groupId', 'designationId',
] as const
const boolKeys = [
  'isSeniorCitizen', 'isHandicapped', 'isSpecialCategory', 'kycAadhaar', 'kycVoterId', 'kycDrivingLicense', 'kycOtherProof', 'isActive',
] as const

function emptyMember(): MemberForm {
  const form = {} as Record<string, unknown>
  for (const k of textKeys) form[k] = ''
  for (const k of selectKeys) form[k] = null
  for (const k of boolKeys) form[k] = k === 'isActive'
  return { ...(form as MemberForm), registrationDate: null, gender: null, dateOfBirth: null, monthlyIncome: '', maritalStatus: null, nomineeDob: null }
}

function toForm(m: MemberDetail): MemberForm {
  const form = {} as Record<string, unknown>
  for (const k of textKeys) form[k] = toStr(m[k])
  for (const k of selectKeys) form[k] = toSel(m[k])
  for (const k of boolKeys) form[k] = m[k]
  return {
    ...(form as MemberForm),
    registrationDate: m.registrationDate,
    gender: m.gender,
    dateOfBirth: m.dateOfBirth,
    monthlyIncome: toNum(m.monthlyIncome),
    maritalStatus: m.maritalStatus,
    nomineeDob: m.nomineeDob,
  }
}

function toPayload(f: MemberForm) {
  const payload: Record<string, unknown> = {}
  for (const k of textKeys) payload[k] = fromStr(f[k])
  for (const k of selectKeys) payload[k] = fromSel(f[k])
  for (const k of boolKeys) payload[k] = f[k]
  return {
    ...payload,
    registrationDate: f.registrationDate,
    gender: f.gender,
    dateOfBirth: f.dateOfBirth,
    monthlyIncome: fromNum(f.monthlyIncome),
    maritalStatus: f.maritalStatus,
    nomineeDob: f.nomineeDob,
  }
}

/** LOAN MASTER > Customer: "Member Master Information". */
export function MemberPage() {
  const params = useParams()
  const id = params.id ? Number(params.id) : null
  const navigate = useNavigate()
  const { data: me } = useMe()
  const canEdit = me?.role === 'ADMIN' || me?.role === 'MANAGER' || me?.role === 'OPERATOR'
  const canManage = me?.role === 'ADMIN' || me?.role === 'MANAGER'

  const form = useForm<MemberForm>({ defaultValues: emptyMember() })
  const ctl = useRecordController<MemberDetail, MemberForm>({
    resource: 'members',
    title: 'Member',
    id,
    form,
    empty: emptyMember,
    toForm,
    toPayload,
    navKey: (m) => m.memberNo,
  })

  const [searchOpen, setSearchOpen] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const c = form.control
  const ro = !ctl.editing || !canEdit
  const [stateId, dob, regDate, nomineeDob] = useWatch({ control: c, name: ['stateId', 'dateOfBirth', 'registrationDate', 'nomineeDob'] })
  const today = me?.today

  return (
    <Stack gap="md">
      <PageHeader
        title="Member Master"
        subtitle={ctl.record ? `${ctl.record.memberNo} · ${ctl.record.memberName}` : 'Register a new customer'}
        badge={ctl.mode !== 'view' ? <Badge color="brand">{ctl.mode === 'new' ? 'New member' : 'Editing'}</Badge> : undefined}
      />
      <RecordToolbar
        mode={ctl.mode}
        hasRecord={!!ctl.record}
        canEdit={canEdit}
        canDelete={canManage}
        saving={ctl.saving}
        onAdd={ctl.add}
        onEdit={ctl.edit}
        onSave={ctl.save}
        onUndo={ctl.undo}
        onDelete={() => setConfirmDelete(true)}
        onSearch={() => setSearchOpen(true)}
        onNavigate={ctl.navigate}
        onPrint={() => window.print()}
        onClose={() => navigate('/')}
      />
      {ctl.loadError && <Alert color="red">{errorMessage(ctl.loadError)}</Alert>}
      {ctl.mode === 'new' && !canEdit && <Alert color="gray">Use Search or the navigation buttons to open a member.</Alert>}

      <Section title="Transaction Details">
        <SimpleGrid cols={{ base: 1, sm: 2, md: 5 }}>
          <TextInput label="Member Id" value={ctl.record?.memberNo ?? (ctl.mode === 'new' ? '(new)' : '')} readOnly />
          <TextField control={c} name="fileNo" label="File No" disabled={ro} maxLength={20} />
          <TextField control={c} name="aadhaarNo" label="Aadhaar No." disabled={ro} maxLength={14} />
          <DateField control={c} name="registrationDate" label="Reg. Date" disabled={ro} maxDate={today} description={ctl.mode === 'new' ? 'Blank = login date' : undefined} />
          <TextInput label="Branch" value={ctl.record?.branchName ?? me?.branchName ?? ''} readOnly />
        </SimpleGrid>
      </Section>

      <Grid gap="md">
        <Grid.Col span={{ base: 12, lg: 8 }}>
          <Stack gap="md">
            <Section title="Customer Information">
              <Grid gap="sm">
                <Grid.Col span={{ base: 4, sm: 2 }}>
                  <LookupField control={c} name="salutationId" label="Title" entity="salutations" labelKey="salutationName" filter={(r) => !r.isRelation} disabled={ro} />
                </Grid.Col>
                <Grid.Col span={{ base: 8, sm: 6 }}>
                  <TextField control={c} name="memberName" label="Name" required disabled={ro} maxLength={100} />
                </Grid.Col>
                <Grid.Col span={{ base: 6, sm: 2 }}>
                  <TextInput label="Age" value={ageOn(dob, regDate ?? today)} readOnly />
                </Grid.Col>
                <Grid.Col span={{ base: 6, sm: 2 }}>
                  <DateField control={c} name="dateOfBirth" label="DOB" disabled={ro} maxDate={today} />
                </Grid.Col>
                <Grid.Col span={{ base: 4, sm: 2 }}>
                  <LookupField control={c} name="guardianRelationId" label="Relation" entity="salutations" labelKey="salutationName" filter={(r) => Boolean(r.isRelation)} disabled={ro} />
                </Grid.Col>
                <Grid.Col span={{ base: 8, sm: 4 }}>
                  <TextField control={c} name="guardianName" label="Guardian Name" disabled={ro} maxLength={100} />
                </Grid.Col>
                <Grid.Col span={{ base: 12, sm: 3 }}>
                  <NumberField control={c} name="monthlyIncome" label="Monthly Income" disabled={ro} min={0} />
                </Grid.Col>
                <Grid.Col span={{ base: 12, sm: 3 }}>
                  <SelectField control={c} name="maritalStatus" label="Marital Status" data={maritalChoices} disabled={ro} />
                </Grid.Col>
                <Grid.Col span={{ base: 12, sm: 6 }}>
                  <TextField control={c} name="motherName" label="Mother Name" disabled={ro} maxLength={100} />
                </Grid.Col>
                <Grid.Col span={{ base: 6, sm: 3 }}>
                  <SelectField control={c} name="gender" label="Gender" required data={genderChoices} disabled={ro} />
                </Grid.Col>
                <Grid.Col span={{ base: 6, sm: 3 }}>
                  <LookupField control={c} name="religionId" label="Religion" entity="religions" labelKey="religionName" disabled={ro} />
                </Grid.Col>
                <Grid.Col span={12}>
                  <Group gap="lg">
                    <Text size="sm" fw={500}>
                      Category
                    </Text>
                    <CheckField control={c} name="isSeniorCitizen" label="Senior Citizen" disabled={ro} />
                    <CheckField control={c} name="isHandicapped" label="Handicapped" disabled={ro} />
                    <CheckField control={c} name="isSpecialCategory" label="Special" disabled={ro} />
                  </Group>
                </Grid.Col>
              </Grid>
            </Section>

            <Section title="Contact Details">
              <Grid gap="sm">
                <Grid.Col span={{ base: 12, sm: 6 }}>
                  <TextField control={c} name="addressLine" label="Address" required multiline disabled={ro} maxLength={300} />
                </Grid.Col>
                <Grid.Col span={{ base: 12, sm: 6 }}>
                  <TextField control={c} name="permanentAddress" label="Permanent Address" multiline disabled={ro} maxLength={300} />
                  {!ro && (
                    <Checkbox
                      mt={4}
                      size="xs"
                      label="Same as member address"
                      onChange={(e) => e.currentTarget.checked && form.setValue('permanentAddress', form.getValues('addressLine'), { shouldDirty: true })}
                    />
                  )}
                </Grid.Col>
                <Grid.Col span={{ base: 12, sm: 4 }}>
                  <LookupField control={c} name="stateId" label="State" entity="states" labelKey="stateName" disabled={ro} onPicked={() => form.setValue('cityId', null)} />
                </Grid.Col>
                <Grid.Col span={{ base: 12, sm: 4 }}>
                  <LookupField control={c} name="cityId" label="City" entity="cities" labelKey="cityName" parentValue={stateId} disabled={ro} />
                </Grid.Col>
                <Grid.Col span={{ base: 12, sm: 4 }}>
                  <LookupField control={c} name="villageId" label="Village" entity="villages" labelKey="villageName" disabled={ro} />
                </Grid.Col>
                <Grid.Col span={{ base: 6, sm: 4 }}>
                  <LookupField control={c} name="countryId" label="Country" entity="countries" labelKey="countryName" disabled={ro} />
                </Grid.Col>
                <Grid.Col span={{ base: 6, sm: 2 }}>
                  <TextField control={c} name="pinCode" label="Pin Code" disabled={ro} maxLength={6} />
                </Grid.Col>
                <Grid.Col span={{ base: 6, sm: 3 }}>
                  <TextField control={c} name="mobileNo" label="Mobile No" disabled={ro} maxLength={10} />
                </Grid.Col>
                <Grid.Col span={{ base: 6, sm: 3 }}>
                  <TextField control={c} name="phoneNo" label="Phone No" disabled={ro} maxLength={20} />
                </Grid.Col>
                <Grid.Col span={{ base: 12, sm: 8 }}>
                  <TextField control={c} name="email" label="Email Id" disabled={ro} maxLength={120} />
                </Grid.Col>
                <Grid.Col span={{ base: 12, sm: 4 }}>
                  <TextField control={c} name="passportNo" label="Passport No" disabled={ro} maxLength={20} />
                </Grid.Col>
              </Grid>
            </Section>

            <SimpleGrid cols={{ base: 1, md: 2 }}>
              <Section
                title="Nominee Details"
                right={
                  !ro && (
                    <Button
                      size="compact-xs"
                      variant="white"
                      onClick={() => form.setValue('nomineeName', form.getValues('guardianName'), { shouldDirty: true })}
                    >
                      Same as guardian
                    </Button>
                  )
                }
              >
                <Grid gap="sm">
                  <Grid.Col span={12}>
                    <TextField control={c} name="nomineeName" label="Name" disabled={ro} maxLength={100} />
                  </Grid.Col>
                  <Grid.Col span={12}>
                    <TextField control={c} name="nomineeGuardianName" label="Guardian Name" disabled={ro} maxLength={100} />
                  </Grid.Col>
                  <Grid.Col span={3}>
                    <TextInput label="Age" value={ageOn(nomineeDob, today)} readOnly />
                  </Grid.Col>
                  <Grid.Col span={9}>
                    <DateField control={c} name="nomineeDob" label="DOB" disabled={ro} maxDate={today} />
                  </Grid.Col>
                  <Grid.Col span={6}>
                    <LookupField control={c} name="nomineeRelationId" label="Relation" entity="relations" labelKey="relationName" disabled={ro} />
                  </Grid.Col>
                  <Grid.Col span={6}>
                    <TextField control={c} name="refMemberNo" label="Ref Member No" disabled={ro} maxLength={20} />
                  </Grid.Col>
                </Grid>
              </Section>

              <Section title="Know Your Customer">
                <Group gap="md" mb="xs">
                  <CheckField control={c} name="kycAadhaar" label="Aadhaar Card" disabled={ro} />
                  <CheckField control={c} name="kycVoterId" label="Voter Id" disabled={ro} />
                  <CheckField control={c} name="kycDrivingLicense" label="Driving License" disabled={ro} />
                  <CheckField control={c} name="kycOtherProof" label="Other Proof" disabled={ro} />
                </Group>
                <Stack gap="sm">
                  <LookupField control={c} name="groupId" label="Group Name" entity="groups" labelKey="groupName" disabled={ro} />
                  <LookupField control={c} name="designationId" label="Designation" entity="designations" labelKey="designationName" disabled={ro} />
                  <TextField control={c} name="idProofNo" label="ID Proof No" disabled={ro} maxLength={30} />
                </Stack>
              </Section>
            </SimpleGrid>
          </Stack>
        </Grid.Col>

        <Grid.Col span={{ base: 12, lg: 4 }}>
          <Stack gap="md">
            <Section title="Bank Details">
              <Stack gap="sm">
                <TextField control={c} name="panNo" label="PAN No" disabled={ro} maxLength={10} />
                <LookupField control={c} name="bankId" label="Bank Name" entity="banks" labelKey="bankName" disabled={ro} />
                <TextField control={c} name="ifscCode" label="IFSC Code" disabled={ro} maxLength={11} />
                <TextField control={c} name="bankAccountNo" label="Account No" disabled={ro} maxLength={30} />
                <LookupField control={c} name="occupationId" label="Occupation" entity="occupations" labelKey="occupationName" disabled={ro} />
                <CheckField control={c} name="isActive" label="Active" disabled={ro} />
              </Stack>
            </Section>
            {ctl.record && ctl.mode === 'view' && <MemberPhotoPanel member={ctl.record} canEdit={canEdit} canVerify={canManage} />}
          </Stack>
        </Grid.Col>
      </Grid>

      <SearchModal<MemberListItem>
        opened={searchOpen}
        title="Search Member"
        url="/api/members"
        onClose={() => setSearchOpen(false)}
        onSelect={(row) => {
          setSearchOpen(false)
          navigate(`/members/${row.id}`)
        }}
        columns={[
          { label: 'Member No', render: (r) => r.memberNo },
          { label: 'File No', render: (r) => r.fileNo },
          { label: 'Name', render: (r) => r.memberName },
          { label: 'Guardian', render: (r) => r.guardianName },
          { label: 'Mobile', render: (r) => r.mobileNo },
          { label: 'Reg. Date', render: (r) => formatDate(r.registrationDate) },
          { label: 'Active', render: (r) => (r.isActive ? 'Yes' : 'No') },
        ]}
      />

      <Modal opened={confirmDelete} onClose={() => setConfirmDelete(false)} title="Delete member" centered>
        <Text mb="md">
          Delete member {ctl.record?.memberNo} - {ctl.record?.memberName}? This cannot be undone.
        </Text>
        <Group justify="flex-end">
          <Button variant="default" onClick={() => setConfirmDelete(false)}>
            Cancel
          </Button>
          <Button
            color="red"
            loading={ctl.deleting}
            onClick={() => {
              setConfirmDelete(false)
              ctl.remove()
            }}
          >
            Delete
          </Button>
        </Group>
      </Modal>
    </Stack>
  )
}
