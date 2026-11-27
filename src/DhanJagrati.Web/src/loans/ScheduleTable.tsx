import { Badge, ScrollArea, Table } from '@mantine/core'
import { formatAmount, formatDate } from '../components/format'
import type { ScheduleRow } from './types'

export function ScheduleTable({ rows, height = 360 }: { rows: ScheduleRow[]; height?: number }) {
  const showStatus = rows.some((r) => r.status)
  return (
    <ScrollArea.Autosize mah={height} type="auto">
      <Table striped stickyHeader withTableBorder miw={460}>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>No</Table.Th>
            <Table.Th>Due date</Table.Th>
            <Table.Th className="dj-num">EMI</Table.Th>
            <Table.Th className="dj-num">Principal</Table.Th>
            <Table.Th className="dj-num">Interest</Table.Th>
            {showStatus && <Table.Th>Status</Table.Th>}
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {rows.map((r) => (
            <Table.Tr key={r.installmentNo}>
              <Table.Td>{r.installmentNo}</Table.Td>
              <Table.Td>{formatDate(r.dueDate)}</Table.Td>
              <Table.Td className="dj-num">{formatAmount(r.emiAmount)}</Table.Td>
              <Table.Td className="dj-num">{formatAmount(r.principalAmount)}</Table.Td>
              <Table.Td className="dj-num">{formatAmount(r.interestAmount)}</Table.Td>
              {showStatus && (
                <Table.Td>
                  <Badge size="xs" variant="light" color={r.status === 'PAID' ? 'green' : r.status === 'PARTIAL' ? 'yellow' : 'gray'}>
                    {r.status}
                  </Badge>
                </Table.Td>
              )}
            </Table.Tr>
          ))}
        </Table.Tbody>
        <Table.Tfoot>
          <Table.Tr fw={600}>
            <Table.Td colSpan={2}>Total</Table.Td>
            <Table.Td className="dj-num">{formatAmount(rows.reduce((s, r) => s + r.emiAmount, 0))}</Table.Td>
            <Table.Td className="dj-num">{formatAmount(rows.reduce((s, r) => s + r.principalAmount, 0))}</Table.Td>
            <Table.Td className="dj-num">{formatAmount(rows.reduce((s, r) => s + r.interestAmount, 0))}</Table.Td>
            {showStatus && <Table.Td />}
          </Table.Tr>
        </Table.Tfoot>
      </Table>
    </ScrollArea.Autosize>
  )
}
