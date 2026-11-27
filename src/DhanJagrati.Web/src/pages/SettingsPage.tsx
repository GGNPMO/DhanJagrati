import { Alert, Box, Button, Group, Paper, Select, Stack, Switch, Table, Text, TextInput, Title } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, errorMessage } from '../api/client'
import type { AppSetting } from '../api/types'
import { isAdmin, meQueryKey, useMe } from '../auth/session'
import { emiMethods } from '../masters/configs'

/** Admin: module switches (e.g. Saving module) and EMI defaults. */
export function SettingsPage() {
  const { data: me } = useMe()
  const settings = useQuery({ queryKey: ['settings'], queryFn: () => api.get<AppSetting[]>('/api/settings') })

  if (!isAdmin(me?.role)) return <Alert color="gray">Only administrators can change settings.</Alert>

  return (
    <Stack maw={900}>
      <Title order={3}>Settings</Title>
      <Paper className="dj-card">
        <Box className="dj-section-title">Application Settings</Box>
        {settings.isError && <Alert color="red">{errorMessage(settings.error)}</Alert>}
        <Table withTableBorder verticalSpacing="sm">
          <Table.Tbody>
            {(settings.data ?? []).map((s) => (
              <SettingRow key={s.settingKey} setting={s} />
            ))}
          </Table.Tbody>
        </Table>
      </Paper>
    </Stack>
  )
}

function SettingRow({ setting }: { setting: AppSetting }) {
  const client = useQueryClient()
  const [value, setValue] = useState(setting.settingValue)

  const save = useMutation({
    mutationFn: (next: string) => api.put(`/api/settings/${encodeURIComponent(setting.settingKey)}`, { value: next }),
    onSuccess: () => {
      notifications.show({ color: 'green', message: `${setting.settingKey} updated.` })
      client.invalidateQueries({ queryKey: ['settings'] })
      client.invalidateQueries({ queryKey: meQueryKey })
    },
    onError: (e) => {
      setValue(setting.settingValue)
      notifications.show({ color: 'red', message: errorMessage(e) })
    },
  })

  let editor
  if (setting.dataType === 'BOOL') {
    editor = (
      <Switch
        checked={value === 'true'}
        onChange={(e) => {
          const next = String(e.currentTarget.checked)
          setValue(next)
          save.mutate(next)
        }}
      />
    )
  } else if (setting.settingKey === 'LOAN.EMI.DEFAULT_METHOD') {
    editor = (
      <Select
        data={emiMethods}
        value={value}
        allowDeselect={false}
        onChange={(next) => {
          if (!next) return
          setValue(next)
          save.mutate(next)
        }}
      />
    )
  } else {
    editor = (
      <Group gap="xs" wrap="nowrap">
        <TextInput value={value} onChange={(e) => setValue(e.currentTarget.value)} />
        <Button size="xs" onClick={() => save.mutate(value)} loading={save.isPending} disabled={value === setting.settingValue}>
          Save
        </Button>
      </Group>
    )
  }

  return (
    <Table.Tr>
      <Table.Td>
        <Text fw={600} size="sm">
          {setting.settingKey}
        </Text>
        <Text size="xs" c="dimmed">
          {setting.description}
        </Text>
      </Table.Td>
      <Table.Td w={320}>{editor}</Table.Td>
    </Table.Tr>
  )
}
