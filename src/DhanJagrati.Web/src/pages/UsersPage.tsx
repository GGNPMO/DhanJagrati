import {
  Alert,
  Badge,
  Button,
  Checkbox,
  Group,
  Modal,
  MultiSelect,
  Paper,
  PasswordInput,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconKey, IconPencil, IconPlus } from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, ApiError, errorMessage } from '../api/client'
import type { BranchOption } from '../api/types'
import { isAdmin, useMe } from '../auth/session'

interface UserRow {
  id: number
  userName: string
  displayName: string
  roleCode: string
  defaultBranchId: number
  defaultBranchName: string
  employeeCode: string | null
  isActive: boolean
  mustChangePassword: boolean
  isLocked: boolean
  lastLoginAt: string | null
  branchIds: string
}

interface UserDraft {
  userName: string
  displayName: string
  roleCode: string | null
  defaultBranchId: string | null
  branchIds: string[]
  employeeCode: string
  isActive: boolean
  temporaryPassword: string
}

const roles = ['ADMIN', 'MANAGER', 'ACCOUNTANT', 'OPERATOR', 'FIELD_OFFICER'].map((r) => ({ value: r, label: r.replace('_', ' ') }))

/** User Master: logins, roles, branch access and password reset (administrators only). */
export function UsersPage() {
  const { data: me } = useMe()
  const client = useQueryClient()
  const users = useQuery({ queryKey: ['users'], queryFn: () => api.get<UserRow[]>('/api/users'), enabled: isAdmin(me?.role) })
  const branches = useQuery({ queryKey: ['users', 'branches'], queryFn: () => api.get<BranchOption[]>('/api/users/branches'), enabled: isAdmin(me?.role) })

  const [editing, setEditing] = useState<{ id: number | null; draft: UserDraft } | null>(null)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [resetFor, setResetFor] = useState<UserRow | null>(null)
  const [resetPassword, setResetPassword] = useState('')

  const save = useMutation({
    mutationFn: ({ id, draft }: { id: number | null; draft: UserDraft }) => {
      const body = {
        ...draft,
        defaultBranchId: Number(draft.defaultBranchId),
        branchIds: draft.branchIds.map(Number),
        employeeCode: draft.employeeCode || null,
        temporaryPassword: id ? null : draft.temporaryPassword,
      }
      return id ? api.put(`/api/users/${id}`, body) : api.post('/api/users', body)
    },
    onSuccess: () => {
      notifications.show({ color: 'green', message: 'User saved.' })
      setEditing(null)
      client.invalidateQueries({ queryKey: ['users'] })
    },
    onError: (e) => {
      if (e instanceof ApiError) setErrors(e.fieldErrors)
      notifications.show({ color: 'red', title: 'Not saved', message: errorMessage(e) })
    },
  })

  const reset = useMutation({
    mutationFn: () => api.post(`/api/users/${resetFor!.id}/reset-password`, { temporaryPassword: resetPassword }),
    onSuccess: () => {
      notifications.show({ color: 'green', message: `Temporary password set for ${resetFor?.userName}. They must change it at next login.` })
      setResetFor(null)
      client.invalidateQueries({ queryKey: ['users'] })
    },
    onError: (e) => notifications.show({ color: 'red', message: errorMessage(e) }),
  })

  if (!isAdmin(me?.role)) return <Alert color="gray">Only administrators can manage users.</Alert>

  const branchOptions = (branches.data ?? []).map((b) => ({ value: String(b.branchId), label: b.branchName }))
  const open = (row: UserRow | null) => {
    setErrors({})
    setEditing({
      id: row?.id ?? null,
      draft: row
        ? {
            userName: row.userName,
            displayName: row.displayName,
            roleCode: row.roleCode,
            defaultBranchId: String(row.defaultBranchId),
            branchIds: row.branchIds ? row.branchIds.split(',') : [],
            employeeCode: row.employeeCode ?? '',
            isActive: row.isActive,
            temporaryPassword: '',
          }
        : {
            userName: '',
            displayName: '',
            roleCode: 'OPERATOR',
            defaultBranchId: me ? String(me.branchId) : null,
            branchIds: me ? [String(me.branchId)] : [],
            employeeCode: '',
            isActive: true,
            temporaryPassword: '',
          },
    })
  }
  const patch = (p: Partial<UserDraft>) => setEditing((e) => (e ? { ...e, draft: { ...e.draft, ...p } } : e))
  const d = editing?.draft

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={3}>User Master</Title>
        <Button leftSection={<IconPlus size={16} />} onClick={() => open(null)}>
          New user
        </Button>
      </Group>
      {users.isError && <Alert color="red">{errorMessage(users.error)}</Alert>}
      <Paper withBorder>
        <Table striped highlightOnHover withTableBorder>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>User Name</Table.Th>
              <Table.Th>Display Name</Table.Th>
              <Table.Th>Role</Table.Th>
              <Table.Th>Default Branch</Table.Th>
              <Table.Th>Employee</Table.Th>
              <Table.Th>Status</Table.Th>
              <Table.Th>Last Login</Table.Th>
              <Table.Th />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {(users.data ?? []).map((u) => (
              <Table.Tr key={u.id}>
                <Table.Td fw={600}>{u.userName}</Table.Td>
                <Table.Td>{u.displayName}</Table.Td>
                <Table.Td>{u.roleCode}</Table.Td>
                <Table.Td>{u.defaultBranchName}</Table.Td>
                <Table.Td>{u.employeeCode}</Table.Td>
                <Table.Td>
                  <Group gap={4}>
                    <Badge size="xs" color={u.isActive ? 'green' : 'gray'}>
                      {u.isActive ? 'Active' : 'Inactive'}
                    </Badge>
                    {u.isLocked && (
                      <Badge size="xs" color="red">
                        Locked
                      </Badge>
                    )}
                    {u.mustChangePassword && (
                      <Badge size="xs" color="yellow">
                        Temp password
                      </Badge>
                    )}
                  </Group>
                </Table.Td>
                <Table.Td>{u.lastLoginAt ? new Date(u.lastLoginAt + 'Z').toLocaleString('en-IN') : ''}</Table.Td>
                <Table.Td>
                  <Group gap={4} wrap="nowrap">
                    <Button size="compact-xs" variant="subtle" leftSection={<IconPencil size={12} />} onClick={() => open(u)}>
                      Edit
                    </Button>
                    <Button
                      size="compact-xs"
                      variant="subtle"
                      color="orange"
                      leftSection={<IconKey size={12} />}
                      onClick={() => {
                        setResetPassword('')
                        setResetFor(u)
                      }}
                    >
                      Reset password
                    </Button>
                  </Group>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Paper>

      <Modal opened={!!editing} onClose={() => setEditing(null)} title={editing?.id ? `Edit user ${d?.userName}` : 'New user'} size="lg">
        {d && (
          <Stack gap="sm">
            <TextInput label="User Name" withAsterisk value={d.userName} disabled={!!editing?.id} error={errors.userName} onChange={(e) => patch({ userName: e.currentTarget.value })} />
            <TextInput label="Display Name" withAsterisk value={d.displayName} error={errors.displayName} onChange={(e) => patch({ displayName: e.currentTarget.value })} />
            <Select label="Role" withAsterisk data={roles} value={d.roleCode} error={errors.roleCode} allowDeselect={false} onChange={(roleCode) => patch({ roleCode })} />
            <MultiSelect
              label="Allowed branches"
              withAsterisk
              data={branchOptions}
              value={d.branchIds}
              error={errors.branchIds}
              onChange={(branchIds) => patch({ branchIds, defaultBranchId: branchIds.includes(d.defaultBranchId ?? '') ? d.defaultBranchId : (branchIds[0] ?? null) })}
            />
            <Select
              label="Default branch"
              withAsterisk
              data={branchOptions.filter((b) => d.branchIds.includes(b.value))}
              value={d.defaultBranchId}
              error={errors.defaultBranchId}
              allowDeselect={false}
              onChange={(defaultBranchId) => patch({ defaultBranchId })}
            />
            <TextInput
              label="Employee code (optional)"
              description="Links the login to an advisor / field officer"
              value={d.employeeCode}
              error={errors.employeeCode}
              onChange={(e) => patch({ employeeCode: e.currentTarget.value })}
            />
            {!editing?.id && (
              <PasswordInput
                label="Temporary password"
                withAsterisk
                description="The user must change it at first login."
                value={d.temporaryPassword}
                error={errors.temporaryPassword}
                onChange={(e) => patch({ temporaryPassword: e.currentTarget.value })}
              />
            )}
            <Checkbox label="Active" checked={d.isActive} onChange={(e) => patch({ isActive: e.currentTarget.checked })} />
            <Group justify="flex-end">
              <Button variant="default" onClick={() => setEditing(null)}>
                Cancel
              </Button>
              <Button loading={save.isPending} onClick={() => editing && save.mutate(editing)}>
                Save
              </Button>
            </Group>
          </Stack>
        )}
      </Modal>

      <Modal opened={!!resetFor} onClose={() => setResetFor(null)} title={`Reset password - ${resetFor?.userName}`} centered>
        <Stack gap="sm">
          <Text size="sm">The user is signed out, unlocked, and must change this temporary password at next login.</Text>
          <PasswordInput label="Temporary password" value={resetPassword} onChange={(e) => setResetPassword(e.currentTarget.value)} />
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setResetFor(null)}>
              Cancel
            </Button>
            <Button color="orange" loading={reset.isPending} disabled={resetPassword.length < 8} onClick={() => reset.mutate()}>
              Reset
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  )
}
