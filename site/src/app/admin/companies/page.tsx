'use client';

import { showError } from '@/lib/toast';
import { Fragment, useEffect, useRef, useState } from 'react';
import {
  Title, Button, Table, Modal, TextInput, Textarea, Group, Stack, Text, Loader, Badge, Checkbox, NumberInput, Pagination, Select,
} from '@mantine/core';
import { Plus, ChevronDown, Pencil } from 'lucide-react';
import Link from 'next/link';
import { adminCompanies, adminBranches, type Company, type Branch, type CompanyRemovalSummary } from '@/lib/adminApi';
import { companyLifecycle, branchLifecycle } from '@/lib/reactivationApi';
import { LifecycleTabs, inLifecycle, type LifecycleStatus } from '@/components/LifecycleTabs';
import { QuoteRequestedBadge, useQuoteRequests } from '@/components/QuoteRequests';
import { BranchPriceHistory } from '@/components/admin/BranchPriceHistory';
import { adminQualityCycle, type QualityCycleTemplate } from '@/lib/qualityCycleApi';

const emptyForm = { businessId: '', name: '', contactName: '', contactEmail: '', contactPhone: '', notes: '', isPartnerEnabled: false, isQualityCycle: false, qualityCycleTemplateId: '' };

export default function CompaniesAdminPage() {
  const quoteRequests = useQuoteRequests();
  const [companies, setCompanies] = useState<Company[]>([]);
  const [status, setStatus] = useState<LifecycleStatus>('active');
  const [branchStatus, setBranchStatus] = useState<LifecycleStatus>('active');
  const [actionError, setActionError] = useState('');
  const [actionBusy, setActionBusy] = useState<string | null>(null);
  // Offered when a deactivate or delete is refused because of the company's shifts or places.
  const [relatedPrompt, setRelatedPrompt] = useState<{ company: Company; action: 'deactivate'; reason: string } | null>(null);
  const [removal, setRemoval] = useState<{ company: Company; summary: CompanyRemovalSummary; reason: string; branches: boolean; shifts: boolean; invoices: boolean; confirmName: string; busy: boolean; error: string } | null>(null);
  const [relatedDeleteAgreements, setRelatedDeleteAgreements] = useState(false);
  const [relatedBusy, setRelatedBusy] = useState(false);
  const [relatedError, setRelatedError] = useState('');
  const [listError, setListError] = useState('');
  const loadGeneration = useRef(0);
  const branchGeneration = useRef(0);
  const [branchLoading, setBranchLoading] = useState<string | null>(null);
  const [branchLoadError, setBranchLoadError] = useState('');
  const visibleCompanies = companies.filter(company => inLifecycle(company, status));
  const [loading, setLoading] = useState(true);
  const [ready, setReady] = useState(false);
  const [query, setQuery] = useState('');
  const [searchText, setSearchText] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [total, setTotal] = useState(0);
  const [counts, setCounts] = useState<{ all: number; active: number; inactive: number } | null>(null);
  // Set before a load-time page correction so the URL is replaced, not pushed; keeps Back from looping.
  const replaceUrlNext = useRef(false);
  const [modalOpen, setModalOpen] = useState(false);
  const [editingCompany, setEditingCompany] = useState<Company | null>(null);
  const [form, setForm] = useState(emptyForm);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [qualityTemplates, setQualityTemplates] = useState<QualityCycleTemplate[]>([]);

  const [expanded, setExpanded] = useState<string | null>(null);
  // A deep-linked company is shown above the page so its branches render whichever page it sits on.
  const [pinned, setPinned] = useState<Company | null>(null);
  const tableRows = pinned && pinned.isActive === (status === 'active') && !visibleCompanies.some(c => c.id === pinned.id)
    ? [pinned, ...visibleCompanies]
    : visibleCompanies;
  const [companyBranchesMap, setCompanyBranchesMap] = useState<Record<string, Branch[]>>({});
  const [branchModal, setBranchModal] = useState(false);
  const [editingBranch, setEditingBranch] = useState<Branch | null>(null);
  const [branchForm, setBranchForm] = useState({ name: '', street: '', postalCode: '', city: '', country: '', contactPhone: '', contactEmail: '', roomCount: '' as number | string, bedCount: '' as number | string, sizeM2: '' as number | string, cleaningType: '', priceNet: '' as number | string, vatRatePercent: 25.5 as number | string, estimatedDurationMinutes: '' as number | string, priceEffectiveFrom: '', priceMode: 'PerOccurrence' });
  const [branchSaving, setBranchSaving] = useState(false);
  const [branchError, setBranchError] = useState('');

  type ListState = { status: LifecycleStatus; page: number; pageSize: number; query: string };
  const load = async (next: ListState) => {
    const version = ++loadGeneration.current;
    setLoading(true); setListError('');
    const skip = (next.page - 1) * next.pageSize;
    const pageResult = await adminCompanies.list(next.query, skip, next.pageSize, next.status === 'inactive' ? 'inactive' : 'active').catch(() => null);
    if (version !== loadGeneration.current) return;
    if (!pageResult || !Array.isArray(pageResult.items)) {
      setListError('Companies could not be loaded. Refresh to retry.'); setLoading(false); return;
    }
    // An empty page after deletions or a shorter result set moves to the last populated page.
    const lastPage = Math.max(1, Math.ceil(pageResult.total / next.pageSize));
    if (pageResult.items.length === 0 && next.page > lastPage) { replaceUrlNext.current = true; setPage(lastPage); setLoading(false); return; }
    setCompanies(pageResult.items); setTotal(pageResult.total);
    setLoading(false);
    // Counts load independently: a slow or failed summary never holds the rows.
    void adminCompanies.summary().catch(() => null).then(summary => {
      if (version !== loadGeneration.current) return;
      setCounts(summary && Number.isInteger(summary.all) && Number.isInteger(summary.active) && Number.isInteger(summary.inactive)
        ? { all: summary.all, active: summary.active, inactive: summary.inactive } : null);
    });
  };
  const reload = () => load({ status, page, pageSize, query });

  // Filter, page and size live in the URL so Back restores the list exactly as it was.
  const applyParams = (params: URLSearchParams) => {
    setStatus(params.get('status') === 'inactive' ? 'inactive' : 'active');
    const requestedPage = Number(params.get('page'));
    setPage(Number.isInteger(requestedPage) && requestedPage > 0 ? requestedPage : 1);
    const requestedSize = Number(params.get('size'));
    setPageSize([25, 50, 100].includes(requestedSize) ? requestedSize : 25);
    const requestedQuery = params.get('q') ?? '';
    setQuery(requestedQuery); setSearchText(requestedQuery);
  };

  useEffect(() => {
    const initialParams = new URLSearchParams(window.location.search);
    requestedCompany.current = initialParams.get('company');
    applyParams(initialParams);
    replaceUrlNext.current = true;
    setReady(true);
    const onPopState = () => applyParams(new URLSearchParams(window.location.search));
    window.addEventListener('popstate', onPopState);
    return () => { window.removeEventListener('popstate', onPopState); loadGeneration.current++; branchGeneration.current++; };
  }, []);

  useEffect(() => {
    if (!ready) return;
    void load({ status, page, pageSize, query });
  }, [ready, status, page, pageSize, query]);

  useEffect(() => {
    if (!ready) return;
    const next = new URLSearchParams();
    next.set('status', status); next.set('page', String(page)); next.set('size', String(pageSize));
    if (query) next.set('q', query);
    // ?company= stays only while the pinned row is shown; dismissing it drops the param so a reload does not reopen it.
    if (pinned) next.set('company', pinned.id);
    const target = `${window.location.pathname}?${next.toString()}`;
    const replace = replaceUrlNext.current;
    replaceUrlNext.current = false;
    if (target === `${window.location.pathname}${window.location.search}`) return;
    if (replace) window.history.replaceState(null, '', target);
    else window.history.pushState(null, '', target);
  }, [ready, status, page, pageSize, query, pinned]);

  // The list state a customer workspace returns to via its Back link.
  const listQuery = new URLSearchParams({ status, page: String(page), size: String(pageSize), ...(query ? { q: query } : {}) }).toString();
  const set = (k: string) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
    setForm((f) => ({ ...f, [k]: e.target.value }));
  const setB = (k: string) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setBranchForm((f) => ({ ...f, [k]: e.target.value }));

  const submit = async () => {
    if (!form.businessId.trim() || !form.name.trim()) { setError('Business ID and name are required.'); return; }
    if (form.isQualityCycle && !form.qualityCycleTemplateId) { setError('Choose a quality cycle template.'); return; }
    setSaving(true); setError('');
    try {
      const fields = {
        businessId: form.businessId.trim(), name: form.name.trim(),
        contactName: form.contactName.trim() || undefined, contactEmail: form.contactEmail.trim() || undefined,
        contactPhone: form.contactPhone.trim() || undefined, notes: form.notes.trim() || undefined, isPartnerEnabled: form.isPartnerEnabled,
        isQualityCycleEnabled: form.isQualityCycle, qualityCycleTemplateId: form.isQualityCycle ? form.qualityCycleTemplateId : null,
      };
      if (editingCompany) await adminCompanies.update(editingCompany.id, { ...fields, isActive: editingCompany.isActive });
      else await adminCompanies.create(fields);
      setForm(emptyForm); setEditingCompany(null); setModalOpen(false); if (!editingCompany) { setStatus('active'); setPage(1); } await reload();
    } catch (failure) {
      const message = failure instanceof Error ? failure.message : 'Could not create company. Please retry.';
      setError(message); showError(message, 'Company not saved');
    } finally { setSaving(false); }
  };

  // Company-scoped templates and shared ones; the server re-checks the scope on save.
  const loadQualityTemplates = async (companyId?: string) => {
    // Company-wide templates only: a property-scoped template cannot be chosen for the whole company.
    try { setQualityTemplates(((await adminQualityCycle.listTemplates(companyId)) ?? []).filter(template => template.isActive && !template.branchId)); }
    catch { setQualityTemplates([]); }
  };

  const openCompanyEdit = (company: Company) => {
    setEditingCompany(company);
    setForm({ businessId: company.businessId, name: company.name, contactName: company.contactName ?? '', contactEmail: company.contactEmail ?? '', contactPhone: company.contactPhone ?? '', notes: company.notes ?? '', isPartnerEnabled: company.isPartnerEnabled ?? false, isQualityCycle: company.isQualityCycleEnabled ?? false, qualityCycleTemplateId: company.qualityCycleTemplateId ?? '' });
    setError(''); setModalOpen(true); void loadQualityTemplates(company.id);
  };

  const loadBranches = async (id: string) => {
    const version = ++branchGeneration.current;
    setBranchLoading(id); setBranchLoadError('');
    const d = await adminCompanies.get(id);
    if (version !== branchGeneration.current) return;
    if (d) setCompanyBranchesMap((m) => ({ ...m, [id]: d.branches }));
    else setBranchLoadError('Branches could not be loaded. Please retry.');
    setBranchLoading(null);
  };
  const toggleBranches = async (id: string) => {
    if (expanded === id) { branchGeneration.current++; setExpanded(null); setPinned(p => (p?.id === id ? null : p)); setBranchLoading(null); return; }
    setExpanded(id); setBranchStatus('active');
    await loadBranches(id);
  };

  const openedCustomer = useRef<string | null>(null);
  // The ?company= value read at mount; the URL loses it once the list state takes over.
  const requestedCompany = useRef<string | null>(null);
  useEffect(() => {
    const requested = requestedCompany.current;
    if (!ready || !requested || openedCustomer.current === requested) return;
    openedCustomer.current = requested;
    void (async () => {
      // A deleted or mistyped id, or a network error, leaves the list as it is instead of an unhandled rejection.
      const detail = await adminCompanies.get(requested).catch(() => null);
      if (!detail) return;
      setStatus(detail.company.isActive ? 'active' : 'inactive');
      setPinned(detail.company);
      setExpanded(detail.company.id); setBranchStatus('active'); void loadBranches(detail.company.id);
    })();
  }, [ready]);

  const submitBranch = async () => {
    if (!expanded || !branchForm.name.trim()) return;
    setBranchSaving(true); setBranchError('');
    try {
      const fields = {
        name: branchForm.name.trim(), street: branchForm.street.trim() || undefined,
        postalCode: branchForm.postalCode.trim() || undefined, city: branchForm.city.trim() || undefined,
        country: branchForm.country.trim() || undefined, contactPhone: branchForm.contactPhone.trim() || undefined, contactEmail: branchForm.contactEmail.trim() || undefined,
        priceEffectiveFrom: branchForm.priceEffectiveFrom || undefined, priceMode: branchForm.priceMode,
        roomCount: branchForm.roomCount === '' ? null : Number(branchForm.roomCount), bedCount: branchForm.bedCount === '' ? null : Number(branchForm.bedCount), sizeM2: branchForm.sizeM2 === '' ? null : Number(branchForm.sizeM2), cleaningType: branchForm.cleaningType || null, priceNet: branchForm.priceNet === '' ? null : Number(branchForm.priceNet), vatRatePercent: branchForm.vatRatePercent === '' ? null : Number(branchForm.vatRatePercent), estimatedDurationMinutes: branchForm.estimatedDurationMinutes === '' ? null : Number(branchForm.estimatedDurationMinutes),
      };
      if (editingBranch) {
        const updated = await adminBranches.update(editingBranch.id, { ...fields, isActive: editingBranch.isActive });
        if (!updated) throw new Error('Could not update branch. Please retry.');
      } else await adminBranches.create(expanded, fields);
      setBranchForm({ name: '', street: '', postalCode: '', city: '', country: '', contactPhone: '', contactEmail: '', roomCount: '' as number | string, bedCount: '' as number | string, sizeM2: '' as number | string, cleaningType: '', priceNet: '' as number | string, vatRatePercent: 25.5 as number | string, estimatedDurationMinutes: '' as number | string, priceEffectiveFrom: '', priceMode: 'PerOccurrence' });
      setEditingBranch(null);
      setBranchModal(false);
      if (!editingBranch) setBranchStatus('active');
      await loadBranches(expanded);
      await quoteRequests?.refresh();
    } catch (failure) {
      setBranchError(failure instanceof Error ? failure.message : 'Could not create branch. Please retry.');
    } finally { setBranchSaving(false); }
  };

  const openBranchEdit = (branch: Branch) => {
    setEditingBranch(branch);
    setBranchForm({ name: branch.name, street: branch.street ?? '', postalCode: branch.postalCode ?? '', city: branch.city ?? '', country: branch.country ?? '', contactPhone: branch.contactPhone ?? '', contactEmail: branch.contactEmail ?? '', roomCount: branch.roomCount ?? '', bedCount: branch.bedCount ?? '', sizeM2: branch.sizeM2 ?? '', cleaningType: branch.cleaningType ?? '', priceNet: branch.priceNet ?? '', vatRatePercent: branch.vatRatePercent ?? 25.5, estimatedDurationMinutes: branch.estimatedDurationMinutes ?? '', priceEffectiveFrom: '', priceMode: [...(branch.priceVersions ?? [])].sort((a, b) => a.effectiveFrom.localeCompare(b.effectiveFrom)).filter(v => v.effectiveFrom <= new Date().toISOString().slice(0, 10)).pop()?.mode ?? 'PerOccurrence' });
    setBranchError(''); setBranchModal(true);
  };

  const isConflict = (failure: unknown) => failure instanceof Error && (failure as { status?: number }).status === 409;
  // Only refusals caused by the company's own shifts or places are offered the clear option; partner or agreement refusals are not.
  const clearableReason = (message: string) => /shift|branch|place/i.test(message) && !/partner|agreement/i.test(message);

  const changeActivity = async (id: string, active: boolean, companyId?: string) => {
    setActionBusy(id); setActionError('');
    try {
      const api = companyId ? branchLifecycle : companyLifecycle;
      await (active ? api.deactivate(id) : api.reactivate(id));
      if (companyId) await loadBranches(companyId);
      else { setExpanded(null); setPinned(null); await reload(); }
    } catch (failure) {
      const message = failure instanceof Error ? failure.message : 'Could not change activity. Please retry.';
      setActionError(message);
      const company = companies.find(c => c.id === id);
      if (!companyId && active && company && isConflict(failure) && clearableReason(message)) setRelatedPrompt({ company, action: 'deactivate', reason: message });
    } finally { setActionBusy(null); }
  };

  const deleteCompany = async (company: Company) => {
    if (!window.confirm(`Permanently delete inactive company “${company.name}”? Companies with branches or business history are retained.`)) return;
    setActionBusy(company.id); setActionError('');
    try { await adminCompanies.deleteInactive(company.id); setExpanded(null); setPinned(null); await reload(); }
    catch (failure) {
      const message = failure instanceof Error ? failure.message : 'Could not delete company. Please retry.';
      setActionError(message);
      // A refused delete (places, shifts, checklists, agreements or partner records) opens the removal dialog for the company.
      if (isConflict(failure)) await openRemoval(company, message);
    }
    finally { setActionBusy(null); }
  };

  const openRemoval = async (company: Company, reason: string) => {
    try {
      const summary = await adminCompanies.removalSummary(company.id);
      if (!summary) throw new Error('Could not load what would be removed. Please retry.');
      setRemoval({ company, summary, reason, branches: false, shifts: false, invoices: false, confirmName: '', busy: false, error: '' });
    } catch (failure) {
      setActionError(failure instanceof Error ? failure.message : 'Could not load what would be removed. Please retry.');
    }
  };

  // Places need their shifts: unticking shifts also unticks places.
  const setPlaces = (checked: boolean) => setRemoval(current => current && { ...current, branches: checked, shifts: checked ? true : current.shifts });
  const setShifts = (checked: boolean) => setRemoval(current => current && { ...current, shifts: checked, branches: checked ? current.branches : false });

  const submitRemoval = async () => {
    if (!removal) return;
    const { company, summary, branches, shifts, invoices, confirmName } = removal;
    const confirmPaidInvoices = invoices && summary.paidInvoices > 0;
    if (confirmPaidInvoices && confirmName.trim() !== company.name.trim()) {
      setRemoval({ ...removal, error: `Type “${company.name}” exactly to confirm removing the paid invoices.` });
      return;
    }
    setRemoval({ ...removal, busy: true, error: '' });
    try {
      await adminCompanies.remove(company.id, { branches, shifts, invoices, confirmPaidInvoices: !!confirmPaidInvoices });
      setCompanyBranchesMap(current => { const next = { ...current }; delete next[company.id]; return next; });
      setExpanded(null); setPinned(null); setRemoval(null);
      await reload();
    } catch (failure) {
      // A removal that stopped part way has already removed some records, so the list and branch panel are reloaded either way.
      setCompanyBranchesMap(current => { const next = { ...current }; delete next[company.id]; return next; });
      setExpanded(null); setPinned(null);
      setRemoval(current => current && { ...current, busy: false, error: failure instanceof Error ? failure.message : 'Could not remove the company. Please retry.' });
      await reload();
    }
  };

  const closeRelatedPrompt = () => { setRelatedPrompt(null); setRelatedError(''); };

  // Clears the shifts, unassigns employees and removes every place, then finishes the original action.
  // Every view that showed the company's shifts or places is reset so nothing stale remains on screen.
  const clearRelatedAndContinue = async () => {
    if (!relatedPrompt) return;
    const { company, action } = relatedPrompt;
    setRelatedBusy(true); setRelatedError('');
    let clearFailure: unknown = null;
    try {
      await adminCompanies.clearRelated(company.id, relatedDeleteAgreements);
    } catch (failure) {
      clearFailure = failure;
    }
    // A clear that stopped part way has already removed some rows, so the cached branch panel and the list are stale either way.
    setCompanyBranchesMap(current => { const next = { ...current }; delete next[company.id]; return next; });
    setBranchLoadError(''); setBranchLoading(null); branchGeneration.current++;
    setExpanded(null); setPinned(null);
    if (clearFailure) {
      setRelatedError(clearFailure instanceof Error ? clearFailure.message : 'Could not clear related shifts and places. Please retry.');
      setRelatedBusy(false);
      await reload();
      return;
    }
    try {
      if (action === 'deactivate') await companyLifecycle.deactivate(company.id);
      closeRelatedPrompt();
    } catch (failure) {
      closeRelatedPrompt();
      setActionError(failure instanceof Error ? failure.message : 'Shifts and places were cleared, but the company could not be finished. Please retry.');
    } finally {
      setRelatedBusy(false);
      await reload();
    }
  };

  const deleteBranch = async (branch: Branch, companyId: string) => {
    if (!window.confirm(`Permanently delete inactive branch “${branch.name}”? Branches with shift or checklist history are retained.`)) return;
    setActionBusy(branch.id); setActionError('');
    try { await adminBranches.deleteInactive(branch.id); await loadBranches(companyId); }
    catch (failure) { setActionError(failure instanceof Error ? failure.message : 'Could not delete branch. Please retry.'); }
    finally { setActionBusy(null); }
  };

  return (
    <>
      <Group justify="space-between" align="center" mb="md" wrap="wrap" gap="sm">
        <Title order={2} component="h1">Companies</Title>
        <Button leftSection={<Plus size={16} />} onClick={() => { setEditingCompany(null); setForm(emptyForm); setError(''); setModalOpen(true); void loadQualityTemplates(); }}>Add company</Button>
      </Group>

      {actionError && <Text c="red" role="alert" mb="sm">{actionError}</Text>}
      <form role="search" onSubmit={e => { e.preventDefault(); setPage(1); setQuery(searchText.trim()); }} style={{ marginBottom: 12 }}>
        <TextInput label="Search companies" placeholder="Name or business ID" value={searchText} onChange={e => setSearchText(e.currentTarget.value)} />
      </form>
      <LifecycleTabs value={status} onChange={next => { setStatus(next); setPage(1); setExpanded(null); branchGeneration.current++; setBranchLoading(null); setBranchLoadError(''); }} label="Company activity"
        activeCount={loading || listError ? undefined : counts?.active}
        inactiveCount={loading || listError ? undefined : counts?.inactive}>
      {loading ? (
        <Loader />
      ) : listError ? (
        <Stack><Text role="alert" c="red">{listError}</Text><Button variant="light" onClick={() => void reload()}>Refresh companies</Button></Stack>
      ) : tableRows.length === 0 ? (
        <Text c="dimmed">{counts !== null && counts.all === 0 && !query ? 'No companies yet. Add one to get started.' : query ? 'No companies match this search.' : `No ${status} companies.`}</Text>
      ) : (
        <Stack gap={6}><Text size="xs" c="dimmed" hiddenFrom="sm">Scroll sideways to see all columns.</Text>
        <Table.ScrollContainer minWidth={550} tabIndex={0} role="region" aria-label="Companies table">
          <Table striped highlightOnHover withTableBorder>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Name</Table.Th>
                <Table.Th>Business ID</Table.Th>
                <Table.Th>Contact</Table.Th>
                <Table.Th w={120}>Branches</Table.Th>
                <Table.Th>Activity</Table.Th><Table.Th w={170}>Actions</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {tableRows.map((c) => {
                const currentBranches = companyBranchesMap[c.id] ?? [];
                const visibleBranches = currentBranches.filter(branch => inLifecycle(branch, branchStatus));
                const isExpanded = expanded === c.id;
                return (
                  <Fragment key={c.id}>
                    <Table.Tr>
                      <Table.Td fw={500}>{c.name}</Table.Td>
                      <Table.Td>{c.businessId}</Table.Td>
                      <Table.Td>{c.contactName || '—'}</Table.Td>
                      <Table.Td>
                        <Button variant="subtle" size="xs" onClick={() => toggleBranches(c.id)}>
                          {isExpanded ? currentBranches.length : 'Branches'} <ChevronDown size={14} />
                        </Button>
                      </Table.Td>
                      <Table.Td><Button size="xs" variant="light" color={c.isActive ? 'red' : undefined}
                        loading={actionBusy === c.id} disabled={actionBusy !== null && actionBusy !== c.id}
                        onClick={() => void changeActivity(c.id, c.isActive)}>{c.isActive ? 'Deactivate' : 'Reactivate'}</Button></Table.Td>
                      <Table.Td><Group gap={4} wrap="nowrap"><Button size="xs" variant="subtle" component={Link} href={`/admin/companies/customer/?id=${c.id}&section=overview&back=${encodeURIComponent(listQuery)}`} aria-label={`Open ${c.name}`}>Open</Button><Button size="xs" variant="subtle" leftSection={<Pencil size={13} />} onClick={() => openCompanyEdit(c)}>Edit</Button>{!c.isActive && <Button size="xs" color="red" variant="subtle" loading={actionBusy === c.id} disabled={!!actionBusy} onClick={() => void deleteCompany(c)}>Delete</Button>}</Group></Table.Td>
                    </Table.Tr>
                    {isExpanded && (
                      <Table.Tr>
                        <Table.Td colSpan={6} p="sm">
                          <Group justify="space-between" align="center" mb="xs" wrap="wrap" gap="xs">
                            <Text size="sm" fw={600}>Branches</Text>
                            <Button size="xs" variant="light" leftSection={<Plus size={14} />} onClick={() => { setEditingBranch(null); setBranchForm({ name: '', street: '', postalCode: '', city: '', country: '', contactPhone: '', contactEmail: '', roomCount: '' as number | string, bedCount: '' as number | string, sizeM2: '' as number | string, cleaningType: '', priceNet: '' as number | string, vatRatePercent: 25.5 as number | string, estimatedDurationMinutes: '' as number | string, priceEffectiveFrom: '', priceMode: 'PerOccurrence' }); setBranchError(''); setBranchModal(true); }}>Add branch</Button>
                          </Group>
                          <LifecycleTabs value={branchStatus} onChange={setBranchStatus} label="Branch activity"
                            activeCount={branchLoading === c.id || branchLoadError ? undefined : currentBranches.filter(b => b.isActive).length}
                            inactiveCount={branchLoading === c.id || branchLoadError ? undefined : currentBranches.filter(b => !b.isActive).length}>
                          {branchLoading === c.id ? <Loader size="sm" /> : branchLoadError ? (
                            <Stack><Text role="alert" c="red">{branchLoadError}</Text><Button variant="light" onClick={() => void loadBranches(c.id)}>Refresh branches</Button></Stack>
                          ) : visibleBranches.length === 0 ? (
                            <Text size="sm" c="dimmed">{currentBranches.length === 0 ? 'No branches yet.' : `No ${branchStatus} branches.`}</Text>
                          ) : (
                            <Stack gap={4}>
                              {visibleBranches.map((b) => (
                                <Group key={b.id} role="group" aria-label={'Branch ' + b.name} justify="space-between" wrap="wrap" gap="xs">
                                  <Stack gap={4}><Text size="sm">{b.name}{b.city ? ` · ${b.city}` : ''}{b.priceNet != null && <Text component="span" size="xs"> · €{b.priceNet.toFixed(2)} excl. VAT</Text>}{b.pricingReady && <Badge size="xs" ml="xs">Partner booking ready</Badge>}</Text><BranchPriceHistory branchName={b.name} versions={b.priceVersions ?? []} /><QuoteRequestedBadge branch={b} /></Stack>
                                  <Group gap="xs">{!b.isActive && <Badge color="gray" size="xs">Inactive</Badge>}
                                    <Button size="xs" variant="subtle" leftSection={<Pencil size={13} />} onClick={() => openBranchEdit(b)}>Edit</Button>
                                    <Button size="xs" variant="light" color={b.isActive ? 'red' : undefined}
                                      loading={actionBusy === b.id} disabled={actionBusy !== null && actionBusy !== b.id}
                                      onClick={() => void changeActivity(b.id, b.isActive, c.id)}>{b.isActive ? 'Deactivate' : 'Reactivate'}</Button>
                                    {!b.isActive && <Button size="xs" color="red" variant="subtle" loading={actionBusy === b.id} disabled={!!actionBusy} onClick={() => void deleteBranch(b, c.id)}>Delete</Button>}
                                  </Group>
                                </Group>
                              ))}
                            </Stack>
                          )}
                          </LifecycleTabs>
                        </Table.Td>
                      </Table.Tr>
                    )}
                  </Fragment>
                );
              })}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer></Stack>
      )}
      {!loading && !listError && total > 0 && (
        <Group justify="space-between" mt="sm" wrap="wrap" gap="sm">
          <Select aria-label="Companies per page" data={['25', '50', '100']} value={String(pageSize)} allowDeselect={false} w={120}
            onChange={value => { if (value) { setPageSize(Number(value)); setPage(1); } }} />
          <Pagination total={Math.max(1, Math.ceil(total / pageSize))} value={page} onChange={setPage}
            getControlProps={control => ({ 'aria-label': control === 'next' ? 'Next page' : control === 'previous' ? 'Previous page' : control === 'first' ? 'First page' : 'Last page' })} />
        </Group>
      )}
      </LifecycleTabs>

      <Modal opened={modalOpen} onClose={() => { setModalOpen(false); setEditingCompany(null); }} title={editingCompany ? 'Edit company' : 'Add company'} radius="md">
        <Stack gap="sm">
          <Checkbox label="Enable partner access" checked={form.isPartnerEnabled} onChange={event => { const checked = event.currentTarget.checked; setForm(current => ({ ...current, isPartnerEnabled: checked, ...(checked ? {} : { isQualityCycle: false, qualityCycleTemplateId: '' }) })); }} description="Partners linked to this company can access its properties and request cleanings. Disabling this removes their portal access." />
          {form.isPartnerEnabled && <>
          <Checkbox label="Quality cycle" checked={form.isQualityCycle} onChange={event => { const checked = event.currentTarget.checked; setForm(current => ({ ...current, isQualityCycle: checked })); }} description="Every partner cleaning request accepted for this company gets the quality cycle checklist. Staff cover it through My shifts like a normal cleaning." />
          {form.isQualityCycle && <Select label="Quality cycle template" required searchable data={qualityTemplates.map(template => ({ value: template.id, label: template.title }))} value={form.qualityCycleTemplateId || null} onChange={value => setForm(current => ({ ...current, qualityCycleTemplateId: value ?? '' }))} />}
          </>}
          <TextInput label="Business ID" required value={form.businessId} onChange={set('businessId')} />
          <TextInput label="Name" required value={form.name} onChange={set('name')} />
          <TextInput label="Contact name" value={form.contactName} onChange={set('contactName')} />
          <TextInput label="Contact email" value={form.contactEmail} onChange={set('contactEmail')} />
          <TextInput label="Contact phone" value={form.contactPhone} onChange={set('contactPhone')} />
          <Textarea label="Notes" value={form.notes} onChange={set('notes')} />
          {error && <Text size="sm" c="red" role="alert">{error}</Text>}
          <Group justify="flex-end" mt="xs">
            <Button variant="default" onClick={() => setModalOpen(false)}>Cancel</Button>
            <Button loading={saving} onClick={submit}>{editingCompany ? 'Save changes' : 'Create company'}</Button>
          </Group>
        </Stack>
      </Modal>

      <Modal opened={!!relatedPrompt} onClose={() => { if (!relatedBusy) closeRelatedPrompt(); }} title="Clear related shifts and places?" radius="md">
        {relatedPrompt && (
          <Stack gap="sm">
            <Text>{relatedPrompt.reason}</Text>
            <Text size="sm" c="dimmed">This deletes the company&apos;s branches and shifts, unassigns every employee from them, removes its partner requests and voids its open invoices. Checklist submissions are kept as separate records. Paid invoices stop the clear: issue a credit note first. Nothing is changed if the clear is refused.</Text>
            <Checkbox label="Also delete this company&apos;s signed agreements" description="Off keeps the signed agreements on record." checked={relatedDeleteAgreements} onChange={event => setRelatedDeleteAgreements(event.currentTarget.checked)} disabled={relatedBusy} />
            {relatedError && <Text c="red" role="alert">{relatedError}</Text>}
            <Button color="red" loading={relatedBusy} onClick={() => void clearRelatedAndContinue()}>Delete related shifts, unassign employees and clear all places</Button>
            <Button variant="default" disabled={relatedBusy} onClick={closeRelatedPrompt}>Skip deletion</Button>
          </Stack>
        )}
      </Modal>

      <Modal opened={!!removal} onClose={() => { if (!removal?.busy) setRemoval(null); }} title="Remove company" radius="md">
        {removal && (() => {
          const everything = removal.branches && removal.shifts && removal.invoices;
          const paidNeedsName = removal.invoices && removal.summary.paidInvoices > 0;
          const nameMatches = removal.confirmName.trim() === removal.company.name.trim();
          const nothingTicked = !removal.branches && !removal.shifts && !removal.invoices;
          return (
            <Stack gap="sm">
              <Text>{removal.reason}</Text>
              <Text size="sm" c="dimmed">Tick what to remove from {removal.company.name}. Anything not ticked is kept.</Text>
              <Checkbox label={`Places (${removal.summary.branches})`} description="The company's branches. Removing places also removes their shifts."
                checked={removal.branches} onChange={event => setPlaces(event.currentTarget.checked)} disabled={removal.busy} />
              <Checkbox label={`Shifts (${removal.summary.shifts}, ${removal.summary.shiftsWithChecklists} with completed checklists)`}
                description="Unassigns employees and removes the shifts, including completed ones." checked={removal.shifts}
                onChange={event => setShifts(event.currentTarget.checked)} disabled={removal.busy} />
              <Checkbox label={`Invoices (${removal.summary.invoices}, ${removal.summary.paidInvoices} paid)`}
                description="Removes the invoices, paid ones included. Paid invoices need the company name typed below."
                checked={removal.invoices} onChange={event => { const checked = event.currentTarget.checked; setRemoval(current => current && { ...current, invoices: checked }); }} disabled={removal.busy} />
              {everything && (
                <Text size="sm" c="dimmed">
                  Ticking all three also removes the company record and everything linked to it: {removal.summary.agreements} signed agreement(s),
                  {' '}{removal.summary.checklistSubmissions} checklist submission(s) and {removal.summary.partnerRequests} partner request(s).
                  {' '}{removal.summary.partnerAccounts} partner account(s) are deactivated and their login is disabled.
                  {' '}{removal.summary.templates} template(s) and {removal.summary.leads} lead(s) are kept and unlinked from the company.
                </Text>
              )}
              {paidNeedsName && (
                <TextInput label={`Type “${removal.company.name}” to confirm removing ${removal.summary.paidInvoices} paid invoice(s)`}
                  value={removal.confirmName} onChange={event => { const value = event.currentTarget.value; setRemoval(current => current && { ...current, confirmName: value }); }} disabled={removal.busy} />
              )}
              {removal.error && <Text c="red" role="alert">{removal.error}</Text>}
              <Button color="red" loading={removal.busy} disabled={nothingTicked || removal.busy || (paidNeedsName && !nameMatches)} onClick={() => void submitRemoval()}>Remove selected</Button>
              <Button variant="default" disabled={removal.busy} onClick={() => setRemoval(null)}>Cancel</Button>
            </Stack>
          );
        })()}
      </Modal>

      <Modal opened={branchModal} onClose={() => { setBranchModal(false); setEditingBranch(null); }} title={editingBranch ? 'Edit branch' : 'Add branch'} radius="md">
        <Stack gap="sm">
          <TextInput label="Name" required value={branchForm.name} onChange={setB('name')} />
          {branchError && <Text c="red" role="alert">{branchError}</Text>}
          <TextInput label="Street" value={branchForm.street} onChange={setB('street')} />
          <TextInput label="Postal code" value={branchForm.postalCode} onChange={setB('postalCode')} />
          <TextInput label="City" value={branchForm.city} onChange={setB('city')} />
          <TextInput label="Country" value={branchForm.country} onChange={setB('country')} />
          <TextInput label="Contact email" type="email" value={branchForm.contactEmail} onChange={setB('contactEmail')} />
          <TextInput label="Phone" value={branchForm.contactPhone} onChange={setB('contactPhone')} />
          <Group grow><NumberInput label="Rooms (optional)" min={0} allowDecimal={false} value={branchForm.roomCount} onChange={value => setBranchForm(current => ({ ...current, roomCount: value }))} /><NumberInput label="Beds (optional)" min={0} allowDecimal={false} value={branchForm.bedCount} onChange={value => setBranchForm(current => ({ ...current, bedCount: value }))} /></Group>
          <NumberInput label="Size in m² (required to schedule)" min={1} max={100000} allowDecimal={false} value={branchForm.sizeM2} onChange={value => setBranchForm(current => ({ ...current, sizeM2: value }))} />
          <Select label="Cleaning type (required to schedule)" placeholder="Choose" clearable value={branchForm.cleaningType || null} onChange={value => setBranchForm(current => ({ ...current, cleaningType: value ?? '' }))} data={[{ value: 'Deep', label: 'Deep' }, { value: 'Normal', label: 'Normal' }]} />
          <Group grow><TextInput label="Price effective from (blank = today)" type="date" value={branchForm.priceEffectiveFrom} onChange={event => setBranchForm(current => ({ ...current, priceEffectiveFrom: event.target.value }))} /><Select label="Pricing basis" allowDeselect={false} value={branchForm.priceMode} onChange={value => setBranchForm(current => ({ ...current, priceMode: value ?? 'PerOccurrence' }))} data={[{ value: 'PerOccurrence', label: 'Per completed visit' }, { value: 'Monthly', label: 'Fixed monthly fee' }, { value: 'Hourly', label: 'Per scheduled hour' }]} /></Group>
          <NumberInput label="Fixed cleaning price, excl. VAT (€)" min={0} decimalScale={2} value={branchForm.priceNet} onChange={value => setBranchForm(current => ({ ...current, priceNet: value }))} description="Leave blank until a quote has been agreed. New requests use this price; existing requests keep their agreed price." />
          <Group grow><NumberInput label="VAT (%)" min={0} max={50} decimalScale={2} value={branchForm.vatRatePercent} onChange={value => setBranchForm(current => ({ ...current, vatRatePercent: value }))} /><NumberInput label="Estimated cleaning duration (minutes)" min={1} allowDecimal={false} value={branchForm.estimatedDurationMinutes} onChange={value => setBranchForm(current => ({ ...current, estimatedDurationMinutes: value }))} /></Group>
          <Group justify="flex-end" mt="xs">
            <Button variant="default" onClick={() => setBranchModal(false)}>Cancel</Button>
            <Button loading={branchSaving} onClick={submitBranch}>{editingBranch ? 'Save changes' : 'Add branch'}</Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}
