import { Alert, Button, Grid, Group, Modal, NumberInput, Paper, Select, SimpleGrid, Stack, Table, Text, Textarea, TextInput } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconLockOpen, IconCircleCheck, IconCalculator } from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { api, ApiError, errorMessage, query } from '../api/client'
import { settingEnabled, useMe } from '../auth/session'
import { formatAmount, formatDate } from '../components/format'
import { PageHeader } from '../components/PageHeader'
import { Section } from '../components/Section'
import { LoanFinder, LoanSummary } from './LoanSummary'
import { paymentModes, type SettlementView } from './types'

function Row({ label, value, strong }: { label: string; value: number; strong?: boolean }) {
  return (
    <Table.Tr fw={strong ? 700 : undefined}>
      <Table.Td c={strong ? undefined : 'dimmed'}>{label}</Table.Td>
      <Table.Td className="dj-num">₹{formatAmount(value)}</Table.Td>
    </Table.Tr>
  )
}

/** LOAN MASTER > Loan Settlement: pre-close or re-open a loan. */
export function SettlementPage() {
  const { data: me } = useMe()
  const client = useQueryClient()
  const savingOn = settingEnabled(me, 'MODULE.SAVING.ENABLED')
  const canAct = me?.role === 'ADMIN' || me?.role === 'MANAGER'
  const [params, setParams] = useSearchParams()
  const loanNo = params.get('loan') ?? ''
  const [date, setDate] = useState<string | null>(me?.businessDate ?? null)
  const [lateFee, setLateFee] = useState<number | string>('')
  const [charges, setCharges] = useState<number | string>('')
  const [mode, setMode] = useState('CASH')
  const [ledgerId, setLedgerId] = useState<string | null>(null)
  const [chequeNo, setChequeNo] = useState('')
  const [remarks, setRemarks] = useState('')
  const [confirm, setConfirm] = useState<'close' | 'reopen' | null>(null)

  const view = useQuery({
    queryKey: ['settlement', loanNo, date],
    queryFn: () => api.get<SettlementView>(`/api/settlements/quote${query({ loanNo, date })}`),
    enabled: !!loanNo,
    retry: false,
  })
  const ledgers = useQuery({
    queryKey: ['accounts', 'cash-bank-ledgers'],
    queryFn: () => api.get<{ ledgerId: number; ledgerName: string }[]>('/api/accounts/cash-bank-ledgers'),
    staleTime: 5 * 60_000,
  })

  const v = view.data
  const q = v?.quote
  const effLateFee = lateFee === '' ? (v?.suggestedLateFee ?? 0) : Number(lateFee)
  const effCharges = charges === '' ? (q?.preCloseCharges ?? 0) : Number(charges)
  const net = (q?.amountToBeReceived ?? 0) + effLateFee + effCharges

  const done = async (message: string) => {
    notifications.show({ color: 'green', message })
    setConfirm(null)
    setLateFee('')
    setCharges('')
    await client.invalidateQueries({ queryKey: ['settlement'] })
    await client.invalidateQueries({ queryKey: ['recovery'] })
    await client.invalidateQueries({ queryKey: ['dashboard'] })
  }

  const close = useMutation({
    mutationFn: () =>
      api.post('/api/settlements/close', {
        loanId: v!.loan.loanId,
        date,
        lateFee: lateFee === '' ? null : Number(lateFee),
        preCloseCharges: charges === '' ? null : Number(charges),
        paymentMode: mode,
        ledgerId: mode === 'SAVING' || !ledgerId ? null : Number(ledgerId),
        chequeNo: chequeNo || null,
        remarks: remarks || null,
      }),
    onSuccess: () => done(`Loan ${v?.loan.loanNo} closed.`),
    onError: (e) => notifications.show({ color: 'red', message: e instanceof ApiError ? (Object.values(e.fieldErrors)[0] ?? e.message) : errorMessage(e) }),
  })
  const reopen = useMutation({
    mutationFn: () => api.post('/api/settlements/reopen', { loanId: v!.loan.loanId, date, remarks: remarks || null }),
    onSuccess: () => done(`Loan ${v?.loan.loanNo} re-opened.`),
    onError: (e) => notifications.show({ color: 'red', message: errorMessage(e) }),
  })

  const notFound = view.error instanceof ApiError && view.error.status === 404
  const modes = savingOn ? [...paymentModes, { value: 'SAVING', label: 'SAVING A/C' }] : paymentModes

  return (
    <Stack gap="md">
      <PageHeader title="Loan Settlement" subtitle="Pre-close a loan, or re-open a settled loan" />
      <LoanFinder initialLoanNo={loanNo} date={date} onDate={setDate} maxDate={me?.today} onFind={(no) => setParams({ loan: no.toUpperCase() })} loading={view.isFetching} />
      {notFound && <Alert color="orange">Loan {loanNo} was not found in this branch.</Alert>}
      {view.isError && !notFound && <Alert color="red">{errorMessage(view.error)}</Alert>}

      {v && q && (
        <>
          <LoanSummary loan={v.loan} />
          {v.last && (
            <Alert color={v.last.actionType === 'CLOSE' ? 'gray' : 'blue'} variant="light">
              Last settlement action: <b>{v.last.actionType}</b> on {formatDate(v.last.actionDate)}
              {v.last.actionType === 'CLOSE' && ` · ₹${formatAmount(v.last.netCloseAmount)} · voucher ${v.last.voucherNo ?? '-'}`}
            </Alert>
          )}
          <Grid gap="md">
            <Grid.Col span={{ base: 12, md: 6 }}>
              <Section title="Loan closer" icon={<IconCalculator size={18} />} padding={0}>
                <Table>
                  <Table.Tbody>
                    <Row label="Total principal received" value={q.principalReceived} />
                    <Row label="Total interest received" value={q.interestReceived} />
                    <Row label="Balance principal" value={q.balancePrincipal} />
                    <Row label="Interest due till date" value={q.interestDueTillDate} />
                    <Row label="Future interest waived" value={q.interestWaived} />
                    <Row label="Balance installment amount" value={q.balanceInstallmentAmount} />
                    <Row label="Amount to be received" value={q.amountToBeReceived} strong />
                  </Table.Tbody>
                </Table>
              </Section>
            </Grid.Col>
            <Grid.Col span={{ base: 12, md: 6 }}>
              <Section title="Loan payment details" icon={<IconCircleCheck size={18} />}>
                <Stack gap="sm">
                  <SimpleGrid cols={2}>
                    <NumberInput label="Late fee" description={`Due ${formatAmount(v.suggestedLateFee)}`} value={lateFee === '' ? v.suggestedLateFee : lateFee} onChange={setLateFee} min={0} decimalScale={2} disabled={!v.canClose} />
                    <NumberInput label="Pre-close charges" description={`${v.loan.preCloseChargesPct}% of balance principal`} value={charges === '' ? q.preCloseCharges : charges} onChange={setCharges} min={0} decimalScale={2} disabled={!v.canClose} />
                    <Select label="Payment mode" data={modes} value={mode} onChange={(x) => setMode(x ?? 'CASH')} allowDeselect={false} disabled={!v.canClose} />
                    {mode === 'SAVING' ? (
                      <TextInput label="Saving A/c" value={v.loan.savingAccountNo ?? ''} readOnly />
                    ) : (
                      <Select label="Received in" placeholder="CASH A/C" data={(ledgers.data ?? []).map((l) => ({ value: String(l.ledgerId), label: l.ledgerName }))} value={ledgerId} onChange={setLedgerId} clearable disabled={!v.canClose} />
                    )}
                  </SimpleGrid>
                  {mode === 'CHEQUE' && <TextInput label="Cheque No" value={chequeNo} onChange={(e) => setChequeNo(e.currentTarget.value)} />}
                  <Textarea label="Remarks" value={remarks} onChange={(e) => setRemarks(e.currentTarget.value)} maxLength={300} autosize minRows={1} />
                  <Paper p="sm" radius="md" bg="var(--dj-page-bg)">
                    <Group justify="space-between">
                      <Text c="dimmed">Net close amount</Text>
                      <Text fw={800} size="xl">
                        ₹{formatAmount(net)}
                      </Text>
                    </Group>
                  </Paper>
                  {canAct ? (
                    <Group justify="flex-end">
                      {v.canReopen && (
                        <Button color="orange" variant="light" leftSection={<IconLockOpen size={16} />} onClick={() => setConfirm('reopen')}>
                          Re-open loan
                        </Button>
                      )}
                      <Button leftSection={<IconCircleCheck size={16} />} disabled={!v.canClose} onClick={() => setConfirm('close')}>
                        Close loan
                      </Button>
                    </Group>
                  ) : (
                    <Text size="xs" c="dimmed">
                      Only managers can close or re-open loans.
                    </Text>
                  )}
                  {!v.canClose && !v.canReopen && (
                    <Text size="xs" c="dimmed">
                      {v.loan.status === 'CLOSED' ? 'Closed by full repayment - reverse the last receipt to re-open.' : `Loan is ${v.loan.status}.`}
                    </Text>
                  )}
                </Stack>
              </Section>
            </Grid.Col>
          </Grid>
        </>
      )}

      {!loanNo && (
        <Paper className="dj-card" p="xl">
          <Text c="dimmed" ta="center">
            Enter a loan number to see its closing amount.
          </Text>
        </Paper>
      )}

      <Modal opened={!!confirm} onClose={() => setConfirm(null)} title={confirm === 'close' ? 'Close loan' : 'Re-open loan'} centered>
        <Stack>
          <Text size="sm">
            {confirm === 'close'
              ? `Collect ₹${formatAmount(net)} and close ${v?.loan.loanNo} on ${formatDate(date)}? Future interest of ₹${formatAmount(q?.interestWaived ?? 0)} is waived.`
              : `Re-open ${v?.loan.loanNo}? The settlement voucher is cancelled and the EMI schedule restored.`}
          </Text>
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setConfirm(null)}>
              Cancel
            </Button>
            <Button
              color={confirm === 'close' ? 'brand' : 'orange'}
              loading={close.isPending || reopen.isPending}
              onClick={() => (confirm === 'close' ? close.mutate() : reopen.mutate())}
            >
              {confirm === 'close' ? 'Close loan' : 'Re-open'}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  )
}
