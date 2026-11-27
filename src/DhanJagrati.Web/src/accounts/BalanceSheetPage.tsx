import { Alert } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api, query } from '../api/client'
import { useMe } from '../auth/session'
import { formatAmount, formatDate } from '../components/format'
import { downloadCsv, useReportBranches } from './reportKit'
import { ReportFilters, type RangeFilter } from './ReportFilters'
import { ReportFrame } from './ReportFrame'
import { TwoSided } from './StatementTables'
import type { StatementRow } from './types'

/** ACCOUNT > Balance Sheet (as on a date; profit or loss to date shown on the liabilities side). */
export function BalanceSheetPage() {
  const { data: me } = useMe()
  const branches = useReportBranches()
  const [draft, setDraft] = useState<RangeFilter>({ branchId: me ? String(me.branchId) : null, from: null, to: me?.businessDate ?? null })
  const [applied, setApplied] = useState<RangeFilter | null>(null)

  const report = useQuery({
    queryKey: ['accounts', 'balance-sheet', applied],
    queryFn: () => api.get<StatementRow[]>(`/api/accounts/reports/balance-sheet${query({ branchId: applied!.branchId, to: applied!.to })}`),
    enabled: !!applied,
  })
  const rows = report.data ?? []
  const liabilities = rows.filter((r) => r.side === 'LIABILITY')
  const assets = rows.filter((r) => r.side === 'ASSET')
  const diff = liabilities.reduce((s, r) => s + r.amount, 0) - assets.reduce((s, r) => s + r.amount, 0)

  return (
    <ReportFrame
      title="Balance Sheet"
      printCaption={applied ? `Branch: ${branches.label(applied.branchId)} · As on ${formatDate(applied.to)}` : undefined}
      loading={report.isFetching}
      error={report.error}
      hasData={!!report.data}
      onCsv={() => downloadCsv('balance-sheet.csv', ['Side', 'Group', 'Ledger', 'Amount'], rows.map((r) => [r.side, r.groupName, r.ledgerName, r.amount]))}
      filters={<ReportFilters value={draft} onChange={setDraft} onShow={() => setApplied({ ...draft })} hideFrom toLabel="As on" loading={report.isFetching} />}
    >
      <TwoSided left={liabilities} right={assets} leftTitle="Liabilities" rightTitle="Assets" />
      {Math.abs(diff) >= 0.005 && (
        <Alert mt="md" color="red" variant="light">
          Liabilities and assets differ by ₹{formatAmount(Math.abs(diff))}. Check opening balances: they must total equal debits and credits per branch.
        </Alert>
      )}
    </ReportFrame>
  )
}
