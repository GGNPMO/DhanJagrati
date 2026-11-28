import { Alert, Badge, Button, Grid, Group, Modal, ScrollArea, SimpleGrid, Stack, Table, Text, TextInput } from '@mantine/core'
import { PageHeader } from '../components/PageHeader'
import { notifications } from '@mantine/notifications'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useNavigate, useParams } from 'react-router'
import { api, ApiError, errorMessage } from '../api/client'
import { useMe } from '../auth/session'
import { CheckField, DateField, LookupField, NumberField, SelectField, TextField } from '../components/fields'
import { ageOn, fromSel, fromStr, genderChoices, toSel, toStr } from '../components/formValues'
import { formatDate } from '../components/format'
import { RecordToolbar } from '../components/RecordToolbar'
import { SearchModal } from '../components/SearchModal'
import { Section } from '../components/Section'
import { useRecordController } from '../components/useRecordController'
import { useCodePicker } from '../components/useCodePicker'
import type { MemberListItem, MemberSummary } from '../members/types'
import type { ChainLink, EmployeeDetail, EmployeeListItem, EmployeeSummary } from './types'

const feeModes = ['CASH', 'CHEQUE', 'NEFT', 'UPI'].map((v) => ({ value: v, label: v }))

type EmployeeForm = {
  joiningDate: string | null
  introducerCode: string
  initialRankId: string | null
  currentRankId: string | null
  memberNo: string
  employeeName: string
  guardianName: string
  dateOfBirth: string | null
  gender: string | null
  addressLine: string
  cityId: string | null
  stateId: string | null
  countryId: string | null
  pinCode: string
  mobileNo: string
  phoneNo: string
  email: string
  passportNo: string
  nomineeName: string
  nomineeDob: string | null
  nomineeRelationId: string | null
  joiningFee: number | ''
  joiningFeeMode: string | null
  joiningFeeRefNo: string
  isActive: boolean
}

const textKeys = [
  'introducerCode', 'memberNo', 'employeeName', 'guardianName', 'addressLine', 'pinCode', 'mobileNo', 'phoneNo',
  'email', 'passportNo', 'nomineeName', 'joiningFeeRefNo',
] as const
const selectKeys = ['initialRankId', 'currentRankId', 'cityId', 'stateId', 'countryId', 'nomineeRelationId'] as const

function emptyEmployee(): EmployeeForm {
  const form = {} as Record<string, unknown>
  for (const k of textKeys) form[k] = ''
  for (const k of selectKeys) form[k] = null
  return { ...(form as EmployeeForm), joiningDate: null, dateOfBirth: null, gender: null, nomineeDob: null, joiningFee: 0, joiningFeeMode: null, isActive: true }
}

function toForm(e: EmployeeDetail): EmployeeForm {
  const form = {} as Record<string, unknown>
  for (const k of textKeys) form[k] = toStr(e[k])
  for (const k of selectKeys) form[k] = toSel(e[k])
  return {
    ...(form as EmployeeForm),
    joiningDate: e.joiningDate,
    dateOfBirth: e.dateOfBirth,
    gender: e.gender,
    nomineeDob: e.nomineeDob,
    joiningFee: e.joiningFee,
    joiningFeeMode: e.joiningFeeMode,
    isActive: e.isActive,
  }
}

function toPayload(f: EmployeeForm) {
  const payload: Record<string, unknown> = {}
  for (const k of textKeys) payload[k] = fromStr(f[k])
  for (const k of selectKeys) payload[k] = fromSel(f[k])
  return {
    ...payload,
    joiningDate: f.joiningDate,
    dateOfBirth: f.dateOfBirth,
    gender: f.gender,
    nomineeDob: f.nomineeDob,
    joiningFee: f.joiningFee === '' ? 0 : Number(f.joiningFee),
    joiningFeeMode: f.joiningFeeMode,
    isActive: f.isActive,
  }
}

