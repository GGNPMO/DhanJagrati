import {
  Alert,
  Badge,
  Box,
  Button,
  Grid,
  Group,
  Modal,
  Pagination,
  Paper,
  ScrollArea,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import { IconDeviceFloppy, IconPlus, IconSearch, IconTrash, IconX } from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router'
import { api, ApiError, errorMessage, query } from '../api/client'
import type { MasterRow } from '../api/types'
import { canEditMasters, useMe } from '../auth/session'
import { formatAmount, formatDate } from '../components/format'
import { FieldInput, type FormValues } from './FieldInput'
import type { ColumnConfig, FieldConfig, MasterConfig } from './types'
import { useLookup } from './useLookup'

const PAGE_SIZE = 50

function emptyValues(fields: FieldConfig[]): FormValues {
  return Object.fromEntries(
    fields.map((f) => [
      f.name,
      f.defaultValue ?? (f.type === 'bool' ? false : f.type === 'text' || f.type === 'int' || f.type === 'decimal' ? '' : null),
    ]),
  )
}

function toFormValues(fields: FieldConfig[], row: MasterRow): FormValues {
  return Object.fromEntries(
    fields.map((f) => {
      const value = row[f.name]
      switch (f.type) {
        case 'lookup':
          return [f.name, value === null || value === undefined ? null : String(value)]
        case 'bool':
          return [f.name, Boolean(value)]
        case 'date':
          return [f.name, typeof value === 'string' ? value.slice(0, 10) : null]
        case 'int':
        case 'decimal':
          return [f.name, value ?? '']
        case 'choice':
          return [f.name, value ?? null]
        default:
          return [f.name, value ?? '']
      }
    }),
  )
}

function toPayload(fields: FieldConfig[], values: FormValues): Record<string, unknown> {
  const payload: Record<string, unknown> = {}
  for (const f of fields) {
    if (f.virtual) continue
    const value = values[f.name]
    switch (f.type) {
      case 'lookup':
        payload[f.name] = value ? Number(value) : null
        break
      case 'int':
      case 'decimal':
        payload[f.name] = value === '' || value === null || value === undefined ? null : Number(value)
        break
      case 'text':
        payload[f.name] = typeof value === 'string' && value.trim() ? value.trim() : null
        break
      default:
        payload[f.name] = value ?? null
    }
  }
  return payload
}

function renderCell(column: ColumnConfig, row: MasterRow) {
  const value = row[column.key]
  switch (column.type) {
    case 'bool':
      return value ? <Badge size="xs" color="green">Yes</Badge> : <Badge size="xs" color="gray">No</Badge>
    case 'date':
      return formatDate(value)
    case 'amount':
      return formatAmount(value)
    case 'choice':
      return column.choices?.find((c) => c.value === value)?.label ?? String(value ?? '')
    default:
      return value === null || value === undefined ? '' : String(value)
  }
}

export function MasterPage({ config }: { config: MasterConfig }) {
  const navigate = useNavigate()
  const client = useQueryClient()
  const { data: me } = useMe()
  const canEdit = canEditMasters(me?.role)

  const [search, setSearch] = useState('')
  const [debouncedSearch] = useDebouncedValue(search, 300)
  const [filterValue, setFilterValue] = useState<string | null>(null)
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<MasterRow | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)

  const filterOptions = useLookup(config.listFilter?.lookup)

  const list = useQuery({
    queryKey: ['masters', config.entity, 'list', debouncedSearch, filterValue],
    queryFn: () => api.get<MasterRow[]>(`/api/masters/${config.entity}${query({ search: debouncedSearch, parentId: filterValue })}`),
  })

  const defaults = useMemo(() => emptyValues(config.fields), [config.fields])
  const form = useForm<FormValues>({ defaultValues: defaults })

  useEffect(() => {
    form.reset(selected ? toFormValues(config.fields, selected) : defaults)
  }, [selected, config.fields, defaults, form])

  const save = useMutation({
    mutationFn: async (values: FormValues) => {
      const payload = toPayload(config.fields, values)
      let id: number
      if (selected) {
        await api.put(`/api/masters/${config.entity}/${selected.id}`, payload)
        id = selected.id
      } else {
        id = (await api.post<{ id: number }>(`/api/masters/${config.entity}`, payload)).id
      }
      return api.get<MasterRow>(`/api/masters/${config.entity}/${id}`)
    },
    onSuccess: (row) => {
      notifications.show({ color: 'green', message: `${config.title} saved.` })
      client.invalidateQueries({ queryKey: ['masters', config.entity] })
      setSelected(row)
    },
    onError: (error) => {
      if (error instanceof ApiError)
        for (const [field, message] of Object.entries(error.fieldErrors)) form.setError(field, { message })
      notifications.show({ color: 'red', title: 'Not saved', message: errorMessage(error) })
    },
  })

  const remove = useMutation({
    mutationFn: (id: number) => api.delete(`/api/masters/${config.entity}/${id}`),
    onSuccess: () => {
      notifications.show({ color: 'green', message: `${config.title} deleted.` })
      client.invalidateQueries({ queryKey: ['masters', config.entity] })
      setSelected(null)
    },
    onError: (error) => notifications.show({ color: 'red', title: 'Not deleted', message: errorMessage(error) }),
    onSettled: () => setConfirmDelete(false),
  })

  const rows = list.data ?? []
  const pageRows = rows.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE)

  return (
    <Stack gap="md">
      <Group justify="space-between" className="dj-no-print">
        <Title order={3}>{config.title} Master</Title>
        <Group gap="xs">
          <Button variant="default" leftSection={<IconPlus size={16} />} onClick={() => setSelected(null)}>
            Add
          </Button>
          {canEdit && (
            <Button
              leftSection={<IconDeviceFloppy size={16} />}
              loading={save.isPending}
              onClick={form.handleSubmit((values) => save.mutate(values))}
            >
              Save
            </Button>
          )}
          {canEdit && (
            <Button color="red" variant="light" leftSection={<IconTrash size={16} />} disabled={!selected} onClick={() => setConfirmDelete(true)}>
              Delete
            </Button>
          )}
          <Button variant="default" onClick={() => form.reset(selected ? toFormValues(config.fields, selected) : defaults)}>
            Clear
          </Button>
          <Button variant="default" leftSection={<IconX size={16} />} onClick={() => navigate('/')}>
            Close
          </Button>
        </Group>
      </Group>

      {!canEdit && (
        <Alert color="gray" variant="light">
          Read-only: only Admin and Manager roles can change master data.
        </Alert>
      )}

      <Grid gap="md">
        <Grid.Col span={{ base: 12, md: 5 }}>
          <Paper withBorder>
            <Box className="dj-section-title">
              {selected ? `Edit ${config.title} #${selected.id}` : config.formTitle}
            </Box>
            <Box
              component="form"
              p="md"
              onSubmit={form.handleSubmit((values) => save.mutate(values))}
            >
              <Grid gap="sm">
                {config.fields.map((field) => (
                  <Grid.Col key={field.name} span={{ base: 12, sm: field.span ?? 6 }}>
                    <FieldInput field={field} fields={config.fields} control={form.control} setValue={form.setValue} disabled={!canEdit} />
                  </Grid.Col>
                ))}
              </Grid>
              <button type="submit" hidden />
            </Box>
          </Paper>
          {selected && config.renderExtra?.(selected)}
        </Grid.Col>

        <Grid.Col span={{ base: 12, md: 7 }}>
          <Paper withBorder>
            <Box className="dj-section-title">{config.listTitle}</Box>
            <Stack p="md" gap="sm">
              <Group grow>
                <TextInput
                  placeholder="Search"
                  leftSection={<IconSearch size={16} />}
                  value={search}
                  onChange={(e) => {
                    setSearch(e.currentTarget.value)
                    setPage(1)
                  }}
                />
                {config.listFilter && (
                  <Select
                    placeholder={`All ${config.listFilter.label.toLowerCase()}s`}
                    data={filterOptions.options}
                    value={filterValue}
                    onChange={(value) => {
                      setFilterValue(value)
                      setPage(1)
                    }}
                    searchable
                    clearable
                  />
                )}
              </Group>
              {list.isError && <Alert color="red">{errorMessage(list.error)}</Alert>}
              <ScrollArea h={520} type="auto">
                <Table className="dj-grid" striped highlightOnHover withTableBorder stickyHeader>
                  <Table.Thead>
                    <Table.Tr>
                      {config.columns.map((c) => (
                        <Table.Th key={c.key}>{c.label}</Table.Th>
                      ))}
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {pageRows.map((row) => (
                      <Table.Tr key={row.id} data-selected={selected?.id === row.id} onClick={() => setSelected(row)}>
                        {config.columns.map((c) => (
                          <Table.Td key={c.key}>{renderCell(c, row)}</Table.Td>
                        ))}
                      </Table.Tr>
                    ))}
                    {!list.isLoading && rows.length === 0 && (
                      <Table.Tr>
                        <Table.Td colSpan={config.columns.length}>
                          <Text c="dimmed" ta="center" py="md">
                            No records.
                          </Text>
                        </Table.Td>
                      </Table.Tr>
                    )}
                  </Table.Tbody>
                </Table>
              </ScrollArea>
              <Group justify="space-between">
                <Text size="sm" c="dimmed">
                  {list.isLoading ? 'Loading...' : `${rows.length} record(s)`}
                </Text>
                {rows.length > PAGE_SIZE && <Pagination size="sm" total={Math.ceil(rows.length / PAGE_SIZE)} value={page} onChange={setPage} />}
              </Group>
            </Stack>
          </Paper>
        </Grid.Col>
      </Grid>

      <Modal opened={confirmDelete} onClose={() => setConfirmDelete(false)} title={`Delete ${config.title}`} centered>
        <Text mb="md">Delete the selected record? This cannot be undone.</Text>
        <Group justify="flex-end">
          <Button variant="default" onClick={() => setConfirmDelete(false)}>
            Cancel
          </Button>
          <Button color="red" loading={remove.isPending} onClick={() => selected && remove.mutate(selected.id)}>
            Delete
          </Button>
        </Group>
      </Modal>
    </Stack>
  )
}
