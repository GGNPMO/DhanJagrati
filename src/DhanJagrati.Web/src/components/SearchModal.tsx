import { Alert, Modal, ScrollArea, Table, Text, TextInput } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { IconSearch } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api, errorMessage, query } from '../api/client'

export interface SearchColumn<T> {
  label: string
  render: (row: T) => React.ReactNode
}

interface Props<T extends { id: number }> {
  opened: boolean
  title: string
  /** API list URL accepting ?search= */
  url: string
  columns: SearchColumn<T>[]
  onClose: () => void
  onSelect: (row: T) => void
  /** Text already typed by the user (re-mount the modal with a new key to change it). */
  initialSearch?: string
}

/** Search dialog used by the record screens' "Search" button and the code pickers. */
export function SearchModal<T extends { id: number }>({ opened, title, url, columns, onClose, onSelect, initialSearch }: Props<T>) {
  const [search, setSearch] = useState(initialSearch ?? '')
  const [debounced] = useDebouncedValue(search, 300)
  const result = useQuery({
    queryKey: [url, 'search', debounced],
    queryFn: () => api.get<T[]>(`${url}${url.includes('?') ? query({ search: debounced }).replace('?', '&') : query({ search: debounced })}`),
    enabled: opened,
  })

  return (
    <Modal opened={opened} onClose={onClose} title={title} size="xl">
      <TextInput
        placeholder="Type number, name, mobile ..."
        leftSection={<IconSearch size={16} />}
        value={search}
        onChange={(e) => setSearch(e.currentTarget.value)}
        data-autofocus
        mb="sm"
      />
      {result.isError && <Alert color="red">{errorMessage(result.error)}</Alert>}
      <ScrollArea h={420} type="auto">
        <Table className="dj-grid" striped highlightOnHover withTableBorder stickyHeader>
          <Table.Thead>
            <Table.Tr>
              {columns.map((c) => (
                <Table.Th key={c.label}>{c.label}</Table.Th>
              ))}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {(result.data ?? []).map((row) => (
              <Table.Tr key={row.id} onClick={() => onSelect(row)}>
                {columns.map((c) => (
                  <Table.Td key={c.label}>{c.render(row)}</Table.Td>
                ))}
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
        {result.data?.length === 0 && (
          <Text c="dimmed" ta="center" py="md">
            No records found.
          </Text>
        )}
      </ScrollArea>
    </Modal>
  )
}
