import { Alert, Button, Checkbox, Grid, Group, List, NumberInput, Paper, ScrollArea, SegmentedControl, SimpleGrid, Stack, Table, Text, TextInput } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { useDebouncedValue } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import { IconFilter, IconReceipt } from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, errorMessage, query } from '../api/client'
import { settingEnabled, useMe } from '../auth/session'
import { LookupField } from '../components/fields'
import { formatAmount, formatDate } from '../components/format'
import { PageHeader } from '../components/PageHeader'
import { Section } from '../components/Section'
import { useForm, useWatch } from 'react-hook-form'
import type { BulkDueItem, BulkResult } from './types'

/** LOAN MASTER > Loan Bulk Recovery (advisor-wise / group-wise collection sheet). */
export function BulkRecoveryPage() {
  const { data: me } = useMe()
  const client = useQueryClient()
  const savingOn = settingEnabled(me, 'MODULE.SAVING.ENABLED')
  const canPost = me?.role !== 'ACCOUNTANT'

  const [employeeCode, setEmployeeCode] = useState('')
  const [debouncedEmployee] = useDebouncedValue(employeeCode, 400)
  const filterForm = useForm<{ groupId: string | null }>({ defaultValues: { groupId: null } })
  const groupId = useWatch({ control: filterForm.control, name: 'groupId' })
  const [from, setFrom] = useState<string | null>(me?.businessDate ?? null)
  const [to, setTo] = useState<string | null>(me?.businessDate ?? null)
  const [paidDate, setPaidDate] = useState<string | null>(me?.businessDate ?? null)
  const [mode, setMode] = useState<'CASH' | 'SAVING'>('CASH')
  const [amounts, setAmounts] = useState<Record<number, number | string>>({})
  const [selected, setSelected] = useState<number[]>([])
  const [results, setResults] = useState<BulkResult[] | null>(null)

  const due = useQuery({
    queryKey: ['recovery', 'bulk-due', debouncedEmployee, groupId, from, to],
    queryFn: () => api.get<BulkDueItem[]>(`/api/recoveries/bulk-due${query({ employeeCode: debouncedEmployee, groupId, from, to })}`),
    enabled: !!from && !!to,
  })
  const rows = due.data ?? []
  const amountOf = (r: BulkDueItem) => (amounts[r.loanId] === undefined ? r.dueAmount : Number(amounts[r.loanId]) || 0)
  const chosen = rows.filter((r) => selected.includes(r.loanId))
  const allChecked = rows.length > 0 && chosen.length === rows.length

  const post = useMutation({
    mutationFn: () =>
      api.post<BulkResult[]>('/api/recoveries/bulk', {
        paidDate,
        paymentMode: mode,
        employeeCode: debouncedEmployee || null,
        groupId: groupId ? Number(groupId) : null,
        dueFrom: from,
        dueTo: to,
        items: chosen.map((r) => ({ loanId: r.loanId, amount: amountOf(r) })),
      }),
    onSuccess: async (res) => {
      const ok = res.filter((r) => r.success).length
      notifications.show({ color: ok === res.length ? 'green' : 'orange', message: `${ok} of ${res.length} receipt(s) saved.` })
      setResults(res)
      setSelected([])
      setAmounts({})
      await client.invalidateQueries({ queryKey: ['recovery'] })
      await client.invalidateQueries({ queryKey: ['dashboard'] })
    },
    onError: (e) => notifications.show({ color: 'red', message: errorMessage(e) }),
  })

  return (
    <Stack gap="md">
      <PageHeader title="Loan Bulk Recovery" subtitle="Collect EMIs for an advisor or group in one go" />

      <Section title="Searching information" icon={<IconFilter size={18} />}>
        <Grid gap="sm" align="flex-end">
          <Grid.Col span={{ base: 6, sm: 3 }}>
            <TextInput label="F.O. code" placeholder="All" value={employeeCode} onChange={(e) => setEmployeeCode(e.currentTarget.value.toUpperCase())} />
          </Grid.Col>
          <Grid.Col span={{ base: 6, sm: 3 }}>
            <LookupField control={filterForm.control} name="groupId" label="Group" entity="groups" labelKey="groupName" />
          </Grid.Col>
          <Grid.Col span={{ base: 6, sm: 3 }}>
            <DateInput label="Due from" value={from} onChange={setFrom} valueFormat="DD-MMM-YYYY" />
          </Grid.Col>
          <Grid.Col span={{ base: 6, sm: 3 }}>
            <DateInput label="Due to" value={to} onChange={setTo} valueFormat="DD-MMM-YYYY" />
          </Grid.Col>
        </Grid>
      </Section>

      <Paper className="dj-card" p="sm">
        <Group justify="space-between" align="flex-end" gap="sm">
          <Group align="flex-end" gap="sm">
            <DateInput label="Paid date" value={paidDate} onChange={setPaidDate} valueFormat="DD-MMM-YYYY" maxDate={me?.today} w={170} />
            {savingOn && (
              <div>
                <Text size="sm" fw={500} mb={4}>
                  Mode
                </Text>
                <SegmentedControl value={mode} onChange={(v) => setMode(v as 'CASH' | 'SAVING')} data={[{ value: 'CASH', label: 'Cash payment' }, { value: 'SAVING', label: 'Saving A/c' }]} />
              </div>
            )}
          </Group>
          <SimpleGrid cols={3} spacing="lg">
            <div>
              <Text size="xs" c="dimmed">
                Loans due
              </Text>
              <Text fw={700}>{rows.length}</Text>
            </div>
            <div>
              <Text size="xs" c="dimmed">
                Selected
              </Text>
              <Text fw={700}>{chosen.length}</Text>
            </div>
            <div>
              <Text size="xs" c="dimmed">
                Total to receive
              </Text>
              <Text fw={700}>₹{formatAmount(chosen.reduce((s, r) => s + amountOf(r), 0))}</Text>
            </div>
          </SimpleGrid>
          {canPost && (
            <Button leftSection={<IconReceipt size={16} />} disabled={chosen.length === 0 || !paidDate} loading={post.isPending} onClick={() => post.mutate()}>
              Save {chosen.length || ''} receipt(s)
            </Button>
          )}
        </Group>
      </Paper>

      {results && (
        <Alert color={results.every((r) => r.success) ? 'green' : 'orange'} withCloseButton onClose={() => setResults(null)} title="Result">
          <List size="sm" spacing={2}>
            {results.map((r) => (
              <List.Item key={r.loanId}>
                <b>{r.loanNo ?? rows.find((x) => x.loanId === r.loanId)?.loanNo ?? `#${r.loanId}`}</b>: {r.message}
              </List.Item>
            ))}
          </List>
        </Alert>
      )}
      {due.isError && <Alert color="red">{errorMessage(due.error)}</Alert>}

      <Paper className="dj-card">
        <ScrollArea type="auto">
          <Table striped highlightOnHover miw={960}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th w={40}>
                  <Checkbox aria-label="Select all" checked={allChecked} indeterminate={chosen.length > 0 && !allChecked} onChange={() => setSelected(allChecked ? [] : rows.map((r) => r.loanId))} />
                </Table.Th>
                <Table.Th>Loan No</Table.Th>
                <Table.Th>Member</Table.Th>
                {mode === 'SAVING' && <Table.Th>Saving A/c</Table.Th>}
                <Table.Th className="dj-num">Ins No</Table.Th>
                <Table.Th>Due date</Table.Th>
                <Table.Th className="dj-num">EMI</Table.Th>
                <Table.Th className="dj-num">Due</Table.Th>
                <Table.Th w={140}>Amount</Table.Th>
                <Table.Th>F.O.</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {rows.map((r) => {
                const checked = selected.includes(r.loanId)
                return (
                  <Table.Tr key={r.loanId} bg={checked ? 'var(--mantine-color-brand-light)' : undefined}>
                    <Table.Td>
                      <Checkbox aria-label={`Select ${r.loanNo}`} checked={checked} onChange={() => setSelected((s) => (checked ? s.filter((x) => x !== r.loanId) : [...s, r.loanId]))} />
                    </Table.Td>
                    <Table.Td fw={600}>{r.loanNo}</Table.Td>
                    <Table.Td>
                      <Text size="sm">{r.memberName}</Text>
                      <Text size="xs" c="dimmed">
                        {r.memberNo}
                        {r.groupName && ` · ${r.groupName}`}
                      </Text>
                    </Table.Td>
                    {mode === 'SAVING' && <Table.Td>{r.savingAccountNo ?? <Text size="xs" c="red">none</Text>}</Table.Td>}
                    <Table.Td className="dj-num">
                      {r.firstInstallmentNo}
                      {r.dueCount > 1 && ` (+${r.dueCount - 1})`}
                    </Table.Td>
                    <Table.Td>{formatDate(r.firstDueDate)}</Table.Td>
                    <Table.Td className="dj-num">{formatAmount(r.emiAmount)}</Table.Td>
                    <Table.Td className="dj-num">{formatAmount(r.dueAmount)}</Table.Td>
                    <Table.Td>
                      <NumberInput size="xs" min={0} decimalScale={2} value={amounts[r.loanId] ?? r.dueAmount} onChange={(v) => setAmounts((a) => ({ ...a, [r.loanId]: v }))} />
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs">{r.employeeName}</Text>
                      <Text size="xs" c="dimmed">
                        {r.employeeCode}
                      </Text>
                    </Table.Td>
                  </Table.Tr>
                )
              })}
              {!due.isLoading && rows.length === 0 && (
                <Table.Tr>
                  <Table.Td colSpan={10}>
                    <Text c="dimmed" ta="center" py="lg">
                      No EMIs due for these conditions.
                    </Text>
                  </Table.Td>
                </Table.Tr>
              )}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      </Paper>
    </Stack>
  )
}
