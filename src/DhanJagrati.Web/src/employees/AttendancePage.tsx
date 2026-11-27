import { Alert, Button, Group, Paper, SegmentedControl, Stack, Table, Text, TextInput, Title } from '@mantine/core'
import { DateInput, TimeInput } from '@mantine/dates'
import { notifications } from '@mantine/notifications'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, errorMessage } from '../api/client'
import { useMe } from '../auth/session'
import type { AttendanceRow } from './types'

const statuses = [
  { value: 'P', label: 'Present' },
  { value: 'A', label: 'Absent' },
  { value: 'L', label: 'Leave' },
  { value: 'H', label: 'Half day' },
  { value: 'W', label: 'Week off' },
]

type Entry = { status: string | null; inTime: string; outTime: string; remarks: string }

/** MASTER > Daily Attendance (advisors of the current branch). */
export function AttendancePage() {
  const { data: me } = useMe()
  const client = useQueryClient()
  const canEdit = me?.role === 'ADMIN' || me?.role === 'MANAGER' || me?.role === 'OPERATOR'
  const [date, setDate] = useState<string | null>(me?.businessDate ?? null)
  const [edits, setEdits] = useState<Record<number, Entry>>({})

  const day = useQuery({
    queryKey: ['attendance', date],
    queryFn: () => api.get<AttendanceRow[]>(`/api/attendance?date=${date}`),
    enabled: !!date,
  })

  const entryOf = (row: AttendanceRow): Entry =>
    edits[row.employeeId] ?? {
      status: row.status,
      inTime: row.inTime?.slice(0, 5) ?? '',
      outTime: row.outTime?.slice(0, 5) ?? '',
      remarks: row.remarks ?? '',
    }
  const update = (row: AttendanceRow, patch: Partial<Entry>) => setEdits((prev) => ({ ...prev, [row.employeeId]: { ...entryOf(row), ...patch } }))

  const save = useMutation({
    mutationFn: () =>
      api.put('/api/attendance', {
        date,
        entries: Object.entries(edits).map(([employeeId, e]) => ({
          employeeId: Number(employeeId),
          status: e.status,
          inTime: e.inTime || null,
          outTime: e.outTime || null,
          remarks: e.remarks || null,
        })),
      }),
    onSuccess: async () => {
      notifications.show({ color: 'green', message: 'Attendance saved.' })
      setEdits({})
      await client.invalidateQueries({ queryKey: ['attendance', date] })
    },
    onError: (e) => notifications.show({ color: 'red', title: 'Not saved', message: errorMessage(e) }),
  })

  const rows = day.data ?? []
  const changed = Object.keys(edits).length

  return (
    <Stack>
      <Title order={3}>Daily Attendance - {me?.branchName}</Title>
      <Group align="end">
        <DateInput
          label="Date"
          value={date}
          onChange={(value) => {
            setDate(value)
            setEdits({})
          }}
          valueFormat="DD-MMM-YYYY"
          maxDate={me?.today}
          w={180}
        />
        {canEdit && (
          <>
            <Button variant="default" disabled={rows.length === 0} onClick={() => rows.forEach((r) => !entryOf(r).status && update(r, { status: 'P' }))}>
              Mark unmarked as present
            </Button>
            <Button onClick={() => save.mutate()} loading={save.isPending} disabled={changed === 0}>
              Save{changed > 0 ? ` (${changed})` : ''}
            </Button>
          </>
        )}
      </Group>
      {day.isError && <Alert color="red">{errorMessage(day.error)}</Alert>}
      <Paper withBorder>
        <Table striped withTableBorder verticalSpacing="xs">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Code</Table.Th>
              <Table.Th>Name</Table.Th>
              <Table.Th>Status</Table.Th>
              <Table.Th>In</Table.Th>
              <Table.Th>Out</Table.Th>
              <Table.Th>Remarks</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {rows.map((row) => {
              const e = entryOf(row)
              return (
                <Table.Tr key={row.employeeId} bg={edits[row.employeeId] ? 'orange.0' : undefined}>
                  <Table.Td>{row.employeeCode}</Table.Td>
                  <Table.Td>{row.employeeName}</Table.Td>
                  <Table.Td>
                    <Group gap={4} wrap="nowrap">
                      <SegmentedControl size="xs" data={statuses} value={e.status ?? ''} disabled={!canEdit} onChange={(status) => update(row, { status })} />
                      {e.status && canEdit && (
                        <Button size="compact-xs" variant="subtle" color="gray" onClick={() => update(row, { status: null, inTime: '', outTime: '' })}>
                          Clear
                        </Button>
                      )}
                    </Group>
                  </Table.Td>
                  <Table.Td>
                    <TimeInput size="xs" w={100} value={e.inTime} disabled={!canEdit || !e.status} onChange={(ev) => update(row, { inTime: ev.currentTarget.value })} />
                  </Table.Td>
                  <Table.Td>
                    <TimeInput size="xs" w={100} value={e.outTime} disabled={!canEdit || !e.status} onChange={(ev) => update(row, { outTime: ev.currentTarget.value })} />
                  </Table.Td>
                  <Table.Td>
                    <TextInput size="xs" value={e.remarks} maxLength={200} disabled={!canEdit || !e.status} onChange={(ev) => update(row, { remarks: ev.currentTarget.value })} />
                  </Table.Td>
                </Table.Tr>
              )
            })}
            {!day.isLoading && rows.length === 0 && (
              <Table.Tr>
                <Table.Td colSpan={6}>
                  <Text c="dimmed" ta="center" py="md">
                    No active advisors in this branch.
                  </Text>
                </Table.Td>
              </Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </Paper>
    </Stack>
  )
}
