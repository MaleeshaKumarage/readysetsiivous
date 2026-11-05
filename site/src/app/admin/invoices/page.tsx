'use client';

import { showError } from '@/lib/toast';
import { Fragment, useEffect, useMemo, useRef, useState } from 'react';
import { Badge, Button, Checkbox, Group, MultiSelect, NumberInput, Paper, Select, Stack, Table, Text, TextInput, Title } from '@mantine/core';
import { adminCompanies, adminShifts, type Company, type Shift } from '@/lib/adminApi';
import { invoiceBillingApi, InvoiceRequestError, type BillingAddress, type InvoiceIssuer, type MonthlyOverride, type ShiftInvoice, type ShiftInvoiceInput, type ManualInvoiceInput } from '@/lib/invoiceBillingApi';
import ManualInvoiceRows, { newInvoiceRow, type EditableInvoiceRow } from './ManualInvoiceRows';

const emptyAddress: BillingAddress = { street: '', postalCode: '', city: '', country: 'FI' };
const money = (value: number) => new Intl.NumberFormat('en', { style: 'currency', currency: 'EUR' }).format(value);
// Completion month is chosen from fixed YYYY-MM values, so the billing period can never be partial or free text.
// The list runs from the current month back to the company's earliest shift start (at least 36 months),
// so backlogged visits stay invoiceable. It is built at render time so the window never goes stale.
const monthLabel = new Intl.DateTimeFormat('en', { month: 'long', year: 'numeric', timeZone: 'UTC' });
const MIN_MONTH_OPTIONS = 36;
function completionMonthOptions(now: Date, earliest?: string | null) {
  const year = now.getUTCFullYear(), month = now.getUTCMonth();
  const match = earliest ? /^(\d{4})-(\d{2})/.exec(earliest) : null;
  const span = match ? (year - Number(match[1])) * 12 + month - (Number(match[2]) - 1) + 1 : 0;
  return Array.from({ length: Math.max(MIN_MONTH_OPTIONS, span) }, (_, index) => {
    const date = new Date(Date.UTC(year, month - index, 1));
    return { value: date.toISOString().slice(0, 7), label: monthLabel.format(date) };
  });
}

