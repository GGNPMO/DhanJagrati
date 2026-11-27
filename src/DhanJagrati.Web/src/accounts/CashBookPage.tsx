import { Grid, Group, Paper, SegmentedControl, Select, SimpleGrid, Table, Text } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { Fragment, useState } from 'react'
import { api, query } from '../api/client'
import { useMe } from '../auth/session'
import { formatAmount, formatDate } from '../components/format'
import { downloadCsv, groupBy, useReportBranches } from './reportKit'
import { ReportFilters, type RangeFilter } from './ReportFilters'
import { ReportFrame } from './ReportFrame'
import type { CashBook, LedgerRow } from './types'

type Mode = 'SUMMARY' | 'DETAIL' | 'LEDGER'

function Box({ label, value }: { label: string; value: number }) {
  return (
    <Paper p="xs" radius="md" bg="var(--dj-page-bg)">
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Text fw={700} style={{ fontVariantNumeric: 'tabular-nums' }}>
        ₹{formatAmount(value)}
      </Text>
    </Paper>
  )
}

/** ACCOUNT > Cash Book (consolidated, detail, ledger-wise). */
export function CashBookPage() {
  const { data: me } = useMe()
  const branches = useReportBranches()
  const [draft, setDraft] = useState<RangeFilter>({ branchId: me ? String(me.branchId) : null, from: me ? `${me.businessDate.slice(0, 8)}01` : null, to: me?.businessDate ?? null })
  const [cashLedger, setCashLedger] = useState<string | null>(null)
  const [group, setGroup] = useState<string | null>(null)
  const [mode, setMode] = useState<Mode>('SUMMARY')
  const [applied, setApplied] = useState<(RangeFilter & { cashLedger: string | null; group: string | null; mode: Mode }) | null>(null)

  const ledgers = useQuery({ queryKey: ['accounts', 'ledgers'], queryFn: () => api.get<LedgerRow[]>('/api/accounts/ledgers'), staleTime: 60_000 })
  const report = useQuery({
    queryKey: ['accounts', 'cash-book', applied],
    queryFn: () =>
      api.get<CashBook>(
        `/api/accounts/reports/cash-book${query({ branchId: applied!.branchId, from: applied!.from, to: applied!.to, cashLedgerId: applied!.cashLedger, groupLedgerId: applied!.group, mode: applied!.mode })}`,
      ),
    enabled: !!applied,
  })

  const cashOptions = (ledgers.data ?? []).filter((l) => l.cashBankType === 'C').map((l) => ({ value: String(l.id), label: l.ledgerName }))
  const groupOptions = (ledgers.data ?? []).filter((l) => l.isGroup).map((l) => ({ value: String(l.id), label: l.ledgerName }))
  const d = report.data
  const caption = applied ? `Branch: ${branches.label(applied.branchId)} · ${formatDate(applied.from)} to ${formatDate(applied.to)}` : undefined
  const receipts = d?.summary.filter((r) => r.receipt > 0) ?? []
  const payments = d?.summary.filter((r) => r.payment > 0) ?? []

  const csv = () => {
    if (!d || !applied) return
    if (applied.mode === 'SUMMARY')
      downloadCsv('cash-book-summary.csv', ['Ledger', 'Receipt (Cr)', 'Payment (Dr)'], d.summary.map((r) => [r.ledgerName, r.receipt, r.payment]))
    else
      downloadCsv(
        'cash-book-detail.csv',
        ['Sr', 'Date', 'Voucher', 'Account head', 'Particulars', 'Debit', 'Credit', 'Mode', 'Cheque'],
        d.detail.map((r) => [r.srNo, r.voucherDate, r.voucherNo, r.accountHead, r.particulars, r.debit, r.credit, r.paymentMode, r.chequeNo]),
      )
  }

  // Ledger-wise: group detail rows by account head with subtotals
  const byLedger = applied?.mode === 'LEDGER' && d ? groupBy(d.detail, (r) => r.accountHead).map(([, rows]) => rows) : []

  return (
    <ReportFrame
      title="Cash Book"
      subtitle="Receipts and payments through cash"
      printCaption={caption}
      loading={report.isFetching}
      error={report.error}
      hasData={!!d}
      onCsv={csv}
      filters={
        <ReportFilters value={draft} onChange={setDraft} onShow={() => setApplied({ ...draft, branchId: draft.branchId === '0' ? null : draft.branchId, cashLedger, group, mode })} loading={report.isFetching}>
          <Select label="Ledger name" data={cashOptions} value={cashLedger} onChange={setCashLedger} placeholder="ALL" clearable w={{ base: '100%', xs: 190 }} />
          <Select label="Group ledger" data={groupOptions} value={group} onChange={setGroup} placeholder="-" clearable searchable w={{ base: '100%', xs: 210 }} />
          <div>
            <Text size="sm" fw={500} mb={4}>
              Type
            </Text>
            <SegmentedControl
              value={mode}
              onChange={(v) => setMode(v as Mode)}
              data={[
                { value: 'SUMMARY', label: 'Consolidate' },
                { value: 'DETAIL', label: 'Detail' },
                { value: 'LEDGER', label: 'Ledger wise' },
              ]}
            />
          </div>
        </ReportFilters>
      }
    >
      {d && applied && (
        <>
          <SimpleGrid cols={{ base: 2, sm: 4 }} mb="md">
            <Box label="Opening balance" value={d.totals.openingBalance} />
            <Box label="Total receipts" value={d.totals.totalReceipts} />
            <Box label="Total payments" value={d.totals.totalPayments} />
            <Box label="Closing balance (cash in hand)" value={d.totals.closingBalance} />
          </SimpleGrid>

          {applied.mode === 'SUMMARY' ? (
            <Grid gap="md">
              {[
                { title: 'Receipts (Credit)', rows: receipts, pick: (r: CashBook['summary'][number]) => r.receipt },
                { title: 'Payments (Debit)', rows: payments, pick: (r: CashBook['summary'][number]) => r.payment },
              ].map((side) => (
                <Grid.Col key={side.title} span={{ base: 12, md: 6 }}>
                  <Table withTableBorder>
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th>{side.title}</Table.Th>
                        <Table.Th className="dj-num">Amount</Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {side.rows.map((r) => (
                        <Table.Tr key={r.ledgerId}>
                          <Table.Td>{r.ledgerName}</Table.Td>
                          <Table.Td className="dj-num">{formatAmount(side.pick(r))}</Table.Td>
                        </Table.Tr>
                      ))}
                      {side.rows.length === 0 && (
                        <Table.Tr>
                          <Table.Td colSpan={2} c="dimmed">
                            None
                          </Table.Td>
                        </Table.Tr>
                      )}
                    </Table.Tbody>
                    <Table.Tfoot>
                      <Table.Tr fw={700}>
                        <Table.Td>Total</Table.Td>
                        <Table.Td className="dj-num">{formatAmount(side.rows.reduce((s, r) => s + side.pick(r), 0))}</Table.Td>
                      </Table.Tr>
                    </Table.Tfoot>
                  </Table>
                </Grid.Col>
              ))}
            </Grid>
          ) : (
            <div style={{ overflowX: 'auto' }}>
              <Table striped withTableBorder miw={820}>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>Sr</Table.Th>
                    <Table.Th>V. Date</Table.Th>
                    <Table.Th>Vr. No</Table.Th>
                    <Table.Th>Account head</Table.Th>
                    <Table.Th>Particulars</Table.Th>
                    <Table.Th className="dj-num">Debit</Table.Th>
                    <Table.Th className="dj-num">Credit</Table.Th>
                    <Table.Th>Mode</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {(applied.mode === 'LEDGER' ? byLedger : [d.detail]).map((rows, i) => (
                    <Fragment key={i}>
                      {(rows ?? []).map((r) => (
                        <Table.Tr key={`${r.voucherNo}-${r.ledgerId}-${r.srNo}`}>
                          <Table.Td>{r.srNo}</Table.Td>
                          <Table.Td>{formatDate(r.voucherDate)}</Table.Td>
                          <Table.Td>{r.voucherNo}</Table.Td>
                          <Table.Td>{r.accountHead}</Table.Td>
                          <Table.Td>
                            <Text size="xs" lineClamp={2}>
                              {r.particulars}
                            </Text>
                          </Table.Td>
                          <Table.Td className="dj-num">{r.debit ? formatAmount(r.debit) : ''}</Table.Td>
                          <Table.Td className="dj-num">{r.credit ? formatAmount(r.credit) : ''}</Table.Td>
                          <Table.Td>
                            {r.paymentMode}
                            {r.chequeNo && ` ${r.chequeNo}`}
                          </Table.Td>
                        </Table.Tr>
                      ))}
                      {applied.mode === 'LEDGER' && rows && rows.length > 0 && (
                        <Table.Tr fw={700} bg="var(--dj-page-bg)">
                          <Table.Td colSpan={5}>Ledger total: {rows[0].accountHead}</Table.Td>
                          <Table.Td className="dj-num">{formatAmount(rows.reduce((s, r) => s + r.debit, 0))}</Table.Td>
                          <Table.Td className="dj-num">{formatAmount(rows.reduce((s, r) => s + r.credit, 0))}</Table.Td>
                          <Table.Td />
                        </Table.Tr>
                      )}
                    </Fragment>
                  ))}
                </Table.Tbody>
              </Table>
              {d.detail.length === 0 && (
                <Text c="dimmed" ta="center" py="md">
                  No cash transactions in this period.
                </Text>
              )}
            </div>
          )}
          <Group justify="flex-end" mt="sm">
            <Text fw={700}>Closing balance: ₹{formatAmount(d.totals.closingBalance)}</Text>
          </Group>
        </>
      )}
    </ReportFrame>
  )
}
