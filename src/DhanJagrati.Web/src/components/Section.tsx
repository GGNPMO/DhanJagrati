import { Box, Group, Paper } from '@mantine/core'
import type { ReactNode } from 'react'

/** Card with a compact header row (icon + title, optional right-side actions). */
export function Section({ title, children, right, icon, padding = 'md' }: { title: ReactNode; children: ReactNode; right?: ReactNode; icon?: ReactNode; padding?: string | number }) {
  return (
    <Paper className="dj-card">
      <div className="dj-section-head">
        <Group gap={8} wrap="nowrap">
          {icon && <span className="dj-section-icon" style={{ display: 'flex' }}>{icon}</span>}
          <span>{title}</span>
        </Group>
        {right}
      </div>
      <Box p={padding}>{children}</Box>
    </Paper>
  )
}