export default function InvoicesPage() {
  const [companies, setCompanies] = useState<Company[]>([]); const [shifts, setShifts] = useState<Shift[]>([]);
  const [invoices, setInvoices] = useState<ShiftInvoice[]>([]); const [issuer, setIssuer] = useState<InvoiceIssuer | null>(null);
  const [savedVat, setSavedVat] = useState(0);
  const [companyId, setCompanyId] = useState<string | null>(null); const [shiftIds, setShiftIds] = useState<string[]>([]);
  const [partnerMonth, setPartnerMonth] = useState(false); const [month, setMonth] = useState('');
  const [manual, setManual] = useState(false);
  const [rows, setRows] = useState<EditableInvoiceRow[]>([{ id: 'initial', description: '', quantity: 1, unitPriceNet: '' }]);
  const [issuing, setIssuing] = useState(false); const [uncertain, setUncertain] = useState(false);
  const [from, setFrom] = useState(''); const [to, setTo] = useState(''); const [language, setLanguage] = useState('en');
  const [address, setAddress] = useState<BillingAddress>(emptyAddress); const [monthlyOverrides, setMonthlyOverrides] = useState<MonthlyOverride[]>([]);
  const [overrideShift, setOverrideShift] = useState<string | null>(null); const [overrideMonth, setOverrideMonth] = useState(''); const [overrideAmount, setOverrideAmount] = useState<number | string>(0); const [overrideReason, setOverrideReason] = useState('');
  const [preview, setPreview] = useState<ShiftInvoice | null>(null); const [previewInput, setPreviewInput] = useState<ShiftInvoiceInput | ManualInvoiceInput | null>(null);
  const [error, setError] = useState(''); const [status, setStatus] = useState(''); const [busy, setBusy] = useState(false); const [showIssuer, setShowIssuer] = useState(false);
  const [loadAttempt, setLoadAttempt] = useState(0); const [loadFailed, setLoadFailed] = useState(false);
  const version = useRef(0); const pending = useRef(false);
  const invalidate = () => { version.current++; setPreview(null); setPreviewInput(null); setStatus(''); };
  const selectMonth = (value: string | null) => {
    invalidate();
    if (!value) { setMonth(''); setFrom(''); setTo(''); return; }
    const [year, number] = value.split('-').map(Number);
    setMonth(value); setFrom(`${value}-01`);
    setTo(`${value}-${String(new Date(Date.UTC(year, number, 0)).getUTCDate()).padStart(2, '0')}`);
  };
  const monthOptions = useMemo(() => {
    const starts = shifts.filter(shift => shift.companyId === companyId && shift.validFrom).map(shift => shift.validFrom as string).sort();
    return completionMonthOptions(new Date(), starts[0]);
  }, [shifts, companyId]);
  const loadInvoices = () => invoiceBillingApi.list().then(setInvoices);
  useEffect(() => {
    let active = true; setLoadFailed(false); setError('');
    Promise.all([adminCompanies.listAll(), adminShifts.list(undefined, undefined, true), invoiceBillingApi.issuer(), invoiceBillingApi.list()])
      .then(([companyList, shiftList, value, records]) => { if (!active) return; if (!companyList || !shiftList) throw new Error('Could not load companies or shifts. Refresh to retry.'); setCompanies(companyList); setShifts(shiftList); setIssuer(value); setSavedVat(value.defaultVatRatePercent); setInvoices(records); })
      .catch(failure => { if (active) { setError(failure.message); setLoadFailed(true); } });
    return () => { active = false; version.current++; };
  }, [loadAttempt]);
  const run = async (action: () => Promise<void>) => {
    if (pending.current) return; pending.current = true; setBusy(true); setError(''); setStatus('');
    try { await action(); } catch (failure) {
      const message = failure instanceof Error ? failure.message : 'Request failed. Please retry.';
      setError(message); showError(message, 'Invoice action not completed');
    }
    finally { pending.current = false; setBusy(false); }
  };
  const buildPreview = () => run(async () => {
    if (manual) {
      if (!companyId || !address.street.trim() || !address.postalCode.trim() || !address.city.trim() || !address.country.trim()) throw new Error('Select a company and enter the full billing address.');
      if (rows.some(row => !row.description.trim() || row.quantity === '' || Number(row.quantity) <= 0 || row.unitPriceNet === '')) throw new Error('Enter a description, positive quantity and net unit price for every row.');
      const input: ManualInvoiceInput = { companyId, submissionId: crypto.randomUUID(), billingAddress: { ...address }, language,
        lines: rows.map(row => ({ description: row.description.trim(), quantity: Number(row.quantity), unitPriceNet: Number(row.unitPriceNet) })) };
      const attempt = ++version.current; setPreview(null); setPreviewInput(null);
      const result = await invoiceBillingApi.previewManual(input); if (attempt !== version.current) return;
      setPreview(result); setPreviewInput(input); return;
    }
    if (!companyId || (!partnerMonth && !shiftIds.length) || !from || !to || from > to || !address.street.trim() || !address.postalCode.trim() || !address.city.trim() || !address.country.trim()) throw new Error('Select a company, shifts or a partner month, and the full billing address.');
    const input = { companyId, shiftIds: partnerMonth ? [] : [...shiftIds], from, to, billingAddress: { ...address }, language, monthlyOverrides: partnerMonth ? [] : [...monthlyOverrides], ...(partnerMonth ? { partnerMonth: true } : {}) };
    const attempt = ++version.current; setPreview(null); setPreviewInput(null);
    const result = await invoiceBillingApi.preview(input); if (attempt !== version.current) return;
    setPreview(result); setPreviewInput(input);
  });
  const issue = () => run(async () => {
    if (!preview?.previewToken || !previewInput) throw new Error('Preview the invoice before issuing it.');
    const isManual = 'lines' in previewInput; setIssuing(true);
    let result: ShiftInvoice;
    try {
      result = isManual ? await invoiceBillingApi.issueManual(previewInput as ManualInvoiceInput, preview.previewToken)
        : await invoiceBillingApi.issue(previewInput as ShiftInvoiceInput, preview.previewToken);
    } catch (failure) {
      if (isManual) {
        const unknown = !(failure instanceof InvoiceRequestError) || failure.status >= 500 || failure.status === 408;
        setUncertain(unknown || (uncertain && !(failure instanceof InvoiceRequestError && failure.status === 409)));
        if (!unknown && failure instanceof InvoiceRequestError && failure.status === 409) invalidate();
      }
      throw failure;
    } finally { setIssuing(false); }
    setUncertain(false); invalidate();
    if (isManual) setRows([newInvoiceRow()]);
    setInvoices(current => [result, ...current.filter(value => value.id !== result.id)]);
    setStatus(`Invoice ${result.invoiceNumber} issued.`);
    // A failed history refresh must not invite a new issue after a confirmed write.
    try { await loadInvoices(); } catch { setError('Invoice issued, but history could not refresh. Reload the page to see the latest list.'); }
  });
  const patchAddress = (key: keyof BillingAddress, value: string) => { invalidate(); setAddress(current => ({ ...current, [key]: value })); };
  const patchIssuer = <K extends keyof InvoiceIssuer>(key: K, value: InvoiceIssuer[K]) => { invalidate(); setIssuer(current => current && ({ ...current, [key]: value })); };
  return <Stack>
    <Group justify="space-between"><Title order={1}>Invoices</Title><Button variant="default" onClick={() => setShowIssuer(!showIssuer)}>Company and payment details</Button></Group>
    {error && <Text role="alert" c="red">{error}</Text>}{loadFailed && <Button variant="light" onClick={() => setLoadAttempt(current => current + 1)}>Retry loading invoice data</Button>}{status && <Text role="status" c="green">{status}</Text>}
    {uncertain && <Text role="alert" c="orange">The issue result could not be confirmed. Retry issuing below to retrieve the same invoice safely before editing or starting another invoice.</Text>}
    <fieldset disabled={issuing || uncertain} ref={element => { if (element) element.inert = issuing || uncertain; }} style={{ border: 0, padding: 0, margin: 0, minWidth: 0 }}><Stack>
    {showIssuer && issuer && <Paper withBorder p="md"><Stack><Title order={2}>Invoice issuer</Title><Text size="sm">These details are saved in each issued invoice. Later edits do not change existing invoices.</Text>
      {(['companyName', 'businessId', 'email', 'phone', 'bankAccountIBAN', 'bankBic'] as const).map(key => <TextInput key={key} label={{ companyName: 'Issuer company name', businessId: 'Business ID', email: 'Issuer email', phone: 'Issuer phone', bankAccountIBAN: 'IBAN', bankBic: 'BIC' }[key]} value={issuer[key]} onChange={event => patchIssuer(key, event.target.value)} />)}
      {(['street', 'postalCode', 'city', 'country'] as const).map(key => <TextInput key={key} label={`Issuer ${key}`} value={issuer.companyAddress[key] ?? ''} onChange={event => patchIssuer('companyAddress', { ...issuer.companyAddress, [key]: event.target.value })} />)}
      <Checkbox label="Sample issuer details" checked={issuer.isSampleData ?? false} onChange={event => patchIssuer("isSampleData", event.currentTarget.checked)} />
      <NumberInput label="Payment terms (days)" min={0} max={365} value={issuer.paymentTermsDays} onChange={value => patchIssuer('paymentTermsDays', Number(value))} />
      <NumberInput label="Default VAT (%)" min={0} max={50} decimalScale={2} value={issuer.defaultVatRatePercent} onChange={value => patchIssuer('defaultVatRatePercent', Number(value))} />
      <Button loading={busy} onClick={() => run(async () => { const value = await invoiceBillingApi.saveIssuer(issuer); if (value) { setIssuer(value); setSavedVat(value.defaultVatRatePercent); } invalidate(); setStatus('Company and payment details saved.'); })}>Save company details</Button>
    </Stack></Paper>}
    <Paper withBorder p="md"><Stack><Title order={2}>{manual ? 'Create manual invoice' : 'Create shift invoice'}</Title>
      <Select label="Invoice type" allowDeselect={false} value={manual ? 'manual' : 'shift'} data={[{ value: 'shift', label: 'Shift invoice' }, { value: 'manual', label: 'Manual invoice' }]} onChange={value => { invalidate(); setManual(value === 'manual'); }} />
      {!manual && <Checkbox label="Completed cleaning month" checked={partnerMonth} onChange={event => { invalidate(); setPartnerMonth(event.currentTarget.checked); setFrom(''); setTo(''); setMonth(''); setShiftIds([]); setMonthlyOverrides([]); }} />}
      <Select label="Company" searchable data={companies.map(company => ({ value: company.id, label: company.name }))} value={companyId} onChange={value => { invalidate(); setCompanyId(value); setPartnerMonth(Boolean(companies.find(company => company.id === value)?.isPartnerEnabled)); setFrom(''); setTo(''); setMonth(''); setShiftIds([]); setMonthlyOverrides([]); }} />
      {!manual && !partnerMonth && <MultiSelect label="Invoice shifts" searchable value={shiftIds} onChange={value => { invalidate(); setShiftIds(value); setMonthlyOverrides(current => current.filter(item => value.includes(item.shiftId))); }} data={shifts.filter(shift => shift.companyId === companyId).map(shift => ({ value: shift.id, label: `${shift.name}${shift.isActive ? '' : ' (inactive)'}` }))} />}
      {!manual && !partnerMonth && <Group className="admin-invoice-fields" grow preventGrowOverflow={false}><TextInput type="date" label="Invoice from" value={from} onChange={event => { invalidate(); setFrom(event.target.value); }} /><TextInput type="date" label="Invoice to (inclusive)" value={to} onChange={event => { invalidate(); setTo(event.target.value); }} /></Group>}
      {!manual && partnerMonth && <><Select label="Completion month" placeholder="Choose a month" data={monthOptions} value={month || null} clearable clearButtonProps={{ 'aria-label': 'Clear completion month' }} searchable onChange={selectMonth} /><Text size="sm">Includes every completed, unbilled cleaning visit for this company finished during the selected month (actual completion date in the tenant time zone). Preview and issue remain manual; no invoice is emailed automatically.</Text></>}
      <Select label="PDF language" allowDeselect={false} value={language} onChange={value => { invalidate(); setLanguage(value || 'en'); }} data={[{ value: 'en', label: 'English' }, { value: 'fi', label: 'Finnish' }, { value: 'sv', label: 'Swedish' }]} />
      <Text fw={600}>Customer billing address</Text><Group className="admin-invoice-fields" grow preventGrowOverflow={false}>{(['street', 'postalCode', 'city', 'country'] as const).map(key => <TextInput key={key} label={`Billing ${key}`} required value={address[key]} onChange={event => patchAddress(key, event.target.value)} />)}</Group>
      {manual && <ManualInvoiceRows rows={rows} vatRate={savedVat} onChange={value => { invalidate(); setRows(value); }} />}
      {!manual && <Text size="sm">Monthly fees are prorated for the covered calendar days. Hourly charges use scheduled hours; on-call visits are charged when completed. Review all amounts before issuing.</Text>}
      {!manual && !partnerMonth && <details><summary>Override a monthly subtotal</summary><Stack mt="sm"><Text size="sm">An override replaces the selected shift’s whole net subtotal for one month. Enter the full monthly fee here if agreed, with a reason.</Text>
        <Select label="Override shift" data={shifts.filter(shift => shiftIds.includes(shift.id)).map(shift => ({ value: shift.id, label: shift.name }))} value={overrideShift} onChange={setOverrideShift} />
        <TextInput type="month" label="Override month" value={overrideMonth} onChange={event => setOverrideMonth(event.target.value)} /><NumberInput label="Monthly subtotal before VAT (€)" min={0} decimalScale={2} value={overrideAmount} onChange={setOverrideAmount} /><TextInput label="Monthly override reason" value={overrideReason} onChange={event => setOverrideReason(event.target.value)} maxLength={500} />
        <Button variant="light" disabled={!overrideShift || !overrideMonth || !overrideReason.trim() || overrideAmount === ''} onClick={() => { invalidate(); setMonthlyOverrides(current => [...current.filter(item => item.shiftId !== overrideShift || item.month !== overrideMonth), { shiftId: overrideShift!, month: overrideMonth, amountNet: Number(overrideAmount), reason: overrideReason.trim() }]); }}>Apply monthly override</Button>
        {monthlyOverrides.map(item => <Group key={`${item.shiftId}-${item.month}`}><Text>{shifts.find(shift => shift.id === item.shiftId)?.name} · {item.month} · {money(item.amountNet)} · {item.reason}</Text><Button size="xs" variant="subtle" onClick={() => { invalidate(); setMonthlyOverrides(current => current.filter(value => value !== item)); }}>Remove override</Button></Group>)}
      </Stack></details>}
      <Button loading={busy} disabled={!issuer || (!manual && ((!partnerMonth && !shiftIds.length) || (partnerMonth && !month)))} onClick={buildPreview}>Preview invoice</Button>
    </Stack></Paper>
    </Stack></fieldset>
    {preview && <Paper component="section" aria-label="Invoice preview" withBorder p="md"><Stack><Title order={2}>Invoice preview</Title>{preview.issuerIsSample && <Badge color="orange">Sample issuer details</Badge>}<Text fw={600}>{preview.issuer.companyName} · {preview.issuer.businessId}</Text><Text size="sm">{preview.issuer.companyAddress.street}, {preview.issuer.companyAddress.postalCode} {preview.issuer.companyAddress.city} · IBAN {preview.issuer.bankAccountIBAN} · BIC {preview.issuer.bankBic}</Text><Text>{preview.customer.name}{!manual && <> · {preview.periodFrom?.slice(0, 10) ?? from} – {preview.periodTo?.slice(0, 10) ?? to}</>}</Text>
      {manual ? <Table.ScrollContainer minWidth={580}><Table><Table.Thead><Table.Tr>{['Description', 'Quantity', 'Unit price (net)', 'Net', 'VAT', 'Total'].map(label => <Table.Th key={label}>{label}</Table.Th>)}</Table.Tr></Table.Thead><Table.Tbody>{preview.lines.map((line, index) => <Table.Tr key={line.lineKey || index}><Table.Td>{line.description}</Table.Td><Table.Td>{line.quantity}</Table.Td><Table.Td>{money(line.unitPriceNet)}</Table.Td><Table.Td>{money(line.netAmount)}</Table.Td><Table.Td>{money(line.vatAmount)} ({line.vatRatePercent}%)</Table.Td><Table.Td>{money(line.total)}</Table.Td></Table.Tr>)}</Table.Tbody></Table></Table.ScrollContainer> : <Table.ScrollContainer minWidth={850}><Table><Table.Thead><Table.Tr>{['Shift / branch', 'Period', 'Scheduled hours', 'Occurrences', 'Basis / quantity', 'Net', 'VAT', 'Total'].map(label => <Table.Th key={label}>{label}</Table.Th>)}</Table.Tr></Table.Thead><Table.Tbody>{[...new Map(preview.lines.map(line => [line.branchId || line.branchName || '', line.branchName || ''])).entries()].sort((a, b) => a[1].localeCompare(b[1])).map(([branchKey, branchName]) => <Fragment key={branchKey}>{branchName && <Table.Tr><Table.Th colSpan={8}>{branchName}</Table.Th></Table.Tr>}{preview.lines.filter(line => (line.branchId || line.branchName || '') === branchKey).sort((a, b) => (a.periodFrom || '').localeCompare(b.periodFrom || '')).map((line, index) => <Table.Tr key={line.lineKey || index}><Table.Td>{line.shiftName || line.description}<Text size="xs">{line.branchName}</Text>{line.completedOn && <Text size="xs">Completed on: {line.completedOn.slice(0, 10)}</Text>}{line.overrideReason && <Text size="xs">Override: {line.overrideReason}</Text>}</Table.Td><Table.Td>{line.periodFrom?.slice(0, 10)} – {line.periodTo?.slice(0, 10)}</Table.Td><Table.Td>{line.hours == null ? '—' : new Intl.NumberFormat('en', { maximumFractionDigits: 2 }).format(line.hours)}</Table.Td><Table.Td>{line.occurrences ?? '—'}</Table.Td><Table.Td>{line.pricingMode} × {new Intl.NumberFormat('en', { maximumFractionDigits: 4 }).format(line.quantity)}<Text size="xs">{line.description}</Text></Table.Td><Table.Td>{money(line.netAmount)}</Table.Td><Table.Td>{money(line.vatAmount)} ({line.vatRatePercent}%)</Table.Td><Table.Td>{money(line.total)}</Table.Td></Table.Tr>)}</Fragment>)}</Table.Tbody></Table></Table.ScrollContainer>}
      <Text fw={700}>Net {money(preview.total.net)} · VAT {money(preview.total.vat)} · Total {money(preview.total.gross)}</Text><Text size="sm">Issuing saves an immutable invoice. Check dates, address and totals first.</Text><Button loading={busy} disabled={!preview.previewToken} onClick={issue}>{uncertain ? 'Retry issuing invoice' : 'Issue invoice'}</Button>
    </Stack></Paper>}
    <Title order={2}>Issued invoices</Title><Table.ScrollContainer minWidth={680}><Table><Table.Thead><Table.Tr><Table.Th>Invoice</Table.Th><Table.Th>Customer</Table.Th><Table.Th>Issued / due</Table.Th><Table.Th>Total</Table.Th><Table.Th>Status</Table.Th><Table.Th>Actions</Table.Th></Table.Tr></Table.Thead><Table.Tbody>{invoices.map(invoice => <Table.Tr key={invoice.id}><Table.Td>{invoice.invoiceNumber}{invoice.issuerIsSample && <Badge color="orange" size="xs">Sample issuer details</Badge>}</Table.Td><Table.Td>{invoice.customer.name}</Table.Td><Table.Td>{invoice.issueDate.slice(0, 10)} / {invoice.dueDate.slice(0, 10)}</Table.Td><Table.Td>{money(invoice.total.gross)}</Table.Td><Table.Td>{invoice.status}</Table.Td><Table.Td><Group gap="xs"><Button size="xs" variant="light" disabled={busy} onClick={() => run(() => invoiceBillingApi.download(invoice.id))}>PDF</Button>{invoice.status === 'Issued' && <><Button size="xs" variant="light" disabled={busy} onClick={() => run(async () => { await invoiceBillingApi.markPaid(invoice.id); await loadInvoices(); setStatus('Invoice marked paid.'); })}>Mark paid</Button><Button size="xs" variant="subtle" color="red" disabled={busy} onClick={() => { if (window.confirm(`Void invoice ${invoice.invoiceNumber}?`)) void run(async () => { await invoiceBillingApi.void(invoice.id); await loadInvoices(); setStatus('Invoice voided.'); }); }}>Void</Button></>}</Group></Table.Td></Table.Tr>)}</Table.Tbody></Table></Table.ScrollContainer>{!invoices.length && <Text c="dimmed">No invoices yet.</Text>}
  </Stack>;
}
