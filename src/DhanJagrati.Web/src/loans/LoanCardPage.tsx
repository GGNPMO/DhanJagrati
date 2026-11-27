import { Alert, Badge, Button, Grid, Group, Paper, ScrollArea, SegmentedControl, Stack, Table, Text, TextInput } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { useDebouncedValue } from '@mantine/hooks'
import { IconEye, IconFilter, IconPrinter } from '@tabler/icons-react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, errorMessage, query } from '../api/client'
import { useMe } from '../auth/session'
import { formatAmount, formatDate } from '../components/format'
import { PageHeader } from '../components/PageHeader'
import { Section } from '../components/Section'
import { LoanCardSheet, SignatureCardSheet } from './CardSheets'
import type { CardSearchItem, LoanCard } from './types'

/** LOAN MASTER > Loan Card Print & Signature Card. */
export function LoanCardPage() {
  const { data: me } = useMe()
  const client = useQueryClient()
  const [from, setFrom] = useState<string | null>(me ? `${me.businessDate.slice(0, 8)}01` : null)
  const [to, setTo] = useState<string | null>(me?.businessDate ?? null)
  const [fileNo, setFileNo] = useState('')
  const [loanNo, setLoanNo] = useState('')
  const [filters] = useDebouncedValue({ from, to, fileNo, loanNo }, 300)
  const [cardType, setCardType] = useState<'LOAN' | 'SIGNATURE'>('LOAN')
  const [loanId, setLoanId] = useState<number | null>(null)

  const list = useQuery({
    queryKey: ['loan-cards', filters],
    queryFn: () => api.get<CardSearchItem[]>(`/api/loan-cards${query(filters)}`),
  })
  const card = useQuery({
    queryKey: ['loan-cards', 'card', loanId],
    queryFn: () => api.get<LoanCard>(`/api/loan-cards/${loanId}`),
    enabled: loanId !== null,
  })

  const print = async () => {
    if (!loanId) return
    window.print()
    await api.post(`/api/loan-cards/${loanId}/printed`, { cardType })
    await client.invalidateQueries({ queryKey: ['loan-cards'] })
  }

  return (
    <Stack gap="md">
      <PageHeader title="Loan Card & Signature Card" subtitle="Preview and print cards for approved loans" />

      <Grid gap="md">
        <Grid.Col span={{ base: 12, lg: 5 }}>
          <Stack gap="md">
            <Section title="Search" icon={<IconFilter size={18} />}>
              <Grid gap="sm">
                <Grid.Col span={6}>
                  <DateInput label="Date from" value={from} onChange={setFrom} valueFormat="DD-MMM-YYYY" />
                </Grid.Col>
                <Grid.Col span={6}>
                  <DateInput label="To" value={to} onChange={setTo} valueFormat="DD-MMM-YYYY" />
                </Grid.Col>
                <Grid.Col span={6}>
                  <TextInput label="File No" value={fileNo} onChange={(e) => setFileNo(e.currentTarget.value)} />
                </Grid.Col>
                <Grid.Col span={6}>
                  <TextInput label="Loan No" value={loanNo} onChange={(e) => setLoanNo(e.currentTarget.value)} />
                </Grid.Col>
                <Grid.Col span={12}>
                  <SegmentedControl
                    fullWidth
                    value={cardType}
                    onChange={(v) => setCardType(v as 'LOAN' | 'SIGNATURE')}
                    data={[
                      { value: 'LOAN', label: 'Loan card' },
                      { value: 'SIGNATURE', label: 'Signature card' },
                    ]}
                  />
                </Grid.Col>
              </Grid>
            </Section>

            <Paper className="dj-card">
              {list.isError && <Alert color="red">{errorMessage(list.error)}</Alert>}
              <ScrollArea.Autosize mah={420} type="auto">
                <Table highlightOnHover className="dj-grid" stickyHeader>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>Loan No</Table.Th>
                      <Table.Th>Member</Table.Th>
                      <Table.Th className="dj-num">Amount</Table.Th>
                      <Table.Th />
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {(list.data ?? []).map((r) => (
                      <Table.Tr key={r.loanId} data-selected={r.loanId === loanId} onClick={() => setLoanId(r.loanId)}>
                        <Table.Td>
                          <Text size="sm" fw={600}>
                            {r.loanNo}
                          </Text>
                          <Text size="xs" c="dimmed">
                            {formatDate(r.disbursedOn)} · File {r.fileNo ?? '-'}
                          </Text>
                        </Table.Td>
                        <Table.Td>{r.memberName}</Table.Td>
                        <Table.Td className="dj-num">{formatAmount(r.loanAmount)}</Table.Td>
                        <Table.Td>
                          {r.prints > 0 ? (
                            <Badge size="xs" variant="light" color="gray">
                              Printed {r.prints}
                            </Badge>
                          ) : (
                            <IconEye size={16} />
                          )}
                        </Table.Td>
                      </Table.Tr>
                    ))}
                    {!list.isLoading && (list.data ?? []).length === 0 && (
                      <Table.Tr>
                        <Table.Td colSpan={4}>
                          <Text c="dimmed" ta="center" py="md" size="sm">
                            No approved loans for these conditions.
                          </Text>
                        </Table.Td>
                      </Table.Tr>
                    )}
                  </Table.Tbody>
                </Table>
              </ScrollArea.Autosize>
            </Paper>
          </Stack>
        </Grid.Col>

        <Grid.Col span={{ base: 12, lg: 7 }}>
          <Section
            title="Preview"
            icon={<IconEye size={18} />}
            right={
              <Button size="xs" leftSection={<IconPrinter size={14} />} disabled={!card.data} onClick={print}>
                Print
              </Button>
            }
          >
            {!loanId && (
              <Text c="dimmed" size="sm" ta="center" py="xl">
                Select a loan to preview its {cardType === 'LOAN' ? 'loan card' : 'signature card'}.
              </Text>
            )}
            {card.isError && <Alert color="red">{errorMessage(card.error)}</Alert>}
            {card.data && me && (
              <ScrollArea type="auto">
                <Group justify="center" miw={620}>
                  <div className="dj-print-area" style={{ width: '100%', maxWidth: 760, boxShadow: '0 0 0 1px var(--dj-card-border)' }}>
                    {cardType === 'LOAN' ? <LoanCardSheet card={card.data} me={me} /> : <SignatureCardSheet card={card.data} me={me} />}
                  </div>
                </Group>
              </ScrollArea>
            )}
          </Section>
        </Grid.Col>
      </Grid>
    </Stack>
  )
}
