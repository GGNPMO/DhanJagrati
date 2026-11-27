import type { Me } from '../api/types'
import { formatAmount, formatDate } from '../components/format'
import type { RecoveryReceipt } from './types'

/** Printable money receipt for a loan recovery. */
export function ReceiptSheet({ receipt, me }: { receipt: RecoveryReceipt; me: Me }) {
  const lateFee = receipt.lines.reduce((s, l) => s + l.lateFee, 0)
  return (
    <div className="dj-print-sheet" style={{ padding: 16, maxWidth: 560 }}>
      <div style={{ textAlign: 'center', borderBottom: '2px solid #333', paddingBottom: 8, marginBottom: 10 }}>
        <div style={{ fontWeight: 700, fontSize: 16 }}>{me.companyName}</div>
        <div style={{ fontSize: 11 }}>{me.companyAddress}</div>
        <div style={{ fontWeight: 600, marginTop: 4 }}>LOAN RECOVERY RECEIPT</div>
      </div>
      <table style={{ marginBottom: 10 }}>
        <tbody>
          <tr>
            <td style={{ border: 'none' }}>
              Receipt No: <b>{receipt.receiptNo}</b>
            </td>
            <td style={{ border: 'none', textAlign: 'right' }}>
              Date: <b>{formatDate(receipt.transDate)}</b>
            </td>
          </tr>
          <tr>
            <td style={{ border: 'none' }}>
              Loan No: <b>{receipt.loanNo}</b>
            </td>
            <td style={{ border: 'none', textAlign: 'right' }}>Mode: {receipt.paymentMode}</td>
          </tr>
          <tr>
            <td style={{ border: 'none' }} colSpan={2}>
              Received from: <b>{receipt.memberName}</b>
            </td>
          </tr>
        </tbody>
      </table>
      <table>
        <thead>
          <tr>
            <th>EMI No</th>
            <th>Trans No</th>
            <th style={{ textAlign: 'right' }}>Principal</th>
            <th style={{ textAlign: 'right' }}>Interest</th>
            <th style={{ textAlign: 'right' }}>Late fee</th>
          </tr>
        </thead>
        <tbody>
          {receipt.lines.map((l) => (
            <tr key={l.transNo}>
              <td>{l.installmentNo}</td>
              <td>{l.transNo}</td>
              <td style={{ textAlign: 'right' }}>{formatAmount(l.principal)}</td>
              <td style={{ textAlign: 'right' }}>{formatAmount(l.interest)}</td>
              <td style={{ textAlign: 'right' }}>{formatAmount(l.lateFee)}</td>
            </tr>
          ))}
          {receipt.bounceCharges > 0 && (
            <tr>
              <td colSpan={4}>Cheque bounce charges</td>
              <td style={{ textAlign: 'right' }}>{formatAmount(receipt.bounceCharges)}</td>
            </tr>
          )}
          <tr style={{ fontWeight: 700 }}>
            <td colSpan={4}>Total received {lateFee > 0 ? '(incl. late fee)' : ''}</td>
            <td style={{ textAlign: 'right' }}>₹ {formatAmount(receipt.total)}</td>
          </tr>
        </tbody>
      </table>
      {receipt.loanClosed && <p style={{ fontWeight: 600 }}>Loan fully repaid and closed.</p>}
      <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 36, fontSize: 12 }}>
        <span>Voucher: {receipt.voucherNo}</span>
        <span style={{ borderTop: '1px solid #333', paddingTop: 4 }}>Authorised signatory</span>
      </div>
    </div>
  )
}
