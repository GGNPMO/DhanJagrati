import {
  ActionIcon,
  Alert,
  Badge,
  Button,
  Grid,
  Group,
  Modal,
  Paper,
  SimpleGrid,
  Stack,
  Table,
  Tabs,
  Text,
  TextInput,
} from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import {
  IconCalculator,
  IconCash,
  IconId,
  IconInfoCircle,
  IconPlus,
  IconSearch,
  IconTrash,
  IconUser,
  IconUserCheck,
} from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useFieldArray, useForm, useWatch } from 'react-hook-form'
import { useNavigate, useParams } from 'react-router'
import { api, ApiError, errorMessage } from '../api/client'
import { useMe } from '../auth/session'
import { DateField, LookupField, NumberField, SelectField, TextField } from '../components/fields'
import { formatAmount, formatDate } from '../components/format'
import { fromNum, fromSel, fromStr, toNum, toSel, toStr } from '../components/formValues'
import { PageHeader } from '../components/PageHeader'
import { RecordToolbar } from '../components/RecordToolbar'
import { SearchModal } from '../components/SearchModal'
import { Section } from '../components/Section'
import { useRecordController } from '../components/useRecordController'
import { useCodePicker } from '../components/useCodePicker'
import type { EmployeeListItem, EmployeeSummary } from '../employees/types'
import type { MemberListItem, MemberSummary } from '../members/types'
import { ScheduleTable } from './ScheduleTable'
import { emiModeLabel, statusColor } from './status'
import type { LoanDetail, LoanListItem, LoanTerms, ScheduleRow } from './types'

const disbursementModes = ['CASH', 'CHEQUE', 'NEFT', 'RTGS', 'IMPS'].map((v) => ({ value: v, label: v }))

type GuarantorForm = { memberNo: string; guarantorName: string; guardianName: string; addressLine: string; mobileNo: string; relationId: string | null; idProofNo: string }
type CollateralForm = { depositAccountNo: string; depositAmount: number | ''; maturityDate: string | null; lienAmount: number | '' }

type LoanForm = {
  applicationDate: string | null
  memberNo: string
  planId: string | null
  loanAmount: number | ''
  interestRate: number | ''
  emiAmount: number | ''
  noOfUnits: number | ''
  emiStartDate: string | null
  disbursementMode: string | null
  disbursementLedgerId: string | null
  chequeNo: string
  remark: string
  employeeCode: string
  groupId: string | null
  processingFeePct: number | ''
  processingFee: number | ''
  gstAmount: number | ''
  shareAmount: number | ''
  insuranceAmount: number | ''
  guarantors: GuarantorForm[]
  collaterals: CollateralForm[]
}

const emptyGuarantor = (): GuarantorForm => ({ memberNo: '', guarantorName: '', guardianName: '', addressLine: '', mobileNo: '', relationId: null, idProofNo: '' })
const emptyCollateral = (): CollateralForm => ({ depositAccountNo: '', depositAmount: '', maturityDate: null, lienAmount: '' })

function emptyLoan(): LoanForm {
  return {
    applicationDate: null,
    memberNo: '',
    planId: null,
    loanAmount: '',
    interestRate: '',
    emiAmount: '',
    noOfUnits: 1,
    emiStartDate: null,
    disbursementMode: 'CASH',
    disbursementLedgerId: null,
    chequeNo: '',
    remark: '',
    employeeCode: '',
    groupId: null,
    processingFeePct: '',
    processingFee: '',
    gstAmount: 0,
    shareAmount: 0,
    insuranceAmount: 0,
    guarantors: [],
    collaterals: [],
  }
}

