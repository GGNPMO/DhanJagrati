import { Badge, Group, Switch, Table, Text } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api, query } from '../api/client'
import { useMe } from '../auth/session'
import { formatAmount, formatDate } from '../components/format'
import { downloadCsv, drCr, financialYearStart, useReportBranches } from './reportKit'
import { ReportFilters, type RangeFilter } from './ReportFilters'
import { ReportFrame } from './ReportFrame'
import type { TrialBalanceRow } from './types'

/** ACCOUNT > Trial Balance. */
export function TrialBalancePage() {
  const { data: me } = useMe()
  const branches = useReportBranches()
  const [draft, setDraft] = useState<RangeFilter>({ branchId: me ? String(me.branchId) : null, from: financialYearStart(me?.businessDate), to: me?.businessDate ?? null })
  const [applied, setApplied] = useState<RangeFilter | null>(null)
  const [movement, setMovement] = useState(false)

  const report = useQuery({
    queryKey: ['accounts', 'trial-balance', applied],
    queryFn: () => api.get<TrialBalanceRow[]>(`/api/accounts/reports/trial-balance${query({ branchId: applied!.branchId, from: applied!.from, to: applied!.to })}`),
    enabled: !!applied,
  })
  const rows = report.data ?? []
  const totalDr = rows.reduce((s, r) => s + r.debit, 0)
  const totalCr = rows.reduce((s, r) => s + r.credit, 0)
  const balanced = Math.abs(totalDr - totalCr) < 0.005

  return (
    <ReportFrame
      title="Trial Balance"
      printCaption={applied ? `Branch: ${branches.label(applied.branchId)} · Date from ${formatDate(applied.from)} to ${formatDate(applied.to)}` : undefined}
      loading={report.isFetching}
      error={report.error}
      hasData={!!report.data}
      onCsv={() =>
        downloadCsv(
          'trial-balance.csv',
          ['Sr', 'Group', 'Account name', 'Opening', 'Period debit', 'Period credit', 'Debit', 'Credit'],
          rows.map((r) => [r.srNo, r.groupName, r.ledgerName, r.openingNet, r.periodDebit, r.periodCredit, r.debit, r.credit]),
        )
      }
      filters={
        <ReportFilters value={draft} onChange={setDraft} onShow={() => setApplied({ ...draft })} loading={report.isFetching}>
          <Switch label="Show opening & movement" checked={movement} onChange={(e) => setMovement(e.currentTarget.checked)} mb={8} />
        </ReportFilters>
      }
    >
      <Group justify="flex-end" mb="xs" className="dj-no-print">
        <Badge color={balanced ? 'green' : 'red'} variant="light">
          {balanced ? 'Balanced' : `Difference ₹${formatAmount(Math.abs(totalDr - totalCr))}`}
        </Badge>
      </Group>
      <div style={{ overflowX: 'auto' }}>
        <Table striped withTableBorder miw={movement ? 900 : 560}>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Sr</Table.Th>
              <Table.Th>Account name</Table.Th>
              <Table.Th>Group</Table.Th>
              {movement && (
                <>
                  <Table.Th className="dj-num">Opening</Table.Th>
                  <Table.Th className="dj-num">Period Dr</Table.Th>
                  <Table.Th className="dj-num">Period Cr</Table.Th>
                </>
              )}
              <Table.Th className="dj-num">Debit</Table.Th>
              <Table.Th className="dj-num">Credit</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {rows.map((r) => (
              <Table.Tr key={r.ledgerId}>
                <Table.Td>{r.srNo}</Table.Td>
                <Table.Td fw={500}>{r.ledgerName}</Table.Td>
                <Table.Td>
                  <Text size="xs" c="dimmed">
                    {r.groupName}
                  </Text>
                </Table.Td>
                {movement && (
                  <>
                    <Table.Td className="dj-num">{drCr(r.openingNet)}</Table.Td>
                    <Table.Td className="dj-num">{formatAmount(r.periodDebit)}</Table.Td>
                    <Table.Td className="dj-num">{formatAmount(r.periodCredit)}</Table.Td>
                  </>
                )}
                <Table.Td className="dj-num">{r.debit ? formatAmount(r.debit) : ''}</Table.Td>
                <Table.Td className="dj-num">{r.credit ? formatAmount(r.credit) : ''}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
          <Table.Tfoot>
            <Table.Tr fw={700}>
              <Table.Td colSpan={movement ? 6 : 3}>Total</Table.Td>
              <Table.Td className="dj-num">{formatAmount(totalDr)}</Table.Td>
              <Table.Td className="dj-num">{formatAmount(totalCr)}</Table.Td>
            </Table.Tr>
          </Table.Tfoot>
        </Table>
      </div>
      {rows.length === 0 && (
        <Text c="dimmed" ta="center" py="md">
          No balances for this period.
        </Text>
      )}
    </ReportFrame>
  )
}
