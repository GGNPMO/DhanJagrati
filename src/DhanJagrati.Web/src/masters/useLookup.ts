import { useQuery } from '@tanstack/react-query'
import { api, query } from '../api/client'
import type { MasterRow } from '../api/types'
import type { Choice, LookupConfig } from './types'

/**
 * Options for a lookup Select (active master rows); disabled until the cascade parent has a value.
 * `filter` narrows the rows, e.g. only guardian relations among salutations.
 */
export function useLookup(lookup: LookupConfig | undefined, parentValue?: string | null, filter?: (row: MasterRow) => boolean) {
  const waitingForParent = !!lookup?.parentField && !parentValue
  const result = useQuery({
    queryKey: ['masters', lookup?.entity, 'lookup', parentValue ?? null],
    queryFn: () =>
      api.get<MasterRow[]>(`/api/masters/${lookup!.entity}${query({ activeOnly: true, parentId: lookup?.parentField ? parentValue : undefined })}`),
    enabled: !!lookup && !waitingForParent,
    staleTime: 5 * 60_000,
  })

  const rows = (result.data ?? []).filter((row) => !filter || filter(row))
  const options: Choice[] = rows.map((row) => ({
    value: String(row.id),
    label: String(row[lookup!.labelKey] ?? row.id),
  }))
  return { options, rows, isLoading: result.isLoading, waitingForParent }
}
