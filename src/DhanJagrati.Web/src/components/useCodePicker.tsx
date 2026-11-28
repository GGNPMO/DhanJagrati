import { useState } from 'react'
import { api } from '../api/client'
import { SearchModal, type SearchColumn } from './SearchModal'

interface Options<T> {
  /** List endpoint that accepts ?search= (may already contain a query string). */
  searchUrl: string
  /** Business code of a row, e.g. memberNo / employeeCode. */
  codeOf: (row: T) => string
  /** Called with the chosen code. */
  onPicked: (code: string) => void | Promise<void>
  title: string
  columns: SearchColumn<T>[]
}

/**
 * "Fetch / Find" behaviour for code fields: an exact code is applied directly; anything else
 * (partial number, a name, or nothing) opens a search dialog pre-filled with what was typed.
 * Avoids the 404 a direct exact-code lookup produces for partial input.
 */
export function useCodePicker<T extends { id: number }>(o: Options<T>) {
  const [term, setTerm] = useState<string | null>(null)

  const find = async (typed: string) => {
    const text = typed.trim()
    if (text) {
      const separator = o.searchUrl.includes('?') ? '&' : '?'
      const rows = await api.get<T[]>(`${o.searchUrl}${separator}search=${encodeURIComponent(text)}`)
      const exact = rows.find((r) => o.codeOf(r).toUpperCase() === text.toUpperCase())
      if (exact) {
        await o.onPicked(o.codeOf(exact))
        return
      }
    }
    setTerm(text)
  }

  const modal = (
    <SearchModal<T>
      key={`${o.title}|${term ?? ''}`}
      opened={term !== null}
      initialSearch={term ?? ''}
      title={o.title}
      url={o.searchUrl}
      columns={o.columns}
      onClose={() => setTerm(null)}
      onSelect={async (row) => {
        setTerm(null)
        await o.onPicked(o.codeOf(row))
      }}
    />
  )

  return { find, modal }
}
