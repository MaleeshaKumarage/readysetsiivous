'use client';
import { useEffect, useRef, useState } from 'react';
import { Badge, Button, Group, Loader, Modal, Select, Stack, Table, Text, TextInput, Title } from '@mantine/core';
import { leadCollaborationApi as api, type CollaborationDetail } from '@/lib/leadCollaborationApi';
import { LeadBranchesEditor } from './LeadBranchesEditor';
import { leadBranches, type LeadRow } from '@/lib/leadsApi';
import { LeadCollaborationPanel } from './LeadCollaborationPanel';
function PrivateImage({ leadId, id }: { leadId: string; id: string }) {
  const [url, setUrl] = useState(''); const [error, setError] = useState(''); const [attempt, setAttempt] = useState(0);
  useEffect(() => { let active = true; let value = ''; setError(''); api.image(leadId, id).then(blob => { if (active) { value = URL.createObjectURL(blob); setUrl(value); } }).catch(failure => { if (active) setError((failure as Error).message); }); return () => { active = false; if (value) URL.revokeObjectURL(value); }; }, [leadId, id, attempt]);
  return error ? <Stack><Text role="alert" c="red">{error}</Text><Button onClick={() => setAttempt(x => x + 1)}>Retry lead image</Button></Stack> : url ? <a href={url} target="_blank" rel="noreferrer"><img src={url} alt="Lead image" width={180} /></a> : <Text>Loading image…</Text>;
}
export function ManagerLeadsWorkspace() {
  const [rows, setRows] = useState<LeadRow[]>([]); const [total, setTotal] = useState(0); const [search, setSearch] = useState(''); const [filter, setFilter] = useState('Active'); const [skip, setSkip] = useState(0);
  const [error, setError] = useState(''); const [loading, setLoading] = useState(true); const [refresh, setRefresh] = useState(0);
  const [opened, setOpened] = useState(false); const [selected, setSelected] = useState<string | null>(null); const [detail, setDetail] = useState<CollaborationDetail | null>(null); const [detailError, setDetailError] = useState('');
  const [dirty, setDirty] = useState(false); const [busy, setBusy] = useState(false); const listVersion = useRef(0); const detailVersion = useRef(0);
  useEffect(() => { const attempt = ++listVersion.current; setLoading(true); const timer = setTimeout(() => { api.list(search, filter, skip).then(value => { if (attempt === listVersion.current) { setRows(value.items); setTotal(value.total); setError(''); } }).catch(failure => { if (attempt === listVersion.current) setError((failure as Error).message); }).finally(() => { if (attempt === listVersion.current) setLoading(false); }); }, 250); return () => { clearTimeout(timer); listVersion.current++; }; }, [search, filter, skip, refresh]);
  useEffect(() => () => { detailVersion.current++; }, []);
  const open = async (id: string) => { const attempt = ++detailVersion.current; setSelected(id); setDetail(null); setDetailError(''); setOpened(true); try { const value = await api.detail(id); if (attempt === detailVersion.current) setDetail(value); } catch (failure) { if (attempt === detailVersion.current) setDetailError((failure as Error).message); } };
  const close = () => { if (busy || (dirty && !window.confirm('Discard the unsaved recording or follow-up?'))) return; detailVersion.current++; setOpened(false); setDetail(null); setDirty(false); };
  return <Stack><Title order={2}>Leads</Title><Text>View prospects, record voice notes and manage internal follow-ups. Customer conversion and lead administration remain with tenant admins.</Text>
    <Group><TextInput label="Search leads" value={search} maxLength={200} onChange={e => { setSearch(e.target.value); setSkip(0); }} /><Select label="Lead filter" value={filter} data={['Active', 'Inactive', 'Converted', 'New', 'Contacted', 'In discussion', 'Lost']} onChange={value => { setFilter(value || 'Active'); setSkip(0); }} /></Group>
    {error && <Text role="alert" c="red">{error}</Text>}<Button variant="default" onClick={() => setRefresh(x => x + 1)}>Refresh leads</Button>
    {loading ? <Loader /> : <><Table.ScrollContainer minWidth={500}><Table><Table.Thead><Table.Tr><Table.Th>Company</Table.Th><Table.Th>Branch</Table.Th><Table.Th>Status</Table.Th><Table.Th>Actions</Table.Th></Table.Tr></Table.Thead><Table.Tbody>{rows.map(row => <Table.Tr key={row.id}><Table.Td>{row.fields.name}</Table.Td><Table.Td>{leadBranches(row.fields, row.id).map(branch => branch.name).join(', ') || 'No branches'}</Table.Td><Table.Td><Badge>{row.status}</Badge></Table.Td><Table.Td><Button onClick={() => void open(row.id)}>View lead</Button></Table.Td></Table.Tr>)}</Table.Tbody></Table></Table.ScrollContainer>{!rows.length && <Text>No leads in this view.</Text>}</>}
    <Group><Button disabled={!skip} onClick={() => setSkip(x => Math.max(0, x - 25))}>Previous</Button><Text>{total} leads</Text><Button disabled={skip + 25 >= total} onClick={() => setSkip(x => x + 25)}>Next</Button></Group>
    <Modal opened={opened} onClose={close} closeButtonProps={{ 'aria-label': 'Close lead' }} withCloseButton={!busy} closeOnClickOutside={false} title={detail?.lead.fields.name || 'Lead'} size="xl">
      {!detail ? detailError ? <Stack><Text role="alert" c="red">{detailError}</Text><Button onClick={() => selected && void open(selected)}>Retry loading lead</Button></Stack> : <Loader /> : <Stack>
        <Title order={3}>Company and branch</Title><Text>{detail.lead.fields.name} · {detail.lead.fields.businessId}</Text><LeadBranchesEditor branches={leadBranches(detail.lead.fields, detail.lead.id)} readOnly />
        <Text>{detail.lead.fields.contactName} · {detail.lead.fields.contactPhone}</Text><Text>{detail.lead.fields.emails.join(', ')}</Text><Text style={{ whiteSpace: 'pre-wrap' }}>{detail.lead.fields.notes}</Text><Text style={{ whiteSpace: 'pre-wrap' }}>{detail.lead.fields.description}</Text><Badge>{detail.lead.status}</Badge><Title order={3}>Images</Title><Group>{detail.images.map(image => <PrivateImage key={image.id} leadId={detail.lead.id} id={image.id} />)}</Group>
        <LeadCollaborationPanel key={detail.lead.id} leadId={detail.lead.id} active={detail.capabilities.canRecord && detail.capabilities.canSchedule} admin={detail.capabilities.canDeleteVoice} converted={!!detail.lead.companyId} onDirtyChange={setDirty} onBusyChange={setBusy} />
      </Stack>}
    </Modal>
  </Stack>;
}
