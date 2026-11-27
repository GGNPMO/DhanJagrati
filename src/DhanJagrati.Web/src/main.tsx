import '@mantine/core/styles.css'
import '@mantine/dates/styles.css'
import '@mantine/notifications/styles.css'
import './app.css'

import { createTheme, MantineProvider } from '@mantine/core'
import { DatesProvider } from '@mantine/dates'
import { Notifications } from '@mantine/notifications'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router'
import { ApiError } from './api/client'
import { meQueryKey } from './auth/session'
import { router } from './routes'

const theme = createTheme({
  primaryColor: 'orange',
  defaultRadius: 'sm',
  fontFamily: 'Segoe UI, system-ui, -apple-system, sans-serif',
  headings: { fontWeight: '600' },
})

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2,
      refetchOnWindowFocus: false,
    },
  },
})

// Session expired on the server -> back to login; password change required -> refresh /me so the guard redirects.
window.addEventListener('dj:unauthorized', () => queryClient.setQueryData(meQueryKey, null))
window.addEventListener('dj:password-change-required', () => queryClient.invalidateQueries({ queryKey: meQueryKey }))

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <MantineProvider theme={theme} defaultColorScheme="light">
      <DatesProvider settings={{ locale: 'en', firstDayOfWeek: 1 }}>
        <Notifications position="top-right" />
        <QueryClientProvider client={queryClient}>
          <RouterProvider router={router} />
        </QueryClientProvider>
      </DatesProvider>
    </MantineProvider>
  </StrictMode>,
)
