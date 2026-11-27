import { Alert, Badge, Button, Group, Paper, SimpleGrid, Skeleton, Stack, Table, Text, ThemeIcon, Tooltip } from '@mantine/core'
import {
  IconAlertTriangle,
  IconCalendarDue,
  IconCash,
  IconClockHour4,
  IconFilePlus,
  IconReportMoney,
  IconUserPlus,
  IconUsers,
  IconUsersGroup,
} from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { Link, useNavigate } from 'react-router'
import { api, errorMessage } from '../api/client'
import { useMe } from '../auth/session'
import { formatAmount, formatDate } from '../components/format'
import { PageHeader } from '../components/PageHeader'
import { Section } from '../components/Section'
import { statusColor } from '../loans/status'

interface Dashboard {
  activeMembers: number
  newMembersThisMonth: number
  activeAdvisors: number
  pendingLoans: number
  pendingAmount: number
  activeLoans: number
  outstandingPrincipal: number
  disbursedThisMonth: number
  disbursedCountThisMonth: number
  dueToday: number
  dueTodayAmount: number
  overdue: number
  overdueAmount: number
  recentLoans: { id: number; loanNo: string; memberName: string; loanAmount: number; status: string; applicationDate: string }[]
  disbursements: { month: string; amount: number; count: number }[]
}

const compact = new Intl.NumberFormat('en-IN', { notation: 'compact', maximumFractionDigits: 1 })

function Stat({ label, value, hint, icon, color }: { label: string; value: ReactNode; hint?: ReactNode; icon: ReactNode; color: string }) {
  return (
    <Paper className="dj-card" p="md">
      <Group justify="space-between" align="flex-start" wrap="nowrap">
        <Stack gap={4} miw={0}>
          <Text size="xs" c="dimmed" fw={600} tt="uppercase">
            {label}
          </Text>
          <div className="dj-stat-value">{value}</div>
          {hint && (
            <Text size="xs" c="dimmed">
              {hint}
            </Text>
          )}
        </Stack>
        <ThemeIcon variant="light" color={color} size={42} radius="md">
          {icon}
        </ThemeIcon>
      </Group>
    </Paper>
  )
}

/** Single-series bar chart: monthly disbursement amount (hover for exact value). */
function DisbursementChart({ points }: { points: Dashboard['disbursements'] }) {
  if (points.length === 0)
    return (
      <Text size="sm" c="dimmed" ta="center" py="xl">
        No disbursements in the last six months.
      </Text>
    )
  const max = Math.max(...points.map((p) => p.amount), 1)
  return (
    <Stack gap={6}>
      <div className="dj-bars" role="img" aria-label="Loan disbursements per month">
        {points.map((p) => (
          <Tooltip key={p.month} label={`${p.month}: ₹${formatAmount(p.amount)} · ${p.count} loan(s)`} withArrow>
            <div className="dj-bar-slot">
              <div className="dj-bar" style={{ height: `${Math.max((p.amount / max) * 100, 1.5)}%` }} />
            </div>
          </Tooltip>
        ))}
      </div>
      <Group gap={10} px={4} wrap="nowrap">
        {points.map((p) => (
          <Text key={p.month} size="xs" c="dimmed" ta="center" style={{ flex: 1 }}>
            {p.month}
          </Text>
        ))}
      </Group>
    </Stack>
  )
}

