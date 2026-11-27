import { Box, Paper } from '@mantine/core'
import type { ReactNode } from 'react'

/** Bordered section with the blue legacy-style heading bar. */
export function Section({ title, children, right }: { title: ReactNode; children: ReactNode; right?: ReactNode }) {
  return (
    <Paper withBorder>
      <Box className="dj-section-title" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <span>{title}</span>
        {right}
      </Box>
      <Box p="md">{children}</Box>
    </Paper>
  )
}