function toForm(l: LoanDetail): LoanForm {
  return {
    applicationDate: l.applicationDate,
    memberNo: l.memberNo,
    planId: toSel(l.planId),
    loanAmount: l.loanAmount,
    interestRate: l.interestRate,
    emiAmount: l.emiAmount,
    noOfUnits: l.noOfUnits,
    emiStartDate: l.emiStartDate,
    disbursementMode: l.disbursementMode,
    disbursementLedgerId: toSel(l.disbursementLedgerId),
    chequeNo: toStr(l.chequeNo),
    remark: toStr(l.remark),
    employeeCode: l.employeeCode,
    groupId: toSel(l.groupId),
    processingFeePct: l.processingFeePct,
    processingFee: l.processingFee,
    gstAmount: l.gstAmount,
    shareAmount: l.shareAmount,
    insuranceAmount: l.insuranceAmount,
    guarantors: l.guarantors.map((g) => ({
      memberNo: toStr(g.memberNo),
      guarantorName: g.guarantorName,
      guardianName: toStr(g.guardianName),
      addressLine: toStr(g.addressLine),
      mobileNo: toStr(g.mobileNo),
      relationId: toSel(g.relationId),
      idProofNo: toStr(g.idProofNo),
    })),
    collaterals: l.collaterals.map((c) => ({
      depositAccountNo: c.depositAccountNo,
      depositAmount: c.depositAmount,
      maturityDate: c.maturityDate,
      lienAmount: toNum(c.lienAmount),
    })),
  }
}

function toPayload(f: LoanForm) {
  return {
    applicationDate: f.applicationDate,
    memberNo: fromStr(f.memberNo),
    planId: fromSel(f.planId) ?? 0,
    loanAmount: fromNum(f.loanAmount) ?? 0,
    interestRate: fromNum(f.interestRate),
    emiAmount: fromNum(f.emiAmount),
    noOfUnits: fromNum(f.noOfUnits) ?? 1,
    emiStartDate: f.emiStartDate,
    disbursementMode: f.disbursementMode,
    disbursementLedgerId: fromSel(f.disbursementLedgerId),
    chequeNo: fromStr(f.chequeNo),
    remark: fromStr(f.remark),
    employeeCode: fromStr(f.employeeCode),
    groupId: fromSel(f.groupId),
    processingFeePct: fromNum(f.processingFeePct),
    processingFee: fromNum(f.processingFee),
    gstAmount: fromNum(f.gstAmount) ?? 0,
    shareAmount: fromNum(f.shareAmount) ?? 0,
    insuranceAmount: fromNum(f.insuranceAmount) ?? 0,
    guarantors: f.guarantors.map((g) => ({
      memberNo: fromStr(g.memberNo),
      guarantorName: fromStr(g.guarantorName),
      guardianName: fromStr(g.guardianName),
      addressLine: fromStr(g.addressLine),
      mobileNo: fromStr(g.mobileNo),
      relationId: fromSel(g.relationId),
      idProofNo: fromStr(g.idProofNo),
    })),
    collaterals: f.collaterals.map((c) => ({
      depositAccountNo: fromStr(c.depositAccountNo),
      depositAmount: fromNum(c.depositAmount) ?? 0,
      maturityDate: c.maturityDate,
      lienAmount: fromNum(c.lienAmount),
    })),
  }
}

function Figure({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <Paper p="xs" radius="md" bg="var(--dj-page-bg)">
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Text fw={strong ? 700 : 600} size={strong ? 'lg' : 'sm'} style={{ fontVariantNumeric: 'tabular-nums' }}>
        {value}
      </Text>
    </Paper>
  )
}

