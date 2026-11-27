import { Badge, Group, Stack, Text, Title } from '@mantine/core'
import type { ReactNode } from 'react'

/** Page title row: title, optional subtitle and status badge, actions on the right (wraps on mobile). */
export function PageHeader({ title, subtitle, badge, actions }: { title: string; subtitle?: ReactNode; badge?: ReactNode; actions?: ReactNode }) {
  return (
    <Group justify="space-between" align="flex-end" gap="sm" className="dj-no-print">
      <Stack gap={2}>
        <Group gap="xs">
          <Title order={2} fz={{ base: 20, sm: 24 }}>
            {title}
          </Title>
          {typeof badge === 'string' ? <Badge variant="light">{badge}</Badge> : badge}
        </Group>
        {subtitle && (
          <Text size="sm" c="dimmed">
            {subtitle}
          </Text>
        )}
      </Stack>
      {actions}
    </Group>
  )
}
