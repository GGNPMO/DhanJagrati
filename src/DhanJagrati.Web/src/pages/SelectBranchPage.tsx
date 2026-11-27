import { Alert, Box, Button, Center, Group, Paper, Select, Stack, Text } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { notifications } from '@mantine/notifications'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import { api, ApiError, errorMessage } from '../api/client'
import type { BranchOption } from '../api/types'
import { useMe } from '../auth/session'

/** HELP > Select Branch: change working branch and login (business) date. */
export function SelectBranchPage() {
  const { data: me } = useMe()
  const navigate = useNavigate()
  const client = useQueryClient()
  const [branchId, setBranchId] = useState<string | null>(me ? String(me.branchId) : null)
  const [date, setDate] = useState<string | null>(me?.businessDate ?? null)
  const canBackDate = me?.role === 'ADMIN' || me?.role === 'MANAGER'

  const branches = useQuery({ queryKey: ['auth', 'branches'], queryFn: () => api.get<BranchOption[]>('/api/auth/branches') })

  const select = useMutation({
    mutationFn: () => api.post('/api/auth/select-branch', { branchId: Number(branchId), businessDate: date }),
    onSuccess: async () => {
      // Everything cached belongs to the previous branch/date.
      await client.invalidateQueries()
      notifications.show({ color: 'green', message: 'Branch and login date updated.' })
      navigate('/')
    },
  })

  const dateError = select.error instanceof ApiError ? select.error.fieldErrors.businessDate : undefined

  return (
    <Center>
      <Paper withBorder w={460}>
        <Box className="dj-section-title">Branch List</Box>
        <Stack p="lg" gap="sm">
          <Group>
            <Text size="sm">Current Branch</Text>
            <Text fw={700} c="red">
              {me?.branchName}
            </Text>
          </Group>
          <Select
            label="Select Branch"
            data={(branches.data ?? []).map((b) => ({ value: String(b.branchId), label: `${b.branchName} (${b.branchCode})` }))}
            value={branchId}
            onChange={setBranchId}
            allowDeselect={false}
          />
          <DateInput
            label="Login Date"
            value={date}
            onChange={setDate}
            valueFormat="DD-MMM-YYYY"
            maxDate={me?.today}
            minDate={canBackDate ? undefined : me?.today}
            error={dateError}
          />
          <Text size="xs" c="red">
            Note: You can change login date and branch.{!canBackDate && ' Back dates need a Manager.'}
          </Text>
          {select.isError && !dateError && <Alert color="red">{errorMessage(select.error)}</Alert>}
          <Group justify="center" mt="sm">
            <Button onClick={() => select.mutate()} loading={select.isPending} disabled={!branchId || !date}>
              Select
            </Button>
            <Button variant="default" onClick={() => navigate(-1)}>
              Cancel
            </Button>
          </Group>
        </Stack>
      </Paper>
    </Center>
  )
}
