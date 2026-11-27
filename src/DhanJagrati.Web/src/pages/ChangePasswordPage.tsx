import { zodResolver } from '@hookform/resolvers/zod'
import { Alert, Box, Button, Center, Group, Paper, PasswordInput, Stack, TextInput } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router'
import { z } from 'zod'
import { api, ApiError, errorMessage } from '../api/client'
import { meQueryKey, useMe } from '../auth/session'

const schema = z
  .object({
    userName: z.string().trim().min(1, 'Required'),
    oldPassword: z.string().min(1, 'Required'),
    newPassword: z
      .string()
      .min(8, 'At least 8 characters')
      .regex(/[A-Za-z]/, 'Must contain a letter')
      .regex(/\d/, 'Must contain a digit'),
    confirmPassword: z.string().min(1, 'Required'),
  })
  .refine((v) => v.newPassword === v.confirmPassword, { path: ['confirmPassword'], message: 'Passwords do not match' })
type ChangePasswordForm = z.infer<typeof schema>

/** HELP > Change Password (legacy "User Change Password"). */
export function ChangePasswordPage() {
  const { data: me } = useMe()
  const navigate = useNavigate()
  const client = useQueryClient()
  const empty = { userName: me?.userName ?? '', oldPassword: '', newPassword: '', confirmPassword: '' }
  const form = useForm<ChangePasswordForm>({ resolver: zodResolver(schema), defaultValues: empty })

  const change = useMutation({
    mutationFn: (values: ChangePasswordForm) => api.post('/api/auth/change-password', values),
    onSuccess: async () => {
      notifications.show({ color: 'green', message: 'Password changed.' })
      await client.invalidateQueries({ queryKey: meQueryKey })
      navigate('/')
    },
    onError: (error) => {
      if (error instanceof ApiError)
        for (const [field, message] of Object.entries(error.fieldErrors)) form.setError(field as keyof ChangePasswordForm, { message })
    },
  })

  const errors = form.formState.errors
  return (
    <Center>
      <Paper withBorder w={460}>
        <Box className="dj-section-title">Change Password</Box>
        <Stack component="form" p="lg" gap="sm" onSubmit={form.handleSubmit((v) => change.mutate(v))}>
          {me?.mustChangePassword && (
            <Alert color="orange">You must change your password before using the application.</Alert>
          )}
          {change.isError && !(change.error instanceof ApiError && change.error.status === 400) && (
            <Alert color="red">{errorMessage(change.error)}</Alert>
          )}
          <TextInput label="Enter User Name" withAsterisk {...form.register('userName')} error={errors.userName?.message} />
          <PasswordInput label="Enter Old Password" withAsterisk autoComplete="current-password" {...form.register('oldPassword')} error={errors.oldPassword?.message} />
          <PasswordInput label="Enter New Password" withAsterisk autoComplete="new-password" {...form.register('newPassword')} error={errors.newPassword?.message} />
          <PasswordInput label="Re-Enter New Password" withAsterisk autoComplete="new-password" {...form.register('confirmPassword')} error={errors.confirmPassword?.message} />
          <Group justify="flex-end" mt="sm">
            <Button type="submit" loading={change.isPending}>
              OK
            </Button>
            <Button variant="default" onClick={() => form.reset(empty)}>
              Reset
            </Button>
            <Button variant="default" disabled={me?.mustChangePassword} onClick={() => navigate('/')}>
              Close
            </Button>
          </Group>
        </Stack>
      </Paper>
    </Center>
  )
}
