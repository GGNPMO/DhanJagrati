import {
  ActionIcon,
  Alert,
  Badge,
  Button,
  Checkbox,
  Grid,
  Group,
  Modal,
  NumberInput,
  Paper,
  ScrollArea,
  SegmentedControl,
  Select,
  SimpleGrid,
  Stack,
  Text,
  TextInput,
  UnstyledButton,
} from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { notifications } from '@mantine/notifications'
import {
  IconBuildingBank,
  IconCash,
  IconChevronDown,
  IconChevronRight,
  IconDeviceFloppy,
  IconFolder,
  IconFolderPlus,
  IconLock,
  IconPlus,
  IconReportSearch,
  IconSearch,
  IconTrash,
} from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { api, ApiError, errorMessage } from '../api/client'
import { useMe } from '../auth/session'
import { formatAmount } from '../components/format'
import { PageHeader } from '../components/PageHeader'
import { Section } from '../components/Section'
import type { AccountNature, LedgerRow } from './types'

type Draft = {
  id: number | null
  isGroup: boolean
  ledgerName: string
  parentLedgerId: string | null
  natureId: string | null
  reportHead: string | null
  cashBankType: 'C' | 'B' | 'N'
  bankAccountNo: string
  ifscCode: string
  isVisibleInBranch: boolean
  isDefaultGroup: boolean
  applySanction: boolean
  isActive: boolean
  openingBalance: number | string
  openingBalType: 'D' | 'C'
  openingDate: string | null
}

const newDraft = (isGroup: boolean, parent: LedgerRow | null): Draft => ({
  id: null,
  isGroup,
  ledgerName: '',
  parentLedgerId: parent ? String(parent.id) : null,
  natureId: null,
  reportHead: null,
  cashBankType: 'N',
  bankAccountNo: '',
  ifscCode: '',
  isVisibleInBranch: true,
  isDefaultGroup: false,
  applySanction: false,
  isActive: true,
  openingBalance: '',
  openingBalType: 'D',
  openingDate: null,
})

const fromRow = (l: LedgerRow): Draft => ({
  id: l.id,
  isGroup: l.isGroup,
  ledgerName: l.ledgerName,
  parentLedgerId: l.parentLedgerId ? String(l.parentLedgerId) : null,
  natureId: String(l.natureId),
  reportHead: l.reportHead,
  cashBankType: l.cashBankType,
  bankAccountNo: l.bankAccountNo ?? '',
  ifscCode: l.ifscCode ?? '',
  isVisibleInBranch: l.isVisibleInBranch,
  isDefaultGroup: l.isDefaultGroup,
  applySanction: l.applySanction,
  isActive: l.isActive,
  openingBalance: l.openingBalance ?? '',
  openingBalType: l.openingBalType ?? 'D',
  openingDate: l.openingDate,
})

function TreeNode({
  node,
  childrenOf,
  depth,
  selectedId,
  onSelect,
  expanded,
  toggle,
  filter,
}: {
  node: LedgerRow
  childrenOf: Map<number | null, LedgerRow[]>
  depth: number
  selectedId: number | null
  onSelect: (l: LedgerRow) => void
  expanded: Set<number>
  toggle: (id: number) => void
  filter: string
}) {
  const kids = childrenOf.get(node.id) ?? []
  const open = expanded.has(node.id) || filter.length > 0
  const matches = (l: LedgerRow): boolean => l.ledgerName.toLowerCase().includes(filter) || (childrenOf.get(l.id) ?? []).some(matches)
  if (filter && !matches(node)) return null
  const Icon = node.isGroup ? IconFolder : node.cashBankType === 'C' ? IconCash : node.cashBankType === 'B' ? IconBuildingBank : null

  return (
    <>
      <Group gap={4} wrap="nowrap" pl={depth * 16} py={2} bg={selectedId === node.id ? 'var(--mantine-color-brand-light)' : undefined} style={{ borderRadius: 6 }}>
        {node.isGroup ? (
          <ActionIcon size="sm" variant="subtle" color="gray" onClick={() => toggle(node.id)} aria-label="Expand">
            {open ? <IconChevronDown size={14} /> : <IconChevronRight size={14} />}
          </ActionIcon>
        ) : (
          <span style={{ width: 22 }} />
        )}
        <UnstyledButton onClick={() => onSelect(node)} style={{ flex: 1, minWidth: 0 }}>
          <Group gap={6} wrap="nowrap">
            {Icon && <Icon size={15} color={node.isGroup ? 'var(--mantine-color-yellow-7)' : 'var(--mantine-color-teal-7)'} />}
            <Text size="sm" fw={node.isGroup ? 600 : 400} truncate c={node.isActive ? undefined : 'dimmed'} td={node.isActive ? undefined : 'line-through'}>
              {node.ledgerName}
            </Text>
            {node.isSystem && <IconLock size={12} color="var(--mantine-color-gray-5)" />}
          </Group>
        </UnstyledButton>
        {!node.isGroup && node.openingBalance ? (
          <Text size="xs" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
            {formatAmount(node.openingBalance)} {node.openingBalType === 'D' ? 'Dr' : 'Cr'}
          </Text>
        ) : null}
      </Group>
      {node.isGroup &&
        open &&
        kids.map((k) => (
          <TreeNode key={k.id} node={k} childrenOf={childrenOf} depth={depth + 1} selectedId={selectedId} onSelect={onSelect} expanded={expanded} toggle={toggle} filter={filter} />
        ))}
    </>
  )
}

