import type { Me } from '../api/types'
import { formatAmount, formatDate } from '../components/format'
import { emiModeLabel } from './status'
import type { LoanCard } from './types'

const ordinal = (n: number) => {
  const s = ['th', 'st', 'nd', 'rd']
  const v = n % 100
  return `${n}${s[(v - 20) % 10] ?? s[v] ?? s[0]}`
}

function Letterhead({ me, title }: { me: Me; title: string }) {
  return (
    <div style={{ border: '1px solid #333', padding: '10px 14px', textAlign: 'center', marginBottom: 12 }}>
      <div style={{ fontWeight: 700, fontSize: 18 }}>{me.companyName ?? 'DHAN JAGRATI MICROFINANCE FOUNDATION'}</div>
      <div style={{ fontSize: 12 }}>{me.companyAddress}</div>
      <div style={{ fontWeight: 600, marginTop: 6, letterSpacing: 1 }}>{title}</div>
    </div>
  )
}

function Field({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <tr>
      <td style={{ border: 'none', width: '42%', textAlign: 'right', fontWeight: 600, padding: '3px 8px' }}>{label}:-</td>
      <td style={{ border: 'none', padding: '3px 8px' }}>{value}</td>
    </tr>
  )
}

/** Loan card handed to the member: loan terms and the EMI schedule with space for collection entries. */
export function LoanCardSheet({ card, me }: { card: LoanCard; me: Me }) {
  const dueDay = Number(card.emiStartDate.slice(8, 10))
  return (
    <div className="dj-print-sheet" style={{ padding: 16 }}>
      <Letterhead me={me} title="LOAN CARD" />
      <table style={{ marginBottom: 12 }}>
        <tbody>
          <Field label="File No" value={card.fileNo ?? '-'} />
          <Field label="Loan No" value={card.loanNo} />
          <Field label="F.O. Name" value={`${card.employeeName} (${card.employeeCode})`} />
          <Field label="Member" value={`${card.memberName} (${card.memberNo})`} />
          <Field label="Guardian" value={card.guardianName ?? '-'} />
          <Field label="Address" value={card.addressLine} />
          <Field label="Disbursed on" value={formatDate(card.disbursedOn)} />
          <Field label="Loan amount" value={`₹ ${formatAmount(card.loanAmount)} (${card.planCode})`} />
          <Field label="No. of EMIs" value={`${card.noOfInstallments} (${emiModeLabel[card.emiMode] ?? card.emiMode})`} />
          <Field label="EMI amount" value={`₹ ${formatAmount(card.emiAmount)}/-`} />
          {card.emiMode === 'M' && <Field label="EMI due" value={`${ordinal(dueDay).toUpperCase()} DAY OF EVERY MONTH`} />}
        </tbody>
      </table>
      <table>
        <thead>
          <tr>
            <th>No</th>
            <th>Due date</th>
            <th style={{ textAlign: 'right' }}>EMI</th>
            <th>Paid on</th>
            <th>Receipt no</th>
            <th>Collector sign</th>
          </tr>
        </thead>
        <tbody>
          {card.schedule.map((s) => (
            <tr key={s.installmentNo}>
              <td>{s.installmentNo}</td>
              <td>{formatDate(s.dueDate)}</td>
              <td style={{ textAlign: 'right' }}>{formatAmount(s.emiAmount)}</td>
              <td style={{ width: '16%' }} />
              <td style={{ width: '16%' }} />
              <td style={{ width: '20%' }} />
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

/** Signature card: member photo, KYC summary, declaration and signatures. */
export function SignatureCardSheet({ card, me }: { card: LoanCard; me: Me }) {
  const amountWords = `Rs.${formatAmount(card.loanAmount)}/-`
  return (
    <div className="dj-print-sheet" style={{ padding: 16 }}>
      <Letterhead me={me} title="SIGNATURE CARD" />
      <div style={{ display: 'flex', gap: 20, alignItems: 'flex-start' }}>
        <div style={{ width: 170, height: 190, border: '1px solid #333', display: 'grid', placeItems: 'center', flexShrink: 0 }}>
          {card.hasPhoto ? (
            <img src={`/api/members/${card.memberId}/image/photo`} alt="Member" style={{ maxWidth: '100%', maxHeight: '100%' }} />
          ) : (
            <span style={{ fontSize: 11, color: '#777' }}>Affix photo</span>
          )}
        </div>
        <table>
          <tbody>
            <Field label="File No" value={card.fileNo ?? '-'} />
            <Field label="Date of joining" value={formatDate(card.dateOfJoining)} />
            <Field label="Name" value={card.memberName} />
            <Field label="Guardian's name" value={card.guardianName ?? '-'} />
            <Field label="Age" value={card.age ?? '-'} />
            <Field label="Address" value={card.addressLine} />
            <Field label="Gender" value={card.gender === 'F' ? 'FEMALE' : card.gender === 'M' ? 'MALE' : 'OTHER'} />
            <Field label="Mobile no." value={card.mobileNo ?? '-'} />
            <Field label="Occupation" value={card.occupationName ?? '-'} />
            <Field label="Loan no." value={card.loanNo} />
          </tbody>
        </table>
      </div>
      <div style={{ marginTop: 18 }}>
        <div style={{ textAlign: 'center', fontWeight: 700, marginBottom: 6 }}>DECLARATION</div>
        <p style={{ margin: '4px 0', lineHeight: 1.6 }}>
          I hereby declare that as per my requested file no. <b>{card.fileNo ?? '______'}</b> and the agreement executed between me &amp;{' '}
          {me.companyName ?? 'DHAN JAGRATI MICROFINANCE FOUNDATION'}, I have received the sum of <b>{amountWords}</b> in{' '}
          {card.disbursementMode} {card.chequeNo ? `bearing transaction number ${card.chequeNo}` : ''}.
        </p>
        <p style={{ margin: '4px 0', lineHeight: 1.6 }}>
          I undertake &amp; declare that I will repay the said loan amount along with interest as per my repayment schedule on time.
        </p>
      </div>
      <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 40, gap: 24 }}>
        <div style={{ textAlign: 'center', flex: 1 }}>
          <div style={{ height: 60, display: 'grid', placeItems: 'end center' }}>
            {card.hasSignature && <img src={`/api/members/${card.memberId}/image/signature`} alt="Signature" style={{ maxHeight: 56 }} />}
          </div>
          <div style={{ borderTop: '1px solid #333', paddingTop: 4 }}>Member's signature</div>
        </div>
        <div style={{ textAlign: 'center', flex: 1 }}>
          <div style={{ height: 60 }} />
          <div style={{ borderTop: '1px solid #333', paddingTop: 4 }}>Field officer ({card.employeeCode})</div>
        </div>
        <div style={{ textAlign: 'center', flex: 1 }}>
          <div style={{ height: 60 }} />
          <div style={{ borderTop: '1px solid #333', paddingTop: 4 }}>Branch manager</div>
        </div>
      </div>
    </div>
  )
}
