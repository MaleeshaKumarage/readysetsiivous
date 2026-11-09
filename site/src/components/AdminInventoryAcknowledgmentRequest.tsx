'use client';

import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Checkbox, Group, Modal, Select, Stack, Text, Textarea } from '@mantine/core';
import { adminInventory, takenLabel, type InventoryUsage } from '@/lib/inventoryApi';
import { inventoryAcknowledgments, type InventoryAcknowledgment } from '@/lib/inventoryAcknowledgmentApi';
import { InventoryEvidence } from './InventoryEvidence';

export function AdminInventoryAcknowledgmentRequest({ rows, onClose, onSent }: { rows: InventoryUsage[]; onClose: () => void; onSent: (rows: InventoryAcknowledgment[]) => void }) {
  const [partners, setPartners] = useState<{ id: string; name: string }[]>([]);
  const [search, setSearch] = useState(''), [partner, setPartner] = useState<string | null>(null);
  const [message, setMessage] = useState(''), [confirmed, setConfirmed] = useState(false);
  const [loading, setLoading] = useState(false), [saving, setSaving] = useState(false), [error, setError] = useState('');
  const [searchError, setSearchError] = useState(''), [retry, setRetry] = useState(0);
  const [preview, setPreview] = useState<string | null>(null);
  const [selectedOption, setSelectedOption] = useState<{ id: string; name: string } | null>(null);
  const batchId = useRef(crypto.randomUUID());
  const attempted = useRef(false);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setSearchError('');
    const timer = setTimeout(() => {
      inventoryAcknowledgments.partners(search, controller.signal).then(data => {
        if (!controller.signal.aborted) setPartners(data);
      }).catch(error => { if (!controller.signal.aborted) setSearchError(error.message); })
        .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    }, 250);
    return () => { clearTimeout(timer); controller.abort(); };
  }, [search, retry]);
  function changed() { if (attempted.current) { batchId.current = crypto.randomUUID(); attempted.current = false; } }
  async function send() {
    if (!partner || !confirmed || saving) return;
    setSaving(true); setError(''); attempted.current = true;
    try { onSent(await inventoryAcknowledgments.create({ batchId: batchId.current, companyId: partner, usageIds: rows.map(row => row.id), message })); }
    catch (error) { setError((error instanceof Error ? error.message : 'Could not send request.') + ' Reload the log to check whether it was saved before changing the request.'); }
    finally { setSaving(false); }
  }
  const options = [...partners];
  if (selectedOption && !options.some(option => option.id === selectedOption.id)) options.push(selectedOption);
  return <Modal opened onClose={() => { if (!saving) onClose(); }} title="Request partner acknowledgment" size="lg" closeOnClickOutside={!saving} closeOnEscape={!saving}>
    <Stack>
      <Text>Share {rows.length} inventory {rows.length === 1 ? 'entry' : 'entries'} with one partner company. Review the notes and photos before sending.</Text>
      <Select label="Partner company" placeholder={loading ? 'Searching partners…' : 'Search active partner companies'} searchable clearable
        searchValue={search} onSearchChange={setSearch} value={partner} disabled={saving}
        data={options.map(option => ({ value: option.id, label: option.name }))} nothingFoundMessage={loading ? 'Searching…' : 'No matching partner companies'}
        onChange={value => { changed(); setPartner(value); setSelectedOption(options.find(option => option.id === value) ?? null); setConfirmed(false); }} />
      <Text size="sm" c="dimmed">Showing up to 25 companies. Type to search for another partner.</Text>
      {searchError && <Alert color="red">{searchError}<Button variant="subtle" onClick={() => setRetry(value => value + 1)}>Retry partner search</Button></Alert>}
      {rows.map(row => <Stack gap="xs" key={row.id} style={{ borderBottom: '1px solid #ddd', paddingBottom: 12 }}>
        <Text fw={600}>{row.itemName} · {row.quantity} {row.unit} · {row.locationName}</Text>
        <Text size="sm">{takenLabel(row.takenUtc)}</Text>
        {row.note && <Text style={{ whiteSpace: 'pre-wrap' }}>{row.note}</Text>}
        <Button variant="subtle" onClick={() => setPreview(preview === row.id ? null : row.id)} aria-expanded={preview === row.id}>Review photos for {row.itemName}</Button>
        {preview === row.id && <InventoryEvidence paths={row.photoIds.map(id => adminInventory.photoPath(row.id, id))} itemName={row.itemName} />}
      </Stack>)}
      <Textarea label="Message to partner (optional)" maxLength={500} value={message} disabled={saving} onChange={event => { changed(); setMessage(event.currentTarget.value); }} />
      <Text size="sm" c="dimmed">The partner will see the item, quantity, location, date, notes and photos. Employee names are not shared. Photos follow the existing three-month retention policy.</Text>
      <Checkbox label={`I confirm these entries and their photos may be shared with ${selectedOption?.name ?? 'the selected partner'}.`} checked={confirmed} disabled={!partner || saving} onChange={event => setConfirmed(event.currentTarget.checked)} />
      {error && <Alert color="red" role="alert">{error}</Alert>}
      <Group justify="flex-end"><Button variant="default" disabled={saving} onClick={onClose}>Cancel</Button><Button style={{ minHeight: 44 }} loading={saving} disabled={!partner || !confirmed} onClick={send}>Send acknowledgment request</Button></Group>
    </Stack>
  </Modal>;
}