/** ACCOUNT > Add Groups and Ledgers. */
export function LedgersPage() {
  const { data: me } = useMe()
  const client = useQueryClient()
  const canEdit = me?.role === 'ADMIN' || me?.role === 'MANAGER' || me?.role === 'ACCOUNTANT'
  const ledgers = useQuery({ queryKey: ['accounts', 'ledgers'], queryFn: () => api.get<LedgerRow[]>('/api/accounts/ledgers') })
  const natures = useQuery({ queryKey: ['accounts', 'natures'], queryFn: () => api.get<AccountNature[]>('/api/accounts/natures'), staleTime: Infinity })

  const [filter, setFilter] = useState('')
  const [expanded, setExpanded] = useState<Set<number>>(new Set())
  const [draft, setDraft] = useState<Draft>(newDraft(false, null))
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [confirmDelete, setConfirmDelete] = useState(false)

  const rows = useMemo(() => ledgers.data ?? [], [ledgers.data])
  const childrenOf = useMemo(() => {
    const map = new Map<number | null, LedgerRow[]>()
    for (const l of rows) map.set(l.parentLedgerId, [...(map.get(l.parentLedgerId) ?? []), l])
    return map
  }, [rows])
  const selected = rows.find((l) => l.id === draft.id) ?? null
  const parent = rows.find((l) => String(l.id) === draft.parentLedgerId) ?? null

  // A group cannot move under itself or its own descendants
  const descendants = useMemo(() => {
    const set = new Set<number>()
    const walk = (id: number) => (childrenOf.get(id) ?? []).forEach((k) => (set.add(k.id), walk(k.id)))
    if (draft.id) walk(draft.id)
    return set
  }, [draft.id, childrenOf])
  const groupOptions = rows
    .filter((l) => l.isGroup && l.id !== draft.id && !descendants.has(l.id))
    .map((l) => ({ value: String(l.id), label: `${l.ledgerName}  ·  ${l.natureName}` }))

  const locked = !canEdit || (selected !== null && !selected.isEditable)
  const systemLocked = selected?.isSystem ?? false
  const patch = (p: Partial<Draft>) => setDraft((d) => ({ ...d, ...p }))

  const save = useMutation({
    mutationFn: async () => {
      const body = {
        ledgerName: draft.ledgerName,
        isGroup: draft.isGroup,
        parentLedgerId: draft.parentLedgerId ? Number(draft.parentLedgerId) : null,
        natureId: draft.natureId ? Number(draft.natureId) : null,
        reportHead: draft.reportHead,
        cashBankType: draft.cashBankType,
        bankAccountNo: draft.bankAccountNo || null,
        ifscCode: draft.ifscCode || null,
        isVisibleInBranch: draft.isVisibleInBranch,
        isDefaultGroup: draft.isDefaultGroup,
        applySanction: draft.applySanction,
        isActive: draft.isActive,
        openingBalance: draft.openingBalance === '' ? null : Number(draft.openingBalance),
        openingBalType: draft.openingBalType,
        openingDate: draft.openingDate,
      }
      if (draft.id) {
        await api.put(`/api/accounts/ledgers/${draft.id}`, body)
        return draft.id
      }
      return (await api.post<{ id: number }>('/api/accounts/ledgers', body)).id
    },
    onSuccess: async (id) => {
      notifications.show({ color: 'green', message: `${draft.isGroup ? 'Group' : 'Ledger'} saved.` })
      setErrors({})
      const fresh = await client.fetchQuery({ queryKey: ['accounts', 'ledgers'], queryFn: () => api.get<LedgerRow[]>('/api/accounts/ledgers') })
      const row = fresh.find((l) => l.id === id)
      if (row) {
        setDraft(fromRow(row))
        if (row.parentLedgerId) setExpanded((e) => new Set(e).add(row.parentLedgerId!))
      }
      client.invalidateQueries({ queryKey: ['accounts', 'cash-bank-ledgers'] })
    },
    onError: (e) => {
      if (e instanceof ApiError) setErrors(e.fieldErrors)
      notifications.show({ color: 'red', title: 'Not saved', message: errorMessage(e) })
    },
  })

  const remove = useMutation({
    mutationFn: () => api.delete(`/api/accounts/ledgers/${draft.id}`),
    onSuccess: async () => {
      notifications.show({ color: 'green', message: 'Deleted.' })
      setConfirmDelete(false)
      setDraft(newDraft(false, parent))
      await client.invalidateQueries({ queryKey: ['accounts'] })
    },
    onError: (e) => {
      setConfirmDelete(false)
      notifications.show({ color: 'red', message: errorMessage(e) })
    },
  })

  return (
    <Stack gap="md">
      <PageHeader
        title="Groups and Ledgers"
        subtitle="Chart of accounts · opening balances are for the current branch"
        actions={
          canEdit && (
            <Group gap="xs">
              <Button variant="light" leftSection={<IconPlus size={16} />} onClick={() => (setErrors({}), setDraft(newDraft(false, selected?.isGroup ? selected : parent)))}>
                New ledger
              </Button>
              <Button variant="default" leftSection={<IconFolderPlus size={16} />} onClick={() => (setErrors({}), setDraft(newDraft(true, selected?.isGroup ? selected : parent)))}>
                New group
              </Button>
            </Group>
          )
        }
      />
      <Grid gap="md">
        <Grid.Col span={{ base: 12, md: 5 }}>
          <Paper className="dj-card" p="sm">
            <TextInput placeholder="Search ledger" leftSection={<IconSearch size={16} />} value={filter} onChange={(e) => setFilter(e.currentTarget.value.toLowerCase())} mb="xs" />
            {ledgers.isError && <Alert color="red">{errorMessage(ledgers.error)}</Alert>}
            <ScrollArea h={{ base: 320, md: 620 }} type="auto">
              {(childrenOf.get(null) ?? []).map((root) => (
                <TreeNode
                  key={root.id}
                  node={root}
                  childrenOf={childrenOf}
                  depth={0}
                  selectedId={draft.id}
                  onSelect={(l) => (setErrors({}), setDraft(fromRow(l)))}
                  expanded={expanded}
                  toggle={(id) => setExpanded((e) => {
                    const next = new Set(e)
                    if (next.has(id)) next.delete(id)
                    else next.add(id)
                    return next
                  })}
                  filter={filter}
                />
              ))}
            </ScrollArea>
          </Paper>
        </Grid.Col>

        <Grid.Col span={{ base: 12, md: 7 }}>
          <Section
            title={draft.id ? `${draft.isGroup ? 'Group' : 'Ledger'}: ${selected?.ledgerName ?? ''}` : draft.isGroup ? 'Create group' : 'Create ledger'}
            icon={draft.isGroup ? <IconFolder size={18} /> : <IconCash size={18} />}
            right={
              <Group gap={6}>
                {selected?.isSystem && (
                  <Badge size="sm" variant="light" color="gray" leftSection={<IconLock size={11} />}>
                    System
                  </Badge>
                )}
                {selected?.hasPostings && (
                  <Badge size="sm" variant="light" color="blue">
                    Has postings
                  </Badge>
                )}
              </Group>
            }
          >
            <Stack gap="sm">
              {selected && !selected.isEditable && <Alert color="gray">Primary groups are fixed. Create sub-groups or ledgers under them.</Alert>}
              {selected?.mappingKeys && (
                <Text size="xs" c="dimmed">
                  Used for automatic postings: {selected.mappingKeys}
                </Text>
              )}
              <TextInput label="Name" withAsterisk value={draft.ledgerName} onChange={(e) => patch({ ledgerName: e.currentTarget.value })} disabled={locked} error={errors.ledgerName} maxLength={150} />
              <SimpleGrid cols={{ base: 1, sm: 2 }}>
                <Select
                  label="Under group"
                  withAsterisk={!draft.isGroup}
                  placeholder={draft.isGroup ? 'None (primary group)' : 'Select group'}
                  data={groupOptions}
                  value={draft.parentLedgerId}
                  onChange={(parentLedgerId) => patch({ parentLedgerId })}
                  searchable
                  clearable={draft.isGroup}
                  disabled={locked || systemLocked}
                  error={errors.parentLedgerId}
                />
                {parent ? (
                  <TextInput label="Account type" value={`${parent.natureName} · ${parent.reportHead === 'BS' ? 'Balance sheet' : 'Profit & loss'}`} readOnly />
                ) : (
                  <Select
                    label="Account type"
                    withAsterisk
                    data={(natures.data ?? []).map((n) => ({ value: String(n.natureId), label: `${n.natureName} (${n.reportHead})` }))}
                    value={draft.natureId}
                    onChange={(natureId) => patch({ natureId, reportHead: natures.data?.find((n) => String(n.natureId) === natureId)?.reportHead ?? null })}
                    disabled={locked}
                    error={errors.natureId}
                  />
                )}
              </SimpleGrid>

              {!draft.isGroup && (
                <>
                  <div>
                    <Text size="sm" fw={500} mb={4}>
                      Cash / bank
                    </Text>
                    <SegmentedControl
                      value={draft.cashBankType}
                      onChange={(v) => patch({ cashBankType: v as Draft['cashBankType'] })}
                      disabled={locked || systemLocked}
                      data={[
                        { value: 'N', label: 'None' },
                        { value: 'C', label: 'Cash' },
                        { value: 'B', label: 'Bank' },
                      ]}
                    />
                  </div>
                  {draft.cashBankType === 'B' && (
                    <SimpleGrid cols={2}>
                      <TextInput label="Bank account no" value={draft.bankAccountNo} onChange={(e) => patch({ bankAccountNo: e.currentTarget.value })} disabled={locked} error={errors.bankAccountNo} />
                      <TextInput label="IFSC" value={draft.ifscCode} onChange={(e) => patch({ ifscCode: e.currentTarget.value.toUpperCase() })} disabled={locked} error={errors.ifscCode} />
                    </SimpleGrid>
                  )}
                  <Paper p="sm" radius="md" bg="var(--dj-page-bg)">
                    <Text size="sm" fw={600} mb={6}>
                      Opening balance · {me?.branchName}
                    </Text>
                    <Group align="flex-end" gap="sm">
                      <NumberInput label="Amount" value={draft.openingBalance} onChange={(v) => patch({ openingBalance: v })} min={0} decimalScale={2} thousandSeparator="," w={160} disabled={locked} error={errors.openingBalance} />
                      <SegmentedControl value={draft.openingBalType} onChange={(v) => patch({ openingBalType: v as 'D' | 'C' })} data={[{ value: 'D', label: 'Dr' }, { value: 'C', label: 'Cr' }]} disabled={locked} />
                      <DateInput label="On date" value={draft.openingDate} onChange={(openingDate) => patch({ openingDate })} valueFormat="DD-MMM-YYYY" w={160} disabled={locked} placeholder="Login date" />
                    </Group>
                  </Paper>
                </>
              )}

              <Group gap="lg">
                <Checkbox label="Visible in branch" checked={draft.isVisibleInBranch} onChange={(e) => patch({ isVisibleInBranch: e.currentTarget.checked })} disabled={locked} />
                {draft.isGroup && <Checkbox label="Default group" checked={draft.isDefaultGroup} onChange={(e) => patch({ isDefaultGroup: e.currentTarget.checked })} disabled={locked} />}
                {!draft.isGroup && <Checkbox label="Apply sanction" checked={draft.applySanction} onChange={(e) => patch({ applySanction: e.currentTarget.checked })} disabled={locked} />}
                <Checkbox label="Active" checked={draft.isActive} onChange={(e) => patch({ isActive: e.currentTarget.checked })} disabled={locked} />
              </Group>

              <Group justify="space-between" mt="xs">
                {draft.id && !draft.isGroup ? (
                  <Button variant="subtle" leftSection={<IconReportSearch size={16} />} component={Link} to={`/ledger-statement?ledger=${draft.id}`}>
                    View statement
                  </Button>
                ) : (
                  <span />
                )}
                {canEdit && (
                  <Group gap="xs">
                    {draft.id && (
                      <Button color="red" variant="light" leftSection={<IconTrash size={16} />} disabled={locked || systemLocked} onClick={() => setConfirmDelete(true)}>
                        Delete
                      </Button>
                    )}
                    <Button leftSection={<IconDeviceFloppy size={16} />} loading={save.isPending} disabled={locked} onClick={() => save.mutate()}>
                      Save
                    </Button>
                  </Group>
                )}
              </Group>
            </Stack>
          </Section>
        </Grid.Col>
      </Grid>

      <Modal opened={confirmDelete} onClose={() => setConfirmDelete(false)} title="Delete" centered>
        <Text mb="md">Delete {selected?.ledgerName}? Ledgers with postings cannot be deleted.</Text>
        <Group justify="flex-end">
          <Button variant="default" onClick={() => setConfirmDelete(false)}>
            Cancel
          </Button>
          <Button color="red" loading={remove.isPending} onClick={() => remove.mutate()}>
            Delete
          </Button>
        </Group>
      </Modal>
    </Stack>
  )
}
