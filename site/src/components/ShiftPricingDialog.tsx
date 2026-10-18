'use client';

import { useEffect, useState } from 'react';
import { Button, Group, Loader, Modal, NumberInput, Select, Stack, Table, Text, TextInput } from '@mantine/core';
import { invoiceBillingApi, type PricingPlan, type ShiftPricing } from '@/lib/invoiceBillingApi';
import type { Shift, ShiftOccurrence } from '@/lib/adminApi';

export function ShiftPricingDialog({ shift, occurrence, onClose }: { shift: Shift; occurrence?: ShiftOccurrence; onClose: () => void }) {
  const [pricing, setPricing] = useState<ShiftPricing | null>(null);
  const [plan, setPlan] = useState<PricingPlan>({ mode: shift.schedule.type === 4 ? 'PerOccurrence' : 'Monthly', rateNet: 0, vatRatePercent: 25.5, effectiveFrom: shift.validFrom?.slice(0, 10) || new Date().toISOString().slice(0, 10) });
  const [reason, setReason] = useState(''); const [error, setError] = useState(''); const [busy, setBusy] = useState(false);
  useEffect(() => {
    let active = true;
    invoiceBillingApi.pricing(shift.id).then(value => {
      if (!active) return; setPricing(value);
      const latest = [...value.plans].sort((a, b) => b.effectiveFrom.localeCompare(a.effectiveFrom))[0];
      if (latest) setPlan(latest);
      const override = value.overrides.find(item => item.occurrenceStartUtc === occurrence?.startUtc);
      if (override) { setPlan(current => ({ ...current, rateNet: override.rateNet })); setReason(override.reason); }
    }).catch(failure => active && setError(failure.message));
    return () => { active = false; };
  }, [shift.id, occurrence?.startUtc]);
  const save = async () => {
    setError(''); setBusy(true);
    try {
      if (occurrence) await invoiceBillingApi.saveOccurrencePrice(shift.id, { occurrenceStartUtc: occurrence.startUtc, rateNet: plan.rateNet, reason });
      else await invoiceBillingApi.savePricing(shift.id, plan);
      onClose();
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'Could not save pricing.'); }
    finally { setBusy(false); }
  };
  return <Modal opened onClose={() => !busy && onClose()} title={`${occurrence ? 'Visit price' : 'Shift pricing'} — ${shift.name}`} size="lg">
    <Stack>
      <Text size="sm">Prices are before VAT and appear only on invoices. Hourly billing uses scheduled hours. Monthly fees are prorated for partial months; invoice previews allow an explained override.</Text>
      {!pricing && !error && <Loader size="sm" />}
      {!occurrence && <><Select label="Pricing basis" value={plan.mode} onChange={value => setPlan({ ...plan, mode: value as PricingPlan['mode'] })} data={[{ value: 'Monthly', label: 'Fixed monthly fee' }, { value: 'Hourly', label: 'Per scheduled hour' }, ...(shift.schedule.type === 4 ? [{ value: 'PerOccurrence', label: 'Per completed visit (on-call)' }] : [])]} allowDeselect={false} />
        <TextInput label="Effective from" type="date" required value={plan.effectiveFrom} onChange={event => setPlan({ ...plan, effectiveFrom: event.target.value })} />
        <NumberInput label="VAT (%)" min={0} max={50} decimalScale={2} value={plan.vatRatePercent} onChange={value => setPlan({ ...plan, vatRatePercent: Number(value) })} /></>}
      <NumberInput label={occurrence ? 'Visit price before VAT (€)' : 'Rate before VAT (€)'} min={0} decimalScale={2} required value={plan.rateNet} onChange={value => setPlan({ ...plan, rateNet: Number(value) })} />
      {occurrence && <TextInput label="Reason for visit price override" required maxLength={500} value={reason} onChange={event => setReason(event.target.value)} />}
      {!occurrence && pricing && pricing.plans.length > 0 && <><Text fw={600}>Pricing history</Text><Table><Table.Thead><Table.Tr><Table.Th>From</Table.Th><Table.Th>Basis</Table.Th><Table.Th>Net rate</Table.Th><Table.Th>VAT</Table.Th></Table.Tr></Table.Thead><Table.Tbody>{pricing.plans.map(item => <Table.Tr key={item.effectiveFrom}><Table.Td>{item.effectiveFrom}</Table.Td><Table.Td>{item.mode}</Table.Td><Table.Td>€{item.rateNet.toFixed(2)}</Table.Td><Table.Td>{item.vatRatePercent}%</Table.Td></Table.Tr>)}</Table.Tbody></Table></>}
      {error && <Text role="alert" c="red">{error}</Text>}
      <Group justify="flex-end"><Button variant="default" disabled={busy} onClick={onClose}>Cancel</Button><Button loading={busy} disabled={!pricing || (!occurrence && !plan.effectiveFrom) || (!!occurrence && !reason.trim())} onClick={save}>Save price</Button></Group>
    </Stack>
  </Modal>;
}
