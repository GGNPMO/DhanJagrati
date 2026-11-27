import type { Choice } from '../masters/types'

export const genderChoices: Choice[] = [
  { value: 'M', label: 'Male' },
  { value: 'F', label: 'Female' },
  { value: 'O', label: 'Other' },
]

/** Completed years between two yyyy-MM-dd dates. */
export function ageOn(dob: string | null | undefined, on: string | null | undefined): string {
  if (!dob || !on) return ''
  const [y1, m1, d1] = dob.split('-').map(Number)
  const [y2, m2, d2] = on.split('-').map(Number)
  let age = y2 - y1
  if (m2 < m1 || (m2 === m1 && d2 < d1)) age--
  return age >= 0 ? String(age) : ''
}

// ---- conversions between API values and form values ----
export const toStr = (v: unknown): string => (v === null || v === undefined ? '' : String(v))
export const toSel = (v: unknown): string | null => (v === null || v === undefined ? null : String(v))
export const toNum = (v: unknown): number | '' => (typeof v === 'number' ? v : '')
export const fromStr = (v: unknown): string | null => (typeof v === 'string' && v.trim() ? v.trim() : null)
export const fromSel = (v: unknown): number | null => (v === null || v === undefined || v === '' ? null : Number(v))
export const fromNum = (v: unknown): number | null => (v === '' || v === null || v === undefined ? null : Number(v))
