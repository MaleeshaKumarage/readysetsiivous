'use client';

import { useEffect, useMemo, useState } from 'react';
import { Alert, Badge, Button, Group, Loader, Paper, ScrollArea, Stack, Table, Text, TextInput, Title } from '@mantine/core';
import { adminEmployees, adminShifts, adminTenant, type Employee, type EmployeeDaySchedule, type OverlapFinding, type ScheduleVisit } from '@/lib/adminApi';

const DAYS = 7;

function isoDate(date: Date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}
function addDays(day: string, count: number) {
  const [y, m, d] = day.split('-').map(Number);
  return isoDate(new Date(y, m - 1, d + count));
}
function mondayOf(day: string) {
  const [y, m, d] = day.split('-').map(Number);
  const weekday = new Date(y, m - 1, d).getDay();
  return addDays(day, weekday === 0 ? -6 : 1 - weekday);
}

export default function EmployeeSchedulePage() {
  const [weekStart, setWeekStart] = useState(() => mondayOf(isoDate(new Date())));
  const [schedule, setSchedule] = useState<EmployeeDaySchedule[] | null>(null);
  const [overlaps, setOverlaps] = useState<OverlapFinding[]>([]);
  const [employees, setEmployees] = useState<Employee[] | null>(null);
  const [timeZone, setTimeZone] = useState<string | undefined>(undefined);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [reloadKey, setReloadKey] = useState(0);
  const weekEnd = addDays(weekStart, DAYS - 1);
  const days = useMemo(() => Array.from({ length: DAYS }, (_, index) => addDays(weekStart, index)), [weekStart]);

  useEffect(() => {
    adminTenant.get().then(tenant => { if (tenant?.timeZoneId) setTimeZone(tenant.timeZoneId); }).catch(() => undefined);
  }, []);

  useEffect(() => {
    let cancelled = false;
    setLoading(true); setError('');
    Promise.all([adminShifts.employeeSchedule(weekStart, weekEnd), adminShifts.overlapAudit(weekStart, weekEnd), adminEmployees.list()])
      .then(([scheduleRows, overlapRows, staff]) => {
        if (cancelled) return;
        if (!scheduleRows || !staff) { setError('Could not load the schedule. Check your connection, then retry.'); setSchedule(null); return; }
        setSchedule(scheduleRows); setOverlaps(overlapRows ?? []); setEmployees(staff);
      })
      .catch(() => { if (!cancelled) setError('Could not load the schedule. Check your connection, then retry.'); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [weekStart, weekEnd, reloadKey]);

  // Times are shown in the tenant zone, the same zone the server uses to group visits by day.
  const timeOf = (value: string) => new Date(value).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', timeZone });
  const visitsFor = (employeeId: string, day: string) => schedule?.find(row => row.employeeId === employeeId && row.localDay.slice(0, 10) === day)?.visits ?? [];
  // Flag a cell when one of its visits is part of an overlap finding for this cleaner.
  const sameVisit = (a: ScheduleVisit, b: ScheduleVisit) => a.shiftId === b.shiftId && a.startUtc === b.startUtc;
  const overlapFor = (employeeId: string, day: string) => visitsFor(employeeId, day).some(visit => overlaps.some(finding => finding.employeeId === employeeId
    && (sameVisit(finding.earlier, visit) || sameVisit(finding.later, visit))));
  const staff = (employees ?? []).filter(employee => employee.isActive && employee.role !== 'partner');
  const visitLabel = (visit: ScheduleVisit) => `${visit.shiftName} ${timeOf(visit.startUtc)}–${timeOf(visit.endUtc)}`;

  return (
    <Stack p="md" gap="md">
      <Title order={2}>Employee schedule</Title>
      <Group align="flex-end">
        <TextInput type="date" label="Week starting" value={weekStart} onChange={event => { if (event.currentTarget.value) setWeekStart(mondayOf(event.currentTarget.value)); }} />
        <Button variant="default" onClick={() => setWeekStart(addDays(weekStart, -DAYS))}>Previous week</Button>
        <Button variant="default" onClick={() => setWeekStart(addDays(weekStart, DAYS))}>Next week</Button>
        <Button variant="subtle" onClick={() => setWeekStart(mondayOf(isoDate(new Date())))}>This week</Button>
      </Group>
      {loading && <Loader aria-label="Loading schedule" />}
      {error && <Alert color="red" title="Schedule unavailable"><Stack gap="xs"><Text>{error}</Text><Button size="xs" variant="light" onClick={() => setReloadKey(key => key + 1)}>Retry</Button></Stack></Alert>}
      {!loading && !error && schedule && staff.length === 0 && <Text>No active cleaners yet. Add employees, then their visits appear here.</Text>}
      {!loading && !error && schedule && staff.length > 0 && schedule.length === 0 && <Text>No visits this week. Choose another week or check the shift schedules.</Text>}
      {!loading && !error && schedule && staff.length > 0 && (
        <ScrollArea type="auto">
          <Table withTableBorder withColumnBorders miw={860}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Cleaner</Table.Th>
                {days.map(day => <Table.Th key={day}>{new Date(`${day}T12:00:00`).toLocaleDateString(undefined, { weekday: 'short', day: 'numeric', month: 'short' })}</Table.Th>)}
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {staff.map(employee => (
                <Table.Tr key={employee.id}>
                  <Table.Th>{employee.firstName} {employee.lastName}</Table.Th>
                  {days.map(day => {
                    const visits = visitsFor(employee.id, day);
                    return (
                      <Table.Td key={day} aria-label={`${employee.firstName} ${employee.lastName}, ${day}`}>
                        {overlapFor(employee.id, day) && <Badge color="red" size="sm" mb={4}>Overlap</Badge>}
                        <Stack gap={4}>
                          {visits.length === 0 && <Text size="sm" c="dimmed">Free</Text>}
                          {visits.map(visit => <Paper key={`${visit.shiftId}-${visit.startUtc}`} p={4} withBorder><Text size="xs">{visitLabel(visit)}</Text></Paper>)}
                        </Stack>
                      </Table.Td>
                    );
                  })}
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      )}
    </Stack>
  );
}
