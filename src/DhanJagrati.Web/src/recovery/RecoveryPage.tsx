import {
  Alert,
  Badge,
  Button,
  Grid,
  Group,
  Modal,
  NumberInput,
  Paper,
  ScrollArea,
  Select,
  SimpleGrid,
  Stack,
  Table,
  Tabs,
  Text,
  Textarea,
  TextInput,
} from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { notifications } from '@mantine/notifications'
import { IconArrowBackUp, IconCash, IconPrinter, IconReceipt } from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { api, ApiError, errorMessage, query } from '../api/client'
import { settingEnabled, useMe } from '../auth/session'
import { formatAmount, formatDate } from '../components/format'
import { PageHeader } from '../components/PageHeader'
import { Section } from '../components/Section'
import { ScheduleTable } from '../loans/ScheduleTable'
import { LoanFinder, LoanSummary } from './LoanSummary'
import { ReceiptSheet } from './ReceiptSheet'
import { paymentModes, type ReceiptHistoryItem, type RecoveryPrefill, type RecoveryReceipt } from './types'

type Ledger = { ledgerId: number; ledgerName: string; cashBankType: string }

function Figure({ label, value, tone }: { label: string; value: string; tone?: string }) {
  return (
    <Paper p="xs" radius="md" bg="var(--dj-page-bg)">
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Text fw={700} c={tone} style={{ fontVariantNumeric: 'tabular-nums' }}>
        {value}
      </Text>
    </Paper>
  )
}

/** Payment form; re-mounted (key) whenever the loan or date changes so defaults come from the prefill. */
function PaymentForm({ prefill, date, onPosted }: { prefill: RecoveryPrefill; date: string | null; onPosted: (r: RecoveryReceipt) => void }) {
  const { data: me } = useMe()
  const savingOn = settingEnabled(me, 'MODULE.SAVING.ENABLED')
  const [amount, setAmount] = useState<number | string>(prefill.overdueAmount > 0 ? prefill.overdueAmount : prefill.nextInstallmentBalance)
  const [lateFee, setLateFee] = useState<number | string>(prefill.lateFeeSuggested)
  const [bounce, setBounce] = useState<number | string>(0)
  const [mode, setMode] = useState<string>('CASH')
  const [ledgerId, setLedgerId] = useState<string | null>(null)
  const [chequeNo, setChequeNo] = useState('')
  const [bankName, setBankName] = useState('')
  const [refTransNo, setRefTransNo] = useState('')
  const [savingAccountNo, setSavingAccountNo] = useState(prefill.loan.savingAccountNo ?? '')
  const [remarks, setRemarks] = useState('')
  const [errors, setErrors] = useState<Record<string, string>>({})

  const ledgers = useQuery({
    queryKey: ['accounts', 'cash-bank-ledgers'],
    queryFn: () => api.get<Ledger[]>('/api/accounts/cash-bank-ledgers'),
    staleTime: 5 * 60_000,
  })

  const post = useMutation({
    mutationFn: () =>
      api.post<RecoveryReceipt>('/api/recoveries', {
        loanId: prefill.loan.loanId,
        transDate: date,
        amount: Number(amount) || 0,
        lateFee: Number(lateFee) || 0,
        chequeBounceCharges: Number(bounce) || 0,
        paymentMode: mode,
        ledgerId: mode === 'SAVING' ? null : ledgerId ? Number(ledgerId) : null,
        chequeNo: chequeNo || null,
        bankName: bankName || null,
        refTransNo: refTransNo || null,
        savingAccountNo: mode === 'SAVING' ? savingAccountNo || null : null,
        remarks: remarks || null,
      }),
    onSuccess: (receipt) => {
      setErrors({})
      onPosted(receipt)
    },
    onError: (e) => {
      if (e instanceof ApiError) setErrors(e.fieldErrors)
      notifications.show({ color: 'red', title: 'Not saved', message: errorMessage(e) })
    },
  })

  const total = (Number(amount) || 0) + (Number(lateFee) || 0) + (Number(bounce) || 0)
  const modes = savingOn ? [...paymentModes, { value: 'SAVING', label: 'SAVING A/C' }] : paymentModes

  return (
    <Section title="Loan Recovery Details" icon={<IconCash size={18} />}>
      <Grid gap="sm">
        <Grid.Col span={{ base: 12, sm: 4 }}>
          <NumberInput label="EMI amount received" value={amount} onChange={setAmount} min={0} decimalScale={2} thousandSeparator="," error={errors.amount} />
        </Grid.Col>
        <Grid.Col span={{ base: 6, sm: 4 }}>
          <NumberInput label="Late fee" description={`Suggested ${formatAmount(prefill.lateFeeSuggested)}`} value={lateFee} onChange={setLateFee} min={0} decimalScale={2} />
        </Grid.Col>
        <Grid.Col span={{ base: 6, sm: 4 }}>
          <NumberInput label="Cheque bounce charges" value={bounce} onChange={setBounce} min={0} decimalScale={2} />
        </Grid.Col>
        <Grid.Col span={{ base: 6, sm: 4 }}>
          <Select label="Payment mode" data={modes} value={mode} onChange={(v) => setMode(v ?? 'CASH')} allowDeselect={false} error={errors.paymentMode} />
        </Grid.Col>
        {mode === 'SAVING' ? (
          <Grid.Col span={{ base: 6, sm: 4 }}>
            <TextInput label="Saving A/c No" value={savingAccountNo} onChange={(e) => setSavingAccountNo(e.currentTarget.value)} error={errors.savingAccountNo} />
          </Grid.Col>
        ) : (
          <Grid.Col span={{ base: 6, sm: 4 }}>
            <Select
              label="Received in"
              placeholder="CASH A/C"
              data={(ledgers.data ?? []).map((l) => ({ value: String(l.ledgerId), label: l.ledgerName }))}
              value={ledgerId}
              onChange={setLedgerId}
              clearable
              error={errors.ledgerId}
            />
          </Grid.Col>
        )}
        {mode !== 'CASH' && mode !== 'SAVING' && (
          <>
            <Grid.Col span={{ base: 6, sm: 4 }}>
              <TextInput label={mode === 'CHEQUE' ? 'Cheque No' : 'Trans No'} value={mode === 'CHEQUE' ? chequeNo : refTransNo} onChange={(e) => (mode === 'CHEQUE' ? setChequeNo : setRefTransNo)(e.currentTarget.value)} error={errors.chequeNo ?? errors.refTransNo} />
            </Grid.Col>
            <Grid.Col span={{ base: 6, sm: 4 }}>
              <TextInput label="Bank name" value={bankName} onChange={(e) => setBankName(e.currentTarget.value)} />
            </Grid.Col>
          </>
        )}
        <Grid.Col span={12}>
          <Textarea label="Remarks" value={remarks} onChange={(e) => setRemarks(e.currentTarget.value)} maxLength={300} autosize minRows={1} />
        </Grid.Col>
      </Grid>
      <Group justify="space-between" mt="md">
        <Text>
          Net amount: <b style={{ fontSize: 20 }}>₹{formatAmount(total)}</b>
        </Text>
        <Button leftSection={<IconReceipt size={16} />} loading={post.isPending} disabled={total <= 0 || prefill.loan.status !== 'APPROVED'} onClick={() => post.mutate()}>
          Save receipt
        </Button>
      </Group>
    </Section>
  )
}

