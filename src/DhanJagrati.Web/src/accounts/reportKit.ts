import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { BranchOption } from '../api/types'
import { useMe } from '../auth/session'
import { formatAmount } from '../components/format'

/** Branch choices for reports: the user's branches, plus "All branches" for administrators (value '0'). */
export function useReportBranches(allowAll = true) {
  const { data: me } = useMe()
  const branches = useQuery({ queryKey: ['auth', 'branches'], queryFn: () => api.get<BranchOption[]>('/api/auth/branches'), staleTime: 5 * 60_000 })
  const options = (branches.data ?? []).map((b) => ({ value: String(b.branchId), label: b.branchName }))
  if (allowAll && me?.role === 'ADMIN') options.unshift({ value: '0', label: 'All branches (H.O.)' })
  const label = (value: string | null) => options.find((o) => o.value === value)?.label ?? me?.branchName ?? ''
  return { options, label }
}

/** Indian financial year start (1 April) for a yyyy-MM-dd date. */
export function financialYearStart(date: string | undefined | null): string | null {
  if (!date) return null
  const [y, m] = date.split('-').map(Number)
  return `${m >= 4 ? y : y - 1}-04-01`
}

/** Dr / Cr presentation of a signed balance (+ = debit). */
export function drCr(value: number): string {
  if (value === 0) return '0.00'
  return `${formatAmount(Math.abs(value))} ${value > 0 ? 'Dr' : 'Cr'}`
}

/** Groups items by key, keeping first-seen order. */
export function groupBy<T>(items: T[], key: (item: T) => string): [string, T[]][] {
  const map = new Map<string, T[]>()
  for (const item of items) {
    const k = key(item)
    const list = map.get(k)
    if (list) list.push(item)
    else map.set(k, [item])
  }
  return [...map.entries()]
}

/** Download rows as CSV (opens in Excel). */
export function downloadCsv(fileName: string, header: string[], rows: (string | number | null | undefined)[][]) {
  const escape = (v: string | number | null | undefined) => {
    const text = v === null || v === undefined ? '' : String(v)
    return /[",\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text
  }
  const csv = [header, ...rows].map((r) => r.map(escape).join(',')).join('\r\n')
  const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = fileName
  a.click()
  URL.revokeObjectURL(url)
}
