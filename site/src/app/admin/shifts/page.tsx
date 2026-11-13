'use client';

import { showError } from '@/lib/toast';
import { Fragment, useEffect, useRef, useState } from 'react';
import {
  Title, Button, Table, Modal, TextInput, Select, MultiSelect, Group, Stack, Text, Loader, ActionIcon, NumberInput, Menu,
} from '@mantine/core';
import { Plus, Trash2, Pencil, MoreHorizontal, Eye, UserPlus, CalendarDays, CalendarPlus, ListChecks, Receipt, PauseCircle, PlayCircle, ChevronDown, ChevronRight, Sparkles, UserRound, CircleCheck, Clock } from 'lucide-react';
import {
  adminShifts, adminBranchVisits, adminCompanies, adminEmployees, adminTenant,
  type Shift, type Branch, type Employee, type ShiftAssignment, type ShiftOccurrence,
} from '@/lib/adminApi';
import { ShiftPricingDialog } from '@/components/ShiftPricingDialog';
import { VisitAssigneesDialog } from '@/components/VisitAssigneesDialog';
import { isAdmin } from '@/lib/auth';
import { adminQualityCycle, type QualityCycleTemplate } from '@/lib/qualityCycleApi';
import { CompanyPicker } from '@/components/admin/CompanyPicker';
import { AddVisitDialog } from '@/components/admin/AddVisitDialog';
import { visitLabel } from '@/lib/visitLabel';
import { LifecycleTabs, type LifecycleStatus } from '@/components/LifecycleTabs';
import { TodayKpis } from '@/components/admin/TodayKpis';
import { ListPager, PAGE_SIZE } from '@/components/ListPager';
import { ShiftWorkspace, scheduleLabel } from '@/components/admin/ShiftWorkspace';
import { autoShiftName } from '@/lib/shiftName';
import { employeeLabel } from '@/lib/employeeLabel';
import { DEEP_SURCHARGE_EUR } from '@/lib/cleaningPricing';
import { Badge } from '@/ds';

// The price version in force today (or the earliest when all start later): the default billing price for a new shift.
const defaultPriceVersion = (versions?: { effectiveFrom: string }[] | null) => {
  const today = new Date().toISOString().slice(0, 10);
  const sorted = [...(versions ?? [])].sort((a, b) => a.effectiveFrom.localeCompare(b.effectiveFrom));
  return (sorted.filter(v => v.effectiveFrom <= today).pop() ?? sorted[0])?.effectiveFrom ?? null;
};

const DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const SCHEDULE_TYPES = [
  { value: '0', label: 'Daily' },
  { value: '1', label: 'Varied times by weekday' },
  { value: '2', label: 'Specific days' },
  { value: '3', label: 'Bi-weekly' },
  { value: '4', label: 'On-call / flexible' },
  { value: '5', label: 'Monthly' },
  { value: '6', label: 'One-time' },
];

/** What a visit's work is doing now, in words an admin can act on. */
const WORK_STATUS_LABEL: Record<string, string> = {
  Completed: 'Done',
  InProgress: 'In progress',
  Scheduled: 'Scheduled',
  NeedsAction: 'Not started',
};

const OCCURRENCE_STATUSES = [
  { value: '1', label: 'Move / change time' },
  { value: '2', label: 'Customer cancelled' },
  { value: '3', label: 'Company cancelled' },
  { value: '4', label: 'Extra visit' },
];

// Performance Optimization: Cache Intl.DateTimeFormat instances by timeZone.
// Reusing formatter instances avoids expensive re-instantiation overhead in JS engines (~10x-20x faster)
// and eliminates GC pressure caused by repeatedly reconstructing formatter instances and intermediate
// object spreading via .reduce() inside tight conversion loops.
const dateTimeFormatterCache = new Map<string, Intl.DateTimeFormat>();

function getDateTimeFormatter(timeZone: string): Intl.DateTimeFormat {
  let fmt = dateTimeFormatterCache.get(timeZone);
  if (!fmt) {
    fmt = new Intl.DateTimeFormat('en-CA', {
      timeZone,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      hourCycle: 'h23',
    });
    dateTimeFormatterCache.set(timeZone, fmt);
  }
  return fmt;
}

const localInputValue = (utc: string, timeZone: string) => {
  const parts = getDateTimeFormatter(timeZone).formatToParts(new Date(utc));
  let year = '', month = '', day = '', hour = '', minute = '';
  for (let i = 0; i < parts.length; i++) {
    const p = parts[i];
    if (p.type === 'year') year = p.value;
    else if (p.type === 'month') month = p.value;
    else if (p.type === 'day') day = p.value;
    else if (p.type === 'hour') hour = p.value;
    else if (p.type === 'minute') minute = p.value;
  }
  return `${year}-${month}-${day}T${hour}:${minute}`;
};

const tenantLocalToUtc = (value: string, timeZone: string) => {
  const [date, time] = value.split('T');
  const [year, month, day] = date.split('-').map(Number);
  const [hour, minute] = time.split(':').map(Number);
  const wanted = Date.UTC(year, month - 1, day, hour, minute);
  let instant = wanted;
  const fmt = getDateTimeFormatter(timeZone);
  for (let attempt = 0; attempt < 4; attempt++) {
    const parts = fmt.formatToParts(new Date(instant));
    let y = 0, m = 0, d = 0, h = 0, min = 0;
    for (let i = 0; i < parts.length; i++) {
      const p = parts[i];
      if (p.type === 'year') y = Number(p.value);
      else if (p.type === 'month') m = Number(p.value);
      else if (p.type === 'day') d = Number(p.value);
      else if (p.type === 'hour') h = Number(p.value);
      else if (p.type === 'minute') min = Number(p.value);
    }
    const represented = Date.UTC(y, m - 1, d, h, min);
    if (represented === wanted) return new Date(instant).toISOString();
    instant += wanted - represented;
  }
  throw new Error('That local time does not exist in the tenant time zone. Choose another time.');
};

const addCalendarDays = (date: string, days: number) => {
  const [year, month, day] = date.split('-').map(Number);
  return new Date(Date.UTC(year, month - 1, day + days)).toISOString().slice(0, 10);
};

/** Active, Completed (finished one-time visits) or Inactive. Inactive wins over completed. */
const shiftView = (shift: Shift): LifecycleStatus => (!shift.isActive ? 'inactive' : shift.isCompleted ? 'completed' : 'active');

