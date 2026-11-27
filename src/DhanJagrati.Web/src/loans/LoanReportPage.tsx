import { Badge, Button, Group, SegmentedControl, Select, Table, Text, TextInput } from '@mantine/core'
import { IconPlayerPlay } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { Navigate, useParams } from 'react-router'
import { api, query } from '../api/client'
import { downloadCsv, useReportBranches } from '../accounts/reportKit'
import { ReportFilters, type RangeFilter } from '../accounts/ReportFilters'
import { ReportFrame } from '../accounts/ReportFrame'
import { useMe } from '../auth/session'
import { LookupField } from '../components/fields'
import { formatAmount, formatDate } from '../components/format'
import { emiModeLabel, statusColor } from './status'

type ColumnType = 'amount' | 'date' | 'int' | 'status' | 'emiMode'
interface Column {
  key: string
  label: string
  type?: ColumnType
  total?: boolean
}
type Filter = 'reportType' | 'loanNo' | 'memberNo' | 'plan' | 'employeeCode' | 'group' | 'emiMode' | 'employeeName' | 'rank'

interface ReportDef {
  title: string
  subtitle: string
  endpoint: string
  /** false = company-wide report without branch/date filters. */
  ranged: boolean
  defaultFrom: 'month' | 'today'
  filters: Filter[]
  columns: Column[]
}

const reports: Record<string, ReportDef> = {
  applications: {
    title: 'Loan Application Report',
    subtitle: 'Applications, sanctions, payments and loans with saving',
    endpoint: '/api/loan-reports/applications',
    ranged: true,
    defaultFrom: 'month',
    filters: ['reportType', 'loanNo', 'memberNo', 'plan'],
    columns: [
      { key: 'loanNo', label: 'Loan No' },
      { key: 'applicationDate', label: 'App. date', type: 'date' },
      { key: 'disbursedOn', label: 'Paid on', type: 'date' },
      { key: 'memberNo', label: 'Member No' },
      { key: 'memberName', label: 'Member' },
      { key: 'planCode', label: 'Plan' },
      { key: 'loanAmount', label: 'Amount', type: 'amount', total: true },
      { key: 'emiAmount', label: 'EMI', type: 'amount' },
      { key: 'processingFee', label: 'Proc. fee', type: 'amount', total: true },
      { key: 'netDisbursedAmount', label: 'Net paid', type: 'amount', total: true },
      { key: 'employeeCode', label: 'Emp. code' },
      { key: 'status', label: 'Status', type: 'status' },
    ],
  },
  'recovery-due': {
    title: 'Loan Recovery Due Report',
    subtitle: 'Instalment reminder: EMIs due in the period',
    endpoint: '/api/loan-reports/recovery-due',
    ranged: true,
    defaultFrom: 'today',
    filters: ['employeeCode', 'group', 'emiMode'],
    columns: [
      { key: 'loanNo', label: 'Loan No' },
      { key: 'memberNo', label: 'Member No' },
      { key: 'memberName', label: 'Member' },
      { key: 'mobileNo', label: 'Mobile' },
      { key: 'installmentNo', label: 'EMI No', type: 'int' },
      { key: 'dueDate', label: 'Due date', type: 'date' },
      { key: 'emiAmount', label: 'EMI', type: 'amount' },
      { key: 'dueAmount', label: 'Due', type: 'amount', total: true },
      { key: 'emiMode', label: 'Mode', type: 'emiMode' },
      { key: 'employeeCode', label: 'Emp. code' },
      { key: 'employeeName', label: 'Employee' },
      { key: 'groupName', label: 'Group' },
    ],
  },
  'recovery-ledger': {
    title: 'Loan Recovery Ledger',
    subtitle: 'EMIs received in the period',
    endpoint: '/api/loan-reports/recovery-ledger',
    ranged: true,
    defaultFrom: 'today',
    filters: ['employeeCode', 'loanNo'],
    columns: [
      { key: 'transDate', label: 'Date', type: 'date' },
      { key: 'receiptNo', label: 'Receipt' },
      { key: 'loanNo', label: 'Loan No' },
      { key: 'memberName', label: 'Member' },
      { key: 'installmentNo', label: 'EMI No', type: 'int' },
      { key: 'principalAmount', label: 'Principal', type: 'amount', total: true },
      { key: 'interestAmount', label: 'Interest', type: 'amount', total: true },
      { key: 'lateFeePaid', label: 'Late fee', type: 'amount', total: true },
      { key: 'netAmount', label: 'Received', type: 'amount', total: true },
      { key: 'paymentMode', label: 'Mode' },
      { key: 'employeeCode', label: 'BDA / Emp.' },
    ],
  },
  closures: {
    title: 'Loan Closer Report',
    subtitle: 'Loans closed by settlement or full repayment',
    endpoint: '/api/loan-reports/closures',
    ranged: true,
    defaultFrom: 'month',
    filters: ['loanNo'],
    columns: [
      { key: 'closeDate', label: 'Closed on', type: 'date' },
      { key: 'loanNo', label: 'Loan No' },
      { key: 'memberName', label: 'Member' },
      { key: 'loanAmount', label: 'Loan', type: 'amount', total: true },
      { key: 'closedBy', label: 'Closed by' },
      { key: 'totalPrincipalReceived', label: 'Principal rec.', type: 'amount', total: true },
      { key: 'totalInterestReceived', label: 'Interest rec.', type: 'amount', total: true },
      { key: 'interestWaived', label: 'Interest waived', type: 'amount', total: true },
      { key: 'lateFeeReceived', label: 'Late fee', type: 'amount', total: true },
      { key: 'preCloseCharges', label: 'Pre-close chg.', type: 'amount', total: true },
      { key: 'employeeCode', label: 'Emp. code' },
    ],
  },
  'employee-wise': {
    title: 'Employee Wise Loan Report',
    subtitle: 'Loans handled by each relationship manager (all branches)',
    endpoint: '/api/loan-reports/employee-wise',
    ranged: false,
    defaultFrom: 'month',
    filters: ['employeeCode', 'employeeName', 'rank'],
    columns: [
      { key: 'employeeCode', label: 'Emp. code' },
      { key: 'employeeName', label: 'Employee' },
      { key: 'rankCode', label: 'Rank' },
      { key: 'branchName', label: 'Branch' },
      { key: 'totalLoans', label: 'Loans', type: 'int', total: true },
      { key: 'activeLoans', label: 'Active', type: 'int', total: true },
      { key: 'closedLoans', label: 'Closed', type: 'int', total: true },
      { key: 'disbursedAmount', label: 'Disbursed', type: 'amount', total: true },
      { key: 'outstandingPrincipal', label: 'Outstanding', type: 'amount', total: true },
    ],
  },
}

