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
  Stack,
  Switch,
  Table,
  Text,
  Textarea,
  TextInput,
} from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { useDebouncedValue } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import { IconBan, IconDeviceFloppy, IconPrinter, IconReceipt2, IconSearch } from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, ApiError, errorMessage, query } from '../api/client'
import type { BranchOption } from '../api/types'
import { useMe } from '../auth/session'
import { formatAmount, formatDate } from '../components/format'
import { PageHeader } from '../components/PageHeader'
import { Section } from '../components/Section'
import type { LedgerRow, VoucherDetail, VoucherListItem } from './types'

const modes = ['CASH', 'CHEQUE', 'NEFT', 'RTGS', 'IMPS', 'UPI', 'TRANSFER'].map((v) => ({ value: v, label: v }))

type Draft = {
  voucherDate: string | null
  debitLedgerId: string | null
  creditLedgerId: string | null
  amount: number | string
  paymentMode: string
  chequeNo: string
  chequeDate: string | null
  payeeName: string
  narration: string
  transferToBranchId: string | null
  sanctionNo: string
}

/** ACCOUNT > Ledger Transaction Journal (manual payment / receipt / journal / contra / branch transfer). */
export function VoucherPage() {
  const { data: me } = useMe()
  const client = useQueryClient()
  const canCancel = me?.role === 'ADMIN' || me?.role === 'MANAGER'
  const empty = (): Draft => ({
    voucherDate: me?.businessDate ?? null,
    debitLedgerId: null,
    creditLedgerId: null,
    amount: '',
    paymentMode: 'CASH',
    chequeNo: '',
    chequeDate: null,
    payeeName: '',
    narration: '',
    transferToBranchId: null,
    sanctionNo: '',
  })
  const [draft, setDraft] = useState<Draft>(empty)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [from, setFrom] = useState<string | null>(me ? `${me.businessDate.slice(0, 8)}01` : null)
  const [to, setTo] = useState<string | null>(me?.businessDate ?? null)
  const [search, setSearch] = useState('')
  const [debounced] = useDebouncedValue(search, 300)
  const [manualOnly, setManualOnly] = useState(false)
  const [viewId, setViewId] = useState<number | null>(null)
  const [confirmCancel, setConfirmCancel] = useState(false)
  const patch = (p: Partial<Draft>) => setDraft((d) => ({ ...d, ...p }))

  const ledgers = useQuery({ queryKey: ['accounts', 'ledgers'], queryFn: () => api.get<LedgerRow[]>('/api/accounts/ledgers'), staleTime: 60_000 })
  const branches = useQuery({ queryKey: ['auth', 'branches'], queryFn: () => api.get<BranchOption[]>('/api/auth/branches'), staleTime: 5 * 60_000 })
  const list = useQuery({
    queryKey: ['accounts', 'vouchers', from, to, debounced, manualOnly],
    queryFn: () => api.get<VoucherListItem[]>(`/api/accounts/vouchers${query({ from, to, search: debounced, manualOnly })}`),
  })
  const voucher = useQuery({
    queryKey: ['accounts', 'voucher', viewId],
    queryFn: () => api.get<VoucherDetail>(`/api/accounts/vouchers/${viewId}`),
    enabled: viewId !== null,
  })

  const leafs = (ledgers.data ?? []).filter((l) => !l.isGroup && l.isActive)
  const options = leafs.map((l) => ({ value: String(l.id), label: l.ledgerName, group: l.cashBankType === 'N' ? l.natureName : 'Cash & bank' }))
  const grouped = Object.entries(
    options.reduce<Record<string, { value: string; label: string }[]>>((acc, o) => ((acc[o.group] ??= []).push({ value: o.value, label: o.label }), acc), {}),
  ).map(([group, items]) => ({ group, items }))
  const byId = (id: string | null) => leafs.find((l) => String(l.id) === id)
  const debit = byId(draft.debitLedgerId)
  const credit = byId(draft.creditLedgerId)
  const transfer = draft.transferToBranchId && draft.transferToBranchId !== String(me?.branchId)
  const voucherType = transfer
    ? 'BRANCH TRANSFER'
    : debit && credit
      ? debit.cashBankType !== 'N' && credit.cashBankType !== 'N'
        ? 'CONTRA'
        : credit.cashBankType !== 'N'
          ? 'PAYMENT'
          : debit.cashBankType !== 'N'
            ? 'RECEIPT'
            : 'JOURNAL'
      : null
  const needsSanction = !!(debit?.applySanction || credit?.applySanction)

  const save = useMutation({
    mutationFn: () =>
      api.post<{ id: number }>('/api/accounts/vouchers', {
        voucherDate: draft.voucherDate,
        debitLedgerId: Number(draft.debitLedgerId) || 0,
        creditLedgerId: Number(draft.creditLedgerId) || 0,
        amount: Number(draft.amount) || 0,
        paymentMode: draft.paymentMode,
        chequeNo: draft.chequeNo || null,
        chequeDate: draft.chequeDate,
        payeeName: draft.payeeName || null,
        narration: draft.narration || null,
        transferToBranchId: transfer ? Number(draft.transferToBranchId) : null,
        sanctionNo: draft.sanctionNo || null,
      }),
    onSuccess: async (r) => {
      notifications.show({ color: 'green', message: 'Voucher saved.' })
      setErrors({})
      setDraft(empty())
      setViewId(r.id)
      await client.invalidateQueries({ queryKey: ['accounts', 'vouchers'] })
      await client.invalidateQueries({ queryKey: ['accounts', 'ledgers'] })
    },
    onError: (e) => {
      if (e instanceof ApiError) setErrors(e.fieldErrors)
      notifications.show({ color: 'red', title: 'Not saved', message: errorMessage(e) })
    },
  })

  const cancel = useMutation({
    mutationFn: () => api.post(`/api/accounts/vouchers/${viewId}/cancel`),
    onSuccess: async () => {
      notifications.show({ color: 'green', message: 'Voucher cancelled.' })
      setConfirmCancel(false)
      await client.invalidateQueries({ queryKey: ['accounts'] })
    },
    onError: (e) => {
      setConfirmCancel(false)
      notifications.show({ color: 'red', message: errorMessage(e) })
    },
  })

  const v = voucher.data

  return (
    <Stack gap="md">
      <PageHeader title="Ledger Transaction Journal" subtitle="Payments, receipts, journals and branch transfers" />

      <Section
        title="Payment details"
        icon={<IconReceipt2 size={18} />}
        right={voucherType && <Badge variant="light">{voucherType}</Badge>}
      >
        <Grid gap="sm">
          <Grid.Col span={{ base: 12, sm: 4 }}>
            <DateInput label="Payment date" value={draft.voucherDate} onChange={(voucherDate) => patch({ voucherDate })} valueFormat="DD-MMM-YYYY" maxDate={me?.today} error={errors.voucherDate} />
          </Grid.Col>
          <Grid.Col span={{ base: 12, sm: 4 }}>
            <Select label="Ledger debit" withAsterisk data={grouped} value={draft.debitLedgerId} onChange={(debitLedgerId) => patch({ debitLedgerId })} searchable error={errors.debitLedgerId} />
          </Grid.Col>
          <Grid.Col span={{ base: 12, sm: 4 }}>
            <Select label="Ledger credit" withAsterisk data={grouped} value={draft.creditLedgerId} onChange={(creditLedgerId) => patch({ creditLedgerId })} searchable error={errors.creditLedgerId} />
          </Grid.Col>
          <Grid.Col span={{ base: 6, sm: 3 }}>
            <NumberInput label="Amount" withAsterisk value={draft.amount} onChange={(amount) => patch({ amount })} min={0.01} decimalScale={2} thousandSeparator="," error={errors.amount} />
          </Grid.Col>
          <Grid.Col span={{ base: 6, sm: 3 }}>
            <Select label="Payment option" data={modes} value={draft.paymentMode} onChange={(m) => patch({ paymentMode: m ?? 'CASH' })} allowDeselect={false} />
          </Grid.Col>
          <Grid.Col span={{ base: 6, sm: 3 }}>
            <TextInput label="Cheque / Ref No" value={draft.chequeNo} onChange={(e) => patch({ chequeNo: e.currentTarget.value })} error={errors.chequeNo} disabled={draft.paymentMode === 'CASH'} />
          </Grid.Col>
          <Grid.Col span={{ base: 6, sm: 3 }}>
            <DateInput label="Cheque date" value={draft.chequeDate} onChange={(chequeDate) => patch({ chequeDate })} valueFormat="DD-MMM-YYYY" disabled={draft.paymentMode === 'CASH'} clearable />
          </Grid.Col>
          <Grid.Col span={{ base: 12, sm: 6 }}>
            <TextInput label="Payee name" value={draft.payeeName} onChange={(e) => patch({ payeeName: e.currentTarget.value })} maxLength={150} />
          </Grid.Col>
          <Grid.Col span={{ base: 6, sm: 3 }}>
            <Select
              label="Transfer to branch"
              placeholder="Same branch"
              data={(branches.data ?? []).filter((b) => b.branchId !== me?.branchId).map((b) => ({ value: String(b.branchId), label: b.branchName }))}
              value={draft.transferToBranchId}
              onChange={(transferToBranchId) => patch({ transferToBranchId })}
              clearable
              error={errors.transferToBranchId}
            />
          </Grid.Col>
          <Grid.Col span={{ base: 6, sm: 3 }}>
            <TextInput label="Sanction No." withAsterisk={needsSanction} value={draft.sanctionNo} onChange={(e) => patch({ sanctionNo: e.currentTarget.value })} error={errors.sanctionNo} />
          </Grid.Col>
          <Grid.Col span={12}>
            <Textarea label="Remarks" withAsterisk value={draft.narration} onChange={(e) => patch({ narration: e.currentTarget.value })} maxLength={500} autosize minRows={2} error={errors.narration} />
          </Grid.Col>
        </Grid>
        {transfer && (
          <Alert mt="sm" color="blue" variant="light">
            Branch transfer: {me?.branchName} posts Dr INTER BRANCH / Cr {credit?.ledgerName ?? 'credit ledger'}; the other branch posts Dr {debit?.ledgerName ?? 'debit ledger'} / Cr INTER BRANCH.
          </Alert>
        )}
        <Group justify="flex-end" mt="md">
          <Button variant="default" onClick={() => (setErrors({}), setDraft(empty()))}>
            Clear
          </Button>
          <Button leftSection={<IconDeviceFloppy size={16} />} loading={save.isPending} onClick={() => save.mutate()}>
            Save voucher
          </Button>
        </Group>
      </Section>

      <Paper className="dj-card">
        <Group p="sm" gap="sm" align="flex-end">
          <DateInput label="From" value={from} onChange={setFrom} valueFormat="DD-MMM-YYYY" w={150} />
          <DateInput label="To" value={to} onChange={setTo} valueFormat="DD-MMM-YYYY" w={150} />
          <TextInput label="Search" placeholder="Voucher no, remarks, payee, cheque" leftSection={<IconSearch size={16} />} value={search} onChange={(e) => setSearch(e.currentTarget.value)} style={{ flex: 1, minWidth: 200 }} />
          <Switch label="Manual only" checked={manualOnly} onChange={(e) => setManualOnly(e.currentTarget.checked)} mb={8} />
        </Group>
        {list.isError && <Alert color="red">{errorMessage(list.error)}</Alert>}
        <ScrollArea type="auto">
          <Table className="dj-grid" striped highlightOnHover miw={880}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Tran. No</Table.Th>
                <Table.Th>Date</Table.Th>
                <Table.Th>Type</Table.Th>
                <Table.Th>Debit</Table.Th>
                <Table.Th>Credit</Table.Th>
                <Table.Th className="dj-num">Amount</Table.Th>
                <Table.Th>Remarks</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {(list.data ?? []).map((r) => (
                <Table.Tr key={r.id} onClick={() => setViewId(r.id)} opacity={r.isCancelled ? 0.5 : 1}>
                  <Table.Td fw={600} td={r.isCancelled ? 'line-through' : undefined}>
                    {r.voucherNo}
                  </Table.Td>
                  <Table.Td>{formatDate(r.voucherDate)}</Table.Td>
                  <Table.Td>
                    <Badge size="xs" variant="light" color={r.sourceModule ? 'gray' : 'brand'}>
                      {r.voucherType.replace('_', ' ')}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    <Text size="xs" lineClamp={1}>
                      {r.debitLedgers}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="xs" lineClamp={1}>
                      {r.creditLedgers}
                    </Text>
                  </Table.Td>
                  <Table.Td className="dj-num">{formatAmount(r.amount)}</Table.Td>
                  <Table.Td>
                    <Text size="xs" lineClamp={1}>
                      {r.narration}
                    </Text>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
          {list.data?.length === 0 && (
            <Text c="dimmed" ta="center" py="md">
              No vouchers in this period.
            </Text>
          )}
        </ScrollArea>
      </Paper>

      <Modal opened={viewId !== null} onClose={() => setViewId(null)} title={v ? `Voucher ${v.voucherNo}` : 'Voucher'} size="xl">
        {v && (
          <Stack>
            {v.isCancelled && <Alert color="red">Cancelled {v.cancelledAt ? new Date(v.cancelledAt + 'Z').toLocaleString('en-IN') : ''}</Alert>}
            <div className="dj-print-area">
              <div className="dj-print-sheet" style={{ padding: 12 }}>
                <div style={{ textAlign: 'center', marginBottom: 10 }}>
                  <div style={{ fontWeight: 700 }}>{me?.companyName}</div>
                  <div style={{ fontSize: 11 }}>{me?.companyAddress}</div>
                  <div style={{ fontWeight: 600, marginTop: 4 }}>{v.voucherType.replace('_', ' ')} VOUCHER</div>
                </div>
                <table style={{ marginBottom: 8 }}>
                  <tbody>
                    <tr>
                      <td style={{ border: 'none' }}>
                        Tran. No: <b>{v.voucherNo}</b>
                      </td>
                      <td style={{ border: 'none' }}>
                        Date: <b>{formatDate(v.voucherDate)}</b>
                      </td>
                      <td style={{ border: 'none' }}>Branch: {v.branchName}</td>
                    </tr>
                    <tr>
                      <td style={{ border: 'none' }}>Mode: {v.paymentMode}</td>
                      <td style={{ border: 'none' }}>{v.chequeNo ? `Cheque: ${v.chequeNo} ${formatDate(v.chequeDate)}` : ''}</td>
                      <td style={{ border: 'none' }}>{v.payeeName ? `Payee: ${v.payeeName}` : ''}</td>
                    </tr>
                  </tbody>
                </table>
                <table>
                  <thead>
                    <tr>
                      <th>Ledger</th>
                      <th>Branch</th>
                      <th style={{ textAlign: 'right' }}>Debit</th>
                      <th style={{ textAlign: 'right' }}>Credit</th>
                    </tr>
                  </thead>
                  <tbody>
                    {v.lines.map((l) => (
                      <tr key={l.lineNumber}>
                        <td>{l.ledgerName}</td>
                        <td>{l.branchName}</td>
                        <td style={{ textAlign: 'right' }}>{l.debit ? formatAmount(l.debit) : ''}</td>
                        <td style={{ textAlign: 'right' }}>{l.credit ? formatAmount(l.credit) : ''}</td>
                      </tr>
                    ))}
                    <tr style={{ fontWeight: 700 }}>
                      <td colSpan={2}>Total</td>
                      <td style={{ textAlign: 'right' }}>{formatAmount(v.lines.reduce((s, l) => s + l.debit, 0))}</td>
                      <td style={{ textAlign: 'right' }}>{formatAmount(v.lines.reduce((s, l) => s + l.credit, 0))}</td>
                    </tr>
                  </tbody>
                </table>
                <p style={{ marginTop: 8 }}>Remarks: {v.narration}</p>
                {v.sanctionNo && <p>Sanction No: {v.sanctionNo}</p>}
                <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 32, fontSize: 12 }}>
                  <span>Prepared by: {v.createdByName}</span>
                  <span style={{ borderTop: '1px solid #333', paddingTop: 4 }}>Authorised signatory</span>
                </div>
              </div>
            </div>
            {v.sourceModule && (
              <Text size="xs" c="dimmed">
                Posted automatically by {v.sourceModule}; reverse it from that screen.
              </Text>
            )}
            <Group justify="flex-end">
              {canCancel && !v.sourceModule && !v.isCancelled && (
                <Button color="red" variant="light" leftSection={<IconBan size={16} />} onClick={() => setConfirmCancel(true)}>
                  Cancel voucher
                </Button>
              )}
              <Button leftSection={<IconPrinter size={16} />} onClick={() => window.print()}>
                Print
              </Button>
            </Group>
          </Stack>
        )}
      </Modal>

      <Modal opened={confirmCancel} onClose={() => setConfirmCancel(false)} title="Cancel voucher" centered>
        <Text mb="md">Cancel voucher {v?.voucherNo}? It stays on record but no longer affects balances.</Text>
        <Group justify="flex-end">
          <Button variant="default" onClick={() => setConfirmCancel(false)}>
            Keep
          </Button>
          <Button color="red" loading={cancel.isPending} onClick={() => cancel.mutate()}>
            Cancel voucher
          </Button>
        </Group>
      </Modal>
    </Stack>
  )
}
