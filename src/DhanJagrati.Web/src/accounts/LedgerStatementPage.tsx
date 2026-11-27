import { Select, Table, Text } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { api, query } from '../api/client'
import { useMe } from '../auth/session'
import { formatAmount, formatDate } from '../components/format'
import { downloadCsv, drCr, useReportBranches } from './reportKit'
import { ReportFilters, type RangeFilter } from './ReportFilters'
import { ReportFrame } from './ReportFrame'
import type { LedgerRow, LedgerStatement } from './types'

/** ACCOUNT > Ledger statement (account-wise detail with running balance). */
export function LedgerStatementPage() {
  const { data: me } = useMe()
  const branches = useReportBranches()
  const [params] = useSearchParams()
  const [ledgerId, setLedgerId] = useState<string | null>(params.get('ledger'))
  const [draft, setDraft] = useState<RangeFilter>({ branchId: me ? String(me.branchId) : null, from: me ? `${me.businessDate.slice(0, 8)}01` : null, to: me?.businessDate ?? null })
  const [applied, setApplied] = useState<(RangeFilter & { ledgerId: string }) | null>(null)

  const ledgers = useQuery({ queryKey: ['accounts', 'ledgers'], queryFn: () => api.get<LedgerRow[]>('/api/accounts/ledgers'), staleTime: 60_000 })
  const report = useQuery({
    queryKey: ['accounts', 'ledger-statement', applied],
    queryFn: () =>
      api.get<LedgerStatement>(`/api/accounts/reports/ledger-statement${query({ ledgerId: applied!.ledgerId, branchId: applied!.branchId, from: applied!.from, to: applied!.to })}`),
    enabled: !!applied,
  })
  const options = (ledgers.data ?? []).filter((l) => !l.isGroup).map((l) => ({ value: String(l.id), label: l.ledgerName }))
  const ledgerName = options.find((o) => o.value === applied?.ledgerId)?.label
  const d = report.data
  const closing = d ? (d.lines.at(-1)?.runningBalance ?? d.openingNet) : 0

  return (
    <ReportFrame
      title={ledgerName ? `Ledger: ${ledgerName}` : 'Ledger Statement'}
      printCaption={applied ? `Branch: ${branches.label(applied.branchId)} · ${formatDate(applied.from)} to ${formatDate(applied.to)}` : undefined}
      loading={report.isFetching}
      error={report.error}
      hasData={!!d}
      onCsv={() =>
        d &&
        downloadCsv(
          'ledger-statement.csv',
          ['Date', 'Voucher', 'Type', 'Particulars', 'Debit', 'Credit', 'Balance'],
          d.lines.map((l) => [l.voucherDate, l.voucherNo, l.voucherType, l.particulars, l.debit, l.credit, l.runningBalance]),
        )
      }
      filters={
        <ReportFilters value={draft} onChange={setDraft} onShow={() => ledgerId && setApplied({ ...draft, ledgerId })} loading={report.isFetching}>
          <Select label="Ledger" data={options} value={ledgerId} onChange={setLedgerId} searchable w={{ base: '100%', xs: 260 }} placeholder="Choose ledger" />
        </ReportFilters>
      }
    >
      {d && (
        <div style={{ overflowX: 'auto' }}>
          <Table striped withTableBorder miw={760}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Date</Table.Th>
                <Table.Th>Voucher</Table.Th>
                <Table.Th>Particulars</Table.Th>
                <Table.Th className="dj-num">Debit</Table.Th>
                <Table.Th className="dj-num">Credit</Table.Th>
                <Table.Th className="dj-num">Balance</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              <Table.Tr fw={600}>
                <Table.Td colSpan={5}>Opening balance</Table.Td>
                <Table.Td className="dj-num">{drCr(d.openingNet)}</Table.Td>
              </Table.Tr>
              {d.lines.map((l, i) => (
                <Table.Tr key={`${l.voucherNo}-${i}`}>
                  <Table.Td>{formatDate(l.voucherDate)}</Table.Td>
                  <Table.Td>
                    {l.voucherNo}
                    <Text size="xs" c="dimmed">
                      {l.voucherType.replace('_', ' ')} · {l.paymentMode}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="xs" lineClamp={2}>
                      {l.particulars}
                    </Text>
                  </Table.Td>
                  <Table.Td className="dj-num">{l.debit ? formatAmount(l.debit) : ''}</Table.Td>
                  <Table.Td className="dj-num">{l.credit ? formatAmount(l.credit) : ''}</Table.Td>
                  <Table.Td className="dj-num">{drCr(l.runningBalance)}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
            <Table.Tfoot>
              <Table.Tr fw={700}>
                <Table.Td colSpan={3}>Closing balance</Table.Td>
                <Table.Td className="dj-num">{formatAmount(d.lines.reduce((s, l) => s + l.debit, 0))}</Table.Td>
                <Table.Td className="dj-num">{formatAmount(d.lines.reduce((s, l) => s + l.credit, 0))}</Table.Td>
                <Table.Td className="dj-num">{drCr(closing)}</Table.Td>
              </Table.Tr>
            </Table.Tfoot>
          </Table>
        </div>
      )}
    </ReportFrame>
  )
}
