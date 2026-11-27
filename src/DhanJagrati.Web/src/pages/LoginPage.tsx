import { zodResolver } from '@hookform/resolvers/zod'
import { Alert, Box, Button, Center, Paper, PasswordInput, Stack, Text, TextInput, Title } from '@mantine/core'
import { IconLock, IconUser } from '@tabler/icons-react'
import { useForm } from 'react-hook-form'
import { Navigate, useLocation, useNavigate } from 'react-router'
import { z } from 'zod'
import { errorMessage } from '../api/client'
import { useLogin, useMe } from '../auth/session'

const schema = z.object({
  userName: z.string().trim().min(1, 'Enter user name'),
  password: z.string().min(1, 'Enter password'),
})
type LoginForm = z.infer<typeof schema>

export function LoginPage() {
  const { data: me } = useMe()
  const login = useLogin()
  const navigate = useNavigate()
  const location = useLocation()
  const from = (location.state as { from?: string } | null)?.from ?? '/'

  const form = useForm<LoginForm>({ resolver: zodResolver(schema), defaultValues: { userName: '', password: '' } })

  if (me) return <Navigate to={from} replace />

  return (
    <Center mih="100vh" bg="orange.0" p="md">
      <Paper withBorder shadow="md" w={380} radius="md">
        <Box className="dj-section-title" py="sm">
          <Title order={4} c="white">
            Dhan Jagrati Microfinance
          </Title>
          <Text size="xs" c="white" opacity={0.85}>
            Sign in to continue
          </Text>
        </Box>
        <Stack
          component="form"
          p="lg"
          gap="md"
          onSubmit={form.handleSubmit((values) => login.mutate(values, { onSuccess: () => navigate(from, { replace: true }) }))}
        >
          {login.isError && <Alert color="red">{errorMessage(login.error)}</Alert>}
          <TextInput
            label="User Name"
            leftSection={<IconUser size={16} />}
            autoComplete="username"
            autoFocus
            {...form.register('userName')}
            error={form.formState.errors.userName?.message}
          />
          <PasswordInput
            label="Password"
            leftSection={<IconLock size={16} />}
            autoComplete="current-password"
            {...form.register('password')}
            error={form.formState.errors.password?.message}
          />
          <Button type="submit" loading={login.isPending} fullWidth>
            Login
          </Button>
        </Stack>
      </Paper>
    </Center>
  )
}