/** EMPLOYEE > Advisor / Loan Relationship Manager ("Advisor Entry Form"). */
export function EmployeePage() {
  const params = useParams()
  const id = params.id ? Number(params.id) : null
  const navigate = useNavigate()
  const { data: me } = useMe()
  const canEdit = me?.role === 'ADMIN' || me?.role === 'MANAGER'

  const form = useForm<EmployeeForm>({ defaultValues: emptyEmployee() })
  const ctl = useRecordController<EmployeeDetail, EmployeeForm>({
    resource: 'employees',
    title: 'Advisor',
    id,
    form,
    empty: emptyEmployee,
    toForm,
    toPayload,
    navKey: (e) => e.employeeCode,
  })

  const [searchOpen, setSearchOpen] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [introducer, setIntroducer] = useState<EmployeeSummary | null>(null)
  const c = form.control
  const ro = !ctl.editing || !canEdit
  const [stateId, dob, introducerCode] = useWatch({ control: c, name: ['stateId', 'dateOfBirth', 'introducerCode'] })

  const chain = useQuery({
    queryKey: ['employees', id, 'chain'],
    queryFn: () => api.get<ChainLink[]>(`/api/employees/${id}/parent-chain`),
    enabled: id !== null && ctl.mode === 'view',
  })

  // Introducer name/rank: from the saved record, or from the last lookup while editing.
  const introducerName = introducer?.employeeCode === introducerCode?.trim().toUpperCase()
    ? introducer.employeeName
    : ctl.record?.introducerCode === introducerCode
      ? ctl.record?.introducerName
      : null
  const introducerRank = introducer?.employeeCode === introducerCode?.trim().toUpperCase() ? introducer.rankNo : ctl.record?.introducerRankNo

  const lookupIntroducer = async (picked: string) => {
    form.setValue('introducerCode', picked, { shouldDirty: true })
    const code = picked.trim()
    if (!code) {
      setIntroducer(null)
      return
    }
    try {
      const found = await api.get<EmployeeSummary>(`/api/employees/lookup?code=${encodeURIComponent(code)}`)
      setIntroducer(found)
      form.clearErrors('introducerCode')
      if (!found.isActive) notifications.show({ color: 'orange', message: `${found.employeeCode} is inactive.` })
    } catch (error) {
      setIntroducer(null)
      form.setError('introducerCode', { message: error instanceof ApiError && error.status === 404 ? 'Introducer code not found.' : errorMessage(error) })
    }
  }

  /** Fill personal details from an existing member (advisor joins as a member). */
  const fetchMember = async (picked: string) => {
    const memberNo = picked.trim()
    if (!memberNo) return
    try {
      const m = await api.get<MemberSummary>(`/api/members/lookup?memberNo=${encodeURIComponent(memberNo)}`)
      const set = (name: keyof EmployeeForm, value: unknown) => form.setValue(name, value as never, { shouldDirty: true })
      set('memberNo', m.memberNo)
      set('employeeName', m.memberName)
      set('guardianName', m.guardianName ?? '')
      set('gender', m.gender)
      set('dateOfBirth', m.dateOfBirth)
      set('addressLine', m.addressLine)
      set('stateId', toSel(m.stateId))
      set('cityId', toSel(m.cityId))
      set('countryId', toSel(m.countryId))
      set('pinCode', m.pinCode ?? '')
      set('mobileNo', m.mobileNo ?? '')
      set('email', m.email ?? '')
      form.clearErrors('memberNo')
    } catch (error) {
      form.setError('memberNo', { message: error instanceof ApiError && error.status === 404 ? 'Member not found.' : errorMessage(error) })
    }
  }

  const memberPicker = useCodePicker<MemberListItem>({
    searchUrl: '/api/members',
    codeOf: (m) => m.memberNo,
    onPicked: fetchMember,
    title: 'Select member',
    columns: [
      { label: 'Member No', render: (r) => r.memberNo },
      { label: 'Name', render: (r) => r.memberName },
      { label: 'Guardian', render: (r) => r.guardianName },
      { label: 'Mobile', render: (r) => r.mobileNo },
    ],
  })

  const introducerPicker = useCodePicker<EmployeeListItem>({
    searchUrl: '/api/employees?allBranches=true&activeOnly=true',
    codeOf: (e) => e.employeeCode,
    onPicked: lookupIntroducer,
    title: 'Select introducer',
    columns: [
      { label: 'Code', render: (r) => r.employeeCode },
      { label: 'Name', render: (r) => r.employeeName },
      { label: 'Rank', render: (r) => r.rankCode },
      { label: 'Branch', render: (r) => r.branchName },
    ],
  })

  return (
    <Stack gap="md">
      <PageHeader
        title="Advisor / Relationship Manager"
        subtitle={ctl.record ? `${ctl.record.employeeCode} · ${ctl.record.employeeName}` : 'Register a new advisor'}
        badge={ctl.mode !== 'view' ? <Badge color="brand">{ctl.mode === 'new' ? 'New advisor' : 'Editing'}</Badge> : undefined}
      />
      <RecordToolbar
        mode={ctl.mode}
        hasRecord={!!ctl.record}
        canEdit={canEdit}
        canDelete={canEdit}
        saving={ctl.saving}
        onAdd={() => {
          setIntroducer(null)
          ctl.add()
        }}
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

      <Grid gap="md">
        <Grid.Col span={{ base: 12, lg: 8 }}>
          <Stack gap="md">
            <Section title="Transaction">
              <SimpleGrid cols={{ base: 1, sm: 3 }}>
                <TextInput label="Advisor Code" value={ctl.record?.employeeCode ?? (ctl.mode === 'new' ? '(new)' : '')} readOnly />
                <DateField control={c} name="joiningDate" label="Joining Date" disabled={ro} maxDate={me?.today} description={ctl.mode === 'new' ? 'Blank = login date' : undefined} />
                <TextInput label="Branch" value={ctl.record?.branchName ?? me?.branchName ?? ''} readOnly />
              </SimpleGrid>
            </Section>

            <Section title="Introducer & Rank">
              <Grid gap="sm" align="end">
                <Grid.Col span={{ base: 6, sm: 3 }}>
                  <TextField control={c} name="introducerCode" label="Introducer Code" disabled={ro} maxLength={20} placeholder="Blank = company" />
                </Grid.Col>
                <Grid.Col span={{ base: 6, sm: 2 }}>
                  <Button variant="light" fullWidth disabled={ro} onClick={() => introducerPicker.find(form.getValues('introducerCode'))}>
                    Find
                  </Button>
                </Grid.Col>
                <Grid.Col span={{ base: 12, sm: 7 }}>
                  <TextInput label="Introducer Name" value={introducerCode?.trim() ? (introducerName ?? '') : (me?.companyName ?? 'COMPANY')} readOnly />
                </Grid.Col>
                <Grid.Col span={{ base: 4 }}>
                  <TextInput label="Intro Rank" value={introducerRank ?? ''} readOnly />
                </Grid.Col>
                <Grid.Col span={{ base: 4 }}>
                  <LookupField control={c} name="currentRankId" label="Current Rank" entity="advisor-ranks" labelKey="rankCode" disabled={ro} />
                </Grid.Col>
                <Grid.Col span={{ base: 4 }}>
                  <LookupField control={c} name="initialRankId" label="Initial Rank" entity="advisor-ranks" labelKey="rankCode" disabled={ro} />
                </Grid.Col>
              </Grid>
            </Section>

            <Section title="Details">
              <Grid gap="sm" align="end">
                <Grid.Col span={{ base: 8, sm: 4 }}>
                  <TextField control={c} name="memberNo" label="Member Code" disabled={ro} maxLength={20} />
                </Grid.Col>
                <Grid.Col span={{ base: 4, sm: 2 }}>
                  <Button variant="light" fullWidth disabled={ro} onClick={() => memberPicker.find(form.getValues('memberNo'))}>
                    Fetch
                  </Button>
                </Grid.Col>
                <Grid.Col span={{ base: 12, sm: 6 }}>
                  <TextField control={c} name="employeeName" label="Name" required disabled={ro} maxLength={100} />
                </Grid.Col>
                <Grid.Col span={{ base: 12, sm: 6 }}>
                  <TextField control={c} name="guardianName" label="Guardian" disabled={ro} maxLength={100} />
                </Grid.Col>
                <Grid.Col span={{ base: 4, sm: 2 }}>
                  <TextInput label="Age" value={ageOn(dob, me?.today)} readOnly />
                </Grid.Col>
                <Grid.Col span={{ base: 8, sm: 2 }}>
                  <DateField control={c} name="dateOfBirth" label="DOB" disabled={ro} maxDate={me?.today} />
                </Grid.Col>
                <Grid.Col span={{ base: 12, sm: 2 }}>
                  <SelectField control={c} name="gender" label="Gender" data={genderChoices} disabled={ro} />
                </Grid.Col>
              </Grid>
            </Section>

            <Section title="Address & Contact">
              <Grid gap="sm">
                <Grid.Col span={{ base: 12, sm: 6 }}>
                  <TextField control={c} name="addressLine" label="Address" multiline disabled={ro} maxLength={300} />
                </Grid.Col>
                <Grid.Col span={{ base: 12, sm: 6 }}>
                  <Stack gap="sm">
                    <LookupField control={c} name="stateId" label="State" entity="states" labelKey="stateName" disabled={ro} onPicked={() => form.setValue('cityId', null)} />
                    <LookupField control={c} name="cityId" label="City" entity="cities" labelKey="cityName" parentValue={stateId} disabled={ro} />
                  </Stack>
                </Grid.Col>
                <Grid.Col span={{ base: 6, sm: 3 }}>
                  <TextField control={c} name="pinCode" label="Pin" disabled={ro} maxLength={6} />
                </Grid.Col>
                <Grid.Col span={{ base: 6, sm: 3 }}>
                  <LookupField control={c} name="countryId" label="Country" entity="countries" labelKey="countryName" disabled={ro} />
                </Grid.Col>
                <Grid.Col span={{ base: 6, sm: 3 }}>
                  <TextField control={c} name="mobileNo" label="Mob No" disabled={ro} maxLength={10} />
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
              <Section title="Nominee Details">
                <Stack gap="sm">
                  <TextField control={c} name="nomineeName" label="Name" disabled={ro} maxLength={100} />
                  <DateField control={c} name="nomineeDob" label="DOB" disabled={ro} maxDate={me?.today} />
                  <LookupField control={c} name="nomineeRelationId" label="Relation" entity="relations" labelKey="relationName" disabled={ro} />
                </Stack>
              </Section>
              <Section title="Joining Fee">
                <Stack gap="sm">
                  <NumberField control={c} name="joiningFee" label="Amount" disabled={ro} min={0} />
                  <SelectField control={c} name="joiningFeeMode" label="Mode" data={feeModes} disabled={ro} />
                  <TextField control={c} name="joiningFeeRefNo" label="Receipt / Ref No" disabled={ro} maxLength={40} />
                  <CheckField control={c} name="isActive" label="Active" disabled={ro} />
                  <Text size="xs" c="dimmed">
                    The joining-fee voucher is posted once the Accounts module (Phase 5) is live.
                  </Text>
                </Stack>
              </Section>
            </SimpleGrid>
          </Stack>
        </Grid.Col>

        <Grid.Col span={{ base: 12, lg: 4 }}>
          <Section title="Parent Chain">
            {!id || ctl.mode !== 'view' ? (
              <Text size="sm" c="dimmed">
                Shown for a saved advisor.
              </Text>
            ) : (
              <ScrollArea.Autosize mah={520}>
                <Table striped withTableBorder>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>Advisor Code</Table.Th>
                      <Table.Th>Rank</Table.Th>
                      <Table.Th>Advisor Name</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {(chain.data ?? []).map((link) => (
                      <Table.Tr key={link.level} fw={link.level === 0 ? 700 : undefined}>
                        <Table.Td>{link.advisorCode}</Table.Td>
                        <Table.Td>{link.advisorRank}</Table.Td>
                        <Table.Td>{link.advisorName}</Table.Td>
                      </Table.Tr>
                    ))}
                    <Table.Tr>
                      <Table.Td colSpan={3}>
                        <Text size="xs" c="dimmed">
                          Root: {me?.companyName}
                        </Text>
                      </Table.Td>
                    </Table.Tr>
                  </Table.Tbody>
                </Table>
              </ScrollArea.Autosize>
            )}
          </Section>
        </Grid.Col>
      </Grid>

      {memberPicker.modal}
      {introducerPicker.modal}
      <SearchModal<EmployeeListItem>
        opened={searchOpen}
        title="Search Advisor"
        url="/api/employees"
        onClose={() => setSearchOpen(false)}
        onSelect={(row) => {
          setSearchOpen(false)
          navigate(`/employees/${row.id}`)
        }}
        columns={[
          { label: 'Code', render: (r) => r.employeeCode },
          { label: 'Name', render: (r) => r.employeeName },
          { label: 'Rank', render: (r) => r.rankCode },
          { label: 'Introducer', render: (r) => r.introducerCode },
          { label: 'Mobile', render: (r) => r.mobileNo },
          { label: 'Joined', render: (r) => formatDate(r.joiningDate) },
          { label: 'Active', render: (r) => (r.isActive ? 'Yes' : 'No') },
        ]}
      />

      <Modal opened={confirmDelete} onClose={() => setConfirmDelete(false)} title="Delete advisor" centered>
        <Text mb="md">
          Delete advisor {ctl.record?.employeeCode} - {ctl.record?.employeeName}? This cannot be undone.
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
