import { notifications } from '@mantine/notifications'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import type { DefaultValues, FieldValues, Path, UseFormReturn } from 'react-hook-form'
import { useNavigate } from 'react-router'
import { api, ApiError, errorMessage, query } from '../api/client'
import type { NavDirection, RecordMode } from './RecordToolbar'

interface Options<TRecord, TForm extends FieldValues> {
  /** e.g. 'members' -> API /api/members, route /members */
  resource: string
  title: string
  id: number | null
  form: UseFormReturn<TForm>
  empty: () => DefaultValues<TForm>
  toForm: (record: TRecord) => DefaultValues<TForm>
  toPayload: (values: TForm) => unknown
  /** Business key used for First/Prev/Next/Last (e.g. memberNo). */
  navKey: (record: TRecord) => string
}

/**
 * Shared state machine for legacy-style record screens:
 * view (read-only) -> Edit / Add -> Save / Undo, plus Search, navigation and Delete.
 */
export function useRecordController<TRecord, TForm extends FieldValues>(o: Options<TRecord, TForm>) {
  const navigate = useNavigate()
  const client = useQueryClient()
  const [mode, setMode] = useState<RecordMode>(o.id ? 'view' : 'new')
  const [loadedId, setLoadedId] = useState(o.id)

  // Moving to another record always returns to view mode (state adjusted during render, not in an effect).
  if (loadedId !== o.id) {
    setLoadedId(o.id)
    setMode(o.id ? 'view' : 'new')
  }

  const record = useQuery({
    queryKey: [o.resource, o.id],
    queryFn: () => api.get<TRecord>(`/api/${o.resource}/${o.id}`),
    enabled: o.id !== null,
  })

  const { form, toForm, empty } = o
  useEffect(() => {
    if (mode === 'view' && record.data) form.reset(toForm(record.data))
    if (mode === 'new') form.reset(empty())
  }, [mode, record.data, form, toForm, empty])

  const save = useMutation({
    mutationFn: async (values: TForm) => {
      const payload = o.toPayload(values)
      if (mode === 'edit' && o.id) {
        await api.put(`/api/${o.resource}/${o.id}`, payload)
        return o.id
      }
      return (await api.post<{ id: number }>(`/api/${o.resource}`, payload)).id
    },
    onSuccess: async (id) => {
      notifications.show({ color: 'green', message: `${o.title} saved.` })
      await client.invalidateQueries({ queryKey: [o.resource] })
      setMode('view')
      navigate(`/${o.resource}/${id}`)
    },
    onError: (error) => {
      if (error instanceof ApiError)
        for (const [field, message] of Object.entries(error.fieldErrors)) form.setError(field as Path<TForm>, { message })
      notifications.show({ color: 'red', title: 'Not saved', message: errorMessage(error) })
    },
  })

  const remove = useMutation({
    mutationFn: () => api.delete(`/api/${o.resource}/${o.id}`),
    onSuccess: async () => {
      notifications.show({ color: 'green', message: `${o.title} deleted.` })
      await client.invalidateQueries({ queryKey: [o.resource] })
      navigate(`/${o.resource}`)
    },
    onError: (error) => notifications.show({ color: 'red', title: 'Not deleted', message: errorMessage(error) }),
  })

  const go = async (direction: NavDirection) => {
    try {
      const from = record.data ? o.navKey(record.data) : undefined
      const { id } = await api.get<{ id: number }>(`/api/${o.resource}/navigate${query({ direction, from })}`)
      navigate(`/${o.resource}/${id}`)
    } catch (error) {
      if (error instanceof ApiError && error.status === 404)
        notifications.show({ color: 'gray', message: direction === 'Next' || direction === 'Last' ? 'No more records.' : 'Already at the first record.' })
      else notifications.show({ color: 'red', message: errorMessage(error) })
    }
  }

  return {
    mode,
    record: record.data ?? null,
    isLoading: record.isLoading,
    loadError: record.error,
    editing: mode !== 'view',
    saving: save.isPending,
    deleting: remove.isPending,
    add: () => {
      navigate(`/${o.resource}`)
      setMode('new')
      form.reset(o.empty())
    },
    edit: () => setMode('edit'),
    save: form.handleSubmit((values) => save.mutate(values)),
    undo: () => {
      if (record.data) {
        setMode('view')
        form.reset(o.toForm(record.data))
      } else {
        form.reset(o.empty())
      }
    },
    remove: () => remove.mutate(),
    navigate: go,
  }
}
