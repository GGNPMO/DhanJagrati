import { useCallback, useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react'
import {
  ArrowDownRight,
  ArrowRight,
  ArrowUpRight,
  Bell,
  CalendarDays,
  Check,
  ChevronDown,
  ChevronRight,
  CircleAlert,
  CircleCheck,
  Clock3,
  Download,
  FileText,
  HandCoins,
  LayoutDashboard,
  Landmark,
  Menu,
  MoreHorizontal,
  Plus,
  Printer,
  RefreshCw,
  Search,
  SlidersHorizontal,
  Sparkles,
  Users,
  Wallet,
  X,
} from 'lucide-react'
import {
  api,
  type BranchInfo,
  type AccountGroupOption,
  type Collection,
  type DashboardSummary,
  type Loan,
  type LoanApplication,
  type LoanProductOption,
  type LedgerOption,
  type Member,
  type ReportData,
} from './api'
import './App.css'

type PageId = 'overview' | 'members' | 'loans' | 'collections' | 'accounting' | 'reports'
const pageIds: PageId[] = ['overview', 'members', 'loans', 'collections', 'accounting', 'reports']

function pageFromLocation(): PageId {
  const requested = window.location.hash.replace(/^#\/?/, '').split('/')[0] as PageId
  return pageIds.includes(requested) ? requested : 'overview'
}

function reportFromLocation() {
  const requested = window.location.hash.replace(/^#\/?/, '').split('/')[1]
  return requested && Object.hasOwn(reportTitles, requested) ? requested : 'cashbook'
}

const navigation: { label: string; links: { id: PageId; title: string; icon: typeof LayoutDashboard }[] }[] = [
  { label: 'WORKSPACE', links: [{ id: 'overview', title: 'Overview', icon: LayoutDashboard }] },
  {
    label: 'OPERATIONS',
    links: [
      { id: 'members', title: 'Members', icon: Users },
      { id: 'loans', title: 'Loan desk', icon: HandCoins },
      { id: 'collections', title: 'Collections', icon: Wallet },
    ],
  },
  {
    label: 'FINANCE',
    links: [
      { id: 'accounting', title: 'Accounting', icon: Landmark },
      { id: 'reports', title: 'Reports', icon: FileText },
    ],
  },
]

const pageCopy: Record<PageId, { title: string; eyebrow: string; description: string }> = {
  overview: { title: 'Good morning, team', eyebrow: 'BRANCH PULSE', description: 'Here’s what is happening at your working branch today.' },
  members: { title: 'Member directory', eyebrow: 'COMMUNITY', description: 'Know your members, their groups and the people behind every account.' },
  loans: { title: 'Loan desk', eyebrow: 'CREDIT OPERATIONS', description: 'Review applications, move decisions forward and keep every step visible.' },
  collections: { title: 'Collections', eyebrow: 'FIELD OPERATIONS', description: 'Record a repayment and keep the branch collection book up to date.' },
  accounting: { title: 'Accounting', eyebrow: 'GENERAL LEDGER', description: 'Post balanced vouchers and maintain a clear audit trail.' },
  reports: { title: 'Reports & statements', eyebrow: 'INSIGHTS', description: 'Choose a report, set the period and generate a print-ready branch view.' },
}

const reportGroups = [
  { label: 'Branch accounts', ids: ['cashbook', 'cashbook-detail', 'trial-balance', 'profit-loss', 'balance-sheet'] },
  { label: 'Loan portfolio', ids: ['loan-applications', 'recovery-due', 'recovery-ledger', 'relationship-manager', 'loan-closer'] },
]

const reportTitles: Record<string, string> = {
  cashbook: 'Cash book summary',
  'cashbook-detail': 'Cash book detail',
  'trial-balance': 'Trial balance',
  'profit-loss': 'Profit & loss statement',
  'balance-sheet': 'Balance sheet',
  'loan-applications': 'Loan applications',
  'recovery-due': 'Loan recovery due',
  'recovery-ledger': 'Loan recovery ledger',
  'relationship-manager': 'Relationship manager',
  'loan-closer': 'Loan closure report',
}

const currentDate = new Date()
const today = `${currentDate.getFullYear()}-${String(currentDate.getMonth() + 1).padStart(2, '0')}-${String(currentDate.getDate()).padStart(2, '0')}`
const monthStart = `${today.slice(0, 8)}01`

function App() {
  const [page, setPage] = useState<PageId>(pageFromLocation)
  const [mobileNavOpen, setMobileNavOpen] = useState(false)
  const [dashboard, setDashboard] = useState<DashboardSummary | null>(null)
  const [members, setMembers] = useState<Member[]>([])
  const [applications, setApplications] = useState<LoanApplication[]>([])
  const [loanAccounts, setLoanAccounts] = useState<Loan[]>([])
  const [collections, setCollections] = useState<Collection[]>([])
  const [branchName, setBranchName] = useState('')
  const [loanProducts, setLoanProducts] = useState<LoanProductOption[]>([])
  const [accountGroups, setAccountGroups] = useState<AccountGroupOption[]>([])
  const [ledgers, setLedgers] = useState<LedgerOption[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [toast, setToast] = useState('')
  const [modal, setModal] = useState<'member' | 'loan' | null>(null)
  const [reportType, setReportType] = useState(reportFromLocation)
  const [report, setReport] = useState<ReportData | null>(null)
  const [reportLoading, setReportLoading] = useState(false)
  const [dateFrom, setDateFrom] = useState(monthStart)
  const [dateTo, setDateTo] = useState(today)
  const [memberSearch, setMemberSearch] = useState('')
  const [loanTab, setLoanTab] = useState<'applications' | 'active'>('applications')

  const refresh = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const [dashboardData, memberRows, applicationRows, accountRows, collectionRows, branch, products, groups, ledgerRows] = await Promise.all([
        api.get<DashboardSummary>('/dashboard'),
        api.get<Member[]>('/members'),
        api.get<LoanApplication[]>('/loans'),
        api.get<Loan[]>('/loan-accounts'),
        api.get<Collection[]>('/collections'),
        api.get<BranchInfo>('/branch'),
        api.get<LoanProductOption[]>('/loan-products'),
        api.get<AccountGroupOption[]>('/accounting/groups'),
        api.get<LedgerOption[]>('/accounting/ledgers'),
      ])
      setDashboard(dashboardData)
      setMembers(memberRows)
      setApplications(applicationRows)
      setLoanAccounts(accountRows)
      setCollections(collectionRows)
      setBranchName(branch.name)
      setLoanProducts(products)
      setAccountGroups(groups)
      setLedgers(ledgerRows)
    } catch (reason) {
      setError(errorMessage(reason))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh])

  useEffect(() => {
    const syncLocation = () => {
      setPage(pageFromLocation())
      if (pageFromLocation() === 'reports') setReportType(reportFromLocation())
    }
    window.addEventListener('hashchange', syncLocation)
    return () => window.removeEventListener('hashchange', syncLocation)
  }, [])

  const generateReport = useCallback(async () => {
    setReportLoading(true)
    setError('')
    try {
      const query = new URLSearchParams({ from: dateFrom, to: dateTo, branch: branchName })
      setReport(await api.get<ReportData>(`/reports/${reportType}?${query.toString()}`))
    } catch (reason) {
      setError(errorMessage(reason))
      setReport(null)
    } finally {
      setReportLoading(false)
    }
  }, [branchName, dateFrom, dateTo, reportType])

  useEffect(() => {
    if (page === 'reports') void generateReport()
  }, [page, generateReport])

  const filteredMembers = useMemo(() => {
    const query = memberSearch.trim().toLowerCase()
    if (!query) return members
    return members.filter((member) =>
      [member.fullName, member.memberNo, member.phone, member.village, member.groupName]
        .some((value) => value.toLowerCase().includes(query)),
    )
  }, [memberSearch, members])

  async function submitForm(event: FormEvent<HTMLFormElement>, endpoint: string, successMessage: string, onSuccess?: () => void) {
    event.preventDefault()
    const formData = new FormData(event.currentTarget)
    const numericFields = new Set(['memberId', 'amount', 'tenureMonths', 'monthlyIncome'])
    const payload = Object.fromEntries(
      Array.from(formData.entries()).map(([key, value]) => [
        key,
        numericFields.has(key) ? (value === '' ? null : Number(value)) : value,
      ]),
    )
    if (endpoint === '/collections') payload.idempotencyKey = crypto.randomUUID()
    try {
      await api.post(endpoint, payload)
      setModal(null)
      setToast(successMessage)
      onSuccess?.()
      await refresh()
    } catch (reason) {
      setError(errorMessage(reason))
    }
  }

  async function decide(applicationNo: string, approved: boolean) {
    try {
      await api.post(`/loans/${encodeURIComponent(applicationNo)}/decision`, { approved })
      setToast(approved ? 'Application approved.' : 'Application declined.')
      await refresh()
    } catch (reason) {
      setError(errorMessage(reason))
    }
  }

  async function disburse(applicationNo: string) {
    try {
      await api.post(`/loans/${encodeURIComponent(applicationNo)}/disburse`, {
        disbursementDate: today,
        firstInstallmentDate: nextMonthDate(today),
        mode: 'Cash',
        reference: '',
      })
      setToast('Loan disbursed and added to the active portfolio.')
      await refresh()
    } catch (reason) {
      setError(errorMessage(reason))
    }
  }

  function showPage(id: PageId) {
    setPage(id)
    if (pageFromLocation() !== id) window.location.hash = `/${id}`
    setMobileNavOpen(false)
    setError('')
  }

  function showReport(type: string) {
    setReportType(type)
    setPage('reports')
    window.location.hash = `/reports/${type}`
    setMobileNavOpen(false)
    setError('')
  }

  function exportReport() {
    if (!report) return
    const lines = [
      report.columns.map((column) => csvCell(column.label)).join(','),
      ...report.rows.map((row) => report.columns.map((column) => csvCell(row[column.key] ?? '')).join(',')),
    ]
    const url = URL.createObjectURL(new Blob([lines.join('\r\n')], { type: 'text/csv;charset=utf-8' }))
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = `${report.type}-${report.dateFrom}-to-${report.dateTo}.csv`
    anchor.click()
    URL.revokeObjectURL(url)
  }

  return (
    <div className="app-shell">
      <aside className={`sidebar ${mobileNavOpen ? 'sidebar-open' : ''}`}>
        <div className="brand-lockup">
          <div className="brand-mark"><span>D</span><i /></div>
          <div className="brand-text"><strong>dhan jagrati</strong><small>MICROFINANCE LTD.</small></div>
          <button className="icon-button sidebar-close" aria-label="Close menu" onClick={() => setMobileNavOpen(false)}><X size={18} /></button>
        </div>

        <button className="branch-switcher" type="button">
          <span className="branch-icon"><Landmark size={17} /></span>
          <span className="branch-copy"><small>WORKING BRANCH</small><strong>{branchName || 'Loading branch...'}</strong></span>
          <ChevronDown size={15} />
        </button>

        <nav className="main-nav" aria-label="Main navigation">
          {navigation.map((group) => (
            <div className="nav-group" key={group.label}>
              <span className="nav-label">{group.label}</span>
              {group.links.map(({ id, title, icon: Icon }) => (
                <button className={`nav-link ${page === id ? 'active' : ''}`} key={id} aria-current={page === id ? 'page' : undefined} onClick={() => showPage(id)}>
                  <Icon size={18} strokeWidth={1.8} /><span>{title}</span>
                  {id === 'collections' && dashboard?.overdueAccounts ? <span className="nav-count">{dashboard.overdueAccounts}</span> : null}
                </button>
              ))}
            </div>
          ))}
        </nav>

        <div className="sidebar-bottom">
          <div className="data-mode"><span className="status-dot" /><span><b>SQL Server</b><small>Connected workspace</small></span></div>
          <div className="sidebar-user">
            <div className="avatar avatar-sand">BA</div>
            <div className="user-copy"><strong>Branch admin</strong><small>Operations team</small></div>
            <MoreHorizontal size={19} />
          </div>
        </div>
      </aside>

      {mobileNavOpen && <button className="mobile-backdrop" aria-label="Close navigation" onClick={() => setMobileNavOpen(false)} />}

      <div className="main-area">
        <header className="topbar">
          <button className="icon-button mobile-menu" aria-label="Open menu" onClick={() => setMobileNavOpen(true)}><Menu size={20} /></button>
          <div className="breadcrumbs"><span>Workspace</span><ChevronRight size={14} /><strong>{pageCopy[page].title}</strong></div>
          <div className="topbar-actions">
            <div className="global-search"><Search size={16} /><input placeholder="Search anything..." aria-label="Search anything" /><kbd>⌘ K</kbd></div>
            <button className="icon-button notification-button" aria-label="Notifications"><Bell size={18} /><i /></button>
            <div className="topbar-avatar avatar avatar-rose">BA</div>
          </div>
        </header>

        <main className="content-area">
          <div className="page-heading">
            <div>
              <div className="eyebrow">{pageCopy[page].eyebrow}</div>
              <h1>{pageCopy[page].title}</h1>
              <p>{pageCopy[page].description}</p>
            </div>
            <div className="heading-actions">
              <button className="button button-quiet" onClick={() => void refresh()} disabled={loading}><RefreshCw size={15} className={loading ? 'spin' : ''} /> Refresh</button>
              {page === 'members' && <button className="button button-primary" onClick={() => setModal('member')}><Plus size={16} /> Add member</button>}
              {page === 'loans' && <button className="button button-primary" onClick={() => setModal('loan')}><Plus size={16} /> New application</button>}
            </div>
          </div>

          {error && <div className="alert alert-error" role="alert"><CircleAlert size={18} /><span>{error}</span><button className="icon-button" aria-label="Dismiss error" onClick={() => setError('')}><X size={16} /></button></div>}
          {toast && <div className="toast-message"><CircleCheck size={17} /><span>{toast}</span><button className="icon-button" aria-label="Dismiss message" onClick={() => setToast('')}><X size={15} /></button></div>}

          {loading && !dashboard ? <LoadingState /> : (
            <>
              {page === 'overview' && dashboard && <Overview dashboard={dashboard} collections={collections} openPage={showPage} />}
              {page === 'members' && <MembersPage members={filteredMembers} search={memberSearch} setSearch={setMemberSearch} openAdd={() => setModal('member')} />}
              {page === 'loans' && <LoansPage applications={applications} accounts={loanAccounts} activeTab={loanTab} setActiveTab={setLoanTab} decide={decide} disburse={disburse} openAdd={() => setModal('loan')} />}
              {page === 'collections' && <CollectionsPage accounts={loanAccounts} collections={collections} onSubmit={(event) => void submitForm(event, '/collections', 'Collection receipt recorded.')} />}
              {page === 'accounting' && <AccountingPage ledgers={ledgers} groups={accountGroups} onSubmit={(event) => void submitForm(event, '/accounting/vouchers', 'Balanced voucher posted.')} onSaved={() => void refresh()} openReports={() => showPage('reports')} />}
              {page === 'reports' && <ReportsPage report={report} reportType={reportType} setReportType={showReport} dateFrom={dateFrom} setDateFrom={setDateFrom} dateTo={dateTo} setDateTo={setDateTo} reportLoading={reportLoading} generate={generateReport} exportReport={exportReport} />}
            </>
          )}
        </main>
      </div>

      {modal === 'member' && <Modal title="Add a member" description={`Create a member profile for ${branchName || 'this branch'}.`} close={() => setModal(null)}>
        <form className="form-grid" onSubmit={(event) => void submitForm(event, '/members', 'Member profile created.')}>
          <Field label="Full name" required><input name="fullName" placeholder="e.g. Anjali Devi" required /></Field>
          <Field label="Primary phone" required><input name="phone" placeholder="+91 98xxx xxxxx" required /></Field>
          <Field label="Date of birth" required><input name="dateOfBirth" type="date" required /></Field>
          <Field label="Guardian / spouse"><input name="guardianName" placeholder="Name of guardian" /></Field>
          <Field label="Village"><input name="village" placeholder="Village or locality" /></Field>
          <Field label="Self-help group"><input name="groupName" placeholder="Group name" /></Field>
          <Field label="Monthly household income"><input name="monthlyIncome" type="number" min="0" placeholder="₹ 0.00" /></Field>
          <Field label="KYC status"><select name="kycStatus"><option>Pending</option><option>Verified</option></select></Field>
          <div className="form-footer"><span>Member number is assigned automatically.</span><button className="button button-primary" type="submit"><Check size={16} /> Save member</button></div>
        </form>
      </Modal>}

      {modal === 'loan' && <Modal title="New loan application" description="Capture the requested facility and its purpose." close={() => setModal(null)}>
        <form className="form-grid" onSubmit={(event) => void submitForm(event, '/loans', 'Loan application submitted.')}>
          <Field label="Member" required><select name="memberId" required defaultValue=""><option value="" disabled>Select a member</option>{members.map((member) => <option value={member.id} key={member.id}>{member.fullName} · {member.memberNo}</option>)}</select></Field>
          <Field label="Loan product" required><select name="product" required defaultValue=""><option value="" disabled>Select a monthly product</option>{loanProducts.map((product) => <option value={product.name} key={product.id}>{product.name}</option>)}</select></Field>
          <Field label="Requested amount" required><input name="amount" type="number" min={loanProducts[0]?.minAmount ?? 1} max={loanProducts[0]?.maxAmount} step="1000" placeholder="₹ 50,000" required /></Field>
          <Field label="Repayment term (months)" required><input name="tenureMonths" type="number" min={loanProducts[0]?.minInstallments ?? 1} max={loanProducts[0]?.maxInstallments} defaultValue={loanProducts[0]?.minInstallments ?? 12} required /></Field>
          <Field label="Purpose" className="field-wide"><textarea name="purpose" rows={3} placeholder="How will the loan help the member?" /></Field>
          <div className="form-footer"><span>Submission enters the review queue.</span><button className="button button-primary" type="submit"><Check size={16} /> Submit application</button></div>
        </form>
      </Modal>}
    </div>
  )
}

function Overview({ dashboard, collections, openPage }: { dashboard: DashboardSummary; collections: Collection[]; openPage: (page: PageId) => void }) {
  const targetPercent = dashboard.collectionTarget ? Math.min(100, Math.round((dashboard.collectionAchieved / dashboard.collectionTarget) * 100)) : 0
  const maxTrend = Math.max(...dashboard.collectionTrend.map((day) => day.amount), 1)
  return (
    <div className="overview-layout">
      <div className="metric-grid">
        <MetricCard label="Active members" value={dashboard.activeMembers.toLocaleString('en-IN')} note="Registered member profiles" icon={<Users size={18} />} tone="teal" />
        <MetricCard label="Live portfolio" value={money(dashboard.portfolioOutstanding)} note="Across active accounts" icon={<HandCoins size={18} />} tone="blue" />
        <MetricCard label="Collected this month" value={money(dashboard.collectionsThisMonth)} note={`${targetPercent}% of branch target`} icon={<Wallet size={18} />} tone="gold" change="up" />
        <MetricCard label="Accounts overdue" value={dashboard.overdueAccounts.toString()} note="Needs field follow-up" icon={<Clock3 size={18} />} tone="rose" change="down" />
      </div>

      <div className="overview-columns">
        <section className="panel collection-panel">
          <div className="panel-heading">
            <div><h2>Collection pulse</h2><p>Daily receipts · last 7 days</p></div>
            <button className="button button-small button-quiet" onClick={() => openPage('collections')}>Record collection <ArrowRight size={14} /></button>
          </div>
          <div className="chart-summary"><strong>{money(dashboard.collectionsThisMonth)}</strong><span><ArrowUpRight size={15} /> <small>posted receipts this month</small></span></div>
          <div className="bar-chart" aria-label="Collection trend for the last seven days">
            {dashboard.collectionTrend.map((day, index) => (
              <div className="bar-column" key={`${day.day}-${index}`}>
                <div className="bar-value" style={{ height: `${Math.max(4, (day.amount / maxTrend) * 100)}%` }} title={money(day.amount)} />
                <span>{day.day}</span>
              </div>
            ))}
          </div>
          <div className="chart-footer"><span><i className="legend-dot" /> Daily collections</span><span>Target <b>{dashboard.collectionTarget === null ? 'Not configured' : money(dashboard.collectionTarget)}</b></span></div>
          <div className="progress-track"><span style={{ width: `${targetPercent}%` }} /></div>
        </section>

        <section className="panel quick-panel">
          <div className="panel-heading"><div><h2>Quick actions</h2><p>Pick up where you left off</p></div><Sparkles size={18} className="muted-icon" /></div>
          <button className="quick-action" onClick={() => openPage('members')}><span className="quick-icon quick-teal"><Users size={17} /></span><span><strong>Register a member</strong><small>Start a new profile</small></span><ChevronRight size={16} /></button>
          <button className="quick-action" onClick={() => openPage('loans')}><span className="quick-icon quick-blue"><HandCoins size={17} /></span><span><strong>Review applications</strong><small>Move a loan forward</small></span><ChevronRight size={16} /></button>
          <button className="quick-action" onClick={() => openPage('reports')}><span className="quick-icon quick-gold"><FileText size={17} /></span><span><strong>Open branch reports</strong><small>Accounts & portfolio</small></span><ChevronRight size={16} /></button>
          <div className="daily-note"><span className="note-star">✳</span><p><strong>A thoughtful reminder</strong><br />Every timely repayment keeps another local business moving.</p></div>
        </section>
      </div>

      <section className="panel table-panel">
        <div className="panel-heading">
          <div><h2>Today’s follow-ups</h2><p>Accounts that need a little attention</p></div>
          <button className="text-button" onClick={() => openPage('collections')}>View all collections <ArrowRight size={14} /></button>
        </div>
        <div className="table-scroll">
          <table><thead><tr><th>MEMBER</th><th>LOAN ACCOUNT</th><th>DUE DATE</th><th>INSTALLMENT</th><th>OUTSTANDING</th><th>FIELD OFFICER</th><th /></tr></thead>
            <tbody>{dashboard.dueLoans.length ? dashboard.dueLoans.map((loan) => <LoanRow loan={loan} key={loan.loanNo} />) : <EmptyTable colSpan={7} text="No overdue installments today. Nice work." />}</tbody>
          </table>
        </div>
      </section>

      <section className="panel table-panel">
        <div className="panel-heading"><div><h2>Recent receipts</h2><p>Latest repayments from this branch</p></div><button className="text-button" onClick={() => openPage('collections')}>All receipts <ArrowRight size={14} /></button></div>
        <div className="table-scroll"><table><thead><tr><th>RECEIPT</th><th>MEMBER</th><th>LOAN</th><th>DATE</th><th>MODE</th><th className="align-right">AMOUNT</th></tr></thead>
          <tbody>{collections.slice(0, 4).map((collection) => <tr key={collection.id}><td><span className="mono">{collection.receiptNo}</span></td><td><MemberCell name={collection.memberName} initials={initials(collection.memberName)} /></td><td>{collection.loanNo}</td><td>{formatDate(collection.paidOn)}</td><td><span className="payment-tag">{collection.mode}</span></td><td className="align-right amount-cell">{money(collection.amount)}</td></tr>)}</tbody>
        </table></div>
      </section>
    </div>
  )
}

function MembersPage({ members, search, setSearch, openAdd }: { members: Member[]; search: string; setSearch: (value: string) => void; openAdd: () => void }) {
  return <section className="panel table-panel">
    <div className="table-toolbar">
      <div className="inline-search"><Search size={16} /><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search member, village or ID..." aria-label="Search members" /></div>
      <div className="toolbar-actions"><span className="result-count">{members.length} members</span><button className="button button-quiet button-small"><SlidersHorizontal size={15} /> Filters</button><button className="button button-primary button-small" onClick={openAdd}><Plus size={15} /> Add member</button></div>
    </div>
    <div className="table-scroll"><table><thead><tr><th>MEMBER</th><th>MEMBER ID</th><th>PHONE</th><th>VILLAGE</th><th>GROUP</th><th>REGISTERED</th><th>KYC</th><th>STATUS</th><th /></tr></thead>
      <tbody>{members.map((member, index) => <tr key={member.id}><td><MemberCell name={member.fullName} initials={initials(member.fullName)} tint={index} /></td><td><span className="mono">{member.memberNo}</span></td><td>{member.phone}</td><td>{member.village || '—'}</td><td>{member.groupName || '—'}</td><td>{formatDate(member.joinedOn)}</td><td><StatusPill value={member.kycStatus} /></td><td><StatusPill value={member.status} /></td><td><button className="icon-button row-more" aria-label={`More options for ${member.fullName}`}><MoreHorizontal size={18} /></button></td></tr>)}
        {!members.length && <EmptyTable colSpan={9} text="No members match this search." />}
      </tbody>
    </table></div>
    <div className="table-footer"><span>Showing <strong>{members.length}</strong> members</span><div className="pagination"><button disabled>Previous</button><button className="current-page">1</button><button disabled>Next</button></div></div>
  </section>
}

function LoansPage({ applications, accounts, activeTab, setActiveTab, decide, disburse, openAdd }: { applications: LoanApplication[]; accounts: Loan[]; activeTab: 'applications' | 'active'; setActiveTab: (tab: 'applications' | 'active') => void; decide: (id: string, approved: boolean) => void; disburse: (id: string) => void; openAdd: () => void }) {
  return <div className="page-stack">
    <div className="metric-grid metric-grid-compact">
      <MetricCard label="Awaiting review" value={applications.filter((item) => item.status === 'Awaiting review').length.toString()} note="Applications in queue" icon={<Clock3 size={18} />} tone="gold" />
      <MetricCard label="Approved this month" value={applications.filter((item) => item.status === 'Approved').length.toString()} note="Ready for disbursement" icon={<CircleCheck size={18} />} tone="teal" />
      <MetricCard label="Active accounts" value={accounts.filter((item) => item.status === 'Active').length.toString()} note={money(accounts.reduce((sum, item) => sum + item.outstanding, 0)) + ' outstanding'} icon={<HandCoins size={18} />} tone="blue" />
    </div>
    <section className="panel table-panel">
      <div className="tab-toolbar"><div className="segmented-tabs"><button className={activeTab === 'applications' ? 'selected' : ''} onClick={() => setActiveTab('applications')}>Applications <span>{applications.length}</span></button><button className={activeTab === 'active' ? 'selected' : ''} onClick={() => setActiveTab('active')}>Active loans <span>{accounts.length}</span></button></div><button className="button button-primary button-small" onClick={openAdd}><Plus size={15} /> New application</button></div>
      {activeTab === 'applications' ? <div className="table-scroll"><table><thead><tr><th>APPLICATION</th><th>MEMBER</th><th>PRODUCT</th><th>REQUESTED</th><th>TENURE</th><th>APPLIED</th><th>STATUS</th><th>REVIEW</th></tr></thead>
        <tbody>{applications.map((application) => <tr key={application.applicationNo}><td><span className="mono">{application.applicationNo}</span></td><td><MemberCell name={application.memberName} initials={initials(application.memberName)} /></td><td>{application.product}</td><td className="amount-cell">{money(application.amountRequested)}</td><td>{application.tenureMonths} months</td><td>{formatDate(application.appliedOn)}</td><td><StatusPill value={application.status} /></td><td>{application.status === 'Awaiting review' ? <div className="decision-actions"><button title="Approve" aria-label={`Approve ${application.applicationNo}`} className="decision-approve" onClick={() => decide(application.applicationNo, true)}><Check size={15} /></button><button title="Decline" aria-label={`Decline ${application.applicationNo}`} className="decision-decline" onClick={() => decide(application.applicationNo, false)}><X size={15} /></button></div> : application.status === 'Approved' ? <button className="button button-quiet button-small" onClick={() => disburse(application.applicationNo)}>Disburse</button> : <span className="muted-text">Reviewed</span>}</td></tr>)}</tbody>
      </table></div> : <div className="table-scroll"><table><thead><tr><th>LOAN ACCOUNT</th><th>MEMBER</th><th>PRODUCT</th><th>SANCTIONED</th><th>OUTSTANDING</th><th>NEXT DUE</th><th>OFFICER</th><th>STATUS</th></tr></thead>
        <tbody>{accounts.map((loan) => <tr key={loan.loanNo}><td><span className="mono">{loan.loanNo}</span></td><td><MemberCell name={loan.memberName} initials={initials(loan.memberName)} /></td><td>{loan.product}</td><td>{money(loan.sanctionedAmount)}</td><td className="amount-cell">{money(loan.outstanding)}</td><td>{formatDate(loan.nextDue)}</td><td>{loan.relationshipManager}</td><td><StatusPill value={loan.status} /></td></tr>)}</tbody>
      </table></div>}
    </section>
  </div>
}

function CollectionsPage({ accounts, collections, onSubmit }: { accounts: Loan[]; collections: Collection[]; onSubmit: (event: FormEvent<HTMLFormElement>) => void }) {
  const [selectedLoan, setSelectedLoan] = useState(accounts[0]?.loanNo ?? '')
  const selected = accounts.find((loan) => loan.loanNo === selectedLoan)
  useEffect(() => {
    if (!accounts.some((loan) => loan.loanNo === selectedLoan)) setSelectedLoan(accounts[0]?.loanNo ?? '')
  }, [accounts, selectedLoan])
  return <div className="page-stack">
    <div className="metric-grid metric-grid-compact">
      <MetricCard label="Collected this month" value={money(collections.reduce((sum, item) => sum + item.amount, 0))} note="Across this branch" icon={<Wallet size={18} />} tone="teal" />
      <MetricCard label="Receipts issued" value={collections.length.toString()} note="Cash, UPI and transfer" icon={<FileText size={18} />} tone="blue" />
      <MetricCard label="Accounts overdue" value={accounts.filter((loan) => loan.nextDue < today).length.toString()} note="Follow-up required" icon={<Clock3 size={18} />} tone="rose" />
    </div>
    <div className="collection-layout">
      <section className="panel form-panel">
        <div className="panel-heading"><div><h2>Record repayment</h2><p>A receipt is created when you save.</p></div><span className="panel-icon panel-icon-teal"><Wallet size={18} /></span></div>
        <form className="stack-form" onSubmit={onSubmit}>
          <Field label="Loan account" required><select name="loanNo" value={selectedLoan} onChange={(event) => setSelectedLoan(event.target.value)} required>{accounts.filter((loan) => loan.status === 'Active').map((loan) => <option key={loan.loanNo} value={loan.loanNo}>{loan.loanNo} · {loan.memberName}</option>)}</select></Field>
          {selected && <div className="loan-balance"><div className="avatar avatar-teal">{initials(selected.memberName)}</div><span><strong>{selected.memberName}</strong><small>Outstanding balance</small></span><b>{money(selected.outstanding)}</b></div>}
          <div className="form-grid">
            <Field label="Amount received" required><input name="amount" type="number" min="1" max={selected?.outstanding} step="0.01" placeholder="₹ 0.00" required /></Field>
            <Field label="Payment date" required><input name="paidOn" type="date" defaultValue={today} required /></Field>
            <Field label="Payment mode" required><select name="mode"><option>Cash</option><option>UPI</option><option>Bank transfer</option><option>Cheque</option></select></Field>
            <Field label="Reference / cheque no."><input name="reference" placeholder="Optional" /></Field>
          </div>
          <div className="form-footer"><span>Payment posting updates the demo loan ledger.</span><button className="button button-primary" type="submit"><Check size={16} /> Issue receipt</button></div>
        </form>
      </section>
      <section className="panel callout-panel">
        <div className="callout-ornament">✳</div><div className="eyebrow">COLLECTION CARE</div><h2>Clear receipts.<br />Stronger relationships.</h2><p>Always verify the loan number and payment reference before issuing a receipt to a member.</p>
        <div className="callout-foot"><span>Branch cash book</span><ArrowRight size={16} /></div>
      </section>
    </div>
    <section className="panel table-panel"><div className="panel-heading"><div><h2>Recent receipts</h2><p>Latest collection entries</p></div><button className="button button-quiet button-small"><Download size={15} /> Export</button></div><div className="table-scroll"><table><thead><tr><th>RECEIPT NO.</th><th>MEMBER</th><th>LOAN ACCOUNT</th><th>DATE</th><th>MODE</th><th>REFERENCE</th><th className="align-right">AMOUNT</th></tr></thead><tbody>{collections.map((item) => <tr key={item.id}><td><span className="mono">{item.receiptNo}</span></td><td><MemberCell name={item.memberName} initials={initials(item.memberName)} /></td><td>{item.loanNo}</td><td>{formatDate(item.paidOn)}</td><td><span className="payment-tag">{item.mode}</span></td><td>{item.reference || '—'}</td><td className="align-right amount-cell">{money(item.amount)}</td></tr>)}</tbody></table></div></section>
  </div>
}

function AccountingPage({ ledgers, groups, onSubmit, onSaved, openReports }: { ledgers: LedgerOption[]; groups: AccountGroupOption[]; onSubmit: (event: FormEvent<HTMLFormElement>) => void; onSaved: () => void; openReports: () => void }) {
  const [ledgerMode, setLedgerMode] = useState<'ledger' | 'group'>('ledger')
  const [ledgerError, setLedgerError] = useState('')

  async function createLedger(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setLedgerError('')
    const form = new FormData(event.currentTarget)
    try {
      await api.post('/accounting/ledgers', {
        code: form.get('code'), name: form.get('name'), ledgerType: form.get('ledgerType'), accountClass: form.get('accountClass'),
        groupId: form.get('groupId') ? Number(form.get('groupId')) : null, isCash: form.get('isCash') === 'on', isBank: form.get('isBank') === 'on',
        visibleInBranch: form.get('visibleInBranch') === 'on', applySanction: form.get('applySanction') === 'on',
      })
      event.currentTarget.reset()
      onSaved()
    } catch (reason) { setLedgerError(errorMessage(reason)) }
  }

  async function createGroup(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setLedgerError('')
    const form = new FormData(event.currentTarget)
    try {
      await api.post('/accounting/groups', { code: form.get('code'), name: form.get('name'), accountClass: form.get('accountClass') })
      event.currentTarget.reset()
      onSaved()
    } catch (reason) { setLedgerError(errorMessage(reason)) }
  }

  const postingLedgers = ledgers.filter((ledger) => ledger.isActive && !ledger.isGroupLedger)
  return <div className="accounting-layout">
    <section className="panel form-panel">
      <div className="panel-heading"><div><h2>New journal voucher</h2><p>Debit and credit must balance before posting.</p></div><span className="panel-icon panel-icon-blue"><Landmark size={18} /></span></div>
      <form className="stack-form" onSubmit={onSubmit}>
        <div className="form-grid">
          <Field label="Voucher date" required><input name="voucherDate" type="date" defaultValue={today} required /></Field>
          <Field label="Payment mode" required><select name="mode"><option>Cash</option><option>Bank transfer</option><option>UPI</option><option>Cheque</option></select></Field>
          <Field label="Debit ledger" required><select name="debitLedger" required defaultValue=""><option value="" disabled>Select ledger</option>{postingLedgers.map((ledger) => <option key={ledger.id}>{ledger.name}</option>)}</select></Field>
          <Field label="Credit ledger" required><select name="creditLedger" required defaultValue=""><option value="" disabled>Select ledger</option>{postingLedgers.map((ledger) => <option key={ledger.id}>{ledger.name}</option>)}</select></Field>
          <Field label="Amount" required><input name="amount" type="number" min="0.01" step="0.01" placeholder="₹ 0.00" required /></Field>
          <Field label="Cheque / reference"><input name="reference" placeholder="Optional" /></Field>
          <Field label="Narration" className="field-wide" required><textarea name="narration" rows={3} placeholder="Briefly describe this transaction" required /></Field>
        </div>
        <div className="balance-hint"><CircleCheck size={16} /><span>Balanced entry · debit and credit post for the same amount.</span></div>
        <div className="form-footer"><span>Voucher entries are immediately reflected in reports.</span><button className="button button-primary" type="submit"><Check size={16} /> Post voucher</button></div>
      </form>
    </section>
    <div className="accounting-side">
      <section className="panel ledger-editor">
        <div className="panel-heading"><div><h2>{ledgerMode === 'ledger' ? 'Create ledger' : 'Create account group'}</h2><p>Maintain the accounts used in vouchers and reports.</p></div><span className="panel-icon panel-icon-blue"><Plus size={18} /></span></div>
        <div className="segmented-tabs ledger-tabs"><button className={ledgerMode === 'ledger' ? 'active' : ''} onClick={() => setLedgerMode('ledger')}>Ledger</button><button className={ledgerMode === 'group' ? 'active' : ''} onClick={() => setLedgerMode('group')}>Group</button></div>
        {ledgerError && <div className="inline-form-error">{ledgerError}</div>}
        {ledgerMode === 'ledger' ? <form className="compact-form" onSubmit={(event) => void createLedger(event)}>
          <Field label="Code" required><input name="code" maxLength={30} required placeholder="e.g. OFFICE_EXP" /></Field>
          <Field label="Ledger name" required><input name="name" maxLength={150} required placeholder="e.g. Office expenses A/C" /></Field>
          <Field label="Ledger type"><input name="ledgerType" placeholder="General / Bank / Cash" /></Field>
          <Field label="Account class" required><select name="accountClass" defaultValue="EXPENSE"><option value="ASSET">Asset</option><option value="LIABILITY">Liability</option><option value="EQUITY">Equity</option><option value="INCOME">Income</option><option value="EXPENSE">Expense</option></select></Field>
          <Field label="Under group"><select name="groupId" defaultValue=""><option value="">Unassigned</option>{groups.map((group) => <option value={group.id} key={group.id}>{group.name}</option>)}</select></Field>
          <div className="check-grid"><label><input name="isCash" type="checkbox" /> Cash</label><label><input name="isBank" type="checkbox" /> Bank</label><label><input name="visibleInBranch" type="checkbox" defaultChecked /> Visible in branch</label><label><input name="applySanction" type="checkbox" /> Apply sanction</label></div>
          <button className="button button-primary" type="submit"><Check size={15} /> Save ledger</button>
        </form> : <form className="compact-form" onSubmit={(event) => void createGroup(event)}>
          <Field label="Group code" required><input name="code" maxLength={30} required placeholder="e.g. CURRENT_ASSETS" /></Field>
          <Field label="Group name" required><input name="name" maxLength={120} required placeholder="e.g. Current assets" /></Field>
          <Field label="Account class" required><select name="accountClass" defaultValue="ASSET"><option value="ASSET">Asset</option><option value="LIABILITY">Liability</option><option value="EQUITY">Equity</option><option value="INCOME">Income</option><option value="EXPENSE">Expense</option></select></Field>
          <button className="button button-primary" type="submit"><Check size={15} /> Save group</button>
        </form>}
      </section>
      <section className="panel ledger-list"><div className="panel-heading"><div><h2>Ledger list</h2><p>{ledgers.length} accounts available</p></div></div><div className="ledger-list-scroll"><table><thead><tr><th>CODE</th><th>LEDGER NAME</th><th>GROUP</th><th>CLASS</th></tr></thead><tbody>{ledgers.slice(0, 8).map((ledger) => <tr key={ledger.id}><td className="mono">{ledger.code}</td><td>{ledger.name}</td><td>{ledger.groupName}</td><td>{ledger.accountClass}</td></tr>)}</tbody></table></div></section>
      <section className="panel journal-note"><div className="panel-icon panel-icon-gold"><FileText size={18} /></div><div className="eyebrow">ACCOUNTING SNAPSHOT</div><h2>One clear record,<br />from entry to report.</h2><p>Each voucher is represented as a balanced debit and credit. Use branch statements to review posted activity.</p><button className="text-button" onClick={openReports}>Explore financial reports <ArrowRight size={14} /></button></section>
      <section className="panel account-legend"><h3>Common ledgers</h3><div><span className="ledger-dot dot-blue" />Assets <small>Cash · loan portfolio</small></div><div><span className="ledger-dot dot-green" />Income <small>Interest income</small></div><div><span className="ledger-dot dot-rose" />Expenses <small>Operating expenses</small></div></section>
    </div>
  </div>
}

function ReportsPage({ report, reportType, setReportType, dateFrom, setDateFrom, dateTo, setDateTo, reportLoading, generate, exportReport }: { report: ReportData | null; reportType: string; setReportType: (type: string) => void; dateFrom: string; setDateFrom: (date: string) => void; dateTo: string; setDateTo: (date: string) => void; reportLoading: boolean; generate: () => void; exportReport: () => void }) {
  return <div className="reports-layout">
    <aside className="report-navigation panel">
      <div className="report-nav-title"><span>REPORT LIBRARY</span><button className="icon-button" aria-label="Report options"><MoreHorizontal size={17} /></button></div>
      {reportGroups.map((group) => <div className="report-group" key={group.label}><span>{group.label}</span>{group.ids.map((id) => <button key={id} className={reportType === id ? 'selected' : ''} onClick={() => setReportType(id)}><FileText size={15} /><span>{reportTitles[id]}</span>{reportType === id && <ChevronRight size={14} />}</button>)}</div>)}
      <div className="report-help"><span className="help-mark">?</span><p><strong>Need a different view?</strong><br />Reports use the selected branch and date range.</p></div>
    </aside>
    <div className="report-workspace">
      <section className="panel report-filter-panel">
        <div><div className="eyebrow">REPORT PARAMETERS</div><h2>{reportTitles[reportType]}</h2></div>
        <div className="report-filters">
          <label><span>From date</span><div className="date-input"><CalendarDays size={15} /><input type="date" value={dateFrom} onChange={(event) => setDateFrom(event.target.value)} /></div></label>
          <label><span>To date</span><div className="date-input"><CalendarDays size={15} /><input type="date" value={dateTo} onChange={(event) => setDateTo(event.target.value)} /></div></label>
          <div className="branch-filter"><span>Branch</span><strong><Landmark size={14} /> {report?.branch ?? 'Loading...'} <ChevronDown size={13} /></strong></div>
          <button className="button button-primary" onClick={generate} disabled={reportLoading}>{reportLoading ? <RefreshCw size={15} className="spin" /> : <SlidersHorizontal size={15} />} Generate</button>
        </div>
      </section>

      {reportLoading ? <div className="report-empty"><RefreshCw size={21} className="spin" /><span>Preparing your report…</span></div> : report ? <section className="report-document">
        <div className="report-document-top"><div className="report-emblem">D</div><div><strong>DHAN JAGRATI MICROFINANCE LTD.</strong><span>COMMUNITY FINANCE · CENTRAL BRANCH</span></div><div className="report-doc-meta"><small>GENERATED</small><strong>{report.generatedAt}</strong></div></div>
        <div className="report-title-block"><div className="eyebrow">BRANCH STATEMENT</div><h2>{report.title}</h2><p>{report.subtitle}</p><div className="report-period"><span><Landmark size={14} /> {report.branch}</span><i /><span><CalendarDays size={14} /> {formatDate(report.dateFrom)} — {formatDate(report.dateTo)}</span></div></div>
        <div className="report-metrics">{report.metrics.map((metric) => <div className={`report-metric ${metric.tone}`} key={metric.label}><span>{metric.label}</span><strong>{metric.value}</strong></div>)}</div>
        <div className="report-table-wrap"><table className="report-table"><thead><tr>{report.columns.map((column) => <th className={column.alignment === 'right' ? 'align-right' : ''} key={column.key}>{column.label}</th>)}</tr></thead><tbody>
          {report.rows.length ? report.rows.map((row, index) => <tr key={`${report.type}-${index}`}>{report.columns.map((column) => <td className={`${column.alignment === 'right' ? 'align-right' : ''} ${/TOTAL|SURPLUS|DEFICIT/.test(row[column.key] ?? '') ? 'emphasis-cell' : ''}`} key={column.key}>{row[column.key] || '—'}</td>)}</tr>) : <tr><td className="report-no-rows" colSpan={report.columns.length}>No records in this period.</td></tr>}
        </tbody></table></div>
        <div className="report-document-footer"><span>System generated · Dhan Jagrati Microfinance Ltd</span><span>Page 1 of 1</span></div>
      </section> : <div className="report-empty"><FileText size={24} /><span>Choose a report and generate a preview.</span></div>}

      {report && <div className="report-actions"><span><CircleCheck size={15} /> Report generated successfully</span><div><button className="button button-quiet" onClick={exportReport}><Download size={15} /> Export CSV</button><button className="button button-primary" onClick={() => window.print()}><Printer size={15} /> Print report</button></div></div>}
    </div>
  </div>
}

function MetricCard({ label, value, note, icon, tone, change }: { label: string; value: string; note: string; icon: ReactNode; tone: string; change?: 'up' | 'down' }) {
  return <div className="panel metric-card"><div className={`metric-icon metric-${tone}`}>{icon}</div><span className="metric-label">{label}</span><strong className="metric-value">{value}</strong><span className={`metric-note ${change === 'up' ? 'note-positive' : change === 'down' ? 'note-warm' : ''}`}>{change === 'up' ? <ArrowUpRight size={13} /> : change === 'down' ? <ArrowDownRight size={13} /> : null}{note}</span></div>
}

function LoanRow({ loan }: { loan: Loan }) {
  return <tr><td><MemberCell name={loan.memberName} initials={initials(loan.memberName)} /></td><td><span className="mono">{loan.loanNo}</span></td><td><span className={loan.nextDue < today ? 'due-date overdue' : 'due-date'}>{formatDate(loan.nextDue)}</span></td><td>{money(loan.installment)}</td><td className="amount-cell">{money(loan.outstanding)}</td><td>{loan.relationshipManager}</td><td><button className="icon-button row-more" aria-label={`Open ${loan.loanNo}`}><MoreHorizontal size={18} /></button></td></tr>
}

function MemberCell({ name, initials: memberInitials, tint = 0 }: { name: string; initials: string; tint?: number }) {
  return <span className="member-cell"><span className={`avatar avatar-tint-${tint % 5}`}>{memberInitials}</span><strong>{name}</strong></span>
}

function StatusPill({ value }: { value: string }) {
  const tone = value.toLowerCase().includes('active') || value.toLowerCase() === 'approved' ? 'status-success' : value.toLowerCase().includes('awaiting') || value.toLowerCase().includes('pending') ? 'status-pending' : value.toLowerCase().includes('declined') ? 'status-danger' : 'status-neutral'
  return <span className={`status-pill ${tone}`}><i />{value}</span>
}

function Field({ label, required, className = '', children }: { label: string; required?: boolean; className?: string; children: ReactNode }) {
  return <label className={`form-field ${className}`}><span>{label}{required && <i> *</i>}</span>{children}</label>
}

function Modal({ title, description, close, children }: { title: string; description: string; close: () => void; children: ReactNode }) {
  return <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) close() }}><section className="modal-card" role="dialog" aria-modal="true" aria-labelledby="modal-title"><div className="modal-header"><div><span className="eyebrow">CENTRAL BRANCH</span><h2 id="modal-title">{title}</h2><p>{description}</p></div><button className="icon-button" aria-label="Close dialog" onClick={close}><X size={19} /></button></div>{children}</section></div>
}

function LoadingState() {
  return <div className="loading-state"><RefreshCw size={20} className="spin" /><span>Connecting to the branch workspace…</span></div>
}

function EmptyTable({ colSpan, text }: { colSpan: number; text: string }) {
  return <tr><td className="empty-table" colSpan={colSpan}>{text}</td></tr>
}

function errorMessage(reason: unknown) {
  return reason instanceof Error ? reason.message : 'Something went wrong. Please try again.'
}

function money(amount: number) {
  return new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 0 }).format(amount)
}

function formatDate(value: string) {
  if (!value) return '—'
  const parsed = new Date(`${value.slice(0, 10)}T00:00:00`)
  return Number.isNaN(parsed.getTime()) ? value : new Intl.DateTimeFormat('en-IN', { day: '2-digit', month: 'short', year: 'numeric' }).format(parsed)
}

function initials(name: string) {
  return name.split(/\s+/).slice(0, 2).map((part) => part.charAt(0)).join('').toUpperCase()
}

function nextMonthDate(value: string) {
  const date = new Date(`${value}T00:00:00`)
  date.setMonth(date.getMonth() + 1)
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

function csvCell(value: string) {
  return `"${value.replaceAll('"', '""')}"`
}

export default App
