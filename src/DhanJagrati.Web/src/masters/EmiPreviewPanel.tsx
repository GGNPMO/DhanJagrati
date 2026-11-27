import { Alert, Box, Button, Group, NumberInput, Paper, ScrollArea, SegmentedControl, SimpleGrid, Stack, Table, Text } from '@mantine/core'
import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { api, errorMessage } from '../api/client'
import type { EmiPreview, MasterRow } from '../api/types'
import { formatAmount, formatDate } from '../components/format'

/** Try the saved plan's EMI configuration with a sample loan amount. */
export function EmiPreviewPanel({ plan }: { plan: MasterRow }) {
  const [amount, setAmount] = useState<number | string>(30000)
  const [gender, setGender] = useState('M')
  const [rate, setRate] = useState<number | string>('')
  const [manualEmi, setManualEmi] = useState<number | string>('')
  const isManual = plan.emiCalcMethod === 'MANUAL'

  const preview = useMutation({
    mutationFn: () =>
      api.post<EmiPreview>(`/api/loan-plans/${plan.id}/emi-preview`, {
        amount: Number(amount),
        gender,
        rate: rate === '' ? null : Number(rate),
        manualEmi: manualEmi === '' ? null : Number(manualEmi),
      }),
  })

  return (
    <Paper withBorder mt="md">
      <Box className="dj-section-title">EMI Preview - {String(plan.planCode)}</Box>
      <Stack p="md" gap="sm">
        <Text size="xs" c="dimmed">
          Uses the saved plan settings ({String(plan.emiCalcMethod)}) and the interest rate effective on the login date. Save changes before previewing.
        </Text>
        <SimpleGrid cols={{ base: 2, sm: 4 }}>
          <NumberInput label="Loan amount" value={amount} onChange={setAmount} min={1} thousandSeparator="," />
          <Box>
            <Text size="sm" fw={500} mb={4}>
              Gender
            </Text>
            <SegmentedControl fullWidth value={gender} onChange={setGender} data={[{ value: 'M', label: 'Male' }, { value: 'F', label: 'Female' }]} />
          </Box>
          <NumberInput label="Rate % (optional)" value={rate} onChange={setRate} min={0} max={100} />
          {isManual && <NumberInput label="EMI (manual)" value={manualEmi} onChange={setManualEmi} min={1} />}
        </SimpleGrid>
        <Group>
          <Button variant="light" onClick={() => preview.mutate()} loading={preview.isPending} disabled={!amount}>
            Calculate
          </Button>
        </Group>
        {preview.isError && <Alert color="red">{errorMessage(preview.error)}</Alert>}
        {preview.data && (
          <>
            <SimpleGrid cols={{ base: 2, sm: 4 }}>
              <Stat label="EMI" value={formatAmount(preview.data.emi)} />
              <Stat label={`Total (${preview.data.installments} EMIs)`} value={formatAmount(preview.data.totalRepayable)} />
              <Stat label={`Interest @ ${preview.data.rate}%`} value={formatAmount(preview.data.totalInterest)} />
              <Stat label="Processing fee" value={formatAmount(preview.data.processingFee)} />
            </SimpleGrid>
            <ScrollArea h={240} type="auto">
              <Table striped withTableBorder stickyHeader>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>No</Table.Th>
                    <Table.Th>Due Date</Table.Th>
                    <Table.Th ta="right">EMI</Table.Th>
                    <Table.Th ta="right">Principal</Table.Th>
                    <Table.Th ta="right">Interest</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {preview.data.schedule.map((line) => (
                    <Table.Tr key={line.installmentNo}>
                      <Table.Td>{line.installmentNo}</Table.Td>
                      <Table.Td>{formatDate(line.dueDate)}</Table.Td>
                      <Table.Td ta="right">{formatAmount(line.emiAmount)}</Table.Td>
                      <Table.Td ta="right">{formatAmount(line.principalAmount)}</Table.Td>
                      <Table.Td ta="right">{formatAmount(line.interestAmount)}</Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </ScrollArea>
          </>
        )}
      </Stack>
    </Paper>
  )
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <Paper withBorder p="xs" bg="gray.0">
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Text fw={700}>{value}</Text>
    </Paper>
  )
}
