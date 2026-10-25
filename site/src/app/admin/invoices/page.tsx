'use client';

import { useEffect, useRef, useState } from 'react';
import { Badge, Button, Checkbox, Group, MultiSelect, NumberInput, Paper, Select, Stack, Table, Text, TextInput, Title } from '@mantine/core';
import { adminCompanies, adminShifts, type Company, type Shift } from '@/lib/adminApi';
import { invoiceBillingApi, type BillingAddress, type InvoiceIssuer, type MonthlyOverride, type ShiftInvoice, type ShiftInvoiceInput } from '@/lib/invoiceBillingApi';

const emptyAddress: BillingAddress = { street: '', postalCode: '', city: '', country: 'FI' };
const money = (value: number) => new Intl.NumberFormat('en', { style: 'currency', currency: 'EUR' }).format(value);

export default function InvoicesPage() {
  const [companies, setCompanies] = useState<Company[]>([]); const [shifts, setShifts] = useState<Shift[]>([]);
  const [invoices, setInvoices] = useState<ShiftInvoice[]>([]); const [issuer, setIssuer] = useState<InvoiceIssuer | null>(null);
  const [companyId, setCompanyId] = useState<string | null>(null); const [shiftIds, setShiftIds] = useState<string[]>([]);
  const [from, setFrom] = useState(''); const [to, setTo] = useState(''); const [language, setLanguage] = useState('en');
  const [address, setAddress] = useState<BillingAddress>(emptyAddress); const [monthlyOverrides, setMonthlyOverrides] = useState<MonthlyOverride[]>([]);
  const [overrideShift, setOverrideShift] = useState<string | null>(null); const [overrideMonth, setOverrideMonth] = useState(''); const [overrideAmount, setOverrideAmount] = useState<number | string>(0); const [overrideReason, setOverrideReason] = useState('');
  const [preview, setPreview] = useState<ShiftInvoice | null>(null); const [previewInput, setPreviewInput] = useState<ShiftInvoiceInput | null>(null);
  const [error, setError] = useState(''); const [status, setStatus] = useState(''); const [busy, setBusy] = useState(false); const [showIssuer, setShowIssuer] = useState(false);
  const [loadAttempt, setLoadAttempt] = useState(0); const [loadFailed, setLoadFailed] = useState(false);
  const version = useRef(0); const pending = useRef(false);
  const invalidate = () => { version.current++; setPreview(null); setPreviewInput(null); setStatus(''); };
  const loadInvoices = () => invoiceBillingApi.list().then(setInvoices);
  useEffect(() => {
    let active = true; setLoadFailed(false); setError('');
    Promise.all([adminCompanies.listAll(), adminShifts.list(undefined, undefined, true), invoiceBillingApi.issuer(), invoiceBillingApi.list()])
      .then(([companyList, shiftList, value, records]) => { if (!active) return; if (!companyList || !shiftList) throw new Error('Could not load companies or shifts. Refresh to retry.'); setCompanies(companyList); setShifts(shiftList); setIssuer(value); setInvoices(records); })
      .catch(failure => { if (active) { setError(failure.message); setLoadFailed(true); } });
    return () => { active = false; version.current++; };
  }, [loadAttempt]);
  const run = async (action: () => Promise<void>) => {
    if (pending.current) return; pending.current = true; setBusy(true); setError(''); setStatus('');
    try { await action(); } catch (failure) { setError(failure instanceof Error ? failure.message : 'Request failed. Please retry.'); }
    finally { pending.current = false; setBusy(false); }
  };
  const buildPreview = () => run(async () => {
    if (!companyId || !shiftIds.length || !from || !to || from > to || !address.street.trim() || !address.postalCode.trim() || !address.city.trim() || !address.country.trim()) throw new Error('Select a company and shifts, an ordered billing date range, and the full billing address.');
    const input = { companyId, shiftIds: [...shiftIds], from, to, billingAddress: { ...address }, language, monthlyOverrides: [...monthlyOverrides] };
    const attempt = ++version.current; setPreview(null); setPreviewInput(null);
    const result = await invoiceBillingApi.preview(input); if (attempt !== version.current) return;
    setPreview(result); setPreviewInput(input);
  });
  const issue = () => run(async () => {
    if (!preview?.previewToken || !previewInput) throw new Error('Preview the invoice before issuing it.');
    const result = await invoiceBillingApi.issue(previewInput, preview.previewToken);
    invalidate(); await loadInvoices(); setStatus(`Invoice ${result.invoiceNumber} issued.`);
  });
  const patchAddress = (key: keyof BillingAddress, value: string) => { invalidate(); setAddress(current => ({ ...current, [key]: value })); };
  const patchIssuer = <K extends keyof InvoiceIssuer>(key: K, value: InvoiceIssuer[K]) => { invalidate(); setIssuer(current => current && ({ ...current, [key]: value })); };
  return <Stack>
    <Group justify="space-between"><Title order={1}>Invoices</Title><Button variant="default" onClick={() => setShowIssuer(!showIssuer)}>Company and payment details</Button></Group>
    {error && <Text role="alert" c="red">{error}</Text>}{loadFailed && <Button variant="light" onClick={() => setLoadAttempt(current => current + 1)}>Retry loading invoice data</Button>}{status && <Text role="status" c="green">{status}</Text>}
    {showIssuer && issuer && <Paper withBorder p="md"><Stack><Title order={2}>Invoice issuer</Title><Text size="sm">These details are saved in each issued invoice. Later edits do not change existing invoices.</Text>
      {(['companyName', 'businessId', 'email', 'phone', 'bankAccountIBAN', 'bankBic'] as const).map(key => <TextInput key={key} label={{ companyName: 'Issuer company name', businessId: 'Business ID', email: 'Issuer email', phone: 'Issuer phone', bankAccountIBAN: 'IBAN', bankBic: 'BIC' }[key]} value={issuer[key]} onChange={event => patchIssuer(key, event.target.value)} />)}
      {(['street', 'postalCode', 'city', 'country'] as const).map(key => <TextInput key={key} label={`Issuer ${key}`} value={issuer.companyAddress[key] ?? ''} onChange={event => patchIssuer('companyAddress', { ...issuer.companyAddress, [key]: event.target.value })} />)}
      <Checkbox label="Sample issuer details" checked={issuer.isSampleData ?? false} onChange={event => patchIssuer("isSampleData", event.currentTarget.checked)} />
      <NumberInput label="Payment terms (days)" min={0} max={365} value={issuer.paymentTermsDays} onChange={value => patchIssuer('paymentTermsDays', Number(value))} />
      <NumberInput label="Default VAT (%)" min={0} max={50} decimalScale={2} value={issuer.defaultVatRatePercent} onChange={value => patchIssuer('defaultVatRatePercent', Number(value))} />
      <Button loading={busy} onClick={() => run(async () => { const value = await invoiceBillingApi.saveIssuer(issuer); if (value) setIssuer(value); setStatus('Company and payment details saved.'); })}>Save company details</Button>
    </Stack></Paper>}
    <Paper withBorder p="md"><Stack><Title order={2}>Create shift invoice</Title>
      <Select label="Company" searchable data={companies.map(company => ({ value: company.id, label: company.name }))} value={companyId} onChange={value => { invalidate(); setCompanyId(value); setShiftIds([]); setMonthlyOverrides([]); }} />
      <MultiSelect label="Invoice shifts" searchable value={shiftIds} onChange={value => { invalidate(); setShiftIds(value); setMonthlyOverrides(current => current.filter(item => value.includes(item.shiftId))); }} data={shifts.filter(shift => shift.companyId === companyId).map(shift => ({ value: shift.id, label: `${shift.name}${shift.isActive ? '' : ' (inactive)'}` }))} />
      <Group className="admin-invoice-fields" grow preventGrowOverflow={false}><TextInput type="date" label="Invoice from" value={from} onChange={event => { invalidate(); setFrom(event.target.value); }} /><TextInput type="date" label="Invoice to (inclusive)" value={to} onChange={event => { invalidate(); setTo(event.target.value); }} /></Group>
      <Select label="PDF language" allowDeselect={false} value={language} onChange={value => { invalidate(); setLanguage(value || 'en'); }} data={[{ value: 'en', label: 'English' }, { value: 'fi', label: 'Finnish' }, { value: 'sv', label: 'Swedish' }]} />
      <Text fw={600}>Customer billing address</Text><Group className="admin-invoice-fields" grow preventGrowOverflow={false}>{(['street', 'postalCode', 'city', 'country'] as const).map(key => <TextInput key={key} label={`Billing ${key}`} required value={address[key]} onChange={event => patchAddress(key, event.target.value)} />)}</Group>
      <Text size="sm">Monthly fees are prorated for the covered calendar days. Hourly charges use scheduled hours; on-call visits are charged when completed. Review all amounts before issuing.</Text>
      <details><summary>Override a monthly subtotal</summary><Stack mt="sm"><Text size="sm">An override replaces the selected shift’s whole net subtotal for one month. Enter the full monthly fee here if agreed, with a reason.</Text>
        <Select label="Override shift" data={shifts.filter(shift => shiftIds.includes(shift.id)).map(shift => ({ value: shift.id, label: shift.name }))} value={overrideShift} onChange={setOverrideShift} />
        <TextInput type="month" label="Override month" value={overrideMonth} onChange={event => setOverrideMonth(event.target.value)} /><NumberInput label="Monthly subtotal before VAT (€)" min={0} decimalScale={2} value={overrideAmount} onChange={setOverrideAmount} /><TextInput label="Monthly override reason" value={overrideReason} onChange={event => setOverrideReason(event.target.value)} maxLength={500} />
        <Button variant="light" disabled={!overrideShift || !overrideMonth || !overrideReason.trim() || overrideAmount === ''} onClick={() => { invalidate(); setMonthlyOverrides(current => [...current.filter(item => item.shiftId !== overrideShift || item.month !== overrideMonth), { shiftId: overrideShift!, month: overrideMonth, amountNet: Number(overrideAmount), reason: overrideReason.trim() }]); }}>Apply monthly override</Button>
        {monthlyOverrides.map(item => <Group key={`${item.shiftId}-${item.month}`}><Text>{shifts.find(shift => shift.id === item.shiftId)?.name} · {item.month} · {money(item.amountNet)} · {item.reason}</Text><Button size="xs" variant="subtle" onClick={() => { invalidate(); setMonthlyOverrides(current => current.filter(value => value !== item)); }}>Remove override</Button></Group>)}
      </Stack></details>
      <Button loading={busy} disabled={!issuer || !shiftIds.length} onClick={buildPreview}>Preview invoice</Button>
    </Stack></Paper>
    {preview && <Paper withBorder p="md"><Stack><Title order={2}>Invoice preview</Title>{preview.issuerIsSample && <Badge color="orange">Sample issuer details</Badge>}<Text fw={600}>{preview.issuer.companyName} · {preview.issuer.businessId}</Text><Text size="sm">{preview.issuer.companyAddress.street}, {preview.issuer.companyAddress.postalCode} {preview.issuer.companyAddress.city} · IBAN {preview.issuer.bankAccountIBAN} · BIC {preview.issuer.bankBic}</Text><Text>{preview.customer.name} · {preview.periodFrom?.slice(0, 10) ?? from} – {preview.periodTo?.slice(0, 10) ?? to}</Text>
      <Table.ScrollContainer minWidth={850}><Table><Table.Thead><Table.Tr>{['Shift / branch', 'Period', 'Scheduled hours', 'Occurrences', 'Basis / quantity', 'Net', 'VAT', 'Total'].map(label => <Table.Th key={label}>{label}</Table.Th>)}</Table.Tr></Table.Thead><Table.Tbody>{preview.lines.map((line, index) => <Table.Tr key={line.lineKey || index}><Table.Td>{line.shiftName || line.description}<Text size="xs">{line.branchName}</Text>{line.overrideReason && <Text size="xs">Override: {line.overrideReason}</Text>}</Table.Td><Table.Td>{line.periodFrom?.slice(0, 10)} – {line.periodTo?.slice(0, 10)}</Table.Td><Table.Td>{line.hours == null ? '—' : new Intl.NumberFormat('en', { maximumFractionDigits: 2 }).format(line.hours)}</Table.Td><Table.Td>{line.occurrences ?? '—'}</Table.Td><Table.Td>{line.pricingMode} × {new Intl.NumberFormat('en', { maximumFractionDigits: 4 }).format(line.quantity)}<Text size="xs">{line.description}</Text></Table.Td><Table.Td>{money(line.netAmount)}</Table.Td><Table.Td>{money(line.vatAmount)} ({line.vatRatePercent}%)</Table.Td><Table.Td>{money(line.total)}</Table.Td></Table.Tr>)}</Table.Tbody></Table></Table.ScrollContainer>
      <Text fw={700}>Net {money(preview.total.net)} · VAT {money(preview.total.vat)} · Total {money(preview.total.gross)}</Text><Text size="sm">Issuing saves an immutable invoice. Check dates, address and totals first.</Text><Button loading={busy} disabled={!preview.previewToken} onClick={issue}>Issue invoice</Button>
    </Stack></Paper>}
    <Title order={2}>Issued invoices</Title><Table.ScrollContainer minWidth={680}><Table><Table.Thead><Table.Tr><Table.Th>Invoice</Table.Th><Table.Th>Customer</Table.Th><Table.Th>Issued / due</Table.Th><Table.Th>Total</Table.Th><Table.Th>Status</Table.Th><Table.Th>Actions</Table.Th></Table.Tr></Table.Thead><Table.Tbody>{invoices.map(invoice => <Table.Tr key={invoice.id}><Table.Td>{invoice.invoiceNumber}{invoice.issuerIsSample && <Badge color="orange" size="xs">Sample issuer details</Badge>}</Table.Td><Table.Td>{invoice.customer.name}</Table.Td><Table.Td>{invoice.issueDate.slice(0, 10)} / {invoice.dueDate.slice(0, 10)}</Table.Td><Table.Td>{money(invoice.total.gross)}</Table.Td><Table.Td>{invoice.status}</Table.Td><Table.Td><Group gap="xs"><Button size="xs" variant="light" disabled={busy} onClick={() => run(() => invoiceBillingApi.download(invoice.id))}>PDF</Button>{invoice.status === 'Issued' && <><Button size="xs" variant="light" disabled={busy} onClick={() => run(async () => { await invoiceBillingApi.markPaid(invoice.id); await loadInvoices(); setStatus('Invoice marked paid.'); })}>Mark paid</Button><Button size="xs" variant="subtle" color="red" disabled={busy} onClick={() => { if (window.confirm(`Void invoice ${invoice.invoiceNumber}?`)) void run(async () => { await invoiceBillingApi.void(invoice.id); await loadInvoices(); setStatus('Invoice voided.'); }); }}>Void</Button></>}</Group></Table.Td></Table.Tr>)}</Table.Tbody></Table></Table.ScrollContainer>{!invoices.length && <Text c="dimmed">No invoices yet.</Text>}
  </Stack>;
}
