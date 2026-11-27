import { zodResolver } from '@hookform/resolvers/zod'
import { Alert, Box, Button, Group, Paper, PasswordInput, SimpleGrid, Stack, Text, TextInput, ThemeIcon, Title } from '@mantine/core'
import { IconCash, IconLock, IconShieldCheck, IconUser, IconUsersGroup } from '@tabler/icons-react'
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

const features = [
  { icon: <IconUsersGroup size={18} />, text: 'Members, advisors and groups in one place' },
  { icon: <IconCash size={18} />, text: 'Loan application, approval and recovery' },
  { icon: <IconShieldCheck size={18} />, text: 'Branch-wise access with full audit trail' },
]

export function LoginPage() {
  const { data: me } = useMe()
  const login = useLogin()
  const navigate = useNavigate()
  const location = useLocation()
  const from = (location.state as { from?: string } | null)?.from ?? '/'
  const form = useForm<LoginForm>({ resolver: zodResolver(schema), defaultValues: { userName: '', password: '' } })

  if (me) return <Navigate to={from} replace />

  return (
    <SimpleGrid cols={{ base: 1, md: 2 }} spacing={0} mih="100vh">
      <Box
        visibleFrom="md"
        p={56}
        style={{
          background: 'linear-gradient(150deg, var(--mantine-color-navy-9) 0%, var(--mantine-color-navy-8) 55%, var(--mantine-color-brand-9) 140%)',
          color: '#fff',
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'space-between',
        }}
      >
        <Group gap="sm">
          <div className="dj-brand-mark">DJ</div>
          <Text fw={700}>Dhan Jagrati Microfinance Foundation</Text>
        </Group>
        <Stack gap="lg" maw={440}>
          <Title order={1} fz={38} lh={1.15} c="white">
            Empowering livelihoods, one loan at a time.
          </Title>
          <Stack gap="sm">
            {features.map((f) => (
              <Group key={f.text} gap="sm" wrap="nowrap">
                <ThemeIcon variant="light" color="brand" radius="xl">
                  {f.icon}
                </ThemeIcon>
                <Text c="gray.3">{f.text}</Text>
              </Group>
            ))}
          </Stack>
        </Stack>
        <Text size="xs" c="gray.5">
          Plot No-7, Second Floor, Adan Bagh Extension, Dayalbagh, Agra
        </Text>
      </Box>

      <Box p={{ base: 'md', sm: 'xl' }} style={{ display: 'grid', placeItems: 'center', background: 'var(--dj-page-bg)' }}>
        <Paper className="dj-card" p={{ base: 'lg', sm: 36 }} w="100%" maw={420} radius="lg">
          <Stack
            component="form"
            gap="md"
            onSubmit={form.handleSubmit((values) => login.mutate(values, { onSuccess: () => navigate(from, { replace: true }) }))}
          >
            <Group gap="sm" hiddenFrom="md">
              <div className="dj-brand-mark">DJ</div>
              <Text fw={700}>Dhan Jagrati Microfinance</Text>
            </Group>
            <Stack gap={4}>
              <Title order={2}>Sign in</Title>
              <Text size="sm" c="dimmed">
                Use your branch login to continue.
              </Text>
            </Stack>
            {login.isError && <Alert color="red">{errorMessage(login.error)}</Alert>}
            <TextInput
              label="User name"
              size="md"
              leftSection={<IconUser size={18} />}
              autoComplete="username"
              autoFocus
              {...form.register('userName')}
              error={form.formState.errors.userName?.message}
            />
            <PasswordInput
              label="Password"
              size="md"
              leftSection={<IconLock size={18} />}
              autoComplete="current-password"
              {...form.register('password')}
              error={form.formState.errors.password?.message}
            />
            <Button type="submit" size="md" loading={login.isPending} fullWidth mt="xs">
              Sign in
            </Button>
          </Stack>
        </Paper>
      </Box>
    </SimpleGrid>
  )
}
