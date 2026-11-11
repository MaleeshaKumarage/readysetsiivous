'use client';

import { useEffect, useState } from 'react';
import { Button, Group, Modal, Select, Stack, Text, Textarea, TextInput } from '@mantine/core';
import { adminBranchVisits, adminCompanies, type Branch, type BranchVisit, type Employee } from '@/lib/adminApi';
import { CompanyPicker } from '@/components/admin/CompanyPicker';

const DEFAULT_CLEANER = 'default';
const NO_CLEANER = 'none';

/**
 * Adds one visit to a property's main shift. Only the property, the dates and the times are asked for:
 * company details and the price come from the property. A date range is one visit.
 */
export function AddVisitDialog({ opened, onClose, onAdded, employees, timeZoneId, preset }: {
  opened: boolean;
  onClose: () => void;
  onAdded: (visit: BranchVisit, propertyName: string) => void;
  employees: Employee[];
  timeZoneId: string;
  /** Set when the dialog is opened from a shift: the property is fixed. */
  preset?: { companyId: string; branchId: string; propertyName: string } | null;
}) {
  const [companyId, setCompanyId] = useState<string | null>(null);
  const [branches, setBranches] = useState<Branch[]>([]);
  const [branchId, setBranchId] = useState<string | null>(null);
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');
  const [startTime, setStartTime] = useState('08:00');
  const [endTime, setEndTime] = useState('10:00');
  const [notes, setNotes] = useState('');
  const [cleaner, setCleaner] = useState<string>(DEFAULT_CLEANER);
  const [cleaningType, setCleaningType] = useState<string>('Normal');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    if (!opened) return;
    setCompanyId(preset?.companyId ?? null); setBranchId(preset?.branchId ?? null); setBranches([]);
    setFromDate(''); setToDate(''); setStartTime('08:00'); setEndTime('10:00'); setNotes(''); setCleaner(DEFAULT_CLEANER); setCleaningType('Normal'); setError('');
  }, [opened, preset?.companyId, preset?.branchId]);

  useEffect(() => {
    if (!opened || preset || !companyId) return;
    let current = true;
    adminCompanies.get(companyId).then(detail => { if (current && detail?.company.id === companyId) setBranches(detail.branches); });
    return () => { current = false; };
  }, [opened, preset, companyId]);

  const propertyName = preset?.propertyName ?? branches.find(branch => branch.id === branchId)?.name ?? 'the property';
  const lastDay = toDate || fromDate;
  const days = fromDate && lastDay >= fromDate ? Math.round((Date.parse(lastDay) - Date.parse(fromDate)) / 86400000) + 1 : 0;

  const save = async () => {
    if (saving) return;
    if (!branchId) { setError('Choose the property for this visit.'); return; }
    if (!fromDate) { setError('Choose the date of the visit.'); return; }
    if (lastDay < fromDate) { setError('The last day must be on or after the first day.'); return; }
    if (!startTime || !endTime) { setError('Enter the start and end time.'); return; }
    if (lastDay === fromDate && endTime <= startTime) { setError('The end time must be after the start time.'); return; }
    setSaving(true); setError('');
    const result = await adminBranchVisits.add({
      branchId, fromDate, toDate: lastDay, startTime, endTime, notes: notes.trim() || null, cleaningType,
      employeeId: cleaner === DEFAULT_CLEANER || cleaner === NO_CLEANER ? null : cleaner,
      useDefaultEmployee: cleaner === DEFAULT_CLEANER,
    });
    setSaving(false);
    // A failed save keeps everything typed, so it can be corrected and sent again.
    if (!result.ok) { setError(result.message); return; }
    onAdded(result.value, propertyName);
  };

  return (
    <Modal opened={opened} onClose={() => { if (!saving) onClose(); }} closeButtonProps={{ disabled: saving }} closeOnEscape={!saving}
      closeOnClickOutside={!saving} title="Add visit" size="lg" radius="md">
      <Stack gap="sm">
        <Text size="sm" c="dimmed">The visit is added to the property&apos;s shift. Company details and the price come from the property. Times use the tenant time zone: {timeZoneId}.</Text>
        {preset ? <TextInput label="Property" readOnly value={preset.propertyName} /> : <>
          <CompanyPicker allowInactive={false} value={companyId} onChange={value => { setBranches([]); setCompanyId(value); setBranchId(null); }} />
          <Select label="Property" placeholder={companyId ? 'Choose a property' : 'Choose a company first'} disabled={!companyId} allowDeselect={false}
            data={branches.filter(branch => branch.isActive !== false).map(branch => ({ value: branch.id, label: branch.name }))} value={branchId} onChange={setBranchId} />
        </>}
        <Group grow wrap="wrap" gap="xs">
          <TextInput label="Date" type="date" required value={fromDate} onChange={event => setFromDate(event.target.value)} />
          <TextInput label="Last day (optional)" description="Leave empty for a single day." type="date" min={fromDate || undefined} value={toDate} onChange={event => setToDate(event.target.value)} />
        </Group>
        <Group grow wrap="wrap" gap="xs">
          <TextInput label="Start time" type="time" required value={startTime} onChange={event => setStartTime(event.target.value)} />
          <TextInput label="End time" type="time" required value={endTime} onChange={event => setEndTime(event.target.value)} />
        </Group>
        {days > 1 && <Text size="sm" role="status">One visit covering {days} days, from {fromDate} {startTime} to {lastDay} {endTime}.</Text>}
        <Select label="Cleaning type" allowDeselect={false} value={cleaningType} onChange={value => setCleaningType(value ?? 'Normal')}
          description="Every visit starts as Normal. You can change it later, including after the cleaning is done."
          data={[{ value: 'Normal', label: 'Normal' }, { value: 'Deep', label: 'Deep' }]} />
        <Select label="Cleaner" allowDeselect={false} searchable value={cleaner} onChange={value => setCleaner(value ?? DEFAULT_CLEANER)}
          description="The cleaner gets an offer. It is theirs once accepted, or at once if they accept work automatically."
          data={[{ value: DEFAULT_CLEANER, label: "Shift's default cleaner" }, { value: NO_CLEANER, label: 'Leave unassigned' },
            ...employees.filter(employee => employee.isActive && employee.role !== 'partner').map(employee => ({ value: employee.id, label: `${employee.firstName} ${employee.lastName}` }))]} />
        <Textarea label="Notes for the cleaner (optional)" maxLength={4000} autosize minRows={2} value={notes} onChange={event => setNotes(event.target.value)} />
        {error && <Text role="alert" c="red">{error}</Text>}
        <Group justify="flex-end">
          <Button variant="default" disabled={saving} onClick={onClose}>Cancel</Button>
          <Button loading={saving} onClick={save}>Add visit</Button>
        </Group>
      </Stack>
    </Modal>
  );
}
