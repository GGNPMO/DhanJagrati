import { Badge, Button, Group, Paper, SimpleGrid, Text, TextInput } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { IconSearch } from '@tabler/icons-react'
import { useState } from 'react'
import { Link } from 'react-router'
import { formatAmount, formatDate } from '../components/format'
import { emiModeLabel, statusColor } from '../loans/status'
import type { LoanHeader } from './types'

/** Loan number + date picker used by Recovery and Settlement. */
export function LoanFinder({
  initialLoanNo,
  date,
  onDate,
  maxDate,
  onFind,
  loading,
}: {
  initialLoanNo?: string
  date: string | null
  onDate: (d: string | null) => void
  maxDate?: string
  onFind: (loanNo: string) => void
  loading?: boolean
}) {
  const [loanNo, setLoanNo] = useState(initialLoanNo ?? '')
  return (
    <Paper className="dj-card" p="sm">
      <Group align="flex-end" gap="sm" wrap="wrap">
        <TextInput
          label="Loan No."
          placeholder="e.g. L002000001"
          value={loanNo}
          onChange={(e) => setLoanNo(e.currentTarget.value)}
          onKeyDown={(e) => e.key === 'Enter' && loanNo.trim() && onFind(loanNo.trim())}
          w={{ base: '100%', xs: 220 }}
          data-autofocus
        />
        <DateInput label="Date" value={date} onChange={onDate} valueFormat="DD-MMM-YYYY" maxDate={maxDate} w={{ base: '100%', xs: 170 }} />
        <Button leftSection={<IconSearch size={16} />} onClick={() => loanNo.trim() && onFind(loanNo.trim())} loading={loading}>
          Find loan
        </Button>
      </Group>
    </Paper>
  )
}

function Item({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div>
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Text size="sm" fw={600}>
        {value}
      </Text>
    </div>
  )
}

export function LoanSummary({ loan }: { loan: LoanHeader }) {
  return (
    <Paper className="dj-card" p="md">
      <Group justify="space-between" mb="sm">
        <Group gap="xs">
          <Text fw={700} component={Link} to={`/loans/${loan.loanId}`} c="brand">
            {loan.loanNo}
          </Text>
          <Badge variant="light" color={statusColor(loan.status)}>
            {loan.status}
          </Badge>
        </Group>
        <Text size="sm" c="dimmed">
          {loan.branchName}
        </Text>
      </Group>
      <SimpleGrid cols={{ base: 2, sm: 4 }} spacing="sm">
        <Item label="Member" value={`${loan.memberName} (${loan.memberNo})`} />
        <Item label="Guardian" value={loan.guardianName ?? '-'} />
        <Item label="Plan" value={`${loan.planCode} · ${emiModeLabel[loan.emiMode] ?? loan.emiMode}`} />
        <Item label="F.O." value={`${loan.employeeName} (${loan.employeeCode})`} />
        <Item label="Sanction amount" value={`₹${formatAmount(loan.loanAmount)}`} />
        <Item label="Interest rate" value={`${loan.interestRate}%`} />
        <Item label="EMI" value={`₹${formatAmount(loan.emiAmount)} × ${loan.noOfInstallments}`} />
        <Item label="Disbursed" value={formatDate(loan.disbursedOn) || '-'} />
      </SimpleGrid>
    </Paper>
  )
}
