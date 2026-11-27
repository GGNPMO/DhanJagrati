import { Checkbox, NumberInput, Select, Textarea, TextInput } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import type { ReactNode } from 'react'
import { Controller, type Control, type FieldValues, type Path } from 'react-hook-form'
import type { MasterRow } from '../api/types'
import type { Choice } from '../masters/types'
import { useLookup } from '../masters/useLookup'

/** react-hook-form bound Mantine inputs used by the record screens (Member, Employee, ...). */

interface BaseProps<T extends FieldValues> {
  control: Control<T>
  name: Path<T>
  label: string
  required?: boolean
  disabled?: boolean
  description?: ReactNode
}

const requiredRule = (required?: boolean) => ({ required: required ? 'Required' : false })

export function TextField<T extends FieldValues>(props: BaseProps<T> & { maxLength?: number; placeholder?: string; multiline?: boolean }) {
  const { control, name, label, required, disabled, description, maxLength, placeholder, multiline } = props
  return (
    <Controller
      control={control}
      name={name}
      rules={requiredRule(required)}
      render={({ field, fieldState }) => {
        const common = {
          label,
          withAsterisk: required,
          disabled,
          description,
          maxLength,
          placeholder,
          error: fieldState.error?.message,
          value: (field.value as string | null) ?? '',
          onChange: (e: { currentTarget: { value: string } }) => field.onChange(e.currentTarget.value),
          onBlur: field.onBlur,
        }
        return multiline ? <Textarea {...common} autosize minRows={2} maxRows={4} /> : <TextInput {...common} />
      }}
    />
  )
}

export function NumberField<T extends FieldValues>(props: BaseProps<T> & { min?: number; max?: number; decimals?: number }) {
  const { control, name, label, required, disabled, description, min, max, decimals = 2 } = props
  return (
    <Controller
      control={control}
      name={name}
      rules={requiredRule(required)}
      render={({ field, fieldState }) => (
        <NumberInput
          label={label}
          withAsterisk={required}
          disabled={disabled}
          description={description}
          error={fieldState.error?.message}
          value={(field.value as number | string | null) ?? ''}
          onChange={field.onChange}
          onBlur={field.onBlur}
          min={min}
          max={max}
          decimalScale={decimals}
          allowDecimal={decimals > 0}
          thousandSeparator={decimals > 0 ? ',' : undefined}
        />
      )}
    />
  )
}

export function DateField<T extends FieldValues>(props: BaseProps<T> & { maxDate?: string; minDate?: string }) {
  const { control, name, label, required, disabled, description, maxDate, minDate } = props
  return (
    <Controller
      control={control}
      name={name}
      rules={requiredRule(required)}
      render={({ field, fieldState }) => (
        <DateInput
          label={label}
          withAsterisk={required}
          disabled={disabled}
          description={description}
          error={fieldState.error?.message}
          value={(field.value as string | null) ?? null}
          onChange={field.onChange}
          onBlur={field.onBlur}
          valueFormat="DD-MMM-YYYY"
          maxDate={maxDate}
          minDate={minDate}
          clearable={!required}
        />
      )}
    />
  )
}

export function SelectField<T extends FieldValues>(props: BaseProps<T> & { data: Choice[]; searchable?: boolean; placeholder?: string }) {
  const { control, name, label, required, disabled, description, data, searchable, placeholder } = props
  return (
    <Controller
      control={control}
      name={name}
      rules={requiredRule(required)}
      render={({ field, fieldState }) => (
        <Select
          label={label}
          withAsterisk={required}
          disabled={disabled}
          description={description}
          error={fieldState.error?.message}
          data={data}
          value={(field.value as string | null) ?? null}
          onChange={field.onChange}
          onBlur={field.onBlur}
          searchable={searchable}
          clearable={!required}
          placeholder={placeholder}
          nothingFoundMessage="No match"
        />
      )}
    />
  )
}

/** Select whose options come from a master table (/api/masters/{entity}). */
export function LookupField<T extends FieldValues>(
  props: BaseProps<T> & { entity: string; labelKey: string; parentValue?: string | null; filter?: (row: MasterRow) => boolean; onPicked?: () => void },
) {
  const { entity, labelKey, parentValue, filter, onPicked, ...rest } = props
  const { options, isLoading, waitingForParent } = useLookup(
    { entity, labelKey, parentField: parentValue === undefined ? undefined : '__parent__' },
    parentValue,
    filter,
  )
  return (
    <Controller
      control={rest.control}
      name={rest.name}
      rules={requiredRule(rest.required)}
      render={({ field, fieldState }) => (
        <Select
          label={rest.label}
          withAsterisk={rest.required}
          disabled={rest.disabled || waitingForParent}
          description={rest.description}
          error={fieldState.error?.message}
          data={options}
          value={(field.value as string | null) ?? null}
          onChange={(value) => {
            field.onChange(value)
            onPicked?.()
          }}
          onBlur={field.onBlur}
          searchable
          clearable={!rest.required}
          placeholder={waitingForParent ? 'Select parent first' : isLoading ? 'Loading...' : 'Select'}
          nothingFoundMessage="No match"
        />
      )}
    />
  )
}

export function CheckField<T extends FieldValues>(props: Omit<BaseProps<T>, 'required'>) {
  const { control, name, label, disabled } = props
  return (
    <Controller
      control={control}
      name={name}
      render={({ field }) => (
        <Checkbox mt={6} label={label} disabled={disabled} checked={Boolean(field.value)} onChange={(e) => field.onChange(e.currentTarget.checked)} />
      )}
    />
  )
}

