import { Alert, Text } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api, query } from '../api/client'
import { useMe } from '../auth/session'
import { formatAmount, formatDate } from '../components/format'
import { downloadCsv, financialYearStart, useReportBranches } from './reportKit'
import { ReportFilters, type RangeFilter } from './ReportFilters'
import { ReportFrame } from './ReportFrame'
import { TwoSided } from './StatementTables'
import type { ProfitLoss } from './types'

/** ACCOUNT > Profit and Loss Statement. */
export function ProfitLossPage() {
  const { data: me } = useMe()
  const branches = useReportBranches()
  const [draft, setDraft] = useState<RangeFilter>({ branchId: me ? String(me.branchId) : null, from: financialYearStart(me?.businessDate), to: me?.businessDate ?? null })
  const [applied, setApplied] = useState<RangeFilter | null>(null)

  const report = useQuery({
    queryKey: ['accounts', 'profit-loss', applied],
    queryFn: () => api.get<ProfitLoss>(`/api/accounts/reports/profit-loss${query({ branchId: applied!.branchId, from: applied!.from, to: applied!.to })}`),
    enabled: !!applied,
  })
  const d = report.data
  const expenses = d?.rows.filter((r) => r.side === 'EXPENSE') ?? []
  const incomes = d?.rows.filter((r) => r.side === 'INCOME') ?? []

  return (
    <ReportFrame
      title="Profit and Loss A/c"
      printCaption={applied ? `Branch: ${branches.label(applied.branchId)} · ${formatDate(applied.from)} to ${formatDate(applied.to)}` : undefined}
      loading={report.isFetching}
      error={report.error}
      hasData={!!d}
      onCsv={() => d && downloadCsv('profit-loss.csv', ['Side', 'Group', 'Ledger', 'Amount'], d.rows.map((r) => [r.side, r.groupName, r.ledgerName, r.amount]))}
      filters={<ReportFilters value={draft} onChange={setDraft} onShow={() => setApplied({ ...draft })} loading={report.isFetching} />}
    >
      {d && (
        <>
          <TwoSided
            left={expenses}
            right={incomes}
            leftTitle="Expense"
            rightTitle="Income"
            leftExtra={{ label: 'Net profit', amount: d.totals.netProfit }}
            rightExtra={{ label: 'Net loss', amount: d.totals.netLoss }}
          />
          <Alert mt="md" color={d.totals.netProfit > 0 ? 'green' : d.totals.netLoss > 0 ? 'red' : 'gray'} variant="light">
            <Text fw={700}>
              {d.totals.netProfit > 0
                ? `Net profit ₹${formatAmount(d.totals.netProfit)}`
                : d.totals.netLoss > 0
                  ? `Net loss ₹${formatAmount(d.totals.netLoss)}`
                  : 'No profit or loss'}
            </Text>
            <Text size="sm">
              Income ₹{formatAmount(d.totals.totalIncome)} − Expense ₹{formatAmount(d.totals.totalExpense)}
            </Text>
          </Alert>
        </>
      )}
    </ReportFrame>
  )
}
