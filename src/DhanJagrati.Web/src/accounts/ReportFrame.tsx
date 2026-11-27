import { Alert, Button, Group, Loader, Paper, Stack, Text } from '@mantine/core'
import { IconFileSpreadsheet, IconPrinter, IconReportAnalytics } from '@tabler/icons-react'
import type { ReactNode } from 'react'
import { errorMessage } from '../api/client'
import { useMe } from '../auth/session'
import { PageHeader } from '../components/PageHeader'

interface Props {
  title: string
  subtitle?: string
  /** Filter inputs + Show button. */
  filters: ReactNode
  /** "Branch: AGRA · 01-Apr-2026 to 27-Nov-2026" shown on the printout. */
  printCaption?: string
  loading?: boolean
  error?: unknown
  hasData: boolean
  onCsv?: () => void
  children: ReactNode
}

/** Shared layout for account reports: filters, print / CSV actions, printable body with letterhead. */
export function ReportFrame({ title, subtitle, filters, printCaption, loading, error, hasData, onCsv, children }: Props) {
  const { data: me } = useMe()
  return (
    <Stack gap="md">
      <PageHeader
        title={title}
        subtitle={subtitle}
        actions={
          <Group gap="xs">
            {onCsv && (
              <Button variant="default" leftSection={<IconFileSpreadsheet size={16} />} disabled={!hasData} onClick={onCsv}>
                Excel (CSV)
              </Button>
            )}
            <Button variant="default" leftSection={<IconPrinter size={16} />} disabled={!hasData} onClick={() => window.print()}>
              Print
            </Button>
          </Group>
        }
      />
      <Paper className="dj-card dj-no-print" p="sm">
        {filters}
      </Paper>
      {error ? <Alert color="red">{errorMessage(error)}</Alert> : null}
      <Paper className="dj-card" p={{ base: 'xs', sm: 'md' }}>
        <div className="dj-print-area">
          <div className="dj-print-only" style={{ textAlign: 'center', marginBottom: 12 }}>
            <div style={{ fontWeight: 700, fontSize: 16 }}>{me?.companyName}</div>
            <div style={{ fontSize: 11 }}>{me?.companyAddress}</div>
            <div style={{ fontWeight: 600, marginTop: 4 }}>{title}</div>
            {printCaption && <div style={{ fontSize: 12 }}>{printCaption}</div>}
          </div>
          {loading ? (
            <Group justify="center" py="xl">
              <Loader size="sm" />
            </Group>
          ) : hasData ? (
            children
          ) : (
            <Stack align="center" gap={4} py="xl">
              <IconReportAnalytics size={32} color="var(--mantine-color-gray-5)" />
              <Text c="dimmed" size="sm">
                Choose the conditions and press Show.
              </Text>
            </Stack>
          )}
        </div>
      </Paper>
    </Stack>
  )
}
