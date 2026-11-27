import { ActionIcon, Button, Group, Paper, Text, Tooltip, type ButtonProps } from '@mantine/core'
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
import type { ReactNode } from 'react'

export type RecordMode = 'view' | 'edit' | 'new'
export type NavDirection = 'First' | 'Previous' | 'Next' | 'Last'

interface Props {
  mode: RecordMode
  hasRecord: boolean
  canEdit: boolean
  canDelete: boolean
  /** Edit disabled for this record (e.g. approved loan). */
  editLocked?: boolean
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

/** Button with a label from md up and icon-only (with tooltip) on small screens. */
function ToolButton({ label, icon, ...rest }: { label: string; icon: ReactNode; onClick?: () => void } & Omit<ButtonProps, 'children' | 'leftSection'>) {
  return (
    <Tooltip label={label} openDelay={400}>
      <Button leftSection={icon} px={{ base: 10, md: 'md' }} aria-label={label} {...rest}>
        <Text span inherit visibleFrom="md">
          {label}
        </Text>
      </Button>
    </Tooltip>
  )
}

/** The legacy record toolbar: Add, Edit, Save, Undo, Delete, Search, First, Prev, Next, Last, Print, Close. */
export function RecordToolbar(p: Props) {
  const editing = p.mode !== 'view'
  const nav = (direction: NavDirection, icon: ReactNode) => (
    <Tooltip label={direction}>
      <ActionIcon variant="default" size={36} disabled={editing} onClick={() => p.onNavigate(direction)} aria-label={direction}>
        {icon}
      </ActionIcon>
    </Tooltip>
  )

  return (
    <Paper className="dj-card dj-no-print" p={8} style={{ position: 'sticky', top: 68, zIndex: 5 }}>
      <Group gap={6} justify="space-between">
        <Group gap={6}>
          {p.canEdit && (
            <>
              <ToolButton label="Add" icon={<IconPlus size={16} />} variant="light" disabled={editing} onClick={p.onAdd} />
              <ToolButton label="Edit" icon={<IconPencil size={16} />} variant="default" disabled={editing || !p.hasRecord || p.editLocked} onClick={p.onEdit} />
              <ToolButton label="Save" icon={<IconDeviceFloppy size={16} />} disabled={!editing} loading={p.saving} onClick={p.onSave} />
              <ToolButton label="Undo" icon={<IconArrowBackUp size={16} />} variant="default" disabled={!editing} onClick={p.onUndo} />
            </>
          )}
          {p.canDelete && (
            <ToolButton label="Delete" icon={<IconTrash size={16} />} color="red" variant="light" disabled={editing || !p.hasRecord || p.editLocked} onClick={p.onDelete} />
          )}
          <ToolButton label="Search" icon={<IconSearch size={16} />} variant="default" disabled={editing} onClick={p.onSearch} />
        </Group>
        <Group gap={6}>
          {nav('First', <IconChevronsLeft size={16} />)}
          {nav('Previous', <IconChevronLeft size={16} />)}
          {nav('Next', <IconChevronRight size={16} />)}
          {nav('Last', <IconChevronsRight size={16} />)}
          <ToolButton label="Print" icon={<IconPrinter size={16} />} variant="default" disabled={editing || !p.hasRecord} onClick={p.onPrint} />
          <ToolButton label="Close" icon={<IconX size={16} />} variant="subtle" color="gray" onClick={p.onClose} />
        </Group>
      </Group>
    </Paper>
  )
}
