'use client';

import { DsGrid } from '@/components/ds/DsGrid';
import { useEffect, useState } from 'react';
import { Button, Group, Loader, Modal, NumberInput, Select, Stack, Table, Text, TextInput } from '@mantine/core';
import { invoiceBillingApi, type BranchPriceVersion, type PricingPlan, type ShiftPricing } from '@/lib/invoiceBillingApi';
import type { Shift, ShiftOccurrence } from '@/lib/adminApi';

// Legacy on-call and one-time visits are priced per completed visit.
const isVisitBased = (shift: Shift) => shift.schedule.type === 4 || shift.schedule.type === 6;
const modeLabel: Record<string, string> = { Monthly: 'Fixed monthly fee', Hourly: 'Per scheduled hour', PerOccurrence: 'Per completed visit' };
const byDate = (a: BranchPriceVersion, b: BranchPriceVersion) => a.effectiveFrom.localeCompare(b.effectiveFrom);
export function ShiftPricingDialog({ shift, occurrence, onClose }: { shift: Shift; occurrence?: ShiftOccurrence; onClose: () => void }) {
  const [pricing, setPricing] = useState<ShiftPricing | null>(null);
  const [plan, setPlan] = useState<PricingPlan>({ mode: isVisitBased(shift) ? 'PerOccurrence' : 'Monthly', rateNet: 0, vatRatePercent: 25.5, effectiveFrom: shift.validFrom?.slice(0, 10) || new Date().toISOString().slice(0, 10) });
  const [versionFrom, setVersionFrom] = useState('');
  const [reason, setReason] = useState(''); const [error, setError] = useState(''); const [busy, setBusy] = useState(false);
  const versions = pricing?.branchPriceVersions ?? [];
  // A shift billed from its branch picks one branch price version. Per-visit overrides still apply on top.
  const linked = !occurrence && !!pricing?.branchPriceEffectiveFrom && versions.length > 0;
  useEffect(() => {
    let active = true;
    invoiceBillingApi.pricing(shift.id).then(value => {
      if (!active) return; setPricing(value);
      if (value.branchPriceVersions?.length && value.branchPriceEffectiveFrom) setVersionFrom(value.branchPriceEffectiveFrom);
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
      else if (linked) await invoiceBillingApi.selectBranchPrice(shift.id, versionFrom);
      else await invoiceBillingApi.savePricing(shift.id, plan);
      onClose();
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'Could not save pricing.'); }
    finally { setBusy(false); }
  };
  return <Modal opened onClose={() => !busy && onClose()} title={`${occurrence ? 'Visit price' : 'Shift pricing'} — ${shift.name}`} size="lg">
    <Stack>
      <Text size="sm">Prices are before VAT and appear only on invoices. Prices are set per property, under its price history. Hourly billing uses scheduled hours. Monthly fees are prorated for partial months; invoice previews allow an explained override.</Text>
      {!pricing && !error && <Loader size="sm" />}
      {linked && <Select label="Price version" description="The property price this shift is billed at. Defaults to the price in force today." value={versionFrom} onChange={value => value && setVersionFrom(value)} allowDeselect={false}
        data={[...versions].sort(byDate).map(v => ({ value: v.effectiveFrom, label: `From ${v.effectiveFrom} · ${modeLabel[v.mode] ?? v.mode} · €${v.priceNet.toFixed(2)} excl. VAT · VAT ${v.vatRatePercent}%` }))} />}
      {!occurrence && !linked && <><Select label="Pricing basis" value={plan.mode} onChange={value => setPlan({ ...plan, mode: value as PricingPlan['mode'] })} data={[{ value: 'Monthly', label: 'Fixed monthly fee' }, { value: 'Hourly', label: 'Per scheduled hour' }, ...(isVisitBased(shift) ? [{ value: 'PerOccurrence', label: shift.schedule.type === 4 ? 'Per completed visit (on-call)' : 'Per completed visit' }] : [])]} allowDeselect={false} />
        <TextInput label="Effective from" type="date" required value={plan.effectiveFrom} onChange={event => setPlan({ ...plan, effectiveFrom: event.target.value })} />
        <NumberInput label="VAT (%)" min={0} max={50} decimalScale={2} value={plan.vatRatePercent} onChange={value => setPlan({ ...plan, vatRatePercent: Number(value) })} /></>}
      {(occurrence || !linked) && <NumberInput label={occurrence ? 'Visit price before VAT (€)' : 'Rate before VAT (€)'} min={0} decimalScale={2} required value={plan.rateNet} onChange={value => setPlan({ ...plan, rateNet: Number(value) })} />}
      {occurrence && <TextInput label="Reason for visit price override" required maxLength={500} value={reason} onChange={event => setReason(event.target.value)} />}
      {!occurrence && linked && <><Text fw={600}>Price history for this property</Text><DsGrid<(typeof versions)[number]>
        label="Price versions"
        rows={[...versions].sort(byDate)}
        getRowId={(item) => item.effectiveFrom}
        columns={[
          { headerName: 'From', colId: 'from', valueGetter: p => p.data?.effectiveFrom ?? '' },
          { headerName: 'Basis', colId: 'basis', valueGetter: p => p.data ? (modeLabel[p.data.mode] ?? p.data.mode) : '' },
          { headerName: 'Net rate', colId: 'net', valueGetter: p => p.data ? `€${p.data.priceNet.toFixed(2)}` : '' },
          { headerName: 'VAT', colId: 'vat', valueGetter: p => p.data ? `${p.data.vatRatePercent}%` : '' },
        ]}
      /></>}
      {!occurrence && !linked && pricing && pricing.plans.length > 0 && <><Text fw={600}>Pricing history</Text><DsGrid<(typeof pricing.plans)[number]>
          label="Pricing history"
          rows={pricing.plans}
          getRowId={(item) => item.effectiveFrom}
          columns={[
            { headerName: 'From', colId: 'from', valueGetter: p => p.data?.effectiveFrom ?? '' },
            { headerName: 'Basis', colId: 'basis', valueGetter: p => p.data?.mode ?? '' },
            { headerName: 'Net rate', colId: 'net', valueGetter: p => p.data ? `€${p.data.rateNet.toFixed(2)}` : '' },
            { headerName: 'VAT', colId: 'vat', valueGetter: p => p.data ? `${p.data.vatRatePercent}%` : '' },
          ]}
        /></>}
      {error && <Text role="alert" c="red">{error}</Text>}
      <Group justify="flex-end"><Button variant="default" disabled={busy} onClick={onClose}>Cancel</Button><Button loading={busy} disabled={!pricing || (!occurrence && !linked && !plan.effectiveFrom) || (linked && !versionFrom) || (!!occurrence && !reason.trim())} onClick={save}>Save price</Button></Group>
    </Stack>
  </Modal>;
}