/** LOAN MASTER > Loan Recovery (single). */
export function RecoveryPage() {
  const { data: me } = useMe()
  const client = useQueryClient()
  const [params, setParams] = useSearchParams()
  const loanNo = params.get('loan') ?? ''
  const [date, setDate] = useState<string | null>(me?.businessDate ?? null)
  const [receipt, setReceipt] = useState<RecoveryReceipt | null>(null)
  const [reverseNo, setReverseNo] = useState<string | null>(null)
  const canReverse = me?.role === 'ADMIN' || me?.role === 'MANAGER'

  const prefill = useQuery({
    queryKey: ['recovery', 'prefill', loanNo, date],
    queryFn: () => api.get<RecoveryPrefill>(`/api/recoveries/prefill${query({ loanNo, date })}`),
    enabled: !!loanNo,
    retry: false,
  })
  const loanId = prefill.data?.loan.loanId
  const history = useQuery({
    queryKey: ['recovery', 'history', loanId],
    queryFn: () => api.get<ReceiptHistoryItem[]>(`/api/recoveries?loanId=${loanId}`),
    enabled: !!loanId,
  })

  const refresh = async () => {
    await client.invalidateQueries({ queryKey: ['recovery'] })
    await client.invalidateQueries({ queryKey: ['dashboard'] })
    await client.invalidateQueries({ queryKey: ['loans'] })
  }

  const reverse = useMutation({
    mutationFn: (receiptNo: string) => api.post(`/api/recoveries/receipts/${encodeURIComponent(receiptNo)}/reverse`, { date }),
    onSuccess: async () => {
      notifications.show({ color: 'green', message: `Receipt ${reverseNo} reversed.` })
      setReverseNo(null)
      await refresh()
    },
    onError: (e) => notifications.show({ color: 'red', message: errorMessage(e) }),
  })

  const p = prefill.data
  const notFound = prefill.error instanceof ApiError && prefill.error.status === 404

  return (
    <Stack gap="md">
      <PageHeader title="Loan Recovery" subtitle="Receive an EMI against a loan" />
      <LoanFinder initialLoanNo={loanNo} date={date} onDate={setDate} maxDate={me?.today} onFind={(no) => setParams({ loan: no.toUpperCase() })} loading={prefill.isFetching} />

      {notFound && <Alert color="orange">Loan {loanNo} was not found in this branch.</Alert>}
      {prefill.isError && !notFound && <Alert color="red">{errorMessage(prefill.error)}</Alert>}

      {p && (
        <>
          <LoanSummary loan={p.loan} />
          {p.loan.status !== 'APPROVED' && (
            <Alert color="gray">This loan is {p.loan.status}; EMIs cannot be received.</Alert>
          )}
          <SimpleGrid cols={{ base: 2, sm: 3, lg: 6 }}>
            <Figure label="EMI No." value={p.nextInstallmentNo ? `${p.nextInstallmentNo} of ${p.loan.noOfInstallments}` : '-'} />
            <Figure label="Due date" value={formatDate(p.nextDueDate) || '-'} />
            <Figure label="Next due date" value={formatDate(p.followingDueDate) || '-'} />
            <Figure label={`Overdue (${p.overdueCount})`} value={`₹${formatAmount(p.overdueAmount)}`} tone={p.overdueCount > 0 ? 'red' : undefined} />
            <Figure label="Late fee due" value={`₹${formatAmount(p.lateFeeSuggested)}`} />
            <Figure label="Total outstanding" value={`₹${formatAmount(p.totalOutstanding)}`} />
          </SimpleGrid>

          <Grid gap="md">
            <Grid.Col span={{ base: 12, lg: 7 }}>
              <PaymentForm
                key={`${p.loan.loanId}-${date}-${history.data?.length ?? 0}`}
                prefill={p}
                date={date}
                onPosted={async (r) => {
                  setReceipt(r)
                  notifications.show({ color: 'green', message: `Receipt ${r.receiptNo} saved.` })
                  await refresh()
                }}
              />
            </Grid.Col>
            <Grid.Col span={{ base: 12, lg: 5 }}>
              <Paper className="dj-card">
                <Tabs defaultValue="receipts">
                  <Tabs.List px="sm" pt={4}>
                    <Tabs.Tab value="receipts">Receipts ({history.data?.length ?? 0})</Tabs.Tab>
                    <Tabs.Tab value="schedule">Schedule</Tabs.Tab>
                  </Tabs.List>
                  <Tabs.Panel value="receipts">
                    <ScrollArea.Autosize mah={380} type="auto">
                      <Table striped fz="sm">
                        <Table.Tbody>
                          {(history.data ?? []).map((h) => (
                            <Table.Tr key={h.receiptNo} opacity={h.isReversed ? 0.55 : 1}>
                              <Table.Td>
                                <Text size="sm" fw={600} td={h.isReversed ? 'line-through' : undefined}>
                                  {h.receiptNo}
                                </Text>
                                <Text size="xs" c="dimmed">
                                  {formatDate(h.transDate)} · EMI {h.installments} · {h.paymentMode}
                                  {h.isBulk && ' · bulk'}
                                </Text>
                              </Table.Td>
                              <Table.Td className="dj-num">₹{formatAmount(h.total)}</Table.Td>
                              <Table.Td w={40}>
                                {h.isReversed ? (
                                  <Badge size="xs" color="gray">
                                    Reversed
                                  </Badge>
                                ) : (
                                  canReverse && (
                                    <Button size="compact-xs" variant="subtle" color="red" leftSection={<IconArrowBackUp size={12} />} onClick={() => setReverseNo(h.receiptNo)}>
                                      Reverse
                                    </Button>
                                  )
                                )}
                              </Table.Td>
                            </Table.Tr>
                          ))}
                        </Table.Tbody>
                      </Table>
                      {history.data?.length === 0 && (
                        <Text size="sm" c="dimmed" ta="center" py="md">
                          No receipts yet.
                        </Text>
                      )}
                    </ScrollArea.Autosize>
                  </Tabs.Panel>
                  <Tabs.Panel value="schedule" p="xs">
                    <ScheduleTable rows={p.schedule} height={380} />
                  </Tabs.Panel>
                </Tabs>
              </Paper>
            </Grid.Col>
          </Grid>
        </>
      )}

      {!loanNo && (
        <Paper className="dj-card" p="xl">
          <Text c="dimmed" ta="center">
            Enter a loan number to receive an EMI.
          </Text>
        </Paper>
      )}

      <Modal opened={!!receipt} onClose={() => setReceipt(null)} title="Receipt saved" size="lg">
        {receipt && me && (
          <Stack>
            {receipt.savingBalance !== null && <Alert color="blue">Saving balance after debit: ₹{formatAmount(receipt.savingBalance)}</Alert>}
            <div className="dj-print-area">
              <ReceiptSheet receipt={receipt} me={me} />
            </div>
            <Group justify="flex-end">
              <Button variant="default" onClick={() => setReceipt(null)}>
                Close
              </Button>
              <Button leftSection={<IconPrinter size={16} />} onClick={() => window.print()}>
                Print receipt
              </Button>
            </Group>
          </Stack>
        )}
      </Modal>

      <Modal opened={!!reverseNo} onClose={() => setReverseNo(null)} title={`Reverse receipt ${reverseNo}`} centered>
        <Stack>
          <Text size="sm">The EMIs return to unpaid, the voucher is cancelled and any saving debit is credited back. Continue?</Text>
          <DateInput label="Reversal date" value={date} onChange={setDate} valueFormat="DD-MMM-YYYY" maxDate={me?.today} />
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setReverseNo(null)}>
              Cancel
            </Button>
            <Button color="red" loading={reverse.isPending} onClick={() => reverseNo && reverse.mutate(reverseNo)}>
              Reverse
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  )
}