export default function ShiftsAdminPage() {
  const [pricingShift, setPricingShift] = useState<Shift | null>(null);
  const [pricingOccurrence, setPricingOccurrence] = useState<ShiftOccurrence | undefined>();
  const [shiftPage, setShiftPage] = useState(1);
  const [pageRows, setPageRows] = useState<Shift[]>([]);
  const [filterCompany, setFilterCompany] = useState<string | null>(null);
  const [filterType, setFilterType] = useState<string | null>(null);
  const [companyChoices, setCompanyChoices] = useState<{ value: string; label: string }[]>([]);
  const [pageTotal, setPageTotal] = useState(0);
  // Property details for the rows on this page, keyed by branch id. A failed load leaves the row on its shift name.
  const [propertyById, setPropertyById] = useState<Record<string, Branch>>({});
  useEffect(() => {
    let live = true;
    for (const companyId of new Set(pageRows.map(s => s.companyId))) {
      adminCompanies.get(companyId).then((d) => {
        if (!live || !d) return;
        setPropertyById(prev => ({ ...prev, ...Object.fromEntries(d.branches.map(b => [b.id, b])) }));
      }).catch(() => { /* the row keeps its shift name */ });
    }
    return () => { live = false; };
  }, [pageRows]);
  // The visits shown under an expanded row: the next 31 days from today, in tenant time. Separate from the visits popup.
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [expandedVisits, setExpandedVisits] = useState<ShiftOccurrence[]>([]);
  const [expandedLoading, setExpandedLoading] = useState(false);
  const [visitsOpen, setVisitsOpen] = useState(true);
  const [expandedFrom, setExpandedFrom] = useState('');
  const [expandedError, setExpandedError] = useState('');
  const expandGeneration = useRef(0);
  const toggleExpanded = async (shift: Shift) => {
    if (expandedId === shift.id) { setExpandedId(null); expandGeneration.current++; return; }
    const version = ++expandGeneration.current;
    setExpandedId(shift.id); setExpandedVisits([]); setExpandedError(''); setExpandedLoading(true); setVisitsOpen(true);
    try {
      const today = localInputValue(new Date().toISOString(), timeZoneId).slice(0, 10);
      setExpandedFrom(today);
      const fromUtc = tenantLocalToUtc(`${today}T00:00`, timeZoneId);
      const nextDayUtc = tenantLocalToUtc(`${addCalendarDays(today, 32)}T00:00`, timeZoneId);
      const result = await adminShifts.occurrences(shift.id, fromUtc, new Date(Date.parse(nextDayUtc) - 1).toISOString());
      if (!result) throw new Error('The visits could not be loaded. Collapse the row and expand it again to retry.');
      if (version === expandGeneration.current) setExpandedVisits(result);
    } catch (failure) {
      if (version === expandGeneration.current) setExpandedError(failure instanceof Error ? failure.message : 'The visits could not be loaded.');
    } finally {
      if (version === expandGeneration.current) setExpandedLoading(false);
    }
  };
  const [tabCounts, setTabCounts] = useState<{ active: number; completed: number; inactive: number } | null>(null);
  const [workspaceFetched, setWorkspaceFetched] = useState<Shift | null>(null);
  const [loading, setLoading] = useState(true);
  const [lifecycle, setLifecycle] = useState<LifecycleStatus>('active');
  const [listError, setListError] = useState('');
  const [shiftNotice, setShiftNotice] = useState('');
  const [timeZoneId, setTimeZoneId] = useState('Europe/Helsinki');
  const [schedulingReady, setSchedulingReady] = useState(false);
  const [restoring, setRestoring] = useState<string | null>(null);
  const [branches, setBranches] = useState<Branch[]>([]);
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [modalOpen, setModalOpen] = useState(false);
  const [editingShift, setEditingShift] = useState<Shift | null>(null);
  const [workspaceId, setWorkspaceId] = useState<string | null>(null);

  const [companyId, setCompanyId] = useState<string | null>(null);
  const [branchId, setBranchId] = useState<string | null>(null);
  const [priceFrom, setPriceFrom] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [validFrom, setValidFrom] = useState('');
  const [validUntil, setValidUntil] = useState('');
  const [scheduleType, setScheduleType] = useState('');
  const [start, setStart] = useState('08:00');
  const [end, setEnd] = useState('17:00');
  const [weekDays, setWeekDays] = useState<string[]>([]);
  const [weekParity, setWeekParity] = useState('1');
  const [monthlyDay, setMonthlyDay] = useState<string | number>(1);
  const [oneTimeDate, setOneTimeDate] = useState('');
  const [oneTimeStart, setOneTimeStart] = useState('08:00');
  const [oneTimeEnd, setOneTimeEnd] = useState('17:00');
  const submitting = useRef(false);
  const [dailyTimes, setDailyTimes] = useState<Record<string, { start: string; end: string }>>({});
  const [notes, setNotes] = useState('');
  const [qualityCycleTemplateId, setQualityCycleTemplateId] = useState<string | null>(null);
  const [templates, setTemplates] = useState<QualityCycleTemplate[]>([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const [templateShift, setTemplateShift] = useState<Shift | null>(null);
  const [assignedTemplate, setAssignedTemplate] = useState<string | null>(null);
  const [templateError, setTemplateError] = useState('');
  const [templateSaving, setTemplateSaving] = useState(false);

  const [assignOpen, setAssignOpen] = useState<string | null>(null);
  const [assignments, setAssignments] = useState<Record<string, ShiftAssignment[]>>({});
  const [assignSel, setAssignSel] = useState<string[]>([]);
  const [assignLoading, setAssignLoading] = useState(false);
  const [assignBusy, setAssignBusy] = useState(false);
  const [assignError, setAssignError] = useState('');
  const [assignStatus, setAssignStatus] = useState('');
  const assignmentGeneration = useRef(0);
  const assignmentPending = useRef(false);
  const [occurrenceShift, setOccurrenceShift] = useState<Shift | null>(null);
  const [occurrences, setOccurrences] = useState<ShiftOccurrence[]>([]);
  const [occurrenceLoading, setOccurrenceLoading] = useState(false);
  const occurrenceGeneration = useRef(0);
  const [occurrenceError, setOccurrenceError] = useState('');
  const [occurrenceEditError, setOccurrenceEditError] = useState('');
  const occurrenceRangeShift = useRef<string | null>(null);
  const [highlightUtc, setHighlightUtc] = useState<string | null>(null);
  const [occurrenceFrom, setOccurrenceFrom] = useState(new Date().toISOString().slice(0, 10));
  const [occurrenceTo, setOccurrenceTo] = useState(new Date(Date.now() + 31 * 86400000).toISOString().slice(0, 10));
  const [editingOccurrence, setEditingOccurrence] = useState<ShiftOccurrence | null>(null);
  const [occurrenceStatus, setOccurrenceStatus] = useState('1');
  const [occurrenceStart, setOccurrenceStart] = useState('');
  const [occurrenceEnd, setOccurrenceEnd] = useState('');
  const [occurrenceReason, setOccurrenceReason] = useState('');
  const [occurrenceEmployee, setOccurrenceEmployee] = useState<string | null>(null);
  const [occurrenceSaving, setOccurrenceSaving] = useState(false);
  const [assigningVisit, setAssigningVisit] = useState<ShiftOccurrence | null>(null);
  // Add visit: null is closed; a preset fixes the property when opened from a shift.
  const [visitDialog, setVisitDialog] = useState<{ preset: { companyId: string; branchId: string; propertyName: string } | null } | null>(null);
  const [cleanerVisit, setCleanerVisit] = useState<ShiftOccurrence | null>(null);
  const [cleanerChoice, setCleanerChoice] = useState<string | null>(null);
  const [cleaningTypeChoice, setCleaningTypeChoice] = useState<string | null>(null), [cleaningTypeSaving, setCleaningTypeSaving] = useState(false), [cleaningTypeError, setCleaningTypeError] = useState('');
  const [cleanerSaving, setCleanerSaving] = useState(false);
  const [cleanerError, setCleanerError] = useState('');
  const [defaultCleanerError, setDefaultCleanerError] = useState('');
  useEffect(() => () => { assignmentGeneration.current++; }, []);

  // The shift list has no assignee data, so the Employees column uses one bulk count request.
  // Every freshly loaded roster also updates that shift's count, so the column always shows the newest
  // of the two sources: a later bulk reload (after another admin's change) replaces an older roster count.
  const [assignmentCounts, setAssignmentCounts] = useState<Record<string, number> | null>(null);
  const [assigneeNames, setAssigneeNames] = useState<Record<string, string[]>>({});
  const [countError, setCountError] = useState('');
  const storeRoster = (id: string, roster: ShiftAssignment[]) => {
    setAssignments(m => ({ ...m, [id]: roster }));
    setAssignmentCounts(c => c && { ...c, [id]: roster.filter(a => a.isActive).length });
    setAssigneeNames(n => ({ ...n, [id]: roster.filter(a => a.isActive).map(a => assigneeName(a.employeeId)) }));
  };
  const loadAssigneeNames = (ids: string[]) => adminShifts.assigneeNames(ids)
    .then(names => { setAssigneeNames(names); setCountError(''); })
    .catch(() => { setAssigneeNames({}); setCountError('Could not load employee names. Refresh the page to retry.'); });
  const assigneeName = (id: string) => { const person = employees.find(e => e.id === id); return person ? `${person.firstName} ${person.lastName}`.trim() : 'Unknown employee'; };
  const loadAssignmentCounts = () => adminShifts.assignmentCounts()
    .then(counts => { setAssignmentCounts(counts); setCountError(''); })
    .catch(() => { setAssignmentCounts(null); });
  // Only the newest tab/page request may update the list, so a slow earlier response cannot show the wrong tab.
  const loadSeq = useRef(0);
  const load = () => { const seq = ++loadSeq.current; setLoading(true); return adminShifts.page({ tab: lifecycle, skip: (shiftPage - 1) * PAGE_SIZE, take: PAGE_SIZE, companyId: filterCompany, scheduleType: filterType }).then((r) => {
    if (seq !== loadSeq.current) return;
    // A reply without the paged shape cannot be shown; say so and keep the retry path instead of crashing the screen.
    if (!r || !Array.isArray(r.items) || typeof r.total !== 'number' || !r.counts) throw new Error('The shift list could not be read. Refresh the page to retry.');
    setPageRows(r.items); setPageTotal(r.total); setTabCounts(r.counts); setListError(''); void loadAssignmentCounts(); void loadAssigneeNames(r.items.map(s => s.id));
    setLoading(false);
  }).catch((failure: Error) => { if (seq !== loadSeq.current) return; setListError(failure.message); setLoading(false); }); };
  // Reload the current tab and page whenever either changes, and once on mount.
  useEffect(() => { void load(); }, [lifecycle, shiftPage, filterCompany, filterType]);
  useEffect(() => {
    adminCompanies.list('', 0, 100).then(r => setCompanyChoices((r?.items ?? []).map(c => ({ value: c.id, label: c.name })))).catch(() => setCompanyChoices([]));
  }, []);
  useEffect(() => { adminTenant.get().then(profile => {
    if (!profile) throw new Error('Could not load scheduling settings. Refresh the page to retry.');
    const zone = profile.timeZoneId?.trim() || 'Europe/Helsinki';
    new Intl.DateTimeFormat('en-CA', { timeZone: zone }).format(new Date());
    setTimeZoneId(zone); setSchedulingReady(true);
  }).catch(() => setListError('Could not load scheduling time zone. Refresh the page or ask a tenant administrator to check its settings.')); }, []);

  const closeAssignments = () => {
    assignmentGeneration.current++;
    setAssignOpen(null); setAssignSel([]); setAssignLoading(false);
    setAssignError(''); setAssignStatus('');
  };
  const changeLifecycle = (status: LifecycleStatus) => {
    if (status === lifecycle) return;
    closeAssignments(); setTemplateShift(null); setLifecycle(status); setShiftPage(1);
  };

  // The server pages and counts the tabs, so the page only holds the current slice.
  const shiftPageCount = Math.max(1, Math.ceil(pageTotal / PAGE_SIZE));
  // Keep the stored page in range when the list shrinks (deactivate, delete, reload), so later growth
  // does not jump to a stale page.
  useEffect(() => {
    if (shiftPage > shiftPageCount) setShiftPage(shiftPageCount);
  }, [shiftPageCount, shiftPage]);
  // A shift opened from the workspace may fall outside the current page; fetch it so its panel still works.
  useEffect(() => {
    if (!workspaceId || pageRows.some(s => s.id === workspaceId)) { setWorkspaceFetched(null); return; }
    let cancelled = false;
    adminShifts.get(workspaceId).then(shift => { if (!cancelled) setWorkspaceFetched(shift ?? null); }).catch(() => { if (!cancelled) setWorkspaceFetched(null); });
    return () => { cancelled = true; };
  }, [workspaceId, pageRows]);

  const activeCount = loading ? undefined : tabCounts?.active;
  const completedCount = loading ? undefined : tabCounts?.completed;
  const inactiveCount = loading ? undefined : tabCounts?.inactive;

  useEffect(() => {
    adminEmployees.list().then((e) => { if (e) setEmployees(e.filter(person => person.role !== 'partner')); });
    adminQualityCycle.listTemplates().then((t) => { if (t) setTemplates(t.filter((x) => x.isActive)); });
  }, []);

  useEffect(() => {
    if (!companyId) { setBranches([]); return; }
    let current = true;
    adminCompanies.get(companyId).then((d) => { if (current && d?.company.id === companyId) setBranches(d.branches); });
    return () => { current = false; };
  }, [companyId]);

  const buildSchedule = () => {
    const t = Number(scheduleType);
    if (t === 0) return { type: 0, dailyStart: start, dailyEnd: end };
    if (t === 1) return { type: 1, dailyTimes: Object.fromEntries(weekDays.map(day => [DAYS[Number(day)], dailyTimes[day] ?? { start, end }])) };
    if (t === 2) return {
      type: 2,
      weeklyDay: weekDays.length ? Number(weekDays[0]) : null,
      weeklyDays: weekDays.map((d) => Number(d)),
      weeklyStart: start,
      weeklyEnd: end,
    };
    if (t === 3) return {
      type: 3,
      biWeeklyWeekParity: Number(weekParity),
      biWeeklyDay: weekDays.length ? Number(weekDays[0]) : 0,
      biWeeklyStart: start,
      biWeeklyEnd: end,
    };
    if (t === 5) return { type: 5, monthlyDay: Number(monthlyDay), monthlyStart: start, monthlyEnd: end };
    if (t === 6) return { type: 6, oneTimeDate, oneTimeStart, oneTimeEnd };
    return { type: 4 };
  };

  const closeModal = () => {
    setName(''); setNotes(''); setWeekDays([]); setCompanyId(null); setBranchId(null); setPriceFrom(null); setQualityCycleTemplateId(null); setScheduleType(''); setStart('08:00'); setEnd('17:00'); setWeekParity('1'); setMonthlyDay(1); setOneTimeDate(''); setOneTimeStart('08:00'); setOneTimeEnd('17:00'); setDailyTimes({}); setEditingShift(null); setError(''); setModalOpen(false);
  };

  // A customer workspace link carries ?company= so the create form opens with that customer selected.
  const preselectedCompany = useRef<string | null>(null);
  useEffect(() => { preselectedCompany.current = new URLSearchParams(window.location.search).get('company'); }, []);
  const openCreate = () => { if (!schedulingReady) return; closeModal(); if (preselectedCompany.current) { setCompanyId(preselectedCompany.current); preselectedCompany.current = null; } setValidFrom(localInputValue(new Date().toISOString(), timeZoneId).slice(0, 10)); setValidUntil(''); setModalOpen(true); };
  const openEdit = async (shift: Shift) => {
    const schedule = shift.schedule;
    setEditingShift(shift); setCompanyId(shift.companyId); setBranchId(shift.branchId);
    setName(shift.name); setNotes(shift.notes ?? ''); setQualityCycleTemplateId(shift.qualityCycleTemplateId ?? null);
    setValidFrom(shift.validFrom?.slice(0, 10) ?? ''); setValidUntil(shift.validUntil?.slice(0, 10) ?? '');
    setScheduleType(String(schedule.type));
    setStart((schedule.dailyStart ?? schedule.weeklyStart ?? schedule.biWeeklyStart ?? schedule.monthlyStart ?? '08:00').slice(0, 5));
    setEnd((schedule.dailyEnd ?? schedule.weeklyEnd ?? schedule.biWeeklyEnd ?? schedule.monthlyEnd ?? '17:00').slice(0, 5));
    const dayIndex = (day: string) => /^\d+$/.test(day) ? Number(day) : DAYS.indexOf(day);
    setWeekDays(schedule.type === 2 ? (schedule.weeklyDays?.length ? schedule.weeklyDays : [schedule.weeklyDay ?? 0]).map(String) : schedule.type === 3 ? [String(schedule.biWeeklyDay ?? 0)] : schedule.type === 1 ? Object.keys(schedule.dailyTimes ?? {}).map(day => String(dayIndex(day))).filter(day => Number(day) >= 0) : []);
    setDailyTimes(Object.fromEntries(Object.entries(schedule.dailyTimes ?? {}).map(([day, range]) => [String(dayIndex(day)), { start: range.start?.slice(0, 5) ?? '08:00', end: range.end?.slice(0, 5) ?? '17:00' }])));
    setWeekParity(String(schedule.biWeeklyWeekParity ?? 1)); setMonthlyDay(schedule.monthlyDay ?? 1);
    setOneTimeDate(schedule.oneTimeDate?.slice(0, 10) ?? ''); setOneTimeStart((schedule.oneTimeStart ?? '08:00').slice(0, 5)); setOneTimeEnd((schedule.oneTimeEnd ?? '17:00').slice(0, 5));
    setError('');
    const detail = await adminCompanies.get(shift.companyId);
    if (detail) {
      setBranches(detail.branches);
    }
    setModalOpen(true);
  };

  const submit = async () => {
    if (submitting.current) return;
    if (!scheduleType) { setError('Choose a schedule type.'); return; }
    const oneTime = scheduleType === '6';
    if (oneTime) {
      if (!oneTimeDate) { setError('One-time visit date is required.'); return; }
      if (!oneTimeStart || !oneTimeEnd) { setError('One-time visit start and end times are required.'); return; }
      if (oneTimeEnd <= oneTimeStart) { setError('End time must be after start time.'); return; }
    } else {
      if ((!editingShift || editingShift.validFrom) && !validFrom) { setError('Shift start date is required.'); return; }
      if (validFrom && validUntil && validUntil < validFrom) { setError('End date must be on or after the start date.'); return; }
    }
    if (!companyId || !branchId) { setError('Company and branch are required.'); return; }
    if (editingShift && !name.trim()) { setError('Enter a shift name.'); return; }
    if (scheduleType === '2' && weekDays.length === 0) { setError('Select at least one day.'); return; }
    // A weekday without its own times uses the shown default (the same fallback the saved schedule uses).
    if (scheduleType === '1' && (!weekDays.length || weekDays.some(day => { const range = dailyTimes[day] ?? { start, end }; return !range.start || !range.end || range.end <= range.start; }))) { setError('Select at least one day and ensure each end time is after its start time.'); return; }
    if (scheduleType === '3' && weekDays.length === 0) { setError('Select a day.'); return; }
    submitting.current = true; setSaving(true); setError('');
    const branchVersions = branches.find(b => b.id === branchId)?.priceVersions;
    const chosenPriceFrom = priceFrom && branchVersions?.some(v => v.effectiveFrom === priceFrom) ? priceFrom : defaultPriceVersion(branchVersions) ?? undefined;
    // A one-time visit is valid only on its date, so the validity dates mirror that date.
    // Create names the shift from its property and schedule. Editing saves the name in the field, which starts from the stored name.
    // The server builds every shift name from the property, so the name sent here is only a placeholder.
    const shiftName = autoShiftName(branches.find(b => b.id === branchId));
    if (!shiftName) { setError('Could not name this shift. Choose a property and schedule, then save again.'); return; }
    const fields = { companyId, branchId, name: shiftName, schedule: buildSchedule(), notes: notes.trim() || undefined, isActive: editingShift?.isActive ?? true, validFrom: oneTime ? oneTimeDate : validFrom || undefined, validUntil: oneTime ? oneTimeDate : validUntil || undefined, qualityCycleTemplateId: qualityCycleTemplateId || undefined,
      // Creating a shift picks the branch price version it bills at; the default is the price in force today.
      priceEffectiveFrom: editingShift ? undefined : chosenPriceFrom };
    const result = await (editingShift ? adminShifts.update(editingShift.id, fields) : adminShifts.create(fields)).finally(() => { submitting.current = false; });
    setSaving(false);
    if (!result.ok) {
      setError(result.message);
      showError(result.message, editingShift ? 'Shift not updated' : 'Shift not created');
      return;
    }
    const created = result.value;
    const wasEditing = Boolean(editingShift);
    closeModal(); if (!wasEditing) changeLifecycle('active'); await load();
    if (!wasEditing && created.schedule.type === 4) {
      void openOccurrences(created, 'On-call shift saved. Schedule a dated visit below to show it on the calendars. Cancelling keeps the shift without a scheduled visit.');
      openOccurrenceEditor(undefined, '4');
    }
  };

  // Deactivate, Reactivate and Delete open this popup first; it runs the task only when confirmed.
  const [confirmAction, setConfirmAction] = useState<{ title: string; body: string; confirmLabel: string; color?: string; run: () => Promise<void> } | null>(null);
  const [confirmBusy, setConfirmBusy] = useState(false);
  const runConfirm = async () => {
    if (!confirmAction) return;
    setConfirmBusy(true);
    try { await confirmAction.run(); setConfirmAction(null); }
    finally { setConfirmBusy(false); }
  };
  const deactivate = async (id: string) => { await adminShifts.deactivate(id); if (assignOpen === id) closeAssignments(); await load(); };
  const reactivate = async (id: string) => {
    setRestoring(id); setListError('');
    try {
      if (!await adminShifts.reactivate(id)) { setListError('Could not reactivate shift. Check that its company and branch are active, then retry.'); return; }
      await load();
    } catch { setListError('Could not reactivate shift. Please retry.'); }
    finally { setRestoring(null); }
  };
  const deleteShift = async (shift: Shift) => {
    setRestoring(shift.id); setListError('');
    try { await adminShifts.deleteInactive(shift.id); await load(); }
    catch (failure) { setListError(failure instanceof Error ? failure.message : 'Could not delete shift. Please retry.'); }
    finally { setRestoring(null); }
  };

  const openAssign = async (id: string) => {
    if (assignmentPending.current) return;
    const version = ++assignmentGeneration.current;
    setAssignError(''); setAssignStatus(''); setAssignSel([]);
    if (assignOpen === id) { setAssignOpen(null); setAssignLoading(false); return; }
    setAssignOpen(id); setAssignLoading(true);
    try {
      const a = await adminShifts.assignments(id);
      if (version === assignmentGeneration.current) storeRoster(id, a);
    } catch (e) {
      if (version === assignmentGeneration.current) {
        setAssignments(m => { const next = { ...m }; delete next[id]; return next; });
        setAssignError(e instanceof Error ? e.message : 'Could not load assignments. Please retry.');
      }
    } finally { if (version === assignmentGeneration.current) setAssignLoading(false); }
  };

  const refreshAssignments = async (id: string, version: number) => {
    try {
      const a = await adminShifts.assignments(id);
      if (version === assignmentGeneration.current) storeRoster(id, a);
      return a;
    } catch (e) {
      if (version === assignmentGeneration.current) setAssignments(m => { const next = { ...m }; delete next[id]; return next; });
      throw e;
    }
  };

  const doAssign = async (shiftId: string) => {
    if (assignmentPending.current || assignLoading || !assignSel.length || !assignments[shiftId]) return;
    assignmentPending.current = true; setAssignBusy(true); setAssignError(''); setAssignStatus('');
    const version = assignmentGeneration.current;
    const failed: string[] = []; const messages: string[] = []; let succeeded = 0;
    try {
      for (const empId of assignSel) {
        try { await adminShifts.assign(shiftId, empId); succeeded++; }
        catch (e) { failed.push(empId); messages.push(`${employeeName(empId)}: ${e instanceof Error ? e.message : 'Could not assign employee. Please retry.'}`); }
      }
      let selected = failed;
      try {
        const a = await refreshAssignments(shiftId, version);
        selected = failed.filter(id => !a.some(x => x.isActive && x.employeeId === id));
      } catch { messages.push('Could not refresh assignments. Refresh them before making further changes.'); }
      if (version === assignmentGeneration.current) {
        setAssignSel(selected); setAssignError(messages.join(' '));
        setAssignStatus(succeeded ? `Assigned ${succeeded} employee${succeeded === 1 ? '' : 's'}.${failed.length ? ` ${failed.length} assignment${failed.length === 1 ? '' : 's'} could not be confirmed.` : ''}` : '');
      }
    } finally {
      assignmentPending.current = false;
      if (version === assignmentGeneration.current) setAssignBusy(false);
    }
  };

  const doUnassign = async (shiftId: string, employeeId: string) => {
    if (assignmentPending.current || assignLoading) return;
    assignmentPending.current = true; setAssignBusy(true); setAssignError(''); setAssignStatus('');
    const version = assignmentGeneration.current;
    try {
      await adminShifts.unassign(shiftId, employeeId);
      if (version === assignmentGeneration.current) setAssignStatus(`${employeeName(employeeId)} removed from this shift.`);
      await refreshAssignments(shiftId, version);
    } catch (e) {
      if (version === assignmentGeneration.current) setAssignError(`${employeeName(employeeId)}: ${e instanceof Error ? e.message : 'Could not update assignments. Please retry.'}`);
    } finally {
      assignmentPending.current = false;
      if (version === assignmentGeneration.current) setAssignBusy(false);
    }
  };

  const employeeName = (id: string) => {
    const e = employees.find((x) => x.id === id);
    return e ? employeeLabel(e, employees) : id.slice(0, 8);
  };

  const loadOccurrences = async (shift = occurrenceShift, fromDate = occurrenceFrom, toDate = occurrenceTo) => {
    if (!shift) return;
    const version = ++occurrenceGeneration.current;
    setOccurrenceLoading(true); setOccurrenceError('');
    try {
      const fromUtc = tenantLocalToUtc(`${fromDate}T00:00`, timeZoneId);
      const nextDayUtc = tenantLocalToUtc(`${addCalendarDays(toDate, 1)}T00:00`, timeZoneId);
      const toUtc = new Date(Date.parse(nextDayUtc) - 1).toISOString();
      const result = await adminShifts.occurrences(shift.id, fromUtc, toUtc);
      if (!result) throw new Error('The visits could not be loaded. Close this window and open it again to retry.');
      if (version === occurrenceGeneration.current) setOccurrences(result);
    } catch (failure) { if (version === occurrenceGeneration.current) setOccurrenceError(failure instanceof Error ? failure.message : 'The visits could not be loaded. Close this window and open it again to retry.'); }
    finally { if (version === occurrenceGeneration.current) setOccurrenceLoading(false); }
  };

  // Widens a date range just enough to include the tenant-local day of a visit, so that visit is always listed.
  const rangeIncluding = (from: string, to: string, utc: string | null): [string, string] => {
    if (!utc) return [from, to];
    const day = localInputValue(utc, timeZoneId).slice(0, 10);
    return [day < from ? day : from, day > to ? day : to];
  };

  // `highlightUtc` marks one visit (the overlapping visit from a refusal) and makes sure the range lists it.
  const openOccurrences = async (shift: Shift, notice = '', highlightUtc: string | null = null) => {
    setShiftNotice(notice);
    setHighlightUtc(highlightUtc);
    setOccurrenceShift(shift); setOccurrences([]); setOccurrenceError('');
    // A new shift starts from tenant-local today onward, the day the calendar opens on (the browser's UTC date can be a day behind near midnight).
    // Reopening the same shift keeps the range the admin chose.
    if (occurrenceRangeShift.current !== shift.id) {
      occurrenceRangeShift.current = shift.id;
      const today = localInputValue(new Date().toISOString(), timeZoneId).slice(0, 10);
      const [from, to] = rangeIncluding(today, addCalendarDays(today, 31), highlightUtc);
      setOccurrenceFrom(from); setOccurrenceTo(to);
      await loadOccurrences(shift, from, to);
      return;
    }
    const [from, to] = rangeIncluding(occurrenceFrom, occurrenceTo, highlightUtc);
    if (from !== occurrenceFrom || to !== occurrenceTo) { setOccurrenceFrom(from); setOccurrenceTo(to); }
    await loadOccurrences(shift, from, to);
  };

  // Opens the clashing shift's visit list from an overlap refusal, with the overlapping visit marked. The assignment
  // dialog stays open if that shift cannot be loaded, so the admin keeps the cleaner and reason they had chosen.
  const openClashingShift = async (shiftId: string, shiftName: string, clashStartUtc: string) => {
    // adminGet resolves to null on a failed request instead of throwing, so a null shift is the failure case.
    const clashing = await adminShifts.get(shiftId, true);
    if (!clashing) {
      showError(`${shiftName} could not be loaded. Retry, or find it in the shift list.`, 'Could not open shift');
      return;
    }
    setAssigningVisit(null);
    await openOccurrences(clashing, `Showing ${shiftName}. The overlapping visit is marked below.`, clashStartUtc);
  };

  const openOccurrenceEditor = (occurrence?: ShiftOccurrence, status = '1') => {
    setEditingOccurrence(occurrence ?? { startUtc: '', endUtc: '' });
    setOccurrenceStatus(occurrence?.status ? String(occurrence.status) : status);
    setOccurrenceStart(occurrence?.startUtc ? localInputValue(occurrence.startUtc, timeZoneId) : '');
    setOccurrenceEnd(occurrence?.endUtc ? localInputValue(occurrence.endUtc, timeZoneId) : '');
    setOccurrenceReason(occurrence?.reason ?? ''); setOccurrenceEmployee(occurrence?.employeeId ?? null); setOccurrenceEditError('');
  };

  const saveOccurrence = async () => {
    if (!occurrenceShift || !editingOccurrence || !occurrenceStart || !occurrenceEnd || !occurrenceReason.trim()) {
      setOccurrenceEditError('Start, end and reason are required.'); return;
    }
    setOccurrenceSaving(true); setOccurrenceEditError('');
    try {
      const status = Number(occurrenceStatus);
      await adminShifts.saveOccurrenceException(occurrenceShift.id, {
        id: editingOccurrence.exceptionId,
        originalStartUtc: status === 4 ? null : (editingOccurrence.originalStartUtc ?? editingOccurrence.startUtc),
        startUtc: tenantLocalToUtc(occurrenceStart, timeZoneId), endUtc: tenantLocalToUtc(occurrenceEnd, timeZoneId),
        status, reason: occurrenceReason.trim(), employeeId: occurrenceEmployee,
      });
      setEditingOccurrence(null);
      setShiftNotice(status === 4 ? 'One-time visit scheduled. It appears in the shift calendar; assigned cleaners see it in My shifts.' : 'Visit updated.');
      // Keep a newly scheduled date visible even when it is outside the old list window.
      const savedDay = occurrenceStart.slice(0, 10);
      if (savedDay < occurrenceFrom || savedDay > occurrenceTo) {
        setOccurrenceFrom(savedDay); setOccurrenceTo(savedDay);
        await loadOccurrences(occurrenceShift, savedDay, savedDay);
      } else await loadOccurrences();
    } catch (failure) { setOccurrenceEditError(failure instanceof Error ? failure.message : 'Could not update this occurrence.'); }
    finally { setOccurrenceSaving(false); }
  };

  // A new visit shows up under its property's shift; an open visit list reloads to include it.
  const visitAdded = async (visit: { number: number; shiftId: string; startUtc: string; status: string }, propertyName: string) => {
    setVisitDialog(null);
    const outcome = visit.status === 'Assigned' ? 'The cleaner has it in My shifts.' : visit.status === 'Offered' ? 'An offer was sent to the cleaner.' : 'It has no cleaner yet.';
    setShiftNotice(`Visit #${visit.number} added to ${propertyName}. ${outcome}`);
    await load();
    if (occurrenceShift?.id === visit.shiftId) {
      const day = localInputValue(visit.startUtc, timeZoneId).slice(0, 10);
      const [from, to] = rangeIncluding(occurrenceFrom, occurrenceTo, visit.startUtc);
      if (day < occurrenceFrom || day > occurrenceTo) { setOccurrenceFrom(from); setOccurrenceTo(to); }
      await loadOccurrences(occurrenceShift, from, to);
    }
  };
  const saveDefaultCleaner = async (employeeId: string | null) => {
    if (!occurrenceShift) return;
    setDefaultCleanerError('');
    const result = await adminBranchVisits.setDefaultEmployee(occurrenceShift.id, employeeId);
    if (!result.ok) { setDefaultCleanerError(result.message); return; }
    setOccurrenceShift({ ...occurrenceShift, defaultEmployeeId: employeeId });
    await load();
  };
  const saveCleaner = async () => {
    if (!occurrenceShift || !cleanerVisit?.exceptionId || cleanerSaving) return;
    setCleanerSaving(true); setCleanerError('');
    const result = await adminBranchVisits.setEmployee(occurrenceShift.id, cleanerVisit.exceptionId, cleanerChoice, cleanerVisit.offerRevision ?? 0);
    setCleanerSaving(false);
    if (!result.ok) { setCleanerError(result.message); return; }
    setCleanerVisit(null);
    await loadOccurrences();
  };
  const saveCleaningType = async () => {
    if (!occurrenceShift || !cleanerVisit?.exceptionId || cleaningTypeSaving) return;
    setCleaningTypeSaving(true); setCleaningTypeError('');
    const result = await adminBranchVisits.setCleaningType(occurrenceShift.id, cleanerVisit.exceptionId, cleaningTypeChoice ?? cleanerVisit.visitCleaningType ?? 'Normal');
    setCleaningTypeSaving(false);
    if (!result.ok) { setCleaningTypeError(result.message); return; }
    setCleaningTypeChoice(null); setCleanerVisit(null);
    await loadOccurrences();
  };
  const restoreOccurrence = async (occurrence: ShiftOccurrence) => {
    if (!occurrenceShift || !occurrence.exceptionId) return;
    setOccurrenceSaving(true); setOccurrenceError('');
    try { await adminShifts.deleteOccurrenceException(occurrenceShift.id, occurrence.exceptionId); await loadOccurrences(); }
    catch (failure) { setOccurrenceError(failure instanceof Error ? failure.message : 'Could not restore this occurrence.'); }
    finally { setOccurrenceSaving(false); }
  };

  // Workspace actions close the workspace first, so each existing dialog or panel opens alone.
  const workspaceShift = workspaceId ? pageRows.find(s => s.id === workspaceId) ?? workspaceFetched : null;
  const fromWorkspace = (action: (shift: Shift) => void) => () => {
    if (!workspaceShift) return;
    setWorkspaceId(null);
    action(workspaceShift);
  };

  return (
    <>
      <Group justify="space-between" align="center" mb="md" wrap="wrap" gap="sm">
        <Title order={2} component="h1">Shifts</Title>
        <Group gap="xs" wrap="wrap">
          <Button leftSection={<Plus size={16} />} disabled={!schedulingReady} onClick={openCreate}>Add shift</Button>
        </Group>
      </Group>

      {listError && <Text role="alert" c="red" mb="md">{listError}</Text>}
      {countError && <Text role="alert" c="red" mb="md">{countError}</Text>}

      <TodayKpis />
      <Group grow align="flex-end" mb="sm">
        <Select label="Filter by company" placeholder="All companies" clearable searchable data={companyChoices} value={filterCompany}
          onChange={(value) => { setFilterCompany(value); setShiftPage(1); }} />
        <Select label="Filter by shift type" placeholder="All types" clearable data={SCHEDULE_TYPES} value={filterType}
          onChange={(value) => { setFilterType(value); setShiftPage(1); }} />
      </Group>
      <LifecycleTabs value={lifecycle} onChange={changeLifecycle} label="Shift activity" disabled={assignBusy || templateSaving}
        showCompleted activeCount={activeCount} completedCount={completedCount} inactiveCount={inactiveCount}>
      {loading ? <Loader /> : pageTotal === 0 ? (
        <Text c="dimmed">{(tabCounts?.active ?? 0) + (tabCounts?.completed ?? 0) + (tabCounts?.inactive ?? 0) === 0 ? 'No shifts yet.' : `No ${lifecycle} shifts. Check the ${lifecycle === 'active' ? 'Inactive' : 'Active'} tab.`}</Text>
      ) : (<>
        <Text size="xs" c="dimmed" hiddenFrom="sm" mb={6}>Scroll sideways to see all columns.</Text>
        <Table.ScrollContainer minWidth={600} tabIndex={0} role="region" aria-label="Shifts table">
          <Table striped highlightOnHover withTableBorder>
            <Table.Thead>
              <Table.Tr><Table.Th>Name</Table.Th><Table.Th>Schedule</Table.Th><Table.Th>Employee names</Table.Th><Table.Th>Status</Table.Th><Table.Th w={200}>Actions</Table.Th></Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {pageRows.map((s) => (
                <Fragment key={s.id}>
                  <Table.Tr
                    tabIndex={0}
                    aria-expanded={expandedId === s.id}
                    style={{ cursor: 'pointer' }}
                    onClick={() => void toggleExpanded(s)}
                    onKeyDown={(e) => { if (e.target === e.currentTarget && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); void toggleExpanded(s); } }}
                  >
                    <Table.Td>
                      {(() => {
                        const branch = propertyById[s.branchId];
                        const address = branch ? [branch.street?.trim(), [branch.postalCode?.trim(), branch.city?.trim()].filter(Boolean).join(' ')].filter(Boolean).join(', ') : '';
                        const chips = branch ? [
                          branch.sizeM2 != null ? `${branch.sizeM2} m²` : null,
                          branch.roomCount != null ? `${branch.roomCount} ${branch.roomCount === 1 ? 'room' : 'rooms'}` : null,
                          branch.bedCount != null ? `${branch.bedCount} ${branch.bedCount === 1 ? 'bed' : 'beds'}` : null,
                        ].filter((c): c is string => !!c) : [];
                        return (
                          <Stack gap={4}>
                            <Text fw={500} size="sm">{address || s.name}</Text>
                            {chips.length > 0 && <Group gap={6}>{chips.map(c => <Badge key={c} variant="outline">{c}</Badge>)}</Group>}
                            <span className="sr-only">{s.name}</span>
                          </Stack>
                        );
                      })()}
                    </Table.Td>
                    <Table.Td>{scheduleLabel(s)}</Table.Td>
                    <Table.Td>{assigneeNames[s.id] ? (assigneeNames[s.id].length === 0 ? 'None' : assigneeNames[s.id].join(', ')) : assignmentCounts ? `${assignmentCounts[s.id] ?? 0} assigned` : '—'}</Table.Td>
                    <Table.Td><Text size="sm">{shiftView(s) === 'inactive' ? 'Inactive' : shiftView(s) === 'completed' ? 'Completed' : s.schedule.type === 6 && s.schedule.oneTimeDate && s.schedule.oneTimeDate.slice(0, 10) < new Date().toLocaleDateString('en-CA', { timeZone: timeZoneId }) ? 'Past visit · check completion' : 'Active'}</Text></Table.Td>
                    <Table.Td onClick={(e) => e.stopPropagation()}>
                      <Group gap={6} wrap="nowrap">
                        <Menu shadow="md" width={230} position="bottom-end" withinPortal>
                          <Menu.Target>
                            <ActionIcon variant="light" size="md" aria-label={`More actions for ${s.name}`}><MoreHorizontal size={16} /></ActionIcon>
                          </Menu.Target>
                          <Menu.Dropdown>
                            <Menu.Item leftSection={<Eye size={13} />} onClick={() => setWorkspaceId(s.id)}>View details</Menu.Item>
                            {isAdmin() && !s.isBranchMain && <Menu.Item leftSection={<Receipt size={13} />} onClick={() => { setPricingShift(s); setPricingOccurrence(undefined); }}>Pricing</Menu.Item>}
                            {isAdmin() && <Menu.Item leftSection={<Pencil size={13} />} onClick={() => openEdit(s)}>Edit schedule</Menu.Item>}
                            {s.isActive && !s.isBranchMain && <Menu.Item disabled={assignBusy} leftSection={<UserPlus size={13} />} onClick={() => openAssign(s.id)}>Assign employees</Menu.Item>}
                            {s.isActive && <Menu.Item leftSection={<CalendarDays size={13} />} onClick={() => openOccurrences(s)}>Occurrences</Menu.Item>}
                            {s.isActive && s.isBranchMain && <Menu.Item leftSection={<Plus size={13} />} onClick={() => setVisitDialog({ preset: { companyId: s.companyId, branchId: s.branchId, propertyName: s.name } })}>Add visit</Menu.Item>}
                            {s.isActive && !s.isBranchMain && s.schedule.type === 4 && <Menu.Item leftSection={<CalendarPlus size={13} />} onClick={() => { void openOccurrences(s); openOccurrenceEditor(undefined, '4'); }}>Schedule one-time visit</Menu.Item>}
                            {isAdmin() && s.isActive && <Menu.Item leftSection={<ListChecks size={13} />} onClick={() => { setTemplateShift(s); setAssignedTemplate(s.qualityCycleTemplateId ?? null); setTemplateError(''); }}>Checklist template</Menu.Item>}
                            {s.isActive && <Menu.Item color="red" leftSection={<PauseCircle size={13} />} disabled={assignBusy} onClick={() => setConfirmAction({ title: `Deactivate “${s.name}”?`, body: 'It stops taking new visits. You can reactivate it later from the Inactive tab.', confirmLabel: 'Deactivate', color: 'red', run: () => deactivate(s.id) })}>Deactivate</Menu.Item>}
                            {!s.isActive && <Menu.Item leftSection={<PlayCircle size={13} />} disabled={!!restoring} onClick={() => setConfirmAction({ title: `Reactivate “${s.name}”?`, body: 'It returns to the Active tab and can take visits again.', confirmLabel: 'Reactivate', run: () => reactivate(s.id) })}>Reactivate</Menu.Item>}
                            {!s.isActive && <Menu.Item color="red" leftSection={<Trash2 size={13} />} disabled={!!restoring} onClick={() => setConfirmAction({ title: `Delete “${s.name}”?`, body: 'Permanently delete this inactive shift and remove its employee assignments. Completed checklist records, photos and reports will remain in history.', confirmLabel: 'Delete', color: 'red', run: () => deleteShift(s) })}>Delete</Menu.Item>}
                          </Menu.Dropdown>
                        </Menu>
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                  {expandedId === s.id && (
                    <Table.Tr>
                      <Table.Td colSpan={5} data-testid="row-visits">
                        <button
                          type="button"
                          aria-expanded={visitsOpen}
                          onClick={(e) => { e.stopPropagation(); setVisitsOpen(open => !open); }}
                          style={{ display: 'flex', alignItems: 'center', gap: 8, background: 'none', border: 0, padding: '6px 0', cursor: 'pointer', font: 'inherit', fontWeight: 600, color: 'inherit', minHeight: 44 }}
                        >
                          {visitsOpen ? <ChevronDown size={16} aria-hidden /> : <ChevronRight size={16} aria-hidden />}
                          <CalendarDays size={16} aria-hidden />
                          <span>Visits</span>
                          {!expandedLoading && <Badge variant="subtle">{expandedVisits.length}</Badge>}
                          <span style={{ fontWeight: 400, color: 'var(--mantine-color-dimmed)', fontSize: 'var(--mantine-font-size-sm)' }}>{`Next 31 days from ${expandedFrom}`}</span>
                        </button>
                        {expandedLoading && <Text size="sm" c="dimmed">Loading visits…</Text>}
                        {expandedError && <Text size="sm" c="red" role="alert">{expandedError}</Text>}
                        {!expandedLoading && !expandedError && expandedVisits.length === 0 && <Text size="sm" c="dimmed">No visits in the next 31 days.</Text>}
                        {visitsOpen && expandedVisits.length > 0 && (
                          <Table withTableBorder>
                            <Table.Thead><Table.Tr>
                              <Table.Th><span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}><ListChecks size={14} aria-hidden />Visit</span></Table.Th>
                              <Table.Th><span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}><Clock size={14} aria-hidden />Starts</span></Table.Th>
                              <Table.Th><span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}><Sparkles size={14} aria-hidden />Type</span></Table.Th>
                              <Table.Th><span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}><UserRound size={14} aria-hidden />Cleaner</span></Table.Th>
                              <Table.Th><span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}><CircleCheck size={14} aria-hidden />Status</span></Table.Th>
                            </Table.Tr></Table.Thead>
                            <Table.Tbody>
                              {expandedVisits.map((item) => (
                                <Table.Tr key={item.exceptionId ?? item.startUtc}>
                                  <Table.Td>{visitLabel(item, timeZoneId) || s.name}</Table.Td>
                                  <Table.Td>{new Date(item.startUtc).toLocaleString(undefined, { timeZone: timeZoneId })}</Table.Td>
                                  <Table.Td>{item.visitNumber ? <Badge variant={item.visitCleaningType === 'Deep' ? 'navy' : 'subtle'}>{`${item.visitCleaningType ?? 'Normal'} cleaning`}</Badge> : '—'}</Table.Td>
                                  <Table.Td>{(item.assigneeIds ?? []).map(employeeName).join(', ') || (item.offeredEmployeeId ? `Offer sent to ${employeeName(item.offeredEmployeeId)}` : 'Unassigned')}</Table.Td>
                                  <Table.Td><Badge variant="outline">{item.visitNumber && item.workStatus ? WORK_STATUS_LABEL[item.workStatus] ?? item.workStatus : item.status ? OCCURRENCE_STATUSES.find(x => x.value === String(item.status))?.label : 'Scheduled'}</Badge></Table.Td>
                                </Table.Tr>
                              ))}
                            </Table.Tbody>
                          </Table>
                        )}
                      </Table.Td>
                    </Table.Tr>
                  )}
                </Fragment>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
        <ListPager label="Shifts" total={pageTotal} page={shiftPage} pageCount={shiftPageCount} onPage={setShiftPage} /></>
      )}
      </LifecycleTabs>

      {workspaceShift && <ShiftWorkspace shift={workspaceShift}
        employeeCount={assignmentCounts ? (assignmentCounts[workspaceShift.id] ?? 0) : assignments[workspaceShift.id] ? assignments[workspaceShift.id].filter(a => a.isActive).length : undefined}
        templateName={templates.find(t => t.id === workspaceShift.qualityCycleTemplateId)?.title ?? null}
        onClose={() => setWorkspaceId(null)} />}
      {assignOpen && <Modal opened onClose={closeAssignments} title={`Assign employees — ${pageRows.find(r => r.id === assignOpen)?.name ?? ''}`} size="lg" radius="md">
        <Stack gap="sm">
          <Group align="flex-end" mb="xs" wrap="wrap" gap="xs">
            <MultiSelect
              label="Assign employees"
              placeholder="Select employees"
              data={employees.filter(e => e.isActive && !(assignments[assignOpen] ?? []).some(a => a.isActive && a.employeeId === e.id)).map((e) => ({ value: e.id, label: `${e.firstName} ${e.lastName}${e.email ? ` (${e.email})` : ''}` }))}
              value={assignSel}
              onChange={setAssignSel}
              style={{ flex: 1, minWidth: 200 }}
              searchable
              disabled={assignLoading || assignBusy || !assignments[assignOpen]}
            />
            <Button size="xs" loading={assignBusy} disabled={assignLoading || !assignSel.length || !assignments[assignOpen]} onClick={() => doAssign(assignOpen)}>Assign</Button>
            <Button size="xs" variant="default" disabled={assignLoading || assignBusy} onClick={async () => {
              const version = ++assignmentGeneration.current;
              setAssignLoading(true); setAssignError(''); setAssignStatus('');
              try {
                const a = await refreshAssignments(assignOpen, version);
                if (version === assignmentGeneration.current) setAssignSel(selected => selected.filter(id => !a.some(x => x.isActive && x.employeeId === id)));
              } catch (e) { if (version === assignmentGeneration.current) setAssignError(e instanceof Error ? e.message : 'Could not load assignments. Please retry.'); }
              finally { if (version === assignmentGeneration.current) setAssignLoading(false); }
            }}>Refresh assignments</Button>
          </Group>
          <Text size="sm" c="dimmed">Overlapping shifts are checked when you assign employees. Any conflict will be shown here.</Text>
          {assignError && <Text role="alert" aria-label="Assignment error" c="red">{assignError}</Text>}
          {assignStatus && <Text role="status">{assignStatus}</Text>}
          {assignLoading ? <Text>Loading assignments…</Text> : !assignments[assignOpen] ? <Text>Assignments could not be loaded. Use Refresh assignments to retry.</Text> : assignments[assignOpen].length === 0 ? (
            <Text size="sm" c="dimmed">No employees assigned.</Text>
          ) : (
            <Stack gap={4}>
              {(assignments[assignOpen] ?? []).map((a) => (
                <Group key={a.id} justify="space-between" wrap="wrap" gap="xs">
                  <Text size="sm">{employeeName(a.employeeId)}</Text>
                  <ActionIcon variant="subtle" color="red" size="sm" disabled={assignBusy || assignLoading} aria-label={`Unassign ${employeeName(a.employeeId)}`} onClick={() => doUnassign(assignOpen, a.employeeId)}><Trash2 size={14} /></ActionIcon>
                </Group>
              ))}
            </Stack>
          )}
        </Stack>
      </Modal>}
      <Modal opened={Boolean(confirmAction)} onClose={() => { if (!confirmBusy) setConfirmAction(null); }} title={confirmAction?.title ?? ''} size="sm" radius="md">
        <Stack>
          <Text size="sm">{confirmAction?.body}</Text>
          <Group justify="flex-end">
            <Button variant="default" disabled={confirmBusy} onClick={() => setConfirmAction(null)}>Cancel</Button>
            <Button color={confirmAction?.color} loading={confirmBusy} onClick={() => void runConfirm()}>{confirmAction?.confirmLabel ?? 'Confirm'}</Button>
          </Group>
        </Stack>
      </Modal>
      {pricingShift && <ShiftPricingDialog key={`${pricingShift.id}-${pricingOccurrence?.startUtc ?? "plan"}`} shift={pricingShift} occurrence={pricingOccurrence} onClose={() => { setPricingShift(null); setPricingOccurrence(undefined); }} />}
      <Modal opened={Boolean(occurrenceShift)} onClose={() => { if (occurrenceSaving) return; occurrenceGeneration.current++; setOccurrenceShift(null); setEditingOccurrence(null); }} closeButtonProps={{ disabled: occurrenceSaving }} closeOnEscape={!occurrenceSaving} closeOnClickOutside={!occurrenceSaving} title={`Occurrences${occurrenceShift ? ` — ${occurrenceShift.name}` : ''}`} size="xl">
        <Stack>
          <Text size="sm" c="dimmed">Times use the tenant time zone: {timeZoneId}. Changes apply only to the selected visit.</Text>
          {occurrenceShift?.schedule.type === 4 && <Text size="sm">On-call shifts have no automatic visits. Schedule each visit with a date and time. Choose a cleaner below or assign employees to this shift.</Text>}
          {shiftNotice && <Text role="status">{shiftNotice}</Text>}
          <Group align="flex-end" wrap="wrap">
            <TextInput label="From" type="date" value={occurrenceFrom} onChange={e => setOccurrenceFrom(e.target.value)} />
            <TextInput label="To" type="date" value={occurrenceTo} onChange={e => setOccurrenceTo(e.target.value)} />
            <Button variant="default" loading={occurrenceLoading} onClick={() => loadOccurrences()}>Load</Button>
            {occurrenceShift?.isBranchMain
              ? <Button onClick={() => setVisitDialog({ preset: { companyId: occurrenceShift.companyId, branchId: occurrenceShift.branchId, propertyName: occurrenceShift.name } })}>Add visit</Button>
              : <Button onClick={() => openOccurrenceEditor(undefined, '4')}>Add extra visit</Button>}
          </Group>
          {occurrenceShift?.isBranchMain && <>
            <Select label="Default cleaner" description="Each new visit is offered to this cleaner. Visits already added keep their cleaner." placeholder="None: new visits start unassigned"
              clearable searchable value={occurrenceShift.defaultEmployeeId ?? null} onChange={value => { void saveDefaultCleaner(value); }}
              data={employees.filter(e => e.isActive && e.role !== 'partner').map(e => ({ value: e.id, label: employeeLabel(e, employees) }))} />
            {defaultCleanerError && <Text role="alert" c="red">{defaultCleanerError}</Text>}
          </>}
          {occurrenceError && <Text role="alert" c="red">{occurrenceError}</Text>}
          {occurrenceLoading ? <Loader /> : occurrences.length === 0 ? <Text c="dimmed">No occurrences in this period.</Text> : (
            <Table.ScrollContainer minWidth={820}><Table striped withTableBorder>
              <Table.Thead><Table.Tr><Table.Th>Start</Table.Th><Table.Th>End</Table.Th><Table.Th>Status</Table.Th><Table.Th>Assignees</Table.Th><Table.Th>Reason</Table.Th><Table.Th></Table.Th></Table.Tr></Table.Thead>
              <Table.Tbody>{occurrences.map((item) => <Table.Tr key={item.exceptionId ?? item.startUtc}>
                <Table.Td>{visitLabel(item, timeZoneId) && <Text size="sm" fw={600} data-testid="visit-name">{visitLabel(item, timeZoneId)}</Text>}
                  {new Date(item.startUtc).toLocaleString(undefined, { timeZone: timeZoneId })}
                  {highlightUtc && Date.parse(item.startUtc) === Date.parse(highlightUtc) && <Text component="span" size="xs" c="orange" fw={600} ml={6}>Overlapping visit</Text>}</Table.Td>
                <Table.Td>{new Date(item.endUtc).toLocaleString(undefined, { timeZone: timeZoneId })}</Table.Td>
                <Table.Td data-testid="occurrence-status">{item.visitNumber && item.workStatus ? WORK_STATUS_LABEL[item.workStatus] ?? item.workStatus : item.status ? OCCURRENCE_STATUSES.find(x => x.value === String(item.status))?.label : 'Scheduled'}</Table.Td>
                <Table.Td><Text size="sm">{(item.assigneeIds ?? []).map(employeeName).join(', ') || (item.offeredEmployeeId ? `Offer sent to ${employeeName(item.offeredEmployeeId)}` : 'Unassigned')}</Text>
                  <Text size="xs" c="dimmed">{item.visitNumber ? (item.isPartnerBooking ? 'Partner booking' : item.offeredEmployeeId ? 'Waiting for the cleaner to accept' : (item.assigneeIds ?? []).length ? 'Accepted' : 'No cleaner yet') : item.assignmentSource ?? 'Recurring'}</Text>{item.visitNumber ? <Text size="xs" c="dimmed">{item.visitCleaningType ?? 'Normal'} cleaning</Text> : null}{item.visitNumber && item.offeredEmployeeId && Date.parse(item.endUtc) < Date.now() ? <Text size="xs" c="orange" data-testid="past-offer-note">This visit has already ended. The cleaner can still accept the offer. To change the cleaner, delete this visit and add it again.</Text> : null}{item.assignmentLockReason && <Text size="xs" c="dimmed">{item.assignmentLockReason}</Text>}</Table.Td>
                <Table.Td>{item.reason ?? '—'}</Table.Td>
                <Table.Td><Group gap={4} wrap="nowrap">
                  {item.isPartnerBooking ? <><Text size="xs" c="dimmed">Managed under Partner requests</Text><Button size="xs" variant="light" onClick={() => { setCleanerVisit(item); setCleaningTypeChoice(null); setCleaningTypeError(''); }}>Change cleaning type</Button></> : <>
                  {isAdmin() && (occurrenceShift?.schedule.type === 4 || occurrenceShift?.schedule.type === 6) && <Button size="xs" variant="light" onClick={() => { setPricingShift(occurrenceShift); setPricingOccurrence(item); }}>Visit price</Button>}
                  {occurrenceShift?.isBranchMain
                    ? <Button size="xs" variant="light" disabled={item.canAssign === false} onClick={() => { setCleanerVisit(item); setCleanerChoice(item.assigneeIds?.[0] ?? item.offeredEmployeeId ?? null); setCleanerError(''); setCleaningTypeChoice(null); setCleaningTypeError(''); }}>Change cleaner</Button>
                    : <Button size="xs" variant="light" disabled={item.canAssign === false || item.status === 2 || item.status === 3} onClick={() => setAssigningVisit(item)}>Assign</Button>}
                  {occurrenceShift?.isBranchMain && item.exceptionId && <Button size="xs" variant="light" onClick={() => { setCleanerVisit(item); setCleaningTypeChoice(null); setCleaningTypeError(''); }}>Change cleaning type</Button>}
                  <Button size="xs" variant="subtle" onClick={() => openOccurrenceEditor(item)}>Change</Button>
                  {!item.exceptionId && <Button size="xs" variant="subtle" color="orange" onClick={() => openOccurrenceEditor(item, '2')}>Cancel</Button>}
                  {item.exceptionId && <Button size="xs" variant="subtle" color="red" loading={occurrenceSaving} onClick={() => restoreOccurrence(item)}>{item.status === 4 ? (occurrenceShift?.isBranchMain ? 'Delete visit' : 'Delete extra') : 'Restore'}</Button>}
                  </>}
                </Group></Table.Td>
              </Table.Tr>)}</Table.Tbody>
            </Table></Table.ScrollContainer>
          )}
        </Stack>
      </Modal>

      <AddVisitDialog opened={Boolean(visitDialog)} preset={visitDialog?.preset ?? null} employees={employees} timeZoneId={timeZoneId}
        onClose={() => setVisitDialog(null)} onAdded={(visit, propertyName) => { void visitAdded(visit, propertyName); }} />
      <Modal opened={Boolean(cleanerVisit)} onClose={() => { if (!cleanerSaving) setCleanerVisit(null); }} closeButtonProps={{ disabled: cleanerSaving }} title={cleanerVisit?.isPartnerBooking ? 'Cleaning type' : 'Change cleaner'}>
        <Stack>
          <Text size="sm">{cleanerVisit && visitLabel(cleanerVisit, timeZoneId)}</Text>
          {!cleanerVisit?.isPartnerBooking && <>
          <Select label="Cleaner" placeholder="Unassigned" clearable searchable value={cleanerChoice} onChange={setCleanerChoice}
            description="The cleaner gets an offer and holds the visit once accepted. A cleaner who already holds it is released."
            data={employees.filter(e => e.isActive && e.role !== 'partner').map(e => ({ value: e.id, label: employeeLabel(e, employees) }))} />
          {cleanerError && <Text role="alert" c="red">{cleanerError}</Text>}
          <Group justify="flex-end"><Button variant="default" disabled={cleanerSaving} onClick={() => setCleanerVisit(null)}>Cancel</Button><Button loading={cleanerSaving} onClick={saveCleaner}>Save cleaner</Button></Group>
          </>}
          <Select label="Cleaning type" allowDeselect={false} value={cleaningTypeChoice ?? cleanerVisit?.visitCleaningType ?? 'Normal'} onChange={setCleaningTypeChoice}
            description={cleaningTypeChoice === 'Deep' || (!cleaningTypeChoice && cleanerVisit?.visitCleaningType === 'Deep') ? `Deep cleaning adds €${DEEP_SURCHARGE_EUR} excl. VAT to this visit.` : 'Every visit starts as Normal. You can change it at any time, including after the cleaning is done.'}
            data={[{ value: 'Normal', label: 'Normal' }, { value: 'Deep', label: 'Deep' }]} />
          {cleaningTypeError && <Text role="alert" c="red">{cleaningTypeError}</Text>}
          <Group justify="flex-end"><Button variant="default" loading={cleaningTypeSaving} onClick={saveCleaningType}>Save cleaning type</Button></Group>
        </Stack>
      </Modal>
      {assigningVisit && occurrenceShift && <VisitAssigneesDialog key={`${assigningVisit.exceptionId ?? assigningVisit.startUtc}`}
        shiftId={occurrenceShift.id} visit={assigningVisit} employees={employees} timeZone={timeZoneId}
        onClose={() => setAssigningVisit(null)} onSaved={async () => { await loadOccurrences(); }} onOpenConflict={openClashingShift} />}
      <Modal opened={Boolean(editingOccurrence)} onClose={() => { if (!occurrenceSaving) setEditingOccurrence(null); }} closeButtonProps={{ disabled: occurrenceSaving }} closeOnEscape={!occurrenceSaving} closeOnClickOutside={!occurrenceSaving} title="Change occurrence">
        <Stack>
          <Select label="Action" value={occurrenceStatus} onChange={value => setOccurrenceStatus(value ?? '1')} data={OCCURRENCE_STATUSES}
            disabled={Boolean(editingOccurrence?.exceptionId)} />
          <TextInput label={`Start (${timeZoneId})`} type="datetime-local" required value={occurrenceStart} onChange={e => setOccurrenceStart(e.target.value)} />
          <TextInput label={`End (${timeZoneId})`} type="datetime-local" required value={occurrenceEnd} onChange={e => setOccurrenceEnd(e.target.value)} />
          <Select label="Replacement employee (optional)" clearable searchable value={occurrenceEmployee} onChange={setOccurrenceEmployee}
            data={employees.filter(e => e.isActive).map(e => ({ value: e.id, label: `${e.firstName} ${e.lastName}` }))} />
          <TextInput label="Reason" required maxLength={500} value={occurrenceReason} onChange={e => setOccurrenceReason(e.target.value)} />
          {occurrenceEditError && <Text role="alert" c="red">{occurrenceEditError}</Text>}
          <Group justify="flex-end"><Button variant="default" disabled={occurrenceSaving} onClick={() => setEditingOccurrence(null)}>Cancel</Button><Button loading={occurrenceSaving} onClick={saveOccurrence}>Save occurrence</Button></Group>
        </Stack>
      </Modal>

      <Modal opened={Boolean(templateShift)} onClose={() => setTemplateShift(null)} title="Assign checklist template">
        <Stack>
          <Text>{templateShift?.name}</Text>
          <Select label="Checklist template" placeholder="None" clearable value={assignedTemplate} onChange={setAssignedTemplate}
            data={templates.filter(t => (!t.companyId || t.companyId === templateShift?.companyId) && (!t.branchId || t.branchId === templateShift?.branchId)).map(t => ({ value: t.id, label: t.title }))} />
          <Text size="sm">Applies to upcoming occurrences and the current occurrence if it has no checklist yet. Started and submitted checklists keep their original items.</Text>
          {templateError && <Text c="red" role="alert">{templateError}</Text>}
          <Button loading={templateSaving} onClick={async () => {
            if (!templateShift) return;
            setTemplateSaving(true); setTemplateError('');
            try { const result = await adminShifts.setQualityCycleTemplate(templateShift.id, assignedTemplate);
              if (!result) { setTemplateError('Could not save the template assignment. Please retry.'); return; }
              setTemplateShift(null); await load();
            } catch { setTemplateError('Could not save the template assignment. Please retry.'); }
            finally { setTemplateSaving(false); }
          }}>Save template assignment</Button>
        </Stack>
      </Modal>
      <Modal opened={modalOpen} onClose={closeModal} title={editingShift ? 'Edit shift' : 'Add shift'} size="lg" radius="md">
        <Stack gap="sm">
          <Text size="sm" c="dimmed">Schedule times use the tenant time zone: {timeZoneId}.</Text>
          <CompanyPicker allowInactive={false} allowedInactiveCompanyId={editingShift?.companyId} value={companyId} onChange={(v) => { setBranches([]); setCompanyId(v); setBranchId(null); setQualityCycleTemplateId(null); }} />
          <Select label="Branch" data={branches.filter(b => b.isActive !== false || b.id === editingShift?.branchId).map((b) => ({ value: b.id, label: `${b.name}${b.isActive === false ? ' (inactive)' : ''}` }))} value={branchId} onChange={v => { setBranchId(v); setPriceFrom(null); setQualityCycleTemplateId(null); }} searchable disabled={!companyId} />
          {editingShift && <Group align="flex-end" gap="xs" wrap="nowrap">
            <TextInput label="Name" readOnly style={{ flex: 1 }} value={name} description="Generated from the property: cleaning type, address, size, rooms and beds." />
          </Group>}
          {!editingShift && (branches.find(b => b.id === branchId)?.priceVersions?.length ?? 0) > 1 && <Select label="Price version" description="The property price this shift is billed at. Defaults to the price in force today." allowDeselect={false} value={priceFrom && (branches.find(b => b.id === branchId)?.priceVersions ?? []).some(v => v.effectiveFrom === priceFrom) ? priceFrom : defaultPriceVersion(branches.find(b => b.id === branchId)?.priceVersions)} onChange={setPriceFrom}
            data={(branches.find(b => b.id === branchId)?.priceVersions ?? []).map(v => ({ value: v.effectiveFrom, label: `From ${v.effectiveFrom} · €${v.priceNet.toFixed(2)} excl. VAT` }))} />}
          {scheduleType !== '6' && <Group grow>
            <TextInput label="Start date" type="date" required={!editingShift || Boolean(editingShift.validFrom)} value={validFrom} onChange={e => setValidFrom(e.target.value)} />
            <TextInput label="End date (optional)" type="date" min={validFrom || undefined} value={validUntil} onChange={e => setValidUntil(e.target.value)} />
          </Group>}
          <Select label="Schedule" required withAsterisk={false} placeholder="Choose a schedule type" allowDeselect={false} data={SCHEDULE_TYPES.filter(type => type.value !== '6' || editingShift?.schedule.type === 6)} value={scheduleType || null} onChange={(v) => { if (v) setScheduleType(v); }} />
          {scheduleType === '4' && <Text size="sm">On-call is a reusable shift without scheduled dates. After creating it, enter the date, time and cleaner for a one-time visit. Only dated visits appear on calendars.</Text>}

          {scheduleType === '2' && (
            <MultiSelect
              label="Days"
              data={DAYS.map((d, i) => ({ value: String(i), label: d }))}
              value={weekDays}
              onChange={setWeekDays}
            />
          )}
          {scheduleType === '1' && <>
            <MultiSelect label="Days" data={DAYS.map((d, i) => ({ value: String(i), label: d }))} value={weekDays} onChange={setWeekDays} />
            {weekDays.map(day => <Group key={day} wrap="wrap" grow gap="xs">
              <Text size="sm" fw={500}>{DAYS[Number(day)]}</Text>
              <TextInput label="Start" type="time" value={(dailyTimes[day] ?? { start, end }).start} onChange={e => setDailyTimes(current => ({ ...current, [day]: { ...(current[day] ?? { start: '08:00', end: '17:00' }), start: e.target.value } }))} />
              <TextInput label="End" type="time" value={(dailyTimes[day] ?? { start, end }).end} onChange={e => setDailyTimes(current => ({ ...current, [day]: { ...(current[day] ?? { start: '08:00', end: '17:00' }), end: e.target.value } }))} />
            </Group>)}
          </>}
          {scheduleType === '3' && (
            <Group wrap="wrap" grow gap="xs">
              <Select label="Week parity" data={[{ value: '1', label: 'Week 1' }, { value: '2', label: 'Week 2' }]} value={weekParity} onChange={(v) => setWeekParity(v ?? '1')} />
              <Select label="Day" data={DAYS.map((d, i) => ({ value: String(i), label: d }))} value={weekDays[0] ?? ''} onChange={(v) => setWeekDays(v ? [v] : [])} />
            </Group>
          )}
          {scheduleType === '5' && (
            <NumberInput label="Day of month (1-31)" min={1} max={31} value={monthlyDay} onChange={setMonthlyDay} />
          )}
          {scheduleType === '6' && (
            <Group wrap="wrap" grow gap="xs">
              <TextInput label="Date" type="date" required value={oneTimeDate} onChange={e => setOneTimeDate(e.target.value)} />
              <TextInput label="Start time" type="time" required value={oneTimeStart} onChange={(e) => setOneTimeStart(e.target.value)} />
              <TextInput label="End time" type="time" required value={oneTimeEnd} onChange={(e) => setOneTimeEnd(e.target.value)} />
            </Group>
          )}
          {scheduleType !== '4' && scheduleType !== '1' && scheduleType !== '6' && (
            <Group wrap="wrap" grow gap="xs">
              <TextInput label="Start" type="time" value={start} onChange={(e) => setStart(e.target.value)} />
              <TextInput label="End" type="time" value={end} onChange={(e) => setEnd(e.target.value)} />
            </Group>
          )}
          <TextInput label="Notes" value={notes} onChange={(e) => setNotes(e.target.value)} />
          <Select
            label="Quality cycle template"
            placeholder="None"
            clearable
            data={templates.filter(t => (!t.companyId || t.companyId === companyId) && (!t.branchId || t.branchId === branchId)).map((t) => ({ value: t.id, label: t.title }))}
            value={qualityCycleTemplateId}
            onChange={setQualityCycleTemplateId}
            searchable
          />
          {error && <Text size="sm" c="red">{error}</Text>}
          <Group justify="flex-end" mt="xs">
            <Button variant="default" onClick={closeModal}>Cancel</Button>
            <Button loading={saving} onClick={submit}>{editingShift ? 'Save changes' : 'Create shift'}</Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}
