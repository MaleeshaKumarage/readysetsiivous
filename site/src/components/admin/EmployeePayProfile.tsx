'use client';

import { useEffect, useState } from 'react';
import { Button, Group, NumberInput, Stack, Text, TextInput } from '@mantine/core';
import { authorizedFetch } from '@/lib/adminApi';

type PayRate = { id: string; hourlyRate: number; effectiveFrom: string; source: string; createdUtc: string };
type PayProfile = { employeeId: string; rates: PayRate[]; legacy: { amount: number | null; confirmed: boolean } };

const SOURCE_LABELS: Record<string, string> = { manual: 'Entered', 'legacy-confirmed': 'Confirmed legacy rate' };
const MAX_RATE = 10000;
const money = (amount: number) => `€${amount.toFixed(2)} / h`;

// No min/max on the input: a clamp would turn a refused value into a valid one before this check runs.
function rateProblem(value: number | string): string | null {
  if (typeof value !== 'number' || !Number.isFinite(value)) return 'Enter an hourly rate.';
  if (value < 0) return 'Hourly rate cannot be negative. Enter 0 or a positive amount.';
  if (value > MAX_RATE) return `Hourly rate must be ${MAX_RATE} or less.`;
  return null;
}

async function problemText(response: Response, fallback: string) {
  const body = await response.json().catch(() => null) as { detail?: string; title?: string } | null;
  return body?.detail || body?.title || fallback;
}

export function EmployeePayProfile({ employeeId }: { employeeId: string }) {
  const [profile, setProfile] = useState<PayProfile | null>(null);
  const [loadError, setLoadError] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [newRate, setNewRate] = useState<number | string>('');
  const [newFrom, setNewFrom] = useState('');
  const [legacyFrom, setLegacyFrom] = useState('');

  const load = async () => {
    setLoadError('');
    try {
      const response = await authorizedFetch(`/api/v1/admin/employees/${employeeId}/pay-profile`, { cache: 'no-store' });
      if (!response.ok) throw new Error('Pay profile could not be loaded. Reload and retry.');
      setProfile(await response.json());
    } catch (failure) {
      setLoadError(failure instanceof Error ? failure.message : 'Pay profile could not be loaded. Reload and retry.');
    }
  };
  useEffect(() => { setProfile(null); load(); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [employeeId]);

  const apply = (next: PayProfile) => { setProfile(next); setError(''); };

  const addRate = async () => {
    const problem = rateProblem(newRate);
    if (problem) { setError(problem); return; }
    if (!newFrom) { setError('Choose the date this rate starts.'); return; }
    setBusy(true); setError(''); setMessage('');
    try {
      const response = await authorizedFetch(`/api/v1/admin/employees/${employeeId}/pay-rates`, {
        method: 'POST', body: JSON.stringify({ hourlyRate: Number(newRate), effectiveFrom: newFrom }), cache: 'no-store',
      });
      if (!response.ok) throw new Error(await problemText(response, 'The pay rate could not be saved. Reload and retry.'));
      apply(await response.json());
      setNewRate(''); setNewFrom('');
      setMessage('Pay rate added.');
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'The pay rate could not be saved.'); }
    finally { setBusy(false); }
  };

  const confirmLegacy = async () => {
    if (!legacyFrom) { setError('Choose the date the legacy rate started.'); return; }
    setBusy(true); setError(''); setMessage('');
    try {
      const response = await authorizedFetch(`/api/v1/admin/employees/${employeeId}/pay-profile/legacy-rate`, {
        method: 'POST', body: JSON.stringify({ effectiveFrom: legacyFrom }), cache: 'no-store',
      });
      if (!response.ok) throw new Error(await problemText(response, 'The legacy rate could not be confirmed. Reload and retry.'));
      apply(await response.json());
      setLegacyFrom('');
      setMessage('Legacy pay rate confirmed.');
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'The legacy rate could not be confirmed.'); }
    finally { setBusy(false); }
  };

  if (loadError) return <Stack gap="xs" data-testid="pay-profile"><Text size="sm" fw={500}>Pay profile</Text><Text role="alert" size="sm" c="red">{loadError}</Text><Button size="xs" variant="light" onClick={load} w="fit-content">Retry</Button></Stack>;
  if (!profile) return <Text size="sm" c="dimmed">Loading pay profile…</Text>;

  const needsLegacyConfirmation = profile.legacy.amount !== null && !profile.legacy.confirmed;

  return (
    <Stack gap="xs" data-testid="pay-profile">
      <Text size="sm" fw={500}>Pay profile</Text>
      {needsLegacyConfirmation && (
        <Stack gap="xs" p="xs" style={{ border: '1px solid var(--mantine-color-yellow-6)', borderRadius: 8 }}>
          <Text size="sm">The saved rate {money(profile.legacy.amount!)} has no start date. Confirm the date it started before it is used for pay.</Text>
          <Group gap="xs" wrap="wrap" align="flex-end">
            <TextInput type="date" label="Legacy rate starts" value={legacyFrom} onChange={e => setLegacyFrom(e.currentTarget.value)} />
            <Button size="sm" variant="light" loading={busy} onClick={confirmLegacy}>Confirm legacy rate</Button>
          </Group>
        </Stack>
      )}
      {profile.rates.length === 0 ? (
        <Text size="sm" c="dimmed">No dated pay rate yet. Visits without a rate are shown as exceptions and are never paid as zero.</Text>
      ) : (
        <ul aria-label="Pay rate history" style={{ listStyle: 'none', margin: 0, padding: 0 }}>
          {profile.rates.map(rate => (
            <li key={rate.id} style={{ display: 'flex', justifyContent: 'space-between', gap: 8, padding: '6px 0', borderBottom: '1px solid var(--mantine-color-default-border)' }}>
              <span>From {rate.effectiveFrom}</span>
              <span><strong>{money(rate.hourlyRate)}</strong> <Text component="span" size="xs" c="dimmed">{SOURCE_LABELS[rate.source] ?? rate.source}</Text></span>
            </li>
          ))}
        </ul>
      )}
      <Group gap="xs" wrap="wrap" align="flex-end">
        <NumberInput label="New hourly rate" decimalScale={2} value={newRate} onChange={setNewRate} />
        <TextInput type="date" label="Effective from" value={newFrom} onChange={e => setNewFrom(e.currentTarget.value)} />
        <Button size="sm" loading={busy} onClick={addRate}>Add rate</Button>
      </Group>
      {message && <Text size="sm" c="teal" role="status">{message}</Text>}
      {error && <Text role="alert" size="sm" c="red">{error}</Text>}
    </Stack>
  );
}
