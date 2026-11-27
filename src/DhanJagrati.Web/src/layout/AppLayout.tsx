import {
  ActionIcon,
  AppShell,
  Avatar,
  Badge,
  Burger,
  Divider,
  Group,
  Menu,
  NavLink,
  ScrollArea,
  Stack,
  Text,
  Tooltip,
  UnstyledButton,
  useComputedColorScheme,
  useMantineColorScheme,
} from '@mantine/core'
import { useDisclosure } from '@mantine/hooks'
import {
  IconBuildingBank,
  IconCalendar,
  IconChevronDown,
  IconKey,
  IconLayoutSidebarLeftCollapse,
  IconLayoutSidebarLeftExpand,
  IconLogout,
  IconMoon,
  IconSun,
} from '@tabler/icons-react'
import { Fragment, useState } from 'react'
import { Link, Outlet, useLocation, useNavigate } from 'react-router'
import type { Me } from '../api/types'
import { settingEnabled, useLogout } from '../auth/session'
import { formatDate } from '../components/format'
import { menu } from './menu'

function isActive(pathname: string, to: string) {
  if (to === '/') return pathname === '/'
  return pathname === to || pathname.startsWith(`${to}/`)
}

export function AppLayout({ me }: { me: Me }) {
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const logout = useLogout()
  const [mobileOpen, mobile] = useDisclosure(false)
  const [desktopOpen, desktop] = useDisclosure(true)
  const { setColorScheme } = useMantineColorScheme()
  const scheme = useComputedColorScheme('light')
  const backDated = me.businessDate !== me.today

  const sections = menu
    .filter((s) => !s.setting || settingEnabled(me, s.setting))
    .map((s) => ({ ...s, items: s.items.filter((i) => !i.roles || i.roles.includes(me.role)) }))

  // Open the section containing the current page; the user can toggle others.
  const [opened, setOpened] = useState<Record<string, boolean>>({})
  const sectionOpen = (label: string, hasActive: boolean) => opened[label] ?? hasActive

  const initials = me.displayName
    .replace(/\(.*\)/, '')
    .trim()
    .split(/\s+/)
    .map((w) => w[0])
    .slice(0, 2)
    .join('')

  return (
    <AppShell
      header={{ height: 60 }}
      navbar={{ width: 264, breakpoint: 'md', collapsed: { mobile: !mobileOpen, desktop: !desktopOpen } }}
      padding={{ base: 'sm', sm: 'lg' }}
    >
      <AppShell.Header className="dj-no-print">
        <Group h="100%" px={{ base: 'sm', sm: 'md' }} justify="space-between" wrap="nowrap">
          <Group gap="sm" wrap="nowrap" miw={0}>
            <Burger opened={mobileOpen} onClick={mobile.toggle} hiddenFrom="md" size="sm" aria-label="Menu" />
            <Tooltip label={desktopOpen ? 'Hide menu' : 'Show menu'}>
              <ActionIcon variant="subtle" color="gray" visibleFrom="md" onClick={desktop.toggle} aria-label="Toggle menu">
                {desktopOpen ? <IconLayoutSidebarLeftCollapse size={20} /> : <IconLayoutSidebarLeftExpand size={20} />}
              </ActionIcon>
            </Tooltip>
            <UnstyledButton component={Link} to="/" miw={0}>
              <Group gap="xs" wrap="nowrap">
                <div className="dj-brand-mark">DJ</div>
                <Stack gap={0} miw={0} visibleFrom="xs">
                  <Text fw={700} size="sm" lh={1.2} truncate>
                    Dhan Jagrati
                  </Text>
                  <Text size="xs" c="dimmed" lh={1.2} truncate>
                    Microfinance Foundation
                  </Text>
                </Stack>
              </Group>
            </UnstyledButton>
          </Group>

          <Group gap="xs" wrap="nowrap">
            <Tooltip label="Change branch / login date">
              <Badge
                size="lg"
                variant="light"
                color="navy"
                leftSection={<IconBuildingBank size={14} />}
                style={{ cursor: 'pointer', textTransform: 'none' }}
                onClick={() => navigate('/help/select-branch')}
              >
                {me.branchName}
              </Badge>
            </Tooltip>
            <Tooltip label={backDated ? 'Working on a back date' : 'Login date'}>
              <Badge
                size="lg"
                variant={backDated ? 'filled' : 'light'}
                color={backDated ? 'red' : 'gray'}
                leftSection={<IconCalendar size={14} />}
                style={{ textTransform: 'none' }}
                visibleFrom="sm"
              >
                {formatDate(me.businessDate)}
              </Badge>
            </Tooltip>
            <Tooltip label={scheme === 'dark' ? 'Light mode' : 'Dark mode'}>
              <ActionIcon variant="default" size="lg" onClick={() => setColorScheme(scheme === 'dark' ? 'light' : 'dark')} aria-label="Toggle colour scheme">
                {scheme === 'dark' ? <IconSun size={18} /> : <IconMoon size={18} />}
              </ActionIcon>
            </Tooltip>
            <Menu position="bottom-end" width={230} shadow="md">
              <Menu.Target>
                <UnstyledButton>
                  <Group gap={6} wrap="nowrap">
                    <Avatar color="brand" radius="xl" size={34}>
                      {initials}
                    </Avatar>
                    <Stack gap={0} visibleFrom="lg">
                      <Text size="sm" fw={600} lh={1.2}>
                        {me.displayName}
                      </Text>
                      <Text size="xs" c="dimmed" lh={1.2}>
                        {me.role}
                      </Text>
                    </Stack>
                    <IconChevronDown size={14} />
                  </Group>
                </UnstyledButton>
              </Menu.Target>
              <Menu.Dropdown>
                <Menu.Label>
                  {me.userName} · {me.role}
                </Menu.Label>
                <Menu.Item leftSection={<IconBuildingBank size={16} />} component={Link} to="/help/select-branch">
                  Select branch
                </Menu.Item>
                <Menu.Item leftSection={<IconKey size={16} />} component={Link} to="/help/change-password">
                  Change password
                </Menu.Item>
                <Menu.Divider />
                <Menu.Item
                  color="red"
                  leftSection={<IconLogout size={16} />}
                  onClick={() => logout.mutate(undefined, { onSettled: () => navigate('/login') })}
                >
                  Log out
                </Menu.Item>
              </Menu.Dropdown>
            </Menu>
          </Group>
        </Group>
      </AppShell.Header>

      <AppShell.Navbar className="dj-navbar dj-no-print" p="sm">
        <AppShell.Section grow component={ScrollArea} scrollbarSize={6}>
          {sections.map((section) => {
            const SectionIcon = section.icon
            if (section.to) {
              return (
                <NavLink
                  key={section.label}
                  component={Link}
                  to={section.to}
                  label={section.label}
                  leftSection={<SectionIcon size={19} stroke={1.6} />}
                  active={isActive(pathname, section.to)}
                  onClick={mobile.close}
                />
              )
            }
            const hasActive = section.items.some((i) => isActive(pathname, i.to))
            return (
              <NavLink
                key={section.label}
                label={section.label}
                leftSection={<SectionIcon size={19} stroke={1.6} />}
                opened={sectionOpen(section.label, hasActive)}
                onChange={(value) => setOpened((o) => ({ ...o, [section.label]: value }))}
                childrenOffset={0}
              >
                {section.items.map((item) => (
                  <Fragment key={item.label}>
                    {item.dividerBefore && <Divider my={4} color="rgba(255,255,255,0.08)" />}
                    <NavLink
                      component={Link}
                      to={item.to}
                      label={item.label}
                      active={isActive(pathname, item.to)}
                      onClick={mobile.close}
                      fz="sm"
                      py={6}
                      rightSection={
                        item.phase ? (
                          <Badge size="xs" variant="outline" color="gray.5" radius="sm">
                            P{item.phase}
                          </Badge>
                        ) : null
                      }
                    />
                  </Fragment>
                ))}
              </NavLink>
            )
          })}
        </AppShell.Section>
        <AppShell.Section>
          <Divider my="sm" color="rgba(255,255,255,0.08)" />
          <Text size="xs" c="gray.5" px="xs">
            {me.companyName ?? 'Dhan Jagrati Microfinance Foundation'}
          </Text>
        </AppShell.Section>
      </AppShell.Navbar>

      <AppShell.Main className="dj-main">
        <Outlet />
      </AppShell.Main>
    </AppShell>
  )
}
