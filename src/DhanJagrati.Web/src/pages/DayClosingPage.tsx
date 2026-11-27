import { Alert, Box, Button, Group, Paper, Stack, Table, Text, TextInput, Title } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, errorMessage } from '../api/client'
import type { DayClosingRow } from '../api/types'
import { useMe } from '../auth/session'
import { formatDate } from '../components/format'

/** MASTER > Date Closing by User: closed dates block further postings for the branch. */
export function DayClosingPage() {
  const { data: me } = useMe()
  const client = useQueryClient()
  const [remarks, setRemarks] = useState('')
  const canClose = me?.role === 'ADMIN' || me?.role === 'MANAGER'
  const canReopen = me?.role === 'ADMIN'

  const list = useQuery({ queryKey: ['day-closing'], queryFn: () => api.get<DayClosingRow[]>('/api/day-closing') })
  const alreadyClosed = list.data?.some((r) => r.businessDate === me?.businessDate) ?? false

  const close = useMutation({
    mutationFn: () => api.post('/api/day-closing', { businessDate: me?.businessDate, remarks }),
    onSuccess: () => {
      notifications.show({ color: 'green', message: `${formatDate(me?.businessDate)} closed.` })
      setRemarks('')
      client.invalidateQueries({ queryKey: ['day-closing'] })
    },
    onError: (e) => notifications.show({ color: 'red', message: errorMessage(e) }),
  })

  const reopen = useMutation({
    mutationFn: (date: string) => api.delete(`/api/day-closing/${date}`),
    onSuccess: () => client.invalidateQueries({ queryKey: ['day-closing'] }),
    onError: (e) => notifications.show({ color: 'red', message: errorMessage(e) }),
  })

  return (
    <Stack maw={760}>
      <Title order={3}>Date Closing - {me?.branchName}</Title>
      <Paper withBorder>
        <Box className="dj-section-title">Close Login Date</Box>
        <Stack p="md">
          {alreadyClosed ? (
            <Alert color="orange">{formatDate(me?.businessDate)} is already closed for this branch.</Alert>
          ) : (
            <Text size="sm">
              Closing <b>{formatDate(me?.businessDate)}</b> prevents any further entries for {me?.branchName} on that date.
            </Text>
          )}
          <Group align="end">
            <TextInput label="Remarks" value={remarks} onChange={(e) => setRemarks(e.currentTarget.value)} maxLength={250} style={{ flex: 1 }} />
            <Button color="red" onClick={() => close.mutate()} loading={close.isPending} disabled={!canClose || alreadyClosed}>
              Close Date
            </Button>
          </Group>
          {!canClose && <Text size="xs" c="dimmed">Only managers can close a date.</Text>}
        </Stack>
      </Paper>
      <Paper withBorder>
        <Box className="dj-section-title">Closed Dates (last 60)</Box>
        <Table striped withTableBorder>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Date</Table.Th>
              <Table.Th>Closed By</Table.Th>
              <Table.Th>Remarks</Table.Th>
              {canReopen && <Table.Th />}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {(list.data ?? []).map((row) => (
              <Table.Tr key={row.businessDate}>
                <Table.Td>{formatDate(row.businessDate)}</Table.Td>
                <Table.Td>{row.closedByName}</Table.Td>
                <Table.Td>{row.remarks}</Table.Td>
                {canReopen && (
                  <Table.Td>
                    <Button size="compact-xs" variant="subtle" onClick={() => reopen.mutate(row.businessDate)}>
                      Re-open
                    </Button>
                  </Table.Td>
                )}
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Paper>
    </Stack>
  )
}
