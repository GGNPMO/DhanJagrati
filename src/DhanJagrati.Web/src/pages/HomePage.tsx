import { Anchor, Badge, Group, Paper, SimpleGrid, Stack, Text, Title } from '@mantine/core'
import { Link } from 'react-router'
import { settingEnabled, useMe } from '../auth/session'
import { formatDate } from '../components/format'
import { menu } from '../layout/menu'

export function HomePage() {
  const { data: me } = useMe()
  if (!me) return null

  return (
    <Stack>
      <Title order={3}>Welcome, {me.displayName}</Title>
      <SimpleGrid cols={{ base: 1, sm: 3 }}>
        <Info label="Branch" value={`${me.branchName} (${me.branchCode})`} />
        <Info label="Login date" value={formatDate(me.businessDate)} warn={me.businessDate !== me.today} />
        <Info label="Role" value={me.role} />
      </SimpleGrid>
      <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }}>
        {menu
          .filter((s) => !s.setting || settingEnabled(me, s.setting))
          .map((section) => (
            <Paper key={section.label} withBorder p="md">
              <Text fw={600} mb="xs">
                {section.label.toUpperCase()}
              </Text>
              <Stack gap={4}>
                {section.items
                  .filter((i) => !i.roles || i.roles.includes(me.role))
                  .map((item) => (
                    <Group key={item.label} justify="space-between" wrap="nowrap">
                      <Anchor component={Link} to={item.to ?? '/'} size="sm">
                        {item.label}
                      </Anchor>
                      {item.phase && (
                        <Badge size="xs" variant="light" color="gray">
                          Phase {item.phase}
                        </Badge>
                      )}
                    </Group>
                  ))}
              </Stack>
            </Paper>
          ))}
      </SimpleGrid>
    </Stack>
  )
}

function Info({ label, value, warn }: { label: string; value: string; warn?: boolean }) {
  return (
    <Paper withBorder p="md">
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Text fw={700} c={warn ? 'red' : undefined}>
        {value}
        {warn && ' (back date)'}
      </Text>
    </Paper>
  )
}
