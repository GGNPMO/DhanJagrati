import { createTheme, type MantineColorsTuple } from '@mantine/core'

/** Brand saffron (from the legacy orange bar), tuned for contrast. */
const brand: MantineColorsTuple = [
  '#fff4e6',
  '#ffe8cc',
  '#ffd0a1',
  '#ffb46b',
  '#ff9b3d',
  '#fd8a1f',
  '#f4780e',
  '#e8590c',
  '#c94a08',
  '#a63c04',
]

/** Deep navy used for the sidebar. */
const navy: MantineColorsTuple = [
  '#eef2f8',
  '#d9e0ec',
  '#b0bfd8',
  '#859cc4',
  '#617fb3',
  '#4a6ca9',
  '#3d62a5',
  '#2f5291',
  '#1c2f55',
  '#111d38',
]

export const theme = createTheme({
  primaryColor: 'brand',
  primaryShade: { light: 7, dark: 6 },
  colors: { brand, navy },
  defaultRadius: 'md',
  fontFamily: 'Inter, "Segoe UI", system-ui, -apple-system, sans-serif',
  headings: { fontFamily: 'Inter, "Segoe UI", system-ui, sans-serif', fontWeight: '650' },
  cursorType: 'pointer',
  components: {
    Paper: { defaultProps: { radius: 'md' } },
    Button: { defaultProps: { radius: 'md' } },
    TextInput: { defaultProps: { radius: 'md' } },
    Select: { defaultProps: { radius: 'md' } },
    Table: { defaultProps: { verticalSpacing: 'xs', fz: 'sm' } },
  },
})
