import { Badge, Button, FileButton, Group, Image, SimpleGrid, Stack, Text } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconUpload } from '@tabler/icons-react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { ApiError, errorMessage } from '../api/client'
import { api } from '../api/client'
import { Section } from '../components/Section'
import type { MemberDetail } from './types'

async function upload(memberId: number, kind: 'photo' | 'signature', file: File) {
  const body = new FormData()
  body.append('file', file)
  const response = await fetch(`/api/members/${memberId}/image/${kind}`, {
    method: 'POST',
    body,
    credentials: 'same-origin',
    headers: { 'X-DJ-Client': 'web' },
  })
  if (!response.ok) {
    const problem = response.headers.get('content-type')?.includes('json') ? await response.json() : { title: response.statusText }
    throw new ApiError(response.status, problem)
  }
}

/** Photo / signature upload and "Photo/Sign Verify". */
export function MemberPhotoPanel({ member, canEdit, canVerify }: { member: MemberDetail; canEdit: boolean; canVerify: boolean }) {
  const client = useQueryClient()
  const [version, setVersion] = useState(() => Date.now())
  const refresh = async () => {
    setVersion(Date.now())
    await client.invalidateQueries({ queryKey: ['members', member.id] })
  }

  const uploadImage = useMutation({
    mutationFn: ({ kind, file }: { kind: 'photo' | 'signature'; file: File }) => upload(member.id, kind, file),
    onSuccess: refresh,
    onError: (e) => notifications.show({ color: 'red', title: 'Upload failed', message: errorMessage(e) }),
  })

  const verify = useMutation({
    mutationFn: (verified: boolean) => api.post(`/api/members/${member.id}/verify-photo`, { verified }),
    onSuccess: refresh,
    onError: (e) => notifications.show({ color: 'red', message: errorMessage(e) }),
  })

  const slot = (kind: 'photo' | 'signature', label: string, present: boolean) => (
    <Stack gap={6} align="center">
      <Text size="sm" fw={500}>
        {label}
      </Text>
      {present ? (
        <Image src={`/api/members/${member.id}/image/${kind}?v=${version}`} h={kind === 'photo' ? 140 : 70} w="auto" fit="contain" radius="sm" />
      ) : (
        <Text size="xs" c="dimmed" h={kind === 'photo' ? 140 : 70}>
          Not uploaded
        </Text>
      )}
      {canEdit && (
        <FileButton accept="image/jpeg,image/png" onChange={(file) => file && uploadImage.mutate({ kind, file })}>
          {(props) => (
            <Button {...props} size="xs" variant="light" leftSection={<IconUpload size={14} />} loading={uploadImage.isPending && uploadImage.variables?.kind === kind}>
              Upload
            </Button>
          )}
        </FileButton>
      )}
    </Stack>
  )

  return (
    <Section
      title="Photo / Signature"
      right={
        <Badge color={member.isPhotoVerified ? 'green' : 'yellow'} variant="white">
          {member.isPhotoVerified ? 'Verified' : 'Not verified'}
        </Badge>
      }
    >
      <SimpleGrid cols={2}>
        {slot('photo', 'Photo', member.hasPhoto)}
        {slot('signature', 'Signature', member.hasSignature)}
      </SimpleGrid>
      <Text size="xs" c="dimmed" mt="xs">
        JPEG or PNG, up to 1 MB. Uploading a new image clears verification.
      </Text>
      {canVerify && (
        <Group justify="center" mt="sm">
          <Button
            size="xs"
            color={member.isPhotoVerified ? 'gray' : 'green'}
            disabled={!member.hasPhoto || !member.hasSignature}
            loading={verify.isPending}
            onClick={() => verify.mutate(!member.isPhotoVerified)}
          >
            {member.isPhotoVerified ? 'Remove verification' : 'Verify photo & signature'}
          </Button>
        </Group>
      )}
    </Section>
  )
}
