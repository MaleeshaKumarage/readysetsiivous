'use client';
import { useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { LeadBranchesEditor } from '@/components/leads/LeadBranchesEditor';
import { LeadCollaborationPanel } from '@/components/leads/LeadCollaborationPanel';
import { Badge, Button, Group, Loader, Modal, Paper, Select, Stack, Table, Tabs, Text, Textarea, TextInput, Title } from '@mantine/core';
import { emptyLead, leadsApi, leadBranches, type LeadDetail, type LeadFields, type LeadRow } from '@/lib/leadsApi';

function LeadPhoto({ leadId, imageId, disabled, remove }: { leadId: string; imageId: string; disabled: boolean; remove?: () => void }) {
  const [url, setUrl] = useState(''); const [error, setError] = useState(''); const [retry, setRetry] = useState(0);
  useEffect(() => { let active = true; let objectUrl = ''; setError(''); setUrl('');
    leadsApi.image(leadId, imageId).then(blob => { if (active) { objectUrl = URL.createObjectURL(blob); setUrl(objectUrl); } }).catch(() => { if (active) setError('Image could not be loaded.'); });
    return () => { active = false; if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [leadId, imageId, retry]);
  return <Paper withBorder p="xs" w={190}><Stack gap="xs">
    {url ? <a href={url} target="_blank" rel="noreferrer" aria-label="Open lead image"><img src={url} alt="Lead image" style={{ width: '100%', height: 130, objectFit: 'contain' }} /></a> : error ? <><Text role="alert" size="sm">{error}</Text><Button size="xs" onClick={() => setRetry(x => x + 1)}>Retry image</Button></> : <Loader size="sm" />}
    {remove && <Button size="xs" color="red" variant="light" disabled={disabled} onClick={remove}>Remove image</Button>}
  </Stack></Paper>;
}
const errorText = (failure: unknown) => failure instanceof Error ? failure.message : 'Operation failed. Please retry.';
const statuses = ['New', 'Contacted', 'In discussion', 'Lost'];
const bounds: Partial<Record<keyof LeadFields, number>> = { name: 200, businessId: 50, contactName: 200, contactPhone: 50, branchName: 200, street: 300, postalCode: 30, city: 100, country: 100, branchPhone: 50, branchEmail: 254 };

export default function LeadsPage() {
  const [collaborationDirty, setCollaborationDirty] = useState(false); const [collaborationBusy, setCollaborationBusy] = useState(false);
  const [rows, setRows] = useState<LeadRow[]>([]); const [total, setTotal] = useState(0); const [search, setSearch] = useState(''); const [filter, setFilter] = useState('Active'); const [skip, setSkip] = useState(0);
  const [loading, setLoading] = useState(true); const [listError, setListError] = useState(''); const [refresh, setRefresh] = useState(0); const generation = useRef(0); const detailGeneration = useRef(0);
  const [opened, setOpened] = useState(false); const [detail, setDetail] = useState<LeadDetail | null>(null); const [form, setForm] = useState<LeadFields>({ ...emptyLead });
  const [busy, setBusy] = useState(''); const [error, setError] = useState(''); const [message, setMessage] = useState(''); const [detailLoading, setDetailLoading] = useState(false);
  const formTop = useRef<HTMLDivElement>(null);
  useEffect(() => { if (error) formTop.current?.scrollIntoView({ block: 'start' }); }, [error]);
  const [convertOpen, setConvertOpen] = useState(false); const [editingId, setEditingId] = useState<string | null>(null);
  const [imageTab, setImageTab] = useState<string | null>('view');
  const converted = !!detail?.lead.companyId; const dirty = !!detail && JSON.stringify(form) !== JSON.stringify(detail.lead.fields);
  useEffect(() => { if (converted) setImageTab('view'); }, [converted]);
  useEffect(() => { const version = ++generation.current; setLoading(true); setListError('');
    const timer = setTimeout(() => { leadsApi.list(search, filter, skip).then(result => { if (version === generation.current) { setRows(result.items); setTotal(result.total); } }).catch(failure => { if (version === generation.current) setListError(errorText(failure)); }).finally(() => { if (version === generation.current) setLoading(false); }); }, 250);
    return () => { clearTimeout(timer); generation.current++; };
  }, [search, filter, skip, refresh]);
  useEffect(() => () => { detailGeneration.current++; }, []);
  const reload = () => setRefresh(x => x + 1);
  const close = () => { if (busy || detailLoading || collaborationBusy) return; if ((dirty || collaborationDirty) && !window.confirm('Discard unsaved lead changes, recording or follow-up?')) return; detailGeneration.current++; setOpened(false); setConvertOpen(false); };
  const openNew = () => { setImageTab('view'); setEditingId(null); detailGeneration.current++; setDetail(null); setForm({ ...emptyLead, emails: [] }); setError(''); setMessage(''); setOpened(true); };
  const openDetail = async (id: string) => { setImageTab('view'); setEditingId(id); const version = ++detailGeneration.current; setOpened(true); setDetailLoading(true); setError(''); setMessage(''); setDetail(null);
    try { const result = await leadsApi.get(id); if (version === detailGeneration.current) { setDetail(result); setForm(result.lead.fields); } }
    catch (failure) { if (version === detailGeneration.current) setError(errorText(failure)); }
    finally { if (version === detailGeneration.current) setDetailLoading(false); }
  };
  const openedFromLink = useRef(false);
  useEffect(() => { const requested = new URLSearchParams(window.location.search).get('lead'); if (requested && !openedFromLink.current) { openedFromLink.current = true; void openDetail(requested); } return () => { openedFromLink.current = false; }; }, []);
  const perform = async (name: string, action: () => Promise<LeadDetail>, replaceForm = true) => { setBusy(name); setError(''); setMessage('');
    try { const result = await action(); setDetail(result); if (replaceForm) setForm(result.lead.fields); reload(); setMessage(name === 'Saving' ? 'Lead saved.' : 'Lead updated.'); return result; }
    catch (failure) { setError(errorText(failure)); return null; } finally { setBusy(''); }
  };
  const save = () => { if ((form.branches || []).some(branch => !branch.name.trim())) { setError('Enter a name for each added branch, or remove the draft branch.'); return; } if (!form.name.trim()) { setError('Company name is required.'); return; } void perform('Saving', () => detail ? leadsApi.update(detail.lead, form) : leadsApi.create(form)); };
  const upload = async (files: FileList | null) => { if (!files?.length || !detail) return;
    const selected = Array.from(files);
    if (selected.length + detail.images.length > 3) { setError('A lead can have at most 3 images.'); return; }
    if (selected.some(file => file.size > 5 * 1024 * 1024 || !['image/jpeg', 'image/png', 'image/webp'].includes(file.type))) { setError('Choose JPEG, PNG or WebP images up to 5 MiB each.'); return; }
    setError(''); setMessage(''); let current = detail;
    try { for (let i = 0; i < selected.length; i++) { setBusy(`Uploading image ${i + 1}/${selected.length}`); current = await leadsApi.upload(current.lead, selected[i]); setDetail(current); } reload(); setMessage('Images uploaded.'); }
    catch (failure) { reload(); setError(errorText(failure) + ' Images already uploaded are kept; you can retry the remaining files.'); } finally { setBusy(''); }
  };
  const input = (key: keyof LeadFields, label: string, required = false) => <TextInput key={key} label={label} required={required} maxLength={bounds[key]} value={form[key] as string} readOnly={converted} disabled={!!busy} onChange={event => setForm(current => ({ ...current, [key]: event.target.value }))} />;
  return <Stack>
    <Group justify="space-between"><Title order={2}>Leads</Title><Button onClick={openNew}>New lead</Button></Group>
    <Group align="end"><TextInput label="Search leads" placeholder="Company, business ID or email" maxLength={200} value={search} onChange={event => { setSearch(event.target.value); setSkip(0); }} /><Select label="Lead filter" value={filter} onChange={value => { setFilter(value || 'Active'); setSkip(0); }} data={['Active', 'Inactive', 'Converted', ...statuses]} /></Group>
    {listError ? <><Text role="alert" c="red">{listError}</Text><Button onClick={reload}>Retry loading leads</Button></> : loading ? <Loader /> : <>
      {rows.length === 0 ? <Text>No leads in this view.</Text> : <Table.ScrollContainer minWidth={680}><Table striped withTableBorder><Table.Thead><Table.Tr>{['Company', 'Branch', 'Contact', 'Status', 'Images', 'Actions'].map(label => <Table.Th key={label}>{label}</Table.Th>)}</Table.Tr></Table.Thead><Table.Tbody>{rows.map(row => <Table.Tr key={row.id}><Table.Td>{row.fields.name}<Text size="xs">{row.fields.businessId}</Text></Table.Td><Table.Td>{leadBranches(row.fields, row.id).map(b => b.name || 'Unnamed branch').join(', ') || 'No branches'}</Table.Td><Table.Td>{row.fields.primaryEmail || row.fields.contactPhone || '—'}{row.fields.emails.length > 1 && <Text size="xs">{row.fields.emails.length} email addresses</Text>}</Table.Td><Table.Td><Badge>{row.status}</Badge></Table.Td><Table.Td>{row.imageCount}/3</Table.Td><Table.Td><Button size="xs" onClick={() => void openDetail(row.id)}>View lead</Button></Table.Td></Table.Tr>)}</Table.Tbody></Table></Table.ScrollContainer>}
      <Group><Button disabled={skip === 0} onClick={() => setSkip(x => Math.max(0, x - 25))}>Previous</Button><Text>{total ? `${skip + 1}–${Math.min(skip + 25, total)} of ${total}` : '0 leads'}</Text><Button disabled={skip + 25 >= total} onClick={() => setSkip(x => x + 25)}>Next</Button></Group>
    </>}
    <Modal opened={opened} onClose={close} closeButtonProps={{ 'aria-label': 'Close lead' }} title={detail ? detail.lead.fields.name : 'New lead'} size="xl" closeOnClickOutside={false} withCloseButton={!busy && !detailLoading && !collaborationBusy}>
      {detailLoading ? <Loader /> : editingId && !detail ? <Stack><Text role="alert" c="red">{error || 'Lead could not be loaded.'}</Text><Button onClick={() => void openDetail(editingId)}>Retry loading lead</Button></Stack> : <Stack>
        <div ref={formTop} />{error && <Text role="alert" c="red">{error}</Text>}{message && <Text role="status" c="teal">{message}</Text>}
        {converted && <Paper withBorder p="sm"><Text>This lead was converted to a customer and is retained as a read-only record.</Text>{detail?.customerAvailable ? <Button component={Link} href={`/admin/companies/?company=${detail.lead.companyId}`} mt="xs">Open customer</Button> : <Text>The converted customer or branch is no longer available.</Text>}</Paper>}
        <Title order={3}>Company details</Title>
        {input('name', 'Company name', true)}{input('businessId', 'Business ID (required for conversion)')}{input('contactName', 'Contact name')}{input('contactPhone', 'Company phone')}
        <Textarea label="Contact email addresses" description="One email per line; up to 20. Choose the primary address below." value={form.emails.join('\n')} readOnly={converted} disabled={!!busy} autosize minRows={2} onChange={event => { const emails = event.target.value.split('\n'); setForm(current => ({ ...current, emails, primaryEmail: emails.includes(current.primaryEmail) ? current.primaryEmail : emails.find(x => x.trim()) || '' })); }} />
        <Select label="Primary company email" clearable disabled={converted || !!busy} data={Array.from(new Set(form.emails.map(email => email.trim()).filter(Boolean)))} value={form.primaryEmail || null} onChange={value => setForm(current => ({ ...current, primaryEmail: value || '' }))} />
        <Select label="Lead status" data={statuses} value={form.status} disabled={converted || !!busy} onChange={value => setForm(current => ({ ...current, status: value || 'New' }))} />
        <Textarea label="Company notes" maxLength={5000} autosize minRows={2} value={form.notes} readOnly={converted} disabled={!!busy} onChange={event => setForm(current => ({ ...current, notes: event.target.value }))} />
        <Textarea label="Description" maxLength={10000} autosize minRows={3} value={form.description} readOnly={converted} disabled={!!busy} onChange={event => setForm(current => ({ ...current, description: event.target.value }))} />
        <Title order={3}>Branches ({form.branches?.length || 0})</Title>
        <LeadBranchesEditor branches={form.branches || []} readOnly={converted} disabled={!!busy} onChange={branches => setForm(current => ({ ...current, branches }))} />
        {!converted && <Group><Button disabled={!!busy || collaborationDirty || collaborationBusy} loading={busy === 'Saving'} onClick={save}>Save lead</Button>{detail && <Button variant="default" disabled={!!busy || collaborationDirty || collaborationBusy} onClick={() => { if (!dirty || window.confirm('Discard unsaved changes and reload?')) void openDetail(detail.lead.id); }}>Reload lead</Button>}</Group>}
        <Title order={3}>Images ({detail?.images.length || 0}/3)</Title>
        <Text size="sm">JPEG, PNG or WebP, up to 5 MiB each. Private to tenant admins and managers.</Text>
        <Tabs value={imageTab} onChange={setImageTab}><Tabs.List><Tabs.Tab value="view">View images</Tabs.Tab>{!converted && <Tabs.Tab value="manage">Manage images</Tabs.Tab>}</Tabs.List>
          <Tabs.Panel value="view" pt="sm">{imageTab === 'view' && (detail?.images.length ? <Group align="start">{detail.images.map(image => <LeadPhoto key={image.id} leadId={detail.lead.id} imageId={image.id} disabled={!!busy} />)}</Group> : <Text>No images attached.</Text>)}</Tabs.Panel>
          {!converted && <Tabs.Panel value="manage" pt="sm">{imageTab === 'manage' && (!detail ? <Text>Save the lead before managing images.</Text> : !detail.lead.isActive ? <Text>Reactivate this lead before managing images.</Text> : <Stack><Text>Upload images or explicitly remove an attachment. Viewing images never shows removal controls.</Text><Group align="start">{detail.images.map(image => <LeadPhoto key={image.id} leadId={detail.lead.id} imageId={image.id} disabled={!!busy} remove={() => { if (window.confirm('Remove this lead image?')) void perform('Removing image', () => leadsApi.removeImage(detail.lead, image.id), false); }} />)}</Group><div><label htmlFor="lead-images">Add images</label><input id="lead-images" type="file" multiple accept="image/jpeg,image/png,image/webp" disabled={!!busy || detail.images.length >= 3} onChange={event => { void upload(event.target.files); event.target.value = ''; }} /></div></Stack>)}</Tabs.Panel>}
        </Tabs>
        {detail && <LeadCollaborationPanel key={detail.lead.id} leadId={detail.lead.id} active={detail.lead.isActive && !converted && detail.lead.status !== 'Lost'} admin converted={converted} onDirtyChange={setCollaborationDirty} onBusyChange={setCollaborationBusy} />}
        {busy && <Text role="status">{busy}…</Text>}
        {detail && !converted && <><Text size="sm">{detail.lead.isActive ? 'Active lead' : 'Inactive lead'}. {dirty && 'Save changes before lifecycle actions or conversion.'}</Text><Group>
          <Button variant="light" disabled={!!busy || dirty || collaborationDirty || collaborationBusy} onClick={() => void perform('Updating activity', () => leadsApi.activity(detail.lead))}>{detail.lead.isActive ? 'Deactivate' : 'Reactivate'}</Button>
          {detail.lead.isActive ? <Button disabled={!!busy || dirty || collaborationDirty || collaborationBusy} onClick={() => setConvertOpen(true)}>Convert to customer</Button> : <Button color="red" disabled={!!busy || dirty || collaborationDirty || collaborationBusy} onClick={async () => { if (!window.confirm('Permanently delete this inactive lead and its images? This cannot be undone.')) return; setBusy('Deleting'); setError(''); try { await leadsApi.remove(detail.lead); setOpened(false); reload(); } catch (failure) { setError(errorText(failure)); } finally { setBusy(''); } }}>Delete lead</Button>}
        </Group></>}
      </Stack>}
    </Modal>
    <Modal opened={convertOpen} onClose={() => { if (!busy) setConvertOpen(false); }} title="Convert lead to customer" closeOnClickOutside={false} withCloseButton={!busy}>
      <Stack><Text>Create a company and branch for {detail?.lead.fields.name}? Business ID is required. All added branches will be created; zero branches is allowed.</Text><Text>The selected primary email becomes the company contact email. All other emails, description and images remain on this linked lead.</Text>
        <Text>Company: {detail?.lead.fields.name} · {detail?.lead.fields.businessId || 'Business ID missing'}</Text><Text>{form.branches?.length || 0} branches will be created.</Text><LeadBranchesEditor branches={form.branches || []} readOnly /><Text>Primary email: {detail?.lead.fields.primaryEmail || 'None'}</Text>
        {error && <Text role="alert" c="red">{error}</Text>}<Group><Button variant="default" disabled={!!busy} onClick={() => setConvertOpen(false)}>Cancel</Button><Button loading={busy === 'Converting'} disabled={!!busy} onClick={async () => { if (!detail) return; const result = await perform('Converting', () => leadsApi.convert(detail.lead)); if (result) setConvertOpen(false); }}>Confirm conversion</Button></Group>
      </Stack>
    </Modal>
  </Stack>;
}