type Row = Record<string, unknown>

function cell(col: Column, value: unknown) {
  switch (col.type) {
    case 'amount':
      return formatAmount(value)
    case 'date':
      return formatDate(value)
    case 'status':
      return value ? (
        <Badge size="xs" variant="light" color={statusColor(String(value))}>
          {String(value)}
        </Badge>
      ) : null
    case 'emiMode':
      return emiModeLabel[String(value)] ?? String(value ?? '')
    default:
      return value === null || value === undefined ? '' : String(value)
  }
}

/** LOAN MASTER > reports 7.1 - 7.5 and EMPLOYEE > employee wise loan report. */
export function LoanReportRoute() {
  const { report = '' } = useParams()
  const def = reports[report]
  if (!def) return <Navigate to="/" replace />
  return <LoanReportPage key={report} def={def} />
}

function LoanReportPage({ def }: { def: ReportDef }) {
  const { data: me } = useMe()
  const branches = useReportBranches(false)
  const monthStart = me ? `${me.businessDate.slice(0, 8)}01` : null
  const [range, setRange] = useState<RangeFilter>({
    branchId: me ? String(me.branchId) : null,
    from: def.defaultFrom === 'month' ? monthStart : (me?.businessDate ?? null),
    to: me?.businessDate ?? null,
  })
  const [text, setText] = useState<Record<string, string>>({ reportType: 'APPLICATION', emiMode: '' })
  const lookups = useForm<{ planId: string | null; groupId: string | null; rankId: string | null }>({ defaultValues: { planId: null, groupId: null, rankId: null } })
  const [planId, groupId, rankId] = useWatch({ control: lookups.control, name: ['planId', 'groupId', 'rankId'] })
  const [applied, setApplied] = useState<Record<string, string | null> | null>(def.ranged ? null : {})

  const report = useQuery({
    queryKey: ['loan-reports', def.endpoint, applied],
    queryFn: () => api.get<Row[]>(`${def.endpoint}${query(applied ?? {})}`),
    enabled: applied !== null,
  })
  const rows = report.data ?? []

  const show = () =>
    setApplied({
      ...(def.ranged ? { branchId: range.branchId, from: range.from, to: range.to } : {}),
      reportType: def.filters.includes('reportType') ? text.reportType : null,
      loanNo: text.loanNo || null,
      memberNo: text.memberNo || null,
      employeeCode: text.employeeCode || null,
      employeeName: text.employeeName || null,
      emiMode: text.emiMode || null,
      planId: def.filters.includes('plan') ? planId : null,
      groupId: def.filters.includes('group') ? groupId : null,
      rankId: def.filters.includes('rank') ? rankId : null,
    })

  const setText1 = (key: string) => (e: React.ChangeEvent<HTMLInputElement>) => setText((t) => ({ ...t, [key]: e.currentTarget.value.toUpperCase() }))
  const extra = (
    <>
      {def.filters.includes('reportType') && (
        <div>
          <Text size="sm" fw={500} mb={4}>
            Report
          </Text>
          <SegmentedControl
            value={text.reportType}
            onChange={(reportType) => setText((t) => ({ ...t, reportType }))}
            data={[
              { value: 'APPLICATION', label: 'Application' },
              { value: 'SANCTION', label: 'Sanction' },
              { value: 'PAYMENT', label: 'Payment' },
              { value: 'WITH_SAVING', label: 'With saving' },
            ]}
          />
        </div>
      )}
      {def.filters.includes('loanNo') && <TextInput label="Loan No" value={text.loanNo ?? ''} onChange={setText1('loanNo')} w={150} />}
      {def.filters.includes('memberNo') && <TextInput label="Membership No" value={text.memberNo ?? ''} onChange={setText1('memberNo')} w={150} />}
      {def.filters.includes('employeeCode') && <TextInput label="Emp. No" value={text.employeeCode ?? ''} onChange={setText1('employeeCode')} w={150} />}
      {def.filters.includes('employeeName') && <TextInput label="Employee name" value={text.employeeName ?? ''} onChange={setText1('employeeName')} w={180} />}
      {def.filters.includes('plan') && (
        <div style={{ width: 150 }}>
          <LookupField control={lookups.control} name="planId" label="Plan code" entity="loan-plans" labelKey="planCode" />
        </div>
      )}
      {def.filters.includes('group') && (
        <div style={{ width: 170 }}>
          <LookupField control={lookups.control} name="groupId" label="Group name" entity="groups" labelKey="groupName" />
        </div>
      )}
      {def.filters.includes('rank') && (
        <div style={{ width: 150 }}>
          <LookupField control={lookups.control} name="rankId" label="Rank" entity="advisor-ranks" labelKey="rankCode" />
        </div>
      )}
      {def.filters.includes('emiMode') && (
        <Select
          label="EMI mode"
          w={140}
          data={[
            { value: '', label: 'All' },
            { value: 'D', label: 'Daily (DLY)' },
            { value: 'W', label: 'Weekly (WLY)' },
            { value: 'F', label: 'By-weekly' },
            { value: 'M', label: 'Monthly (MLY)' },
          ]}
          value={text.emiMode}
          onChange={(emiMode) => setText((t) => ({ ...t, emiMode: emiMode ?? '' }))}
          allowDeselect={false}
        />
      )}
    </>
  )

  return (
    <ReportFrame
      title={def.title}
      subtitle={def.subtitle}
      printCaption={
        applied && def.ranged ? `Branch: ${branches.label(applied.branchId ?? null)} · ${formatDate(applied.from)} to ${formatDate(applied.to)}` : undefined
      }
      loading={report.isFetching}
      error={report.error}
      hasData={!!report.data}
      onCsv={() =>
        downloadCsv(
          `${def.title.toLowerCase().replace(/\s+/g, '-')}.csv`,
          def.columns.map((c) => c.label),
          rows.map((r) => def.columns.map((c) => (r[c.key] as string | number | null | undefined) ?? '')),
        )
      }
      filters={
        def.ranged ? (
          <ReportFilters value={range} onChange={setRange} onShow={show} loading={report.isFetching} allowAllBranches={false}>
            {extra}
          </ReportFilters>
        ) : (
          <Group align="flex-end" gap="sm">
            {extra}
            <Button leftSection={<IconPlayerPlay size={16} />} onClick={show} loading={report.isFetching}>
              Show
            </Button>
          </Group>
        )
      }
    >
      <div style={{ overflowX: 'auto' }}>
        <Table striped withTableBorder miw={def.columns.length * 95} fz="sm">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>#</Table.Th>
              {def.columns.map((c) => (
                <Table.Th key={c.key} className={c.type === 'amount' || c.type === 'int' ? 'dj-num' : undefined}>
                  {c.label}
                </Table.Th>
              ))}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {rows.map((r, i) => (
              <Table.Tr key={i}>
                <Table.Td>{i + 1}</Table.Td>
                {def.columns.map((c) => (
                  <Table.Td key={c.key} className={c.type === 'amount' || c.type === 'int' ? 'dj-num' : undefined}>
                    {cell(c, r[c.key])}
                  </Table.Td>
                ))}
              </Table.Tr>
            ))}
          </Table.Tbody>
          {rows.length > 0 && (
            <Table.Tfoot>
              <Table.Tr fw={700}>
                <Table.Td>Total</Table.Td>
                {def.columns.map((c) => (
                  <Table.Td key={c.key} className="dj-num">
                    {c.total
                      ? c.type === 'int'
                        ? rows.reduce((s, r) => s + (Number(r[c.key]) || 0), 0)
                        : formatAmount(rows.reduce((s, r) => s + (Number(r[c.key]) || 0), 0))
                      : ''}
                  </Table.Td>
                ))}
              </Table.Tr>
            </Table.Tfoot>
          )}
        </Table>
      </div>
      <Text size="sm" c="dimmed" mt="xs">
        {rows.length} record(s)
      </Text>
    </ReportFrame>
  )
}
