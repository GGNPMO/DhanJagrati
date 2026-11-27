import { Button, Center, Paper, Stack, Text, Title } from '@mantine/core'
import { Link, useParams } from 'react-router'

export function ComingSoonPage() {
  const { feature = '' } = useParams()
  return (
    <Center mt="xl">
      <Paper withBorder p="xl" maw={480}>
        <Stack align="center">
          <Title order={3}>{feature}</Title>
          <Text c="dimmed" ta="center">
            This screen is planned for a later phase and is not available yet.
          </Text>
          <Button component={Link} to="/" variant="light">
            Back to home
          </Button>
        </Stack>
      </Paper>
    </Center>
  )
}
