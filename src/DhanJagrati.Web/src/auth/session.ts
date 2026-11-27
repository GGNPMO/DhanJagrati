import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from '../api/client'
import type { Me, Role } from '../api/types'

export const meQueryKey = ['me'] as const

/** Current session (user, branch, business date). `null` when not logged in. */
export function useMe() {
  return useQuery({
    queryKey: meQueryKey,
    queryFn: async (): Promise<Me | null> => {
      try {
        return await api.get<Me>('/api/auth/me')
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) return null
        throw error
      }
    },
    staleTime: 60_000,
  })
}

export function useLogin() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (body: { userName: string; password: string }) => api.post('/api/auth/login', body),
    onSuccess: () => client.invalidateQueries({ queryKey: meQueryKey }),
  })
}

export function useLogout() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: () => api.post('/api/auth/logout'),
    onSettled: () => {
      client.setQueryData(meQueryKey, null)
      client.removeQueries({ predicate: (q) => q.queryKey[0] !== meQueryKey[0] })
    },
  })
}

export const canEditMasters = (role: Role | undefined) => role === 'ADMIN' || role === 'MANAGER'
export const isAdmin = (role: Role | undefined) => role === 'ADMIN'

export function settingEnabled(me: Me | null | undefined, key: string) {
  return me?.settings.find((s) => s.key === key)?.value.toLowerCase() === 'true'
}
