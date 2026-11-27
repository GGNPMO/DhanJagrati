import { Button, Group, Tooltip } from '@mantine/core'
import {
  IconArrowBackUp,
  IconChevronLeft,
  IconChevronRight,
  IconChevronsLeft,
  IconChevronsRight,
  IconDeviceFloppy,
  IconPencil,
  IconPlus,
  IconPrinter,
  IconSearch,
  IconTrash,
  IconX,
} from '@tabler/icons-react'

export type RecordMode = 'view' | 'edit' | 'new'
export type NavDirection = 'First' | 'Previous' | 'Next' | 'Last'

interface Props {
  mode: RecordMode
  hasRecord: boolean
  canEdit: boolean
  canDelete: boolean
  saving?: boolean
  onAdd: () => void
  onEdit: () => void
  onSave: () => void
  onUndo: () => void
  onDelete: () => void
  onSearch: () => void
  onNavigate: (direction: NavDirection) => void
  onPrint: () => void
  onClose: () => void
}

/** The legacy record toolbar: Add, Edit, Save, Undo, Delete, Search, First, Prev, Next, Last, Print, Close. */
export function RecordToolbar(p: Props) {
  const editing = p.mode !== 'view'
  const nav = (direction: NavDirection, icon: React.ReactNode, label: string) => (
    <Tooltip label={label}>
      <Button variant="default" px="xs" disabled={editing} onClick={() => p.onNavigate(direction)} aria-label={label}>
        {icon}
      </Button>
    </Tooltip>
  )

  return (
    <Group gap={6} className="dj-no-print" wrap="wrap">
      {p.canEdit && (
        <>
          <Button variant="default" leftSection={<IconPlus size={16} />} disabled={editing} onClick={p.onAdd}>
            Add
          </Button>
          <Button variant="default" leftSection={<IconPencil size={16} />} disabled={editing || !p.hasRecord} onClick={p.onEdit}>
            Edit
          </Button>
          <Button leftSection={<IconDeviceFloppy size={16} />} disabled={!editing} loading={p.saving} onClick={p.onSave}>
            Save
          </Button>
          <Button variant="default" leftSection={<IconArrowBackUp size={16} />} disabled={!editing} onClick={p.onUndo}>
            Undo
          </Button>
        </>
      )}
      {p.canDelete && (
        <Button color="red" variant="light" leftSection={<IconTrash size={16} />} disabled={editing || !p.hasRecord} onClick={p.onDelete}>
          Delete
        </Button>
      )}
      <Button variant="default" leftSection={<IconSearch size={16} />} disabled={editing} onClick={p.onSearch}>
        Search
      </Button>
      {nav('First', <IconChevronsLeft size={16} />, 'First')}
      {nav('Previous', <IconChevronLeft size={16} />, 'Previous')}
      {nav('Next', <IconChevronRight size={16} />, 'Next')}
      {nav('Last', <IconChevronsRight size={16} />, 'Last')}
      <Button variant="default" leftSection={<IconPrinter size={16} />} disabled={editing || !p.hasRecord} onClick={p.onPrint}>
        Print
      </Button>
      <Button variant="default" leftSection={<IconX size={16} />} onClick={p.onClose}>
        Close
      </Button>
    </Group>
  )
}
