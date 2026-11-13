'use client';

import { Group, Modal, Paper, Stack, Text, Title } from '@mantine/core';
import { CalendarClock, ListChecks, Receipt, Users } from 'lucide-react';
import { Badge } from '@/ds';
import type { Shift } from '@/lib/adminApi';

const DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

/** One time format for every schedule type: HH:mm, seconds dropped. */
const hm = (time?: string | null) => (time ?? '').slice(0, 5);

/** Schedule summary shown in the shift list and workspace. */
export function scheduleLabel(shift: Shift) {
  const s = shift.schedule;
  if (s.type === 0) return `Daily ${hm(s.dailyStart)}–${hm(s.dailyEnd)}`;
  if (s.type === 1) return `Varied times: ${Object.keys(s.dailyTimes ?? {}).join(', ')}`;
  if (s.type === 2) return s.weeklyDays?.length
    ? `${s.weeklyDays.map((d) => DAYS[d]).join(', ')} ${hm(s.weeklyStart)}–${hm(s.weeklyEnd)}`
    : `${DAYS[s.weeklyDay ?? 0]} ${hm(s.weeklyStart)}–${hm(s.weeklyEnd)}`;
  if (s.type === 3) return `Bi-weekly ${DAYS[s.biWeeklyDay ?? 0]}${s.biWeeklyStart && s.biWeeklyEnd ? ` ${hm(s.biWeeklyStart)}–${hm(s.biWeeklyEnd)}` : ''}`;
  if (s.type === 5) return `Monthly day ${s.monthlyDay}${s.monthlyStart && s.monthlyEnd ? ` ${hm(s.monthlyStart)}–${hm(s.monthlyEnd)}` : ''}`;
  if (s.type === 4) return shift.isBranchMain ? 'Visit shift (numbered visits)' : 'On-call';
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
  if (shift.isBranchMain) return 'Admin: add a visit. It is offered to the default cleaner, or to the cleaner you choose.';
  if (shift.schedule.type === 4) return 'Admin: schedule a dated visit, then assign a cleaner.';
  if (employeeCount === undefined) return 'Assignments loading.';
  if (employeeCount === 0) return 'Admin: assign employees to this shift.';
  return 'Assigned cleaners see visits in My shifts. Admin: review visit changes under Occurrences.';
}

export type ShiftWorkspaceProps = {
  shift: Shift;
  employeeCount: number | undefined;
  templateName: string | null;
  onClose: () => void;
};

/**
 * Per-shift overview: status, the next step and each section's details. Every action lives in the
 * "More actions" menu on the shift row, so the modal never repeats a button from that menu.
 */
export function ShiftWorkspace({ shift, employeeCount, templateName, onClose }: ShiftWorkspaceProps) {
  const isMain = Boolean(shift.isBranchMain);
  const isOnCall = shift.schedule.type === 4 && !isMain;
  const status = !shift.isActive ? { label: 'Inactive', variant: 'subtle' as const } : shift.isCompleted ? { label: 'Completed', variant: 'navy' as const } : { label: 'Active', variant: 'green' as const };
  return (
    <Modal opened onClose={onClose} title={`Manage — ${shift.name}`} size="lg" radius="md" className="shift-workspace">
      <Stack gap="md">
        <Group gap="xs" wrap="wrap" aria-label="Shift status">
          <Badge variant={status.variant} size="md">{status.label}</Badge>
        </Group>
        <Paper className="shift-next-step" p="sm" radius="md" role="status">
          <Text size="sm"><strong>Next step:</strong> {nextStep(shift, employeeCount)}</Text>
        </Paper>
        <Text size="xs" c="dimmed">To change this shift, use the More actions menu on its row.</Text>

        <Stack gap={4} component="section" aria-labelledby={`ws-schedule-${shift.id}`}>
          <Title order={5} id={`ws-schedule-${shift.id}`}><CalendarClock size={16} aria-hidden="true" className="shift-section-icon" />Schedule</Title>
          <Text size="sm">{scheduleLabel(shift)}</Text>
        </Stack>

        <Stack gap={4} component="section" aria-labelledby={`ws-assignees-${shift.id}`}>
          <Title order={5} id={`ws-assignees-${shift.id}`}><Users size={16} aria-hidden="true" className="shift-section-icon" />Assignees</Title>
          {isMain ? <Text size="sm">Each visit has its own cleaner. Set the default cleaner for new visits under Occurrences.</Text>
            : <Text size="sm">{employeeCount === undefined ? 'Loading assignments…' : `${employeeCount} assigned`}</Text>}
        </Stack>

        <Stack gap={4} component="section" aria-labelledby={`ws-visits-${shift.id}`}>
          <Title order={5} id={`ws-visits-${shift.id}`}><ListChecks size={16} aria-hidden="true" className="shift-section-icon" />Visits</Title>
          {isOnCall && <Text size="sm">On-call shifts have no automatic visits. Schedule each one with a date, time and cleaner.</Text>}
          {isMain && <Text size="sm">Every booking and every added visit for this property is a numbered visit on this shift.</Text>}
          {!isMain && !isOnCall && <Text size="sm">Visits follow the schedule above.</Text>}
        </Stack>

        <Stack gap={4} component="section" aria-labelledby={`ws-checklist-${shift.id}`}>
          <Title order={5} id={`ws-checklist-${shift.id}`}><ListChecks size={16} aria-hidden="true" className="shift-section-icon" />Checklist</Title>
          <Text size="sm">{templateName ?? 'No checklist template'}</Text>
        </Stack>

        <Stack gap={4} component="section" aria-labelledby={`ws-pricing-${shift.id}`}>
          <Title order={5} id={`ws-pricing-${shift.id}`}><Receipt size={16} aria-hidden="true" className="shift-section-icon" />Pricing</Title>
          <Text size="sm">{isMain ? 'Each visit uses the property price in force on its date. A partner booking keeps the price it was booked at.' : 'Pricing is set from the More actions menu.'}</Text>
        </Stack>
      </Stack>
    </Modal>
  );
}