/** LOAN MASTER > Loan Application (with loan calculator, deductions, guarantors and deposit). */
export function LoanApplicationPage() {
  const params = useParams()
  const id = params.id ? Number(params.id) : null
  const navigate = useNavigate()
  const { data: me } = useMe()
  const canEdit = me?.role === 'ADMIN' || me?.role === 'MANAGER' || me?.role === 'OPERATOR'
  const canDelete = me?.role === 'ADMIN' || me?.role === 'MANAGER'

  const form = useForm<LoanForm>({ defaultValues: emptyLoan() })
  const ctl = useRecordController<LoanDetail, LoanForm>({
    resource: 'loans',
    title: 'Loan application',
    id,
    form,
    empty: emptyLoan,
    toForm,
    toPayload,
    navKey: (l) => l.loanNo,
  })
  const guarantors = useFieldArray({ control: form.control, name: 'guarantors' })
  const collaterals = useFieldArray({ control: form.control, name: 'collaterals' })

  const [searchOpen, setSearchOpen] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [member, setMember] = useState<MemberSummary | null>(null)
  const [employee, setEmployee] = useState<EmployeeSummary | null>(null)
  const c = form.control
  const ro = !ctl.editing || !canEdit
  const record = ctl.record

  const [planId, loanAmount, interestRate, emiAmount, emiStartDate, memberNo, employeeCode, mode, fee, gst, share, insurance] = useWatch({
    control: c,
    name: ['planId', 'loanAmount', 'interestRate', 'emiAmount', 'emiStartDate', 'memberNo', 'employeeCode', 'disbursementMode', 'processingFee', 'gstAmount', 'shareAmount', 'insuranceAmount'],
  })

  // Member / employee details shown beside the codes
  const memberMatches = member && member.memberNo === memberNo?.trim().toUpperCase()
  const memberView = memberMatches
    ? { name: member.memberName, guardian: member.guardianName, address: member.addressLine, mobile: member.mobileNo, gender: member.gender }
    : record && record.memberNo === memberNo
      ? { name: record.memberName, guardian: record.guardianName, address: record.memberAddress, mobile: record.memberMobile, gender: record.gender, fileNo: record.fileNo }
      : null
  const employeeName =
    employee && employee.employeeCode === employeeCode?.trim().toUpperCase()
      ? employee.employeeName
      : record?.employeeCode === employeeCode
        ? record?.employeeName
        : null

  // Live calculator (only while editing)
  const calcInput = {
    planId: Number(planId) || 0,
    amount: Number(loanAmount) || 0,
    gender: memberView?.gender ?? 'M',
    rate: interestRate === '' ? null : Number(interestRate),
    emi: emiAmount === '' ? null : Number(emiAmount),
    emiStartDate,
  }
  const [debouncedCalc] = useDebouncedValue(calcInput, 400)
  const calc = useQuery({
    queryKey: ['loans', 'calculate', debouncedCalc],
    queryFn: () => api.post<LoanTerms>('/api/loans/calculate', debouncedCalc),
    enabled: ctl.editing && debouncedCalc.planId > 0 && debouncedCalc.amount > 0,
    retry: false,
  })
  const terms = ctl.editing ? calc.data : undefined

  const savedSchedule = useQuery({
    queryKey: ['loans', id, 'schedule'],
    queryFn: () => api.get<ScheduleRow[]>(`/api/loans/${id}/schedule`),
    enabled: id !== null && ctl.mode === 'view' && record?.status !== 'PENDING' && !!record,
  })

  const ledgers = useQuery({
    queryKey: ['accounts', 'cash-bank-ledgers'],
    queryFn: () => api.get<{ ledgerId: number; ledgerName: string; cashBankType: string }[]>('/api/accounts/cash-bank-ledgers'),
    staleTime: 5 * 60_000,
  })

  const effectiveFee = fee === '' ? (terms?.processingFee ?? 0) : Number(fee)
  const amount = Number(loanAmount) || 0
  const net = amount - effectiveFee - (Number(gst) || 0) - (Number(share) || 0) - (Number(insurance) || 0)

  const lookupMember = async (picked: string) => {
    const no = picked.trim()
    if (!no) return
    try {
      const m = await api.get<MemberSummary>(`/api/members/lookup?memberNo=${encodeURIComponent(no)}`)
      if (me && m.branchId !== me.branchId) throw new Error('Member belongs to another branch.')
      if (!m.isActive) throw new Error('Member is inactive.')
      setMember(m)
      form.setValue('memberNo', m.memberNo)
      form.clearErrors('memberNo')
    } catch (error) {
      setMember(null)
      form.setError('memberNo', { message: error instanceof ApiError && error.status === 404 ? 'Member not found.' : errorMessage(error) })
    }
  }

  const lookupEmployee = async (picked: string) => {
    const code = picked.trim()
    if (!code) return
    try {
      const e = await api.get<EmployeeSummary>(`/api/employees/lookup?code=${encodeURIComponent(code)}`)
      if (!e.isActive) throw new Error('Employee is inactive.')
      setEmployee(e)
      form.setValue('employeeCode', e.employeeCode)
      form.clearErrors('employeeCode')
    } catch (error) {
      setEmployee(null)
      form.setError('employeeCode', { message: error instanceof ApiError && error.status === 404 ? 'Employee code not found.' : errorMessage(error) })
    }
  }

  // Find buttons: exact code -> applied; partial number / name -> search dialog (members of this branch)
  const memberPicker = useCodePicker<MemberListItem>({
    searchUrl: '/api/members',
    codeOf: (m) => m.memberNo,
    onPicked: lookupMember,
    title: 'Select member',
    columns: [
      { label: 'Member No', render: (r) => r.memberNo },
      { label: 'Name', render: (r) => r.memberName },
      { label: 'Guardian', render: (r) => r.guardianName },
      { label: 'Mobile', render: (r) => r.mobileNo },
    ],
  })
  const employeePicker = useCodePicker<EmployeeListItem>({
    searchUrl: '/api/employees?allBranches=true&activeOnly=true',
    codeOf: (e) => e.employeeCode,
    onPicked: lookupEmployee,
    title: 'Select relationship manager',
    columns: [
      { label: 'Code', render: (r) => r.employeeCode },
      { label: 'Name', render: (r) => r.employeeName },
      { label: 'Rank', render: (r) => r.rankCode },
      { label: 'Branch', render: (r) => r.branchName },
    ],
  })

  const status = record?.status
  const calcError = calc.error instanceof ApiError ? Object.values(calc.error.fieldErrors)[0] ?? calc.error.message : calc.error ? errorMessage(calc.error) : null
  const schedule = ctl.editing ? (terms?.schedule ?? []) : record?.status === 'PENDING' ? [] : (savedSchedule.data ?? [])

  return (
    <Stack gap="md">
      <PageHeader
        title="Loan Application"
        subtitle={record ? `${record.loanNo} · ${record.memberName}` : 'Create a new loan application'}
        badge={
          ctl.mode !== 'view' ? (
            <Badge color="brand">{ctl.mode === 'new' ? 'New' : 'Editing'}</Badge>
          ) : status ? (
            <Badge color={statusColor(status)} variant="light">
              {status}
            </Badge>
          ) : undefined
        }
      />
      <RecordToolbar
        mode={ctl.mode}
        hasRecord={!!record}
        canEdit={canEdit}
        canDelete={canDelete}
        editLocked={!!record && record.status !== 'PENDING'}
        saving={ctl.saving}
        onAdd={() => {
          setMember(null)
          setEmployee(null)
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
      {record && record.status !== 'PENDING' && ctl.mode === 'view' && (
        <Alert color={statusColor(record.status)} variant="light">
          {record.status === 'APPROVED'
            ? `Approved and disbursed on ${formatDate(record.disbursedOn)} (voucher ${record.disbursementVoucherNo ?? '-'}). Approved loans cannot be edited.`
            : `This application is ${record.status}.`}
        </Alert>
      )}

      <Grid gap="md">
        <Grid.Col span={{ base: 12, lg: 8 }}>
          <Stack gap="md">
            <Section title="Application" icon={<IconId size={18} />}>
              <SimpleGrid cols={{ base: 1, xs: 3 }}>
                <TextInput label="Application No." value={record?.loanNo ?? (ctl.mode === 'new' ? '(new)' : '')} readOnly />
                <DateField control={c} name="applicationDate" label="App. Date" disabled={ro} maxDate={me?.today} description={ctl.mode === 'new' ? 'Blank = login date' : undefined} />
                <TextInput label="Branch" value={record?.branchName ?? me?.branchName ?? ''} readOnly />
              </SimpleGrid>
            </Section>

            <Section title="Member" icon={<IconUser size={18} />}>
              <Grid gap="sm" align="flex-end">
                <Grid.Col span={{ base: 8, sm: 4 }}>
                  <TextField control={c} name="memberNo" label="Member No." required disabled={ro} maxLength={20} />
                </Grid.Col>
                <Grid.Col span={{ base: 4, sm: 2 }}>
                  <Button fullWidth variant="light" leftSection={<IconSearch size={16} />} disabled={ro} onClick={() => memberPicker.find(form.getValues('memberNo'))}>
                    Find
                  </Button>
                </Grid.Col>
                <Grid.Col span={{ base: 12, sm: 6 }}>
                  <TextInput label="Member Name" value={memberView?.name ?? ''} readOnly />
                </Grid.Col>
                <Grid.Col span={{ base: 12, sm: 6 }}>
                  <TextInput label="Guardian Name" value={memberView?.guardian ?? ''} readOnly />
                </Grid.Col>
                <Grid.Col span={{ base: 6, sm: 3 }}>
                  <TextInput label="Mobile No" value={memberView?.mobile ?? ''} readOnly />
                </Grid.Col>
                <Grid.Col span={{ base: 6, sm: 3 }}>
                  <TextInput label="Gender" value={memberView?.gender === 'F' ? 'Female' : memberView?.gender === 'M' ? 'Male' : (memberView?.gender ?? '')} readOnly />
                </Grid.Col>
                <Grid.Col span={12}>
                  <TextInput label="Address" value={memberView?.address ?? ''} readOnly />
                </Grid.Col>
              </Grid>
            </Section>

            <Section
              title="Loan Calculator"
              icon={<IconCalculator size={18} />}
              right={terms && <Badge variant="light" color="navy">{terms.method} method</Badge>}
            >
              <Grid gap="sm">
                <Grid.Col span={{ base: 12, sm: 4 }}>
                  <LookupField control={c} name="planId" label="Loan Plan" required entity="loan-plans" labelKey="planCode" disabled={ro} />
                </Grid.Col>
                <Grid.Col span={{ base: 6, sm: 4 }}>
                  <NumberField control={c} name="loanAmount" label="Loan Amount" required disabled={ro} min={1} />
                </Grid.Col>
                <Grid.Col span={{ base: 6, sm: 4 }}>
                  <NumberField
                    control={c}
                    name="interestRate"
                    label="Rate %"
                    disabled={ro}
                    min={0}
                    max={100}
                    description={ctl.editing ? (terms ? `Blank = plan rate ${terms.rate}%` : 'Blank = plan rate') : undefined}
                  />
                </Grid.Col>
                <Grid.Col span={{ base: 6, sm: 4 }}>
                  <NumberField
                    control={c}
                    name="emiAmount"
                    label="EMI"
                    disabled={ro || (!!terms && terms.method !== 'MANUAL' && !terms.allowEmiOverride)}
                    min={1}
                    description={
                      ctl.editing && terms
                        ? terms.method === 'MANUAL'
                          ? 'Enter EMI (manual plan)'
                          : `Calculated ${formatAmount(terms.calculatedEmi)}${terms.allowEmiOverride ? ' · blank = calculated' : ''}`
                        : undefined
                    }
                  />
                </Grid.Col>
                <Grid.Col span={{ base: 6, sm: 4 }}>
                  <DateField control={c} name="emiStartDate" label="EMI Start Date" disabled={ro} description={ctl.editing && terms ? `Default ${formatDate(terms.firstDueDate)}` : undefined} />
                </Grid.Col>
                <Grid.Col span={{ base: 6, sm: 4 }}>
                  <NumberField control={c} name="noOfUnits" label="No. of Units" disabled={ro} min={1} decimals={0} />
                </Grid.Col>
              </Grid>
              {calcError && ctl.editing && (
                <Alert color="red" variant="light" mt="sm">
                  {calcError}
                </Alert>
              )}
              <SimpleGrid cols={{ base: 2, sm: 4 }} mt="md">
                <Figure label="EMI mode" value={emiModeLabel[terms?.emiMode ?? record?.emiMode ?? ''] ?? '-'} />
                <Figure label="Installments" value={String(terms?.installments ?? record?.noOfInstallments ?? '-')} />
                <Figure label="EMI" value={terms ? formatAmount(terms.emi) : record ? formatAmount(record.emiAmount) : '-'} strong />
                <Figure
                  label="Total repayable"
                  value={terms ? formatAmount(terms.totalRepayable) : record ? formatAmount(record.emiAmount * record.noOfInstallments) : '-'}
                />
              </SimpleGrid>
            </Section>

            <Paper className="dj-card">
              <Tabs defaultValue="deductions" keepMounted={false}>
                <Tabs.List px="sm" pt={4}>
                  <Tabs.Tab value="deductions">Deductions</Tabs.Tab>
                  <Tabs.Tab value="guarantors">Guarantors ({guarantors.fields.length})</Tabs.Tab>
                  <Tabs.Tab value="deposit">Loan against Deposit ({collaterals.fields.length})</Tabs.Tab>
                  <Tabs.Tab value="schedule">EMI Schedule</Tabs.Tab>
                </Tabs.List>

                <Tabs.Panel value="deductions" p="md">
                  <Grid gap="sm">
                    <Grid.Col span={{ base: 6, sm: 4 }}>
                      <NumberField control={c} name="processingFeePct" label="Processing %" disabled={ro} min={0} max={100} description={terms ? `Plan ${terms.processingFeePct}%` : undefined} />
                    </Grid.Col>
                    <Grid.Col span={{ base: 6, sm: 4 }}>
                      <NumberField control={c} name="processingFee" label="Processing Fee" disabled={ro} min={0} description={ctl.editing && terms ? `Blank = ${formatAmount(terms.processingFee)}` : undefined} />
                    </Grid.Col>
                    <Grid.Col span={{ base: 6, sm: 4 }}>
                      <NumberField control={c} name="gstAmount" label="GST Amt." disabled={ro} min={0} />
                    </Grid.Col>
                    <Grid.Col span={{ base: 6, sm: 4 }}>
                      <NumberField control={c} name="shareAmount" label="Share Amt." disabled={ro} min={0} />
                    </Grid.Col>
                    <Grid.Col span={{ base: 6, sm: 4 }}>
                      <NumberField control={c} name="insuranceAmount" label="Insurance Amt." disabled={ro} min={0} />
                    </Grid.Col>
                    <Grid.Col span={{ base: 6, sm: 4 }}>
                      <TextInput label="Net amount paid" value={amount ? formatAmount(net) : ''} readOnly styles={{ input: { fontWeight: 700 } }} />
                    </Grid.Col>
                  </Grid>
                </Tabs.Panel>

                <Tabs.Panel value="guarantors" p="md">
                  <Stack gap="sm">
                    {guarantors.fields.map((g, index) => (
                      <Paper key={g.id} p="sm" withBorder radius="md">
                        <Grid gap="xs" align="flex-end">
                          <Grid.Col span={{ base: 6, sm: 3 }}>
                            <TextField control={c} name={`guarantors.${index}.memberNo`} label="Member No (optional)" disabled={ro} maxLength={20} />
                          </Grid.Col>
                          <Grid.Col span={{ base: 6, sm: 4 }}>
                            <TextField control={c} name={`guarantors.${index}.guarantorName`} label="Name" required disabled={ro} maxLength={100} />
                          </Grid.Col>
                          <Grid.Col span={{ base: 10, sm: 4 }}>
                            <TextField control={c} name={`guarantors.${index}.guardianName`} label="Guardian" disabled={ro} maxLength={100} />
                          </Grid.Col>
                          <Grid.Col span={{ base: 2, sm: 1 }}>
                            <ActionIcon color="red" variant="subtle" size="lg" disabled={ro} onClick={() => guarantors.remove(index)} aria-label="Remove guarantor">
                              <IconTrash size={16} />
                            </ActionIcon>
                          </Grid.Col>
                          <Grid.Col span={{ base: 12, sm: 5 }}>
                            <TextField control={c} name={`guarantors.${index}.addressLine`} label="Address" disabled={ro} maxLength={300} />
                          </Grid.Col>
                          <Grid.Col span={{ base: 6, sm: 2 }}>
                            <TextField control={c} name={`guarantors.${index}.mobileNo`} label="Mobile" disabled={ro} maxLength={10} />
                          </Grid.Col>
                          <Grid.Col span={{ base: 6, sm: 2 }}>
                            <LookupField control={c} name={`guarantors.${index}.relationId`} label="Relation" entity="relations" labelKey="relationName" disabled={ro} />
                          </Grid.Col>
                          <Grid.Col span={{ base: 12, sm: 3 }}>
                            <TextField control={c} name={`guarantors.${index}.idProofNo`} label="ID Proof No" disabled={ro} maxLength={30} />
                          </Grid.Col>
                        </Grid>
                      </Paper>
                    ))}
                    {guarantors.fields.length === 0 && (
                      <Text size="sm" c="dimmed">
                        No guarantor added.
                      </Text>
                    )}
                    {!ro && (
                      <Group>
                        <Button variant="light" size="xs" leftSection={<IconPlus size={14} />} onClick={() => guarantors.append(emptyGuarantor())}>
                          Add guarantor
                        </Button>
                      </Group>
                    )}
                  </Stack>
                </Tabs.Panel>

                <Tabs.Panel value="deposit" p="md">
                  <Stack gap="sm">
                    {collaterals.fields.map((x, index) => (
                      <Grid key={x.id} gap="xs" align="flex-end">
                        <Grid.Col span={{ base: 6, sm: 3 }}>
                          <TextField control={c} name={`collaterals.${index}.depositAccountNo`} label="Deposit A/c No" required disabled={ro} maxLength={30} />
                        </Grid.Col>
                        <Grid.Col span={{ base: 6, sm: 3 }}>
                          <NumberField control={c} name={`collaterals.${index}.depositAmount`} label="Deposit Amount" required disabled={ro} min={1} />
                        </Grid.Col>
                        <Grid.Col span={{ base: 6, sm: 3 }}>
                          <DateField control={c} name={`collaterals.${index}.maturityDate`} label="Maturity" disabled={ro} />
                        </Grid.Col>
                        <Grid.Col span={{ base: 4, sm: 2 }}>
                          <NumberField control={c} name={`collaterals.${index}.lienAmount`} label="Lien Amt." disabled={ro} min={0} />
                        </Grid.Col>
                        <Grid.Col span={{ base: 2, sm: 1 }}>
                          <ActionIcon color="red" variant="subtle" size="lg" disabled={ro} onClick={() => collaterals.remove(index)} aria-label="Remove deposit">
                            <IconTrash size={16} />
                          </ActionIcon>
                        </Grid.Col>
                      </Grid>
                    ))}
                    {collaterals.fields.length === 0 && (
                      <Text size="sm" c="dimmed">
                        No deposit pledged.
                      </Text>
                    )}
                    {!ro && (
                      <Group>
                        <Button variant="light" size="xs" leftSection={<IconPlus size={14} />} onClick={() => collaterals.append(emptyCollateral())}>
                          Add deposit
                        </Button>
                      </Group>
                    )}
                  </Stack>
                </Tabs.Panel>

                <Tabs.Panel value="schedule" p="md">
                  {schedule.length > 0 ? (
                    <ScheduleTable rows={schedule} />
                  ) : (
                    <Text size="sm" c="dimmed">
                      {ctl.editing ? 'Enter plan and amount to preview the schedule.' : 'The schedule is generated when the loan is approved.'}
                    </Text>
                  )}
                </Tabs.Panel>
              </Tabs>
            </Paper>
          </Stack>
        </Grid.Col>

        <Grid.Col span={{ base: 12, lg: 4 }}>
          <Stack gap="md">
            <Section title="Disbursement" icon={<IconCash size={18} />}>
              <Stack gap="sm">
                <SelectField control={c} name="disbursementMode" label="Mode" required data={disbursementModes} disabled={ro} />
                <SelectField
                  control={c}
                  name="disbursementLedgerId"
                  label="Paid from (cash / bank)"
                  data={(ledgers.data ?? []).map((l) => ({ value: String(l.ledgerId), label: l.ledgerName }))}
                  disabled={ro}
                  placeholder="CASH A/C"
                />
                {mode !== 'CASH' && <TextField control={c} name="chequeNo" label="Cheque / Ref No" required={mode === 'CHEQUE'} disabled={ro} maxLength={30} />}
                <TextInput label={mode === 'CASH' ? 'Cash to pay' : 'Cheque amount'} value={amount ? formatAmount(net) : ''} readOnly />
                <TextField control={c} name="remark" label="Remark" multiline disabled={ro} maxLength={500} />
              </Stack>
            </Section>

            <Section title="Relationship" icon={<IconUserCheck size={18} />}>
              <Stack gap="sm">
                <Group align="flex-end" gap="xs" wrap="nowrap">
                  <div style={{ flex: 1 }}>
                    <TextField control={c} name="employeeCode" label="Emp. Code" required disabled={ro} maxLength={20} />
                  </div>
                  <Button variant="light" disabled={ro} onClick={() => employeePicker.find(form.getValues('employeeCode'))}>
                    Find
                  </Button>
                </Group>
                <TextInput label="Emp. Name" value={employeeName ?? ''} readOnly />
                <LookupField control={c} name="groupId" label="Group Name" entity="groups" labelKey="groupName" disabled={ro} />
              </Stack>
            </Section>

            {record && (
              <Section title="Status" icon={<IconInfoCircle size={18} />}>
                <Table fz="sm">
                  <Table.Tbody>
                    <Table.Tr>
                      <Table.Td c="dimmed">Status</Table.Td>
                      <Table.Td>
                        <Badge variant="light" color={statusColor(record.status)}>
                          {record.status}
                        </Badge>
                      </Table.Td>
                    </Table.Tr>
                    <Table.Tr>
                      <Table.Td c="dimmed">EMI method</Table.Td>
                      <Table.Td>
                        {record.emiCalcMethod ?? '-'}
                        {record.isEmiOverridden && ' (overridden)'}
                      </Table.Td>
                    </Table.Tr>
                    <Table.Tr>
                      <Table.Td c="dimmed">Approved on</Table.Td>
                      <Table.Td>{formatDate(record.approvedOn) || '-'}</Table.Td>
                    </Table.Tr>
                    <Table.Tr>
                      <Table.Td c="dimmed">Voucher</Table.Td>
                      <Table.Td>{record.disbursementVoucherNo ?? '-'}</Table.Td>
                    </Table.Tr>
                  </Table.Tbody>
                </Table>
              </Section>
            )}
          </Stack>
        </Grid.Col>
      </Grid>

      {memberPicker.modal}
      {employeePicker.modal}
      <SearchModal<LoanListItem>
        opened={searchOpen}
        title="Search loan application"
        url="/api/loans"
        onClose={() => setSearchOpen(false)}
        onSelect={(row) => {
          setSearchOpen(false)
          navigate(`/loans/${row.id}`)
        }}
        columns={[
          { label: 'Loan No', render: (r) => r.loanNo },
          { label: 'Date', render: (r) => formatDate(r.applicationDate) },
          { label: 'Member', render: (r) => `${r.memberNo} - ${r.memberName}` },
          { label: 'Plan', render: (r) => r.planCode },
          { label: 'Amount', render: (r) => formatAmount(r.loanAmount) },
          {
            label: 'Status',
            render: (r) => (
              <Badge size="sm" variant="light" color={statusColor(r.status)}>
                {r.status}
              </Badge>
            ),
          },
        ]}
      />

      <Modal opened={confirmDelete} onClose={() => setConfirmDelete(false)} title="Delete application" centered>
        <Text mb="md">Delete application {record?.loanNo}? This cannot be undone.</Text>
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
