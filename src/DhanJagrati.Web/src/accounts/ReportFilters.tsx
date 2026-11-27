import { Button, Group, Select } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { IconPlayerPlay } from '@tabler/icons-react'
import type { ReactNode } from 'react'
import { useMe } from '../auth/session'
import { useReportBranches } from './reportKit'

export interface RangeFilter {
  branchId: string | null
  from: string | null
  to: string | null
}

/** Branch + date range (+ extra inputs) + Show. Set hideFrom for "as on" reports (balance sheet). */
export function ReportFilters({
  value,
  onChange,
  onShow,
  hideFrom,
  toLabel = 'Date to',
  children,
  loading,
}: {
  value: RangeFilter
  onChange: (v: RangeFilter) => void
  onShow: () => void
  hideFrom?: boolean
  toLabel?: string
  children?: ReactNode
  loading?: boolean
}) {
  const { data: me } = useMe()
  const branches = useReportBranches()
  return (
    <Group align="flex-end" gap="sm" wrap="wrap">
      <Select
        label="Branch"
        data={branches.options}
        value={value.branchId}
        onChange={(branchId) => onChange({ ...value, branchId })}
        allowDeselect={false}
        w={{ base: '100%', xs: 210 }}
      />
      {!hideFrom && (
        <DateInput label="Date from" value={value.from} onChange={(from) => onChange({ ...value, from })} valueFormat="DD-MMM-YYYY" w={{ base: '47%', xs: 160 }} />
      )}
      <DateInput label={toLabel} value={value.to} onChange={(to) => onChange({ ...value, to })} valueFormat="DD-MMM-YYYY" maxDate={me?.today} w={{ base: '47%', xs: 160 }} />
      {children}
      <Button leftSection={<IconPlayerPlay size={16} />} onClick={onShow} loading={loading} disabled={!value.to || (!hideFrom && !value.from)}>
        Show
      </Button>
    </Group>
  )
}
