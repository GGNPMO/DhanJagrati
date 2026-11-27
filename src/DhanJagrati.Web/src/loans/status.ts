/** Badge colour per loan status (always shown with the status text, never colour alone). */
export function statusColor(status: string): string {
  switch (status) {
    case 'PENDING':
      return 'yellow'
    case 'APPROVED':
      return 'green'
    case 'REJECTED':
      return 'red'
    case 'CLOSED':
      return 'gray'
    default:
      return 'blue'
  }
}

export const emiModeLabel: Record<string, string> = { D: 'Daily', W: 'Weekly', F: 'Fortnightly', M: 'Monthly' }