export function HomePage() {
  const { data: me } = useMe()
  const navigate = useNavigate()
  const dashboard = useQuery({ queryKey: ['dashboard', me?.branchId, me?.businessDate], queryFn: () => api.get<Dashboard>('/api/dashboard') })
  const d = dashboard.data

  const actions = [
    { label: 'New member', icon: <IconUserPlus size={18} />, to: '/members' },
    { label: 'New loan application', icon: <IconFilePlus size={18} />, to: '/loans' },
    { label: 'Approve loans', icon: <IconReportMoney size={18} />, to: '/loan-approve', roles: ['ADMIN', 'MANAGER'] },
    { label: 'Advisors', icon: <IconUsersGroup size={18} />, to: '/employees' },
  ].filter((a) => !a.roles || (me && a.roles.includes(me.role)))

  return (
    <Stack gap="lg">
      <PageHeader
        title={`Welcome, ${me?.displayName.replace(/\s*\(.*\)/, '') ?? ''}`}
        subtitle={`${me?.branchName} branch · login date ${formatDate(me?.businessDate)}`}
        actions={
          <Group gap="xs">
            {actions.map((a) => (
              <Button key={a.label} variant="default" leftSection={a.icon} component={Link} to={a.to} size="sm">
                {a.label}
              </Button>
            ))}
          </Group>
        }
      />

      {dashboard.isError && <Alert color="red">{errorMessage(dashboard.error)}</Alert>}

      <SimpleGrid cols={{ base: 1, xs: 2, lg: 4 }}>
        {!d ? (
          Array.from({ length: 4 }, (_, i) => <Skeleton key={i} h={108} radius="md" />)
        ) : (
          <>
            <Stat label="Active members" value={d.activeMembers.toLocaleString('en-IN')} hint={`+${d.newMembersThisMonth} this month`} icon={<IconUsers size={22} />} color="navy" />
            <Stat label="Outstanding principal" value={`₹${compact.format(d.outstandingPrincipal)}`} hint={`${d.activeLoans} active loan(s)`} icon={<IconCash size={22} />} color="brand" />
            <Stat label="Due today" value={d.dueToday} hint={`₹${formatAmount(d.dueTodayAmount)}`} icon={<IconCalendarDue size={22} />} color="teal" />
            <Stat
              label="Overdue EMIs"
              value={d.overdue}
              hint={`₹${formatAmount(d.overdueAmount)}`}
              icon={<IconAlertTriangle size={22} />}
              color={d.overdue > 0 ? 'red' : 'gray'}
            />
          </>
        )}
      </SimpleGrid>

      <SimpleGrid cols={{ base: 1, lg: 3 }}>
        <Section title="Disbursements - last 6 months" icon={<IconReportMoney size={18} />}>
          {d ? <DisbursementChart points={d.disbursements} /> : <Skeleton h={200} />}
          {d && (
            <Text size="xs" c="dimmed" mt="sm">
              This month: ₹{formatAmount(d.disbursedThisMonth)} across {d.disbursedCountThisMonth} loan(s)
            </Text>
          )}
        </Section>

        <Section title="Pending approval" icon={<IconClockHour4 size={18} />}>
          <Stack gap="xs">
            <div className="dj-stat-value">{d?.pendingLoans ?? '-'}</div>
            <Text size="sm" c="dimmed">
              application(s) worth ₹{formatAmount(d?.pendingAmount ?? 0)}
            </Text>
            <Text size="sm" c="dimmed">
              {d?.activeAdvisors ?? 0} active advisor(s) in this branch
            </Text>
            {(me?.role === 'ADMIN' || me?.role === 'MANAGER') && (
              <Button variant="light" mt="xs" component={Link} to="/loan-approve" disabled={!d?.pendingLoans}>
                Review applications
              </Button>
            )}
          </Stack>
        </Section>

        <Section title="Recent applications" icon={<IconFilePlus size={18} />} padding={0}>
          {d && d.recentLoans.length === 0 ? (
            <Text size="sm" c="dimmed" ta="center" py="xl">
              No loan applications yet.
            </Text>
          ) : (
            <Table highlightOnHover className="dj-grid">
              <Table.Tbody>
                {(d?.recentLoans ?? []).map((l) => (
                  <Table.Tr key={l.id} onClick={() => navigate(`/loans/${l.id}`)}>
                    <Table.Td>
                      <Text size="sm" fw={600}>
                        {l.loanNo}
                      </Text>
                      <Text size="xs" c="dimmed" truncate maw={160}>
                        {l.memberName}
                      </Text>
                    </Table.Td>
                    <Table.Td className="dj-num">₹{formatAmount(l.loanAmount)}</Table.Td>
                    <Table.Td>
                      <Badge size="sm" variant="light" color={statusColor(l.status)}>
                        {l.status}
                      </Badge>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          )}
        </Section>
      </SimpleGrid>
    </Stack>
  )
}
