import { Grid, Table, Text } from '@mantine/core'
import { Fragment } from 'react'
import { formatAmount } from '../components/format'
import { groupBy } from './reportKit'
import type { StatementRow } from './types'

/** Two-sided statement (P&L: Expense | Income, Balance sheet: Liabilities | Assets), grouped by account group. */
export function TwoSided({
  left,
  right,
  leftTitle,
  rightTitle,
  leftExtra,
  rightExtra,
}: {
  left: StatementRow[]
  right: StatementRow[]
  leftTitle: string
  rightTitle: string
  leftExtra?: { label: string; amount: number }
  rightExtra?: { label: string; amount: number }
}) {
  const side = (rows: StatementRow[], title: string, extra?: { label: string; amount: number }) => {
    const groups = groupBy(rows, (r) => r.groupName ?? 'OTHER')
    const total = rows.reduce((s, r) => s + r.amount, 0) + (extra?.amount ?? 0)
    return (
      <Table withTableBorder>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>{title}</Table.Th>
            <Table.Th className="dj-num">Amount</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {groups.map(([group, items]) => (
            <Fragment key={group}>
              <Table.Tr bg="var(--dj-page-bg)">
                <Table.Td fw={600} fz="xs" tt="uppercase" c="dimmed">
                  {group}
                </Table.Td>
                <Table.Td className="dj-num" fw={600} fz="xs" c="dimmed">
                  {formatAmount((items ?? []).reduce((s, r) => s + r.amount, 0))}
                </Table.Td>
              </Table.Tr>
              {(items ?? []).map((r) => (
                <Table.Tr key={`${r.ledgerId}-${r.ledgerName}`}>
                  <Table.Td pl="lg">{r.ledgerName}</Table.Td>
                  <Table.Td className="dj-num">{formatAmount(r.amount)}</Table.Td>
                </Table.Tr>
              ))}
            </Fragment>
          ))}
          {extra && extra.amount !== 0 && (
            <Table.Tr fw={700}>
              <Table.Td>{extra.label}</Table.Td>
              <Table.Td className="dj-num">{formatAmount(extra.amount)}</Table.Td>
            </Table.Tr>
          )}
          {rows.length === 0 && !extra?.amount && (
            <Table.Tr>
              <Table.Td colSpan={2}>
                <Text size="sm" c="dimmed">
                  None
                </Text>
              </Table.Td>
            </Table.Tr>
          )}
        </Table.Tbody>
        <Table.Tfoot>
          <Table.Tr fw={700}>
            <Table.Td>Total</Table.Td>
            <Table.Td className="dj-num">{formatAmount(total)}</Table.Td>
          </Table.Tr>
        </Table.Tfoot>
      </Table>
    )
  }

  return (
    <Grid gap="md">
      <Grid.Col span={{ base: 12, md: 6 }}>{side(left, leftTitle, leftExtra)}</Grid.Col>
      <Grid.Col span={{ base: 12, md: 6 }}>{side(right, rightTitle, rightExtra)}</Grid.Col>
    </Grid>
  )
}
