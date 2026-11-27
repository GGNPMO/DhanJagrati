import type { ReactNode } from 'react'
import type { MasterRow } from '../api/types'

export interface Choice {
  value: string
  label: string
}

export interface LookupConfig {
  /** Master entity to load options from, e.g. 'states'. */
  entity: string
  /** Row property shown as the option label, e.g. 'stateName'. */
  labelKey: string
  /** Form field whose value filters this lookup (?parentId=), e.g. districts filtered by stateId. */
  parentField?: string
}

export interface FieldConfig {
  /** camelCase key, matches the API column (e.g. 'districtName'). */
  name: string
  label: string
  type: 'text' | 'int' | 'decimal' | 'bool' | 'date' | 'choice' | 'lookup'
  required?: boolean
  maxLength?: number
  min?: number
  max?: number
  decimals?: number
  choices?: Choice[]
  lookup?: LookupConfig
  /** Helper only (cascade parent), not sent to the API. */
  virtual?: boolean
  defaultValue?: unknown
  description?: string
  /** Grid span out of 12. */
  span?: number
}

export interface ColumnConfig {
  key: string
  label: string
  type?: 'bool' | 'date' | 'amount' | 'choice'
  choices?: Choice[]
}

export interface MasterConfig {
  entity: string
  title: string
  /** Heading of the form section, as in the legacy screens ("Add Salutation"). */
  formTitle: string
  listTitle: string
  fields: FieldConfig[]
  columns: ColumnConfig[]
  /** Optional filter above the list (e.g. Interest Rates by plan). */
  listFilter?: { label: string; lookup: LookupConfig }
  /** Extra panel rendered under the form for the selected record. */
  renderExtra?: (record: MasterRow) => ReactNode
}
