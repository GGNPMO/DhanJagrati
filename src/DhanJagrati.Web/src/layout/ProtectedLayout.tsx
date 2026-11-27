import { Center, Loader } from '@mantine/core'
import { Navigate, useLocation } from 'react-router'
import { useMe } from '../auth/session'
import { AppLayout } from './AppLayout'

export const CHANGE_PASSWORD_PATH = '/help/change-password'

/** Requires a session; while a password change is pending only the Change Password page is reachable. */
export function ProtectedLayout() {
  const { data: me, isPending } = useMe()
  const location = useLocation()

  if (isPending)
    return (
      <Center h="100vh">
        <Loader />
      </Center>
    )
  if (!me) return <Navigate to="/login" replace state={{ from: location.pathname }} />
  if (me.mustChangePassword && location.pathname !== CHANGE_PASSWORD_PATH) return <Navigate to={CHANGE_PASSWORD_PATH} replace />
  return <AppLayout me={me} />
}
