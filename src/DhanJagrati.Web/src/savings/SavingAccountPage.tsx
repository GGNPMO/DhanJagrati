import { Alert, Badge, Button, Group, Modal, NumberInput, Paper, ScrollArea, Select, SimpleGrid, Stack, Table, Text, TextInput } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { notifications } from '@mantine/notifications'
import { IconArrowDownLeft, IconArrowUpRight, IconLock, IconPrinter } from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useParams } from 'react-router'
import { api, ApiError, errorMessage, query } from '../api/client'
import { useMe } from '../auth/session'
import { formatAmount, formatDate } from '../components/format'
import { PageHeader } from '../components/PageHeader'
import { Section } from '../components/Section'
import { paymentModes } from '../recovery/types'
import type { SavingAccountDetail, SavingStatement } from './types'

/** SAVING > account: balance, deposit / withdrawal, statement. */
export function SavingAccountPage() {
  const { id } = useParams()
  const { data: me } = useMe()
  const client = useQueryClient()
  const canClose = me?.role === 'ADMIN' || me?.role === 'MANAGER'
  const [from, setFrom] = useState<string | null>(me ? `${me.businessDate.slice(0, 8)}01` : null)
  const [to, setTo] = useState<string | null>(me?.businessDate ?? null)
  const [txnType, setTxnType] = useState<'DEPOSIT' | 'WITHDRAWAL' | null>(null)
  const [closing, setClosing] = useState(false)

  const account = useQuery({ queryKey: ['savings', 'account', id], queryFn: () => api.get<SavingAccountDetail>(`/api/savings/accounts/${id}`) })
  const statement = useQuery({
    queryKey: ['savings', 'statement', id, from, to],
    queryFn: () => api.get<SavingStatement>(`/api/savings/accounts/${id}/statement${query({ from, to })}`),
    enabled: !!from && !!to,
  })

  const close = useMutation({
    mutationFn: () => api.post(`/api/savings/accounts/${id}/close`, {}),
    onSuccess: async () => {
      notifications.show({ color: 'green', message: 'Account closed.' })
      setClosing(false)
      await client.invalidateQueries({ queryKey: ['savings'] })
    },
    onError: (e) => notifications.show({ color: 'red', message: errorMessage(e) }),
  })

  const a = account.data
  if (account.isError) return <Alert color="red">{errorMessage(account.error)}</Alert>
  if (!a) return null
  const active = a.status === 'ACTIVE'

  return (
    <Stack gap="md">
      <PageHeader
        title={`Saving A/c ${a.accountNo}`}
        subtitle={`${a.memberName} (${a.memberNo}) · ${a.productName}`}
        badge={
          <Badge variant="light" color={active ? 'green' : 'gray'}>
            {a.status}
          </Badge>
        }
        actions={
          <Group gap="xs">
            <Button color="green" leftSection={<IconArrowDownLeft size={16} />} disabled={!active} onClick={() => setTxnType('DEPOSIT')}>
              Deposit
            </Button>
            <Button color="orange" variant="light" leftSection={<IconArrowUpRight size={16} />} disabled={!active || !a.allowWithdrawal} onClick={() => setTxnType('WITHDRAWAL')}>
              Withdraw
            </Button>
            {canClose && (
              <Button variant="default" leftSection={<IconLock size={16} />} disabled={!active} onClick={() => setClosing(true)}>
                Close
              </Button>
            )}
          </Group>
        }
      />

      <SimpleGrid cols={{ base: 2, sm: 4 }}>
        <Paper className="dj-card" p="md">
          <Text size="xs" c="dimmed">
            Balance
          </Text>
          <div className="dj-stat-value">₹{formatAmount(a.currentBalance)}</div>
        </Paper>
        <Paper className="dj-card" p="md">
          <Text size="xs" c="dimmed">
            Available to withdraw
          </Text>
          <Text fw={700} size="lg">
            ₹{formatAmount(Math.max(0, a.currentBalance - a.minBalance))}
          </Text>
        </Paper>
        <Paper className="dj-card" p="md">
          <Text size="xs" c="dimmed">
            Opened
          </Text>
          <Text fw={700}>{formatDate(a.openDate)}</Text>
        </Paper>
        <Paper className="dj-card" p="md">
          <Text size="xs" c="dimmed">
            Nominee / joint
          </Text>
          <Text fw={600} size="sm">
            {a.nomineeName ?? '-'}
            {a.jointMemberName && ` · ${a.jointMemberName}`}
          </Text>
        </Paper>
      </SimpleGrid>

      <Section
        title="Statement"
        right={
          <Button size="xs" variant="default" leftSection={<IconPrinter size={14} />} onClick={() => window.print()}>
            Print
          </Button>
        }
        padding={0}
      >
        <Group p="sm" gap="sm" className="dj-no-print">
          <DateInput label="From" value={from} onChange={setFrom} valueFormat="DD-MMM-YYYY" w={160} />
          <DateInput label="To" value={to} onChange={setTo} valueFormat="DD-MMM-YYYY" w={160} />
        </Group>
        <div className="dj-print-area">
          <Text p="sm" fw={600} className="dj-print-only">
            {me?.companyName} — Saving statement {a.accountNo} · {a.memberName} · {formatDate(from)} to {formatDate(to)}
          </Text>
          <ScrollArea type="auto">
            <Table striped miw={720}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>Date</Table.Th>
                  <Table.Th>Txn No</Table.Th>
                  <Table.Th>Particulars</Table.Th>
                  <Table.Th className="dj-num">Withdrawal</Table.Th>
                  <Table.Th className="dj-num">Deposit</Table.Th>
                  <Table.Th className="dj-num">Balance</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                <Table.Tr fw={600}>
                  <Table.Td colSpan={5}>Opening balance</Table.Td>
                  <Table.Td className="dj-num">{formatAmount(statement.data?.openingBalance ?? 0)}</Table.Td>
                </Table.Tr>
                {(statement.data?.lines ?? []).map((l) => (
                  <Table.Tr key={l.txnNo}>
                    <Table.Td>{formatDate(l.txnDate)}</Table.Td>
                    <Table.Td>{l.txnNo}</Table.Td>
                    <Table.Td>
                      <Text size="sm">{l.narration ?? l.txnType}</Text>
                      <Text size="xs" c="dimmed">
                        {l.txnType} · {l.paymentMode}
                        {l.chequeNo && ` · ${l.chequeNo}`}
                      </Text>
                    </Table.Td>
                    <Table.Td className="dj-num">{l.withdrawal ? formatAmount(l.withdrawal) : ''}</Table.Td>
                    <Table.Td className="dj-num">{l.deposit ? formatAmount(l.deposit) : ''}</Table.Td>
                    <Table.Td className="dj-num">{formatAmount(l.balanceAfter)}</Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </ScrollArea>
        </div>
      </Section>

      <TxnModal account={a} type={txnType} onClose={() => setTxnType(null)} />

      <Modal opened={closing} onClose={() => setClosing(false)} title="Close account" centered>
        <Stack>
          <Text size="sm">Only an account with zero balance can be closed. Close {a.accountNo}?</Text>
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setClosing(false)}>
              Cancel
            </Button>
            <Button color="red" loading={close.isPending} onClick={() => close.mutate()}>
              Close account
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  )
}

function TxnModal({ account, type, onClose }: { account: SavingAccountDetail; type: 'DEPOSIT' | 'WITHDRAWAL' | null; onClose: () => void }) {
  const { data: me } = useMe()
  const client = useQueryClient()
  const [amount, setAmount] = useState<number | string>('')
  const [date, setDate] = useState<string | null>(me?.businessDate ?? null)
  const [mode, setMode] = useState('CASH')
  const [chequeNo, setChequeNo] = useState('')
  const [narration, setNarration] = useState('')
  const [errors, setErrors] = useState<Record<string, string>>({})

  const post = useMutation({
    mutationFn: () =>
      api.post<{ txnNo: string; balanceAfter: number }>(`/api/savings/accounts/${account.id}/transactions`, {
        txnType: type,
        txnDate: date,
        amount: Number(amount) || 0,
        paymentMode: mode,
        chequeNo: chequeNo || null,
        narration: narration || null,
      }),
    onSuccess: async (r) => {
      notifications.show({ color: 'green', message: `${type === 'DEPOSIT' ? 'Deposit' : 'Withdrawal'} ${r.txnNo} saved. Balance ₹${formatAmount(r.balanceAfter)}.` })
      setAmount('')
      setNarration('')
      setErrors({})
      onClose()
      await client.invalidateQueries({ queryKey: ['savings'] })
    },
    onError: (e) => {
      if (e instanceof ApiError) setErrors(e.fieldErrors)
      notifications.show({ color: 'red', title: 'Not saved', message: errorMessage(e) })
    },
  })

  return (
    <Modal opened={!!type} onClose={onClose} title={type === 'DEPOSIT' ? 'Deposit' : 'Withdrawal'} centered>
      <Stack gap="sm">
        <Text size="sm" c="dimmed">
          {account.accountNo} · balance ₹{formatAmount(account.currentBalance)}
        </Text>
        <NumberInput label="Amount" withAsterisk value={amount} onChange={setAmount} min={1} decimalScale={2} thousandSeparator="," error={errors.amount} data-autofocus />
        <DateInput label="Date" value={date} onChange={setDate} valueFormat="DD-MMM-YYYY" maxDate={me?.today} error={errors.txnDate} />
        <Select label="Mode" data={paymentModes} value={mode} onChange={(v) => setMode(v ?? 'CASH')} allowDeselect={false} />
        {mode !== 'CASH' && <TextInput label="Cheque / Ref No" value={chequeNo} onChange={(e) => setChequeNo(e.currentTarget.value)} />}
        <TextInput label="Narration" value={narration} onChange={(e) => setNarration(e.currentTarget.value)} maxLength={300} />
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button color={type === 'DEPOSIT' ? 'green' : 'orange'} loading={post.isPending} disabled={!amount} onClick={() => post.mutate()}>
            Save
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}
