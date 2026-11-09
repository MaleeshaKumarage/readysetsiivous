'use client';

import { Button, Group, Modal, Stack, Text, Title } from '@mantine/core';
import type { Shift } from '@/lib/adminApi';

const DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

/** One time format for every schedule type: HH:mm, seconds dropped. */
const hm = (time?: string | null) => (time ?? '').slice(0, 5);

/** Schedule summary shown in the shift list and workspace. */
export function scheduleLabel(shift: Shift) {
  const s = shift.schedule;
  if (s.type === 0) return `Daily ${hm(s.dailyStart)}–${hm(s.dailyEnd)}`;
  if (s.type === 1) return `Daily by day (${Object.keys(s.dailyTimes ?? {}).length} days)`;
  if (s.type === 2) return s.weeklyDays?.length
    ? `${s.weeklyDays.map((d) => DAYS[d]).join(', ')} ${hm(s.weeklyStart)}–${hm(s.weeklyEnd)}`
    : `${DAYS[s.weeklyDay ?? 0]} ${hm(s.weeklyStart)}–${hm(s.weeklyEnd)}`;
  if (s.type === 3) return `Bi-weekly ${DAYS[s.biWeeklyDay ?? 0]}`;
  if (s.type === 5) return `Monthly day ${s.monthlyDay}${s.monthlyStart && s.monthlyEnd ? ` ${hm(s.monthlyStart)}–${hm(s.monthlyEnd)}` : ''}`;
  if (s.type === 4) return 'On-call';
  if (s.type === 6) return `One-time ${s.oneTimeDate?.slice(0, 10)} ${hm(s.oneTimeStart)}–${hm(s.oneTimeEnd)}`;
  return '';
}

/**
 * Who acts next, derived only from fields the shift list already returns.
 * Request, offer and in-progress states are not on the shift, so they are not shown here.
 */
export function nextStep(shift: Shift, employeeCount: number | undefined) {
  if (!shift.isActive) return 'Admin: reactivate to schedule again, or delete if permanent removal is intended.';
  if (shift.isCompleted) return 'Completed. No further visits.';
  if (shift.schedule.type === 4) return 'Admin: schedule a dated visit, then assign a cleaner.';
  if (employeeCount === undefined) return 'Assignments loading.';
  if (employeeCount === 0) return 'Admin: assign employees to this shift.';
  return 'Assigned cleaners see visits in My shifts. Admin: review visit changes under Occurrences.';
}

export type ShiftWorkspaceProps = {
  shift: Shift;
  employeeCount: number | undefined;
  templateName: string | null;
  busy: boolean;
  isAdmin: boolean;
  onClose: () => void;
  onEdit: () => void;
  onPricing: () => void;
  onAssign: () => void;
  onOccurrences: () => void;
  onScheduleOneTime: () => void;
  onTemplate: () => void;
  onDeactivate: () => void;
  onReactivate: () => void;
  onDelete: () => void;
};

/** Per-shift workspace: each section opens the existing editor, dialog or panel for that task. */
export function ShiftWorkspace({ shift, employeeCount, templateName, busy, isAdmin, onClose, onEdit, onPricing, onAssign, onOccurrences, onScheduleOneTime, onTemplate, onDeactivate, onReactivate, onDelete }: ShiftWorkspaceProps) {
  const isOnCall = shift.schedule.type === 4;
  return (
    <Modal opened onClose={onClose} title={`Manage — ${shift.name}`} size="lg" radius="md" className="shift-workspace">
      <Stack gap="md">
        <Text size="sm" fw={500} role="status">Next step: {nextStep(shift, employeeCount)}</Text>

        <Stack gap={4} component="section" aria-labelledby={`ws-schedule-${shift.id}`}>
          <Title order={5} id={`ws-schedule-${shift.id}`}>Schedule</Title>
          <Text size="sm">{scheduleLabel(shift)}</Text>
          {isAdmin && <Group><Button size="xs" variant="light" onClick={onEdit}>Edit schedule</Button></Group>}
        </Stack>

        <Stack gap={4} component="section" aria-labelledby={`ws-assignees-${shift.id}`}>
          <Title order={5} id={`ws-assignees-${shift.id}`}>Assignees</Title>
          <Text size="sm">{employeeCount === undefined ? 'Loading assignments…' : `${employeeCount} assigned`}</Text>
          {shift.isActive && <Group><Button size="xs" variant="light" disabled={busy} onClick={onAssign}>Assign employees</Button></Group>}
        </Stack>

        <Stack gap={4} component="section" aria-labelledby={`ws-visits-${shift.id}`}>
          <Title order={5} id={`ws-visits-${shift.id}`}>Visits</Title>
          {isOnCall && <Text size="sm">On-call shifts have no automatic visits. Schedule each one with a date, time and cleaner.</Text>}
          {shift.isActive && <Group gap="xs">
            <Button size="xs" variant="light" onClick={onOccurrences}>Occurrences</Button>
            {isOnCall && <Button size="xs" variant="light" onClick={onScheduleOneTime}>Schedule one-time visit</Button>}
          </Group>}
        </Stack>

        <Stack gap={4} component="section" aria-labelledby={`ws-checklist-${shift.id}`}>
          <Title order={5} id={`ws-checklist-${shift.id}`}>Checklist</Title>
          <Text size="sm">{templateName ?? 'No checklist template'}</Text>
          {isAdmin && shift.isActive && <Group><Button size="xs" variant="light" onClick={onTemplate}>Checklist template</Button></Group>}
        </Stack>

        <Stack gap={4} component="section" aria-labelledby={`ws-pricing-${shift.id}`}>
          <Title order={5} id={`ws-pricing-${shift.id}`}>Pricing</Title>
          {isAdmin && <Group><Button size="xs" variant="light" onClick={onPricing}>Pricing</Button></Group>}
        </Stack>

        <Group gap="xs" justify="flex-end">
          {shift.isActive && <Button size="xs" variant="subtle" color="red" disabled={busy} onClick={onDeactivate}>Deactivate</Button>}
          {!shift.isActive && <Button size="xs" disabled={busy} onClick={onReactivate}>Reactivate</Button>}
          {!shift.isActive && <Button size="xs" color="red" variant="subtle" disabled={busy} onClick={onDelete}>Delete</Button>}
        </Group>
      </Stack>
    </Modal>
  );
}
