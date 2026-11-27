import { Checkbox, NumberInput, Select, TextInput } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { Controller, useWatch, type Control, type UseFormSetValue } from 'react-hook-form'
import type { FieldConfig } from './types'
import { useLookup } from './useLookup'

export type FormValues = Record<string, unknown>

interface Props {
  field: FieldConfig
  fields: FieldConfig[]
  control: Control<FormValues>
  setValue: UseFormSetValue<FormValues>
  disabled: boolean
}

/** Clears every lookup that cascades from `name` (State -> District -> Tehsil -> Village). */
function clearDependants(name: string, fields: FieldConfig[], setValue: UseFormSetValue<FormValues>) {
  for (const child of fields.filter((f) => f.lookup?.parentField === name)) {
    setValue(child.name, null, { shouldDirty: true })
    clearDependants(child.name, fields, setValue)
  }
}

function LookupInput({ field, fields, control, setValue, disabled }: Props) {
  const parentValue = useWatch({ control, name: field.lookup?.parentField ?? '__none__' }) as string | null
  const { options, isLoading, waitingForParent } = useLookup(field.lookup, parentValue)

  return (
    <Controller
      name={field.name}
      control={control}
      rules={{ required: field.required ? 'Required' : false }}
      render={({ field: f, fieldState }) => (
        <Select
          label={field.label}
          withAsterisk={field.required}
          data={options}
          value={(f.value as string | null) ?? null}
          onChange={(value) => {
            f.onChange(value)
            clearDependants(field.name, fields, setValue)
          }}
          onBlur={f.onBlur}
          searchable
          clearable={!field.required}
          nothingFoundMessage="No match"
          placeholder={waitingForParent ? 'Select parent first' : isLoading ? 'Loading...' : 'Select'}
          disabled={disabled || waitingForParent}
          error={fieldState.error?.message}
          description={field.description}
        />
      )}
    />
  )
}

export function FieldInput(props: Props) {
  const { field, control, disabled } = props
  if (field.type === 'lookup') return <LookupInput {...props} />

  return (
    <Controller
      name={field.name}
      control={control}
      rules={{ required: field.required && field.type !== 'bool' ? 'Required' : false }}
      render={({ field: f, fieldState }) => {
        const common = {
          label: field.label,
          withAsterisk: field.required,
          disabled,
          error: fieldState.error?.message,
          description: field.description,
          onBlur: f.onBlur,
        }
        switch (field.type) {
          case 'bool':
            return (
              <Checkbox
                mt="xs"
                label={field.label}
                checked={Boolean(f.value)}
                onChange={(e) => f.onChange(e.currentTarget.checked)}
                disabled={disabled}
              />
            )
          case 'int':
          case 'decimal':
            return (
              <NumberInput
                {...common}
                value={(f.value as number | string | undefined) ?? ''}
                onChange={f.onChange}
                min={field.min}
                max={field.max}
                allowDecimal={field.type === 'decimal'}
                decimalScale={field.type === 'decimal' ? (field.decimals ?? 2) : 0}
                thousandSeparator={field.type === 'decimal' && !field.decimals ? ',' : undefined}
              />
            )
          case 'date':
            return (
              <DateInput
                {...common}
                value={(f.value as string | null) ?? null}
                onChange={f.onChange}
                valueFormat="DD-MMM-YYYY"
                clearable={!field.required}
              />
            )
          case 'choice':
            return (
              <Select
                {...common}
                data={field.choices ?? []}
                value={(f.value as string | null) ?? null}
                onChange={f.onChange}
                clearable={!field.required}
              />
            )
          default:
            return (
              <TextInput
                {...common}
                value={(f.value as string | undefined) ?? ''}
                onChange={(e) => f.onChange(e.currentTarget.value)}
                maxLength={field.maxLength}
              />
            )
        }
      }}
    />
  )
}
