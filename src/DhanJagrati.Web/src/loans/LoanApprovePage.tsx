import {
  Alert,
  Button,
  Checkbox,
  Grid,
  Group,
  List,
  Modal,
  Paper,
  ScrollArea,
  SegmentedControl,
  SimpleGrid,
  Stack,
  Table,
  Text,
  Textarea,
  TextInput,
} from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { useDebouncedValue } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import { IconCheck, IconCircleX, IconFilter, IconRotateClockwise2 } from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { api, errorMessage, query } from '../api/client'
import { useMe } from '../auth/session'
import { formatAmount, formatDate } from '../components/format'
import { PageHeader } from '../components/PageHeader'
import { Section } from '../components/Section'
import type { ApprovalListItem, ApprovalResult } from './types'

type Action = 'approve' | 'disapprove' | 'reject'
const actionText: Record<Action, { title: string; verb: string; color: string }> = {
  approve: { title: 'Approve & disburse', verb: 'Approve', color: 'green' },
  disapprove: { title: 'Dis-approve', verb: 'Dis-approve', color: 'orange' },
  reject: { title: 'Reject', verb: 'Reject', color: 'red' },
}

/** LOAN MASTER > Loan Approve: approve, dis-approve or reject applications in bulk. */
export function LoanApprovePage() {
  const { data: me } = useMe()
  const client = useQueryClient()
  const canApprove = me?.role === 'ADMIN' || me?.role === 'MANAGER'
  const monthStart = me ? `${me.businessDate.slice(0, 8)}01` : null

  const [status, setStatus] = useState<'PENDING' | 'APPROVED'>('PENDING')
  const [from, setFrom] = useState<string | null>(monthStart)
  const [to, setTo] = useState<string | null>(me?.businessDate ?? null)
  const [loanNo, setLoanNo] = useState('')
  const [debouncedLoanNo] = useDebouncedValue(loanNo, 300)
  const [actionDate, setActionDate] = useState<string | null>(me?.businessDate ?? null)
  const [selected, setSelected] = useState<number[]>([])
  const [pending, setPending] = useState<Action | null>(null)
  const [remarks, setRemarks] = useState('')
  const [results, setResults] = useState<ApprovalResult[] | null>(null)

  const list = useQuery({
    queryKey: ['loan-approvals', status, from, to, debouncedLoanNo],
    queryFn: () => api.get<ApprovalListItem[]>(`/api/loan-approvals${query({ status, from, to, loanNo: debouncedLoanNo })}`),
  })
  const rows = list.data ?? []
  const visibleIds = new Set(rows.map((r) => r.loanId))
  const chosen = selected.filter((id) => visibleIds.has(id))
  const chosenRows = rows.filter((r) => chosen.includes(r.loanId))
  const allChecked = rows.length > 0 && chosen.length === rows.length

  const run = useMutation({
    mutationFn: (action: Action) => api.post<ApprovalResult[]>(`/api/loan-approvals/${action}`, { loanIds: chosen, date: actionDate, remarks: remarks || null }),
    onSuccess: async (res) => {
      const ok = res.filter((r) => r.success).length
      notifications.show({ color: ok === res.length ? 'green' : 'orange', message: `${ok} of ${res.length} loan(s) processed.` })
      setResults(res)
      setSelected([])
      setPending(null)
      setRemarks('')
      await client.invalidateQueries({ queryKey: ['loan-approvals'] })
      await client.invalidateQueries({ queryKey: ['loans'] })
      await client.invalidateQueries({ queryKey: ['dashboard'] })
    },
    onError: (e) => notifications.show({ color: 'red', message: errorMessage(e) }),
  })

  if (!canApprove) return <Alert color="gray">Only managers can approve loans.</Alert>

  const total = rows.reduce((s, r) => s + r.loanAmount, 0)
  const chosenTotal = chosenRows.reduce((s, r) => s + r.loanAmount, 0)

  return (
    <Stack gap="md">
      <PageHeader title="Loan Approve" subtitle={`${me?.branchName} branch`} />

      <Section title="Conditions" icon={<IconFilter size={18} />}>
        <Grid gap="sm" align="flex-end">
          <Grid.Col span={{ base: 12, sm: 4, lg: 3 }}>
            <Text size="sm" fw={500} mb={4}>
              Show
            </Text>
            <SegmentedControl
              fullWidth
              value={status}
              onChange={(v) => {
                setStatus(v as 'PENDING' | 'APPROVED')
                setSelected([])
                setResults(null)
              }}
              data={[
                { value: 'PENDING', label: 'Pending' },
                { value: 'APPROVED', label: 'Approved' },
              ]}
            />
          </Grid.Col>
          <Grid.Col span={{ base: 12, sm: 4, lg: 3 }}>
            <TextInput label="Loan No." value={loanNo} onChange={(e) => setLoanNo(e.currentTarget.value)} placeholder="All" />
          </Grid.Col>
          <Grid.Col span={{ base: 6, sm: 2, lg: 3 }}>
            <DateInput label="Date from" value={from} onChange={setFrom} valueFormat="DD-MMM-YYYY" />
          </Grid.Col>
          <Grid.Col span={{ base: 6, sm: 2, lg: 3 }}>
            <DateInput label="To" value={to} onChange={setTo} valueFormat="DD-MMM-YYYY" />
          </Grid.Col>
        </Grid>
      </Section>

      <SimpleGrid cols={{ base: 2, sm: 4 }}>
        <Paper className="dj-card" p="sm">
          <Text size="xs" c="dimmed">
            Total no. of loans
          </Text>
          <Text fw={700} size="lg">
            {rows.length}
          </Text>
        </Paper>
        <Paper className="dj-card" p="sm">
          <Text size="xs" c="dimmed">
            Total amount
          </Text>
          <Text fw={700} size="lg">
            ₹{formatAmount(total)}
          </Text>
        </Paper>
        <Paper className="dj-card" p="sm">
          <Text size="xs" c="dimmed">
            Selected
          </Text>
          <Text fw={700} size="lg">
            {chosen.length}
          </Text>
        </Paper>
        <Paper className="dj-card" p="sm">
          <Text size="xs" c="dimmed">
            Selected amount
          </Text>
          <Text fw={700} size="lg">
            ₹{formatAmount(chosenTotal)}
          </Text>
        </Paper>
      </SimpleGrid>

      <Paper className="dj-card" p="sm">
        <Group justify="space-between" gap="sm">
          <DateInput label={status === 'PENDING' ? 'Approval / disbursement date' : 'Action date'} value={actionDate} onChange={setActionDate} valueFormat="DD-MMM-YYYY" maxDate={me?.today} w={220} />
          <Group gap="xs">
            {status === 'PENDING' ? (
              <>
                <Button color="green" leftSection={<IconCheck size={16} />} disabled={chosen.length === 0 || !actionDate} onClick={() => setPending('approve')}>
                  Approve
                </Button>
                <Button color="red" variant="light" leftSection={<IconCircleX size={16} />} disabled={chosen.length === 0} onClick={() => setPending('reject')}>
                  Loan Reject
                </Button>
              </>
            ) : (
              <Button color="orange" variant="light" leftSection={<IconRotateClockwise2 size={16} />} disabled={chosen.length === 0} onClick={() => setPending('disapprove')}>
                Dis-approve
              </Button>
            )}
          </Group>
        </Group>
      </Paper>

      {results && (
        <Alert color={results.every((r) => r.success) ? 'green' : 'orange'} withCloseButton onClose={() => setResults(null)} title="Result">
          <List size="sm" spacing={2}>
            {results.map((r) => (
              <List.Item key={r.loanId}>
                <b>{r.loanNo ?? `#${r.loanId}`}</b>: {r.message}
              </List.Item>
            ))}
          </List>
        </Alert>
      )}
      {list.isError && <Alert color="red">{errorMessage(list.error)}</Alert>}

      <Paper className="dj-card">
        <ScrollArea type="auto">
          <Table striped highlightOnHover miw={900}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th w={40}>
                  <Checkbox
                    aria-label="Select all"
                    checked={allChecked}
                    indeterminate={chosen.length > 0 && !allChecked}
                    onChange={() => setSelected(allChecked ? [] : rows.map((r) => r.loanId))}
                  />
                </Table.Th>
                <Table.Th>Loan No</Table.Th>
                <Table.Th>Date</Table.Th>
                <Table.Th>Member</Table.Th>
                <Table.Th>Plan</Table.Th>
                <Table.Th className="dj-num">Amount</Table.Th>
                <Table.Th className="dj-num">Rate</Table.Th>
                <Table.Th className="dj-num">EMIs</Table.Th>
                <Table.Th className="dj-num">EMI</Table.Th>
                <Table.Th>Advisor</Table.Th>
                {status === 'APPROVED' && <Table.Th>Approved</Table.Th>}
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {rows.map((r) => {
                const checked = chosen.includes(r.loanId)
                return (
                  <Table.Tr key={r.loanId} bg={checked ? 'var(--mantine-color-brand-light)' : undefined}>
                    <Table.Td>
                      <Checkbox
                        aria-label={`Select ${r.loanNo}`}
                        checked={checked}
                        onChange={() => setSelected((s) => (checked ? s.filter((x) => x !== r.loanId) : [...s, r.loanId]))}
                      />
                    </Table.Td>
                    <Table.Td>
                      <Text component={Link} to={`/loans/${r.loanId}`} size="sm" fw={600} c="brand">
                        {r.loanNo}
                      </Text>
                    </Table.Td>
                    <Table.Td>{formatDate(r.applicationDate)}</Table.Td>
                    <Table.Td>
                      <Text size="sm">{r.memberName}</Text>
                      <Text size="xs" c="dimmed">
                        {r.memberNo}
                      </Text>
                    </Table.Td>
                    <Table.Td>{r.planCode}</Table.Td>
                    <Table.Td className="dj-num">{formatAmount(r.loanAmount)}</Table.Td>
                    <Table.Td className="dj-num">{r.interestRate}</Table.Td>
                    <Table.Td className="dj-num">{r.noOfInstallments}</Table.Td>
                    <Table.Td className="dj-num">{formatAmount(r.emiAmount)}</Table.Td>
                    <Table.Td>
                      <Text size="sm">{r.employeeName}</Text>
                      <Text size="xs" c="dimmed">
                        {r.employeeCode}
                      </Text>
                    </Table.Td>
                    {status === 'APPROVED' && <Table.Td>{formatDate(r.approvedOn)}</Table.Td>}
                  </Table.Tr>
                )
              })}
              {!list.isLoading && rows.length === 0 && (
                <Table.Tr>
                  <Table.Td colSpan={11}>
                    <Text c="dimmed" ta="center" py="lg">
                      No {status.toLowerCase()} loans for these conditions.
                    </Text>
                  </Table.Td>
                </Table.Tr>
              )}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      </Paper>

      <Modal opened={!!pending} onClose={() => setPending(null)} title={pending ? actionText[pending].title : ''} centered>
        {pending && (
          <Stack gap="sm">
            <Text size="sm">
              {actionText[pending].verb} <b>{chosen.length}</b> loan(s) worth <b>₹{formatAmount(chosenTotal)}</b> on <b>{formatDate(actionDate)}</b>?
            </Text>
            {pending === 'approve' && (
              <Alert color="blue" variant="light">
                The EMI schedule is generated and a disbursement voucher is posted for each loan.
              </Alert>
            )}
            {pending === 'disapprove' && (
              <Alert color="orange" variant="light">
                Only loans with no EMI received can be dis-approved. Their disbursement vouchers are cancelled.
              </Alert>
            )}
            {pending !== 'approve' && <Textarea label="Remarks" value={remarks} onChange={(e) => setRemarks(e.currentTarget.value)} maxLength={300} />}
            <Group justify="flex-end">
              <Button variant="default" onClick={() => setPending(null)}>
                Cancel
              </Button>
              <Button color={actionText[pending].color} loading={run.isPending} onClick={() => run.mutate(pending)}>
                {actionText[pending].verb}
              </Button>
            </Group>
          </Stack>
        )}
      </Modal>
    </Stack>
  )
}
