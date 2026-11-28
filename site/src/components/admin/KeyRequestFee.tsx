'use client';

import { useEffect, useState, type FormEvent } from 'react';
import { Button, Group, NumberInput, Paper, Text } from '@mantine/core';
import { staffPartnerApi } from '@/lib/partnerApi';
import { showError, showSuccess } from '@/lib/toast';

/** The fee charged for each partner key pickup or handover. It moved here from Partner operations. */
export function KeyRequestFee() {
  const [fee, setFee] = useState<number | string>('');
  const [loaded, setLoaded] = useState(false);
  const [busy, setBusy] = useState(false);
  const [loadFailed, setLoadFailed] = useState(false);
  // A failed read is shown in the box with a retry, not as a toast: nothing the user did failed, and a toast
  // would sit over the page (on a phone, over the navigation) until it timed out.
  const load = () => {
    setLoadFailed(false);
    staffPartnerApi('admin').keyRequests().then(list => { setFee(list.feeNet ?? 0); setLoaded(true); })
      .catch(() => setLoadFailed(true));
  };
  useEffect(load, []);
  const save = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const value = Number(fee);
    if (fee === '' || !Number.isFinite(value) || value < 0) { showError('Enter the fee as a number of euros, 0 or more.', 'Key request fee not saved'); return; }
    setBusy(true);
    try {
      const next = await staffPartnerApi('admin').setKeyRequestFee(value);
      setFee(next.feeNet ?? value);
      showSuccess(`Key request fee saved: €${Number(next.feeNet ?? value).toFixed(2)} excl. VAT. New key requests use this fee.`);
    } catch (error) { showError(error instanceof Error ? error.message : 'The fee could not be saved.', 'Key request fee not saved'); }
    finally { setBusy(false); }
  };
  return (
    <Paper component="form" aria-label="Key request fee" onSubmit={save} withBorder radius="lg" p="md" mb="md">
      <Group align="flex-end" wrap="wrap" gap="sm">
        <NumberInput label="Partner key request fee, excl. VAT (€)" min={0} max={10000} step={0.01} decimalScale={2} fixedDecimalScale value={fee} onChange={setFee} disabled={!loaded} w={260} />
        <Button type="submit" variant="default" disabled={!loaded} loading={busy}>Save fee</Button>
      </Group>
      <Text size="sm" c="dimmed" mt="xs">Charged for each key pickup or handover a partner requests. New requests use the new fee.</Text>
      {loadFailed && <Group gap="xs" mt="xs"><Text size="sm" role="alert">The key request fee could not be loaded.</Text><Button size="xs" variant="subtle" onClick={load}>Retry</Button></Group>}
    </Paper>
  );
}
