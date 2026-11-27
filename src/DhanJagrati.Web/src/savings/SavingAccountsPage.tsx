import { Alert, Badge, Button, Grid, Group, Modal, NumberInput, Paper, ScrollArea, Select, Stack, Table, Text, TextInput } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { useDebouncedValue } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import { IconPlus, IconSearch } from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router'
import { api, ApiError, errorMessage, query } from '../api/client'
import { settingEnabled, useMe } from '../auth/session'
import { LookupField } from '../components/fields'
import { formatAmount, formatDate } from '../components/format'
import { PageHeader } from '../components/PageHeader'
import { paymentModes } from '../recovery/types'
import type { SavingListItem, SavingProduct } from './types'

/** SAVING > accounts list and account opening. */
export function SavingAccountsPage() {
  const { data: me } = useMe()
  const navigate = useNavigate()
  const client = useQueryClient()
  const [search, setSearch] = useState('')
  const [debounced] = useDebouncedValue(search, 300)
  const [opening, setOpening] = useState(false)

  const enabled = settingEnabled(me, 'MODULE.SAVING.ENABLED')
  const list = useQuery({
    queryKey: ['savings', 'accounts', debounced],
    queryFn: () => api.get<SavingListItem[]>(`/api/savings/accounts${query({ search: debounced })}`),
    enabled,
  })

  if (!enabled) return <Alert color="gray">The Saving module is switched off. An administrator can enable it in Help → Settings.</Alert>

  return (
    <Stack gap="md">
      <PageHeader
        title="Saving Accounts"
        subtitle={`${me?.branchName} branch`}
        actions={
          <Button leftSection={<IconPlus size={16} />} onClick={() => setOpening(true)}>
            Open account
          </Button>
        }
      />
      <Paper className="dj-card" p="sm">
        <TextInput placeholder="Search account no, member no or name" leftSection={<IconSearch size={16} />} value={search} onChange={(e) => setSearch(e.currentTarget.value)} />
      </Paper>
      {list.isError && <Alert color="red">{errorMessage(list.error)}</Alert>}
      <Paper className="dj-card">
        <ScrollArea type="auto">
          <Table className="dj-grid" striped highlightOnHover miw={680}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Account No</Table.Th>
                <Table.Th>Member</Table.Th>
                <Table.Th>Product</Table.Th>
                <Table.Th>Opened</Table.Th>
                <Table.Th className="dj-num">Balance</Table.Th>
                <Table.Th>Status</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {(list.data ?? []).map((a) => (
                <Table.Tr key={a.id} onClick={() => navigate(`/savings/${a.id}`)}>
                  <Table.Td fw={600}>{a.accountNo}</Table.Td>
                  <Table.Td>
                    {a.memberName}
                    <Text size="xs" c="dimmed">
                      {a.memberNo}
                    </Text>
                  </Table.Td>
                  <Table.Td>{a.productCode}</Table.Td>
                  <Table.Td>{formatDate(a.openDate)}</Table.Td>
                  <Table.Td className="dj-num">{formatAmount(a.currentBalance)}</Table.Td>
                  <Table.Td>
                    <Badge size="sm" variant="light" color={a.status === 'ACTIVE' ? 'green' : 'gray'}>
                      {a.status}
                    </Badge>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
          {list.data?.length === 0 && (
            <Text c="dimmed" ta="center" py="lg">
              No saving accounts yet.
            </Text>
          )}
        </ScrollArea>
      </Paper>

      <OpenAccountModal
        opened={opening}
        onClose={() => setOpening(false)}
        onOpened={async (id) => {
          setOpening(false)
          await client.invalidateQueries({ queryKey: ['savings'] })
          navigate(`/savings/${id}`)
        }}
      />
    </Stack>
  )
}

function OpenAccountModal({ opened, onClose, onOpened }: { opened: boolean; onClose: () => void; onOpened: (id: number) => void }) {
  const { data: me } = useMe()
  const form = useForm<{ nomineeRelationId: string | null }>({ defaultValues: { nomineeRelationId: null } })
  const [memberNo, setMemberNo] = useState('')
  const [jointMemberNo, setJointMemberNo] = useState('')
  const [productId, setProductId] = useState<string | null>(null)
  const [openDate, setOpenDate] = useState<string | null>(me?.businessDate ?? null)
  const [employeeCode, setEmployeeCode] = useState('')
  const [nomineeName, setNomineeName] = useState('')
  const [deposit, setDeposit] = useState<number | string>(0)
  const [mode, setMode] = useState('CASH')
  const [errors, setErrors] = useState<Record<string, string>>({})

  const products = useQuery({ queryKey: ['savings', 'products'], queryFn: () => api.get<SavingProduct[]>('/api/savings/products'), enabled: opened })
  const product = products.data?.find((p) => String(p.productId) === productId)

  const open = useMutation({
    mutationFn: () =>
      api.post<{ id: number }>('/api/savings/accounts', {
        memberNo,
        jointMemberNo: jointMemberNo || null,
        productId: Number(productId) || 0,
        openDate,
        employeeCode: employeeCode || null,
        nomineeName: nomineeName || null,
        nomineeRelationId: form.getValues('nomineeRelationId') ? Number(form.getValues('nomineeRelationId')) : null,
        openingDeposit: Number(deposit) || 0,
        paymentMode: mode,
      }),
    onSuccess: (r) => {
      notifications.show({ color: 'green', message: 'Saving account opened.' })
      setErrors({})
      onOpened(r.id)
    },
    onError: (e) => {
      if (e instanceof ApiError) setErrors(e.fieldErrors)
      notifications.show({ color: 'red', title: 'Not opened', message: errorMessage(e) })
    },
  })

  return (
    <Modal opened={opened} onClose={onClose} title="Open saving account" size="lg">
      <Grid gap="sm">
        <Grid.Col span={{ base: 12, sm: 6 }}>
          <TextInput label="Member No" withAsterisk value={memberNo} onChange={(e) => setMemberNo(e.currentTarget.value.toUpperCase())} error={errors.memberNo} />
        </Grid.Col>
        <Grid.Col span={{ base: 12, sm: 6 }}>
          <TextInput label="Joint member No (optional)" value={jointMemberNo} onChange={(e) => setJointMemberNo(e.currentTarget.value.toUpperCase())} error={errors.jointMemberNo} />
        </Grid.Col>
        <Grid.Col span={{ base: 12, sm: 6 }}>
          <Select
            label="Product"
            withAsterisk
            data={(products.data ?? []).map((p) => ({ value: String(p.productId), label: `${p.productCode} - ${p.productName}` }))}
            value={productId}
            onChange={setProductId}
            error={errors.productId}
            description={product ? `Min. opening ₹${formatAmount(product.minOpeningDeposit)} · min. balance ₹${formatAmount(product.minBalance)} · ${product.interestRate}% p.a.` : undefined}
          />
        </Grid.Col>
        <Grid.Col span={{ base: 12, sm: 6 }}>
          <DateInput label="Open date" value={openDate} onChange={setOpenDate} valueFormat="DD-MMM-YYYY" maxDate={me?.today} error={errors.openDate} />
        </Grid.Col>
        <Grid.Col span={{ base: 12, sm: 6 }}>
          <TextInput label="Introducer / F.O. code" value={employeeCode} onChange={(e) => setEmployeeCode(e.currentTarget.value.toUpperCase())} error={errors.employeeCode} />
        </Grid.Col>
        <Grid.Col span={{ base: 12, sm: 6 }}>
          <TextInput label="Nominee name" value={nomineeName} onChange={(e) => setNomineeName(e.currentTarget.value)} />
        </Grid.Col>
        <Grid.Col span={{ base: 12, sm: 6 }}>
          <LookupField control={form.control} name="nomineeRelationId" label="Nominee relation" entity="relations" labelKey="relationName" />
        </Grid.Col>
        <Grid.Col span={{ base: 6, sm: 3 }}>
          <NumberInput label="Opening deposit" value={deposit} onChange={setDeposit} min={0} decimalScale={2} error={errors.openingDeposit} />
        </Grid.Col>
        <Grid.Col span={{ base: 6, sm: 3 }}>
          <Select label="Mode" data={paymentModes} value={mode} onChange={(v) => setMode(v ?? 'CASH')} allowDeselect={false} />
        </Grid.Col>
      </Grid>
      <Group justify="flex-end" mt="md">
        <Button variant="default" onClick={onClose}>
          Cancel
        </Button>
        <Button loading={open.isPending} onClick={() => open.mutate()}>
          Open account
        </Button>
      </Group>
    </Modal>
  )
}
