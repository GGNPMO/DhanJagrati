import { AppShell, Badge, Button, Group, Menu, Menubar, Stack, Text, UnstyledButton } from '@mantine/core'
import { IconBuildingBank, IconCalendar, IconLogout, IconUser } from '@tabler/icons-react'
import { Fragment } from 'react'
import { Link, Outlet, useNavigate } from 'react-router'
import type { Me } from '../api/types'
import { settingEnabled, useLogout } from '../auth/session'
import { formatDate } from '../components/format'
import { menu } from './menu'

export function AppLayout({ me }: { me: Me }) {
  const navigate = useNavigate()
  const logout = useLogout()
  const backDated = me.businessDate !== me.today

  const sections = menu
    .filter((s) => !s.setting || settingEnabled(me, s.setting))
    .map((s) => ({ ...s, items: s.items.filter((i) => !i.roles || i.roles.includes(me.role)) }))

  return (
    <AppShell header={{ height: 96 }} padding="md">
      <AppShell.Header className="dj-no-print">
        <Group h={52} px="md" justify="space-between" bg="orange.6" c="white" wrap="nowrap">
          <UnstyledButton component={Link} to="/" c="white">
            <Stack gap={0}>
              <Text fw={700} size="lg" lh={1.2}>
                {me.companyName ?? 'Dhan Jagrati Microfinance Foundation'}
              </Text>
              <Text size="xs" opacity={0.9} lh={1.2} visibleFrom="sm">
                {me.companyAddress}
              </Text>
            </Stack>
          </UnstyledButton>
          <Group gap="xs" wrap="nowrap">
            <Badge leftSection={<IconUser size={14} />} variant="white" color="dark" visibleFrom="sm">
              {me.displayName}
            </Badge>
            <Badge
              leftSection={<IconBuildingBank size={14} />}
              variant="white"
              color="dark"
              style={{ cursor: 'pointer' }}
              onClick={() => navigate('/help/select-branch')}
            >
              {me.branchName}
            </Badge>
            <Badge
              leftSection={<IconCalendar size={14} />}
              variant={backDated ? 'filled' : 'white'}
              color={backDated ? 'red' : 'dark'}
              title={backDated ? 'Working on a back date' : 'Login date'}
            >
              {formatDate(me.businessDate)}
            </Badge>
            <Button
              size="xs"
              variant="white"
              color="dark"
              leftSection={<IconLogout size={14} />}
              loading={logout.isPending}
              onClick={() => logout.mutate(undefined, { onSettled: () => navigate('/login') })}
            >
              Log out
            </Button>
          </Group>
        </Group>
        <Menubar px="xs" h={44} style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
          {sections.map((section) => (
            <Menubar.Menu key={section.label} width={260} position="bottom-start">
              <Menubar.Target>
                <Button variant="subtle" color="dark" size="sm">
                  {section.label.toUpperCase()}
                </Button>
              </Menubar.Target>
              <Menubar.Dropdown mah="75vh" style={{ overflowY: 'auto' }}>
                {section.items.map((item) => (
                  <Fragment key={item.label}>
                    {item.dividerBefore && <Menu.Divider />}
                    <Menu.Item
                      component={Link}
                      to={item.to ?? '/'}
                      rightSection={
                        item.phase ? (
                          <Badge size="xs" variant="light" color="gray">
                            Phase {item.phase}
                          </Badge>
                        ) : null
                      }
                    >
                      {item.label}
                    </Menu.Item>
                  </Fragment>
                ))}
              </Menubar.Dropdown>
            </Menubar.Menu>
          ))}
        </Menubar>
      </AppShell.Header>
      <AppShell.Main>
        <Outlet />
      </AppShell.Main>
    </AppShell>
  )
}
