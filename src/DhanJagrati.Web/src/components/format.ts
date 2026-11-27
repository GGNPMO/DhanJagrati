import dayjs from 'dayjs'

/** 2026-11-19 -> 19-Nov-2026 (format used throughout the legacy screens). */
export function formatDate(value: unknown): string {
  if (typeof value !== 'string' || value.length < 10) return ''
  return dayjs(value.slice(0, 10)).format('DD-MMM-YYYY')
}

const amountFormatter = new Intl.NumberFormat('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

export function formatAmount(value: unknown): string {
  return typeof value === 'number' ? amountFormatter.format(value) : ''
}
