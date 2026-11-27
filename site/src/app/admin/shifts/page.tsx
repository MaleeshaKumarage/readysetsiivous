'use client';

import { showError } from '@/lib/toast';
import { Fragment, useEffect, useRef, useState } from 'react';
import {
  Title, Button, Table, Modal, TextInput, Textarea, Select, MultiSelect, Group, Stack, Text, Loader, ActionIcon, NumberInput, Menu,
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
import { ListPager, PAGE_SIZE } from '@/components/ListPager';
import { ShiftWorkspace, scheduleLabel } from '@/components/admin/ShiftWorkspace';
import { autoShiftName } from '@/lib/shiftName';
import { employeeLabel } from '@/lib/employeeLabel';
import { DEEP_SURCHARGE_EUR } from '@/lib/cleaningPricing';
import { Badge, Button as DsButton, Card as DsCard } from '@/ds';

// The price version in force today (or the earliest when all start later): the default billing price for a new shift.
const defaultPriceVersion = (versions?: { effectiveFrom: string }[] | null) => {
  const today = new Date().toISOString().slice(0, 10);
  const sorted = [...(versions ?? [])].sort((a, b) => a.effectiveFrom.localeCompare(b.effectiveFrom));
  return (sorted.filter(v => v.effectiveFrom <= today).pop() ?? sorted[0])?.effectiveFrom ?? null;
};

const DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
/** The Show visits filter of a work order's visits panel. */
type VisitFilter = 'all' | 'scheduled' | 'ongoing' | 'completed' | 'cancelled' | 'noclean';
const VISIT_FILTERS: [VisitFilter, string][] = [['all', 'All'], ['scheduled', 'Scheduled'], ['ongoing', 'Ongoing'], ['completed', 'Completed'], ['cancelled', 'Cancelled'], ['noclean', 'No cleaner']];
const visitStatusOf = (v: ShiftOccurrence): 'scheduled' | 'ongoing' | 'completed' | 'cancelled' =>
  v.isCancelledManualVisit ? 'cancelled' : v.workStatus === 'Completed' ? 'completed' : v.workStatus === 'InProgress' ? 'ongoing' : 'scheduled';
const visitMatchesFilter = (v: ShiftOccurrence, filter: VisitFilter) =>
  filter === 'all' ? true : filter === 'noclean' ? visitStatusOf(v) === 'scheduled' && (v.assigneeIds ?? []).length === 0 : visitStatusOf(v) === filter;
const VISIT_NOTES: Record<string, string> = {
  ongoing: 'Ongoing: the time can’t change and it can’t be cancelled.',
  completed: 'Completed: only the cleaning type and price can change.',
  cancelled: 'Cancelled: restore it to change employees or time.',
};

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
  const [propertySearch, setPropertySearch] = useState('');
  const [expandedFilter, setExpandedFilter] = useState<VisitFilter>('all');
  useEffect(() => {
    let live = true;
    for (const companyId of new Set(pageRows.map(s => s.companyId))) {
      adminCompanies.get(companyId).then((d) => {
        if (!live || !Array.isArray(d?.branches)) return;
        const branches = d.branches;
        setPropertyById(prev => ({ ...prev, ...Object.fromEntries(branches.map(b => [b.id, b])) }));
      }).catch(() => { /* the row keeps its shift name */ });
    }
    return () => { live = false; };
  }, [pageRows]);
  // The visits shown under an expanded row, for one calendar month in tenant time. Separate from the visits popup.
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [expandedMonth, setExpandedMonth] = useState('');
  const panelMonth = useRef<{ shiftId: string; month: string } | null>(null);
  // First and last day of a month (YYYY-MM), as tenant-local dates.
  const monthRange = (ym: string): [string, string] => {
    const [year, month] = ym.split('-').map(Number);
    const days = new Date(Date.UTC(year, month, 0)).getUTCDate();
    return [`${ym}-01`, `${ym}-${String(days).padStart(2, '0')}`];
  };
  // Two months back to nine months ahead of the current tenant-local month.
  const monthChoices = (): { value: string; label: string }[] => {
    const [year, month] = localInputValue(new Date().toISOString(), timeZoneId).slice(0, 7).split('-').map(Number);
    return Array.from({ length: 12 }, (_, i) => {
      const date = new Date(Date.UTC(year, month - 1 - 2 + i, 1));
      return { value: date.toISOString().slice(0, 7), label: date.toLocaleDateString(undefined, { month: 'long', year: 'numeric', timeZone: 'UTC' }) };
    });
  };
  // The visits panel of one work order opens and closes from its Visits button or menu item.
  // The label of a YYYY-MM month, as the month list shows it.
  const monthLabelOf = (ym: string) => {
    const [year, month] = ym.split('-').map(Number);
    return new Date(Date.UTC(year, month - 1, 1)).toLocaleDateString(undefined, { month: 'long', year: 'numeric', timeZone: 'UTC' });
  };
  // The month list offers the window around today, and also the open month when it lies outside that window.
  const panelMonthChoices = () => {
    const choices = monthChoices();
    if (expandedMonth && !choices.some(choice => choice.value === expandedMonth)) choices.push({ value: expandedMonth, label: monthLabelOf(expandedMonth) });
    return choices;
  };
  const toggleExpanded = async (shift: Shift) => {
    if (expandedId === shift.id) { occurrenceGeneration.current++; setExpandedId(null); setOccurrenceShift(null); setOccurrences([]); return; }
    await openOccurrences(shift);
  };
  // Changing the month of the visits panel reloads its visits for that month.
  const changeExpandedMonth = (month: string | null) => {
    if (!month || !occurrenceShift) return;
    setExpandedMonth(month);
    panelMonth.current = { shiftId: occurrenceShift.id, month };
    const [from, to] = monthRange(month);
    setOccurrenceFrom(from); setOccurrenceTo(to);
    void loadOccurrences(occurrenceShift, from, to);
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
  // The shift's own contract price. 'branch' keeps the property price.
  const [pricingChoice, setPricingChoice] = useState<'branch' | 'Monthly' | 'PerVisit'>('branch');
  const [contractRate, setContractRate] = useState<string>('');
  const [contractVat, setContractVat] = useState<string>('25.5');
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
  const [highlightUtc, setHighlightUtc] = useState<string | null>(null);
  const [occurrenceFrom, setOccurrenceFrom] = useState(new Date().toISOString().slice(0, 10));
  const [occurrenceTo, setOccurrenceTo] = useState(new Date(Date.now() + 31 * 86400000).toISOString().slice(0, 10));
  const [editingOccurrence, setEditingOccurrence] = useState<ShiftOccurrence | null>(null);
  const [occurrenceStatus, setOccurrenceStatus] = useState('1');
  const [occurrenceStart, setOccurrenceStart] = useState('');
  const [occurrenceEnd, setOccurrenceEnd] = useState('');
  const [occurrenceReason, setOccurrenceReason] = useState('');
  const [occurrenceNotes, setOccurrenceNotes] = useState('');
  // Cancelling a manually created visit keeps it in the list so it can be restored.
  const [cancellingVisit, setCancellingVisit] = useState<ShiftOccurrence | null>(null);
  const [cancelVisitStatus, setCancelVisitStatus] = useState('3');
  const [cancelVisitReason, setCancelVisitReason] = useState('');
  const [cancelVisitError, setCancelVisitError] = useState('');
  const [occurrenceEmployee, setOccurrenceEmployee] = useState<string | null>(null);
  const [occurrenceSaving, setOccurrenceSaving] = useState(false);
  const [assigningVisit, setAssigningVisit] = useState<ShiftOccurrence | null>(null);
  // Add visit: null is closed; a preset fixes the property when opened from a shift.
  const [visitDialog, setVisitDialog] = useState<{ preset: { companyId: string; branchId: string; propertyName: string } | null } | null>(null);
  const [cleanerVisit, setCleanerVisit] = useState<ShiftOccurrence | null>(null);
  const [cleanerMode, setCleanerMode] = useState<'employee' | 'type'>('type');
  const [cleanerChoice, setCleanerChoice] = useState<string | null>(null);
  const [cleaningTypeChoice, setCleaningTypeChoice] = useState<string | null>(null), [cleaningTypeSaving, setCleaningTypeSaving] = useState(false), [cleaningTypeError, setCleaningTypeError] = useState('');
  const [cleanerSaving, setCleanerSaving] = useState(false);
  const [cleanerError, setCleanerError] = useState('');
  const [defaultCleanerError, setDefaultCleanerError] = useState('');
  // Assign employees on a property's main shift sets its default employee (new visits are offered to them).
  const [defaultCleanerFor, setDefaultCleanerFor] = useState<Shift | null>(null);
  const [defaultCleanerPick, setDefaultCleanerPick] = useState<string | null>(null);
  const [defaultCleanerSaving, setDefaultCleanerSaving] = useState(false);
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
  // Visit progress for the list: one occurrence request per visible work order, for the current month.
  type VisitProgress = { total: number; completed: number; ongoing: number; scheduled: number; cancelled: number };
  const [progressById, setProgressById] = useState<Record<string, VisitProgress>>({});
  const progressSeq = useRef(0);
  const loadProgress = (items: Shift[]) => {
    const seq = ++progressSeq.current;
    const now = new Date();
    const from = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1)).toISOString();
    const to = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() + 1, 1)).toISOString();
    void Promise.all(items.map(s => adminShifts.occurrences(s.id, from, to).then(list => {
      const visits = list ?? [];
      const live = visits.filter(v => !v.isCancelledManualVisit);
      const cancelled = visits.length - live.length;
      const completed = live.filter(v => v.workStatus === 'Completed').length;
      const ongoing = live.filter(v => v.workStatus === 'InProgress').length;
      return [s.id, { total: live.length, completed, ongoing, scheduled: live.length - completed - ongoing, cancelled }] as const;
    }).catch(() => [s.id, null] as const))).then(pairs => {
      if (seq !== progressSeq.current) return;
      setProgressById(Object.fromEntries(pairs.filter((p): p is readonly [string, VisitProgress] => p[1] !== null)));
    });
  };
  // Only the newest tab/page request may update the list, so a slow earlier response cannot show the wrong tab.
  const loadSeq = useRef(0);
  const load = () => { const seq = ++loadSeq.current; setLoading(true); return adminShifts.page({ tab: lifecycle, skip: (shiftPage - 1) * PAGE_SIZE, take: PAGE_SIZE, companyId: filterCompany, scheduleType: filterType }).then((r) => {
    if (seq !== loadSeq.current) return;
    // A reply without the paged shape cannot be shown; say so and keep the retry path instead of crashing the screen.
    if (!r || !Array.isArray(r.items) || typeof r.total !== 'number' || !r.counts) throw new Error('The shift list could not be read. Refresh the page to retry.');
    setPageRows(r.items); setPageTotal(r.total); setTabCounts(r.counts); setListError(''); void loadAssignmentCounts(); void loadAssigneeNames(r.items.map(s => s.id)); loadProgress(r.items);
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
    setName(''); setNotes(''); setWeekDays([]); setCompanyId(null); setBranchId(null); setPriceFrom(null); setPricingChoice('branch'); setContractRate(''); setContractVat('25.5'); setQualityCycleTemplateId(null); setScheduleType(''); setStart('08:00'); setEnd('17:00'); setWeekParity('1'); setMonthlyDay(1); setOneTimeDate(''); setOneTimeStart('08:00'); setOneTimeEnd('17:00'); setDailyTimes({}); setEditingShift(null); setError(''); setModalOpen(false);
  };

  // A customer workspace link carries ?company= so the create form opens with that customer selected.
  const preselectedCompany = useRef<string | null>(null);
  useEffect(() => { preselectedCompany.current = new URLSearchParams(window.location.search).get('company'); }, []);
  const openCreate = () => { if (!schedulingReady) return; closeModal(); if (preselectedCompany.current) { setCompanyId(preselectedCompany.current); preselectedCompany.current = null; } setValidFrom(localInputValue(new Date().toISOString(), timeZoneId).slice(0, 10)); setValidUntil(''); setModalOpen(true); };
  const openEdit = async (shift: Shift) => {
    const schedule = shift.schedule;
    setEditingShift(shift); setCompanyId(shift.companyId); setBranchId(shift.branchId);
    setName(shift.name); setNotes(shift.notes ?? ''); setQualityCycleTemplateId(shift.qualityCycleTemplateId ?? null);
    setPricingChoice(shift.pricingMode === 0 ? 'Monthly' : shift.pricingMode === 1 ? 'PerVisit' : 'branch');
    setContractRate(shift.rateNet != null ? String(shift.rateNet) : ''); setContractVat(shift.vatRatePercent != null ? String(shift.vatRatePercent) : '25.5');
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
    if (pricingChoice !== 'branch' && (!(Number(contractRate) > 0) || !(Number(contractVat) >= 0 && Number(contractVat) <= 100))) { setError('Enter a price above zero and a VAT rate from 0 to 100.'); return; }
    submitting.current = true; setSaving(true); setError('');
    const branchVersions = branches.find(b => b.id === branchId)?.priceVersions;
    const chosenPriceFrom = priceFrom && branchVersions?.some(v => v.effectiveFrom === priceFrom) ? priceFrom : defaultPriceVersion(branchVersions) ?? undefined;
    // A one-time visit is valid only on its date, so the validity dates mirror that date.
    // Create names the shift from its property and schedule. Editing saves the name in the field, which starts from the stored name.
    // The server builds every shift name from the property, so the name sent here is only a placeholder.
    const shiftName = autoShiftName(branches.find(b => b.id === branchId));
    if (!shiftName) { setError('Could not name this shift. Choose a property and schedule, then save again.'); return; }
    // Editing always sends the price choice; the server clears the shift's own price when 'Property price' is chosen.
    const priceFields = pricingChoice === 'branch'
      ? { changePrice: true, pricingMode: null, rateNet: null, vatRatePercent: null }
      : { changePrice: true, pricingMode: pricingChoice === 'Monthly' ? 0 : 1, rateNet: Number(contractRate), vatRatePercent: Number(contractVat) };
    const fields = { companyId, branchId, name: shiftName, schedule: buildSchedule(), notes: notes.trim() || undefined, isActive: editingShift?.isActive ?? true, validFrom: oneTime ? oneTimeDate : validFrom || undefined, validUntil: oneTime ? oneTimeDate : validUntil || undefined, qualityCycleTemplateId: qualityCycleTemplateId || undefined,
      // Creating a shift picks the branch price version it bills at; the default is the price in force today.
      priceEffectiveFrom: editingShift ? undefined : chosenPriceFrom, ...priceFields };
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
  const openOccurrences = async (shift: Shift, notice = '', highlight: string | null = null) => {
    setShiftNotice(notice);
    setHighlightUtc(highlight);
    // A work order reopens on the month last chosen for it; another work order starts on the current month.
    const month = highlight ? localInputValue(highlight, timeZoneId).slice(0, 7)
      : panelMonth.current?.shiftId === shift.id ? panelMonth.current.month : localInputValue(new Date().toISOString(), timeZoneId).slice(0, 7);
    panelMonth.current = { shiftId: shift.id, month };
    setExpandedId(shift.id); setExpandedMonth(month); setExpandedFilter('all');
    setOccurrenceShift(shift); setOccurrences([]); setOccurrenceError('');
    const [from, to] = monthRange(month);
    setOccurrenceFrom(from); setOccurrenceTo(to);
    await loadOccurrences(shift, from, to);
  };

  // Opens the clashing shift's visit list from an overlap refusal, with the overlapping visit marked. The assignment
  // dialog stays open if that shift cannot be loaded, so the admin keeps the employee and reason they had chosen.
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

  // A new one-time visit on an on-call shift prefills its default employee and notes; the user only sets date and times.
  const openOccurrenceEditor = (occurrence?: ShiftOccurrence, status = '1', shift: Shift | null = occurrenceShift) => {
    const quick = !occurrence && status === '4' && shift?.schedule.type === 4 ? shift : null;
    setEditingOccurrence(occurrence ?? { startUtc: '', endUtc: '' });
    setOccurrenceStatus(occurrence?.status ? String(occurrence.status) : status);
    setOccurrenceStart(occurrence?.startUtc ? localInputValue(occurrence.startUtc, timeZoneId) : '');
    setOccurrenceEnd(occurrence?.endUtc ? localInputValue(occurrence.endUtc, timeZoneId) : '');
    setOccurrenceReason(occurrence?.reason ?? (quick ? (quick.notes?.trim() || 'Quick visit') : ''));
    setOccurrenceEmployee(occurrence?.employeeId ?? quick?.defaultEmployeeId ?? null); setOccurrenceEditError('');
    setOccurrenceNotes(occurrence?.visitNotes ?? '');
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
        status, reason: occurrenceReason.trim(), employeeId: occurrenceEmployee, notes: occurrenceNotes.trim() ? occurrenceNotes : (editingOccurrence.visitNotes ? '' : undefined),
      });
      setEditingOccurrence(null);
      setShiftNotice(status === 4 ? 'One-time visit scheduled. It appears in the shift calendar; assigned employees see it in My shifts.' : 'Visit updated.');
      // Keep a newly scheduled date visible even when it is outside the old list window.
      const savedDay = occurrenceStart.slice(0, 10);
      // The panel shows the saved visit's month, so the new visit is listed.
      const savedMonth = savedDay.slice(0, 7);
      if (savedMonth !== expandedMonth) {
        setExpandedMonth(savedMonth); panelMonth.current = { shiftId: occurrenceShift.id, month: savedMonth };
        const [from, to] = monthRange(savedMonth);
        setOccurrenceFrom(from); setOccurrenceTo(to);
        await loadOccurrences(occurrenceShift, from, to);
      } else await loadOccurrences();
    } catch (failure) { setOccurrenceEditError(failure instanceof Error ? failure.message : 'Could not update this visit.'); }
    finally { setOccurrenceSaving(false); }
  };

  const openCancelVisit = (item: ShiftOccurrence) => { setCancellingVisit(item); setCancelVisitStatus('3'); setCancelVisitReason(''); setCancelVisitError(''); };
  const confirmCancelVisit = async () => {
    if (!occurrenceShift || !cancellingVisit?.exceptionId) return;
    if (!cancelVisitReason.trim()) { setCancelVisitError('Enter a reason.'); return; }
    setOccurrenceSaving(true); setCancelVisitError('');
    try {
      await adminShifts.cancelVisit(occurrenceShift.id, cancellingVisit.exceptionId, { status: Number(cancelVisitStatus), reason: cancelVisitReason.trim() });
      setCancellingVisit(null); setShiftNotice('Visit cancelled. You can restore it from this list.');
      await loadOccurrences();
    } catch (failure) { setCancelVisitError(failure instanceof Error ? failure.message : 'Could not cancel this visit.'); }
    finally { setOccurrenceSaving(false); }
  };
  const restoreManualVisit = async (item: ShiftOccurrence) => {
    if (!occurrenceShift || !item.exceptionId) return;
    setOccurrenceSaving(true); setOccurrenceError('');
    try { await adminShifts.restoreVisit(occurrenceShift.id, item.exceptionId); setShiftNotice('Visit restored.'); await loadOccurrences(); }
    catch (failure) { setOccurrenceError(failure instanceof Error ? failure.message : 'Could not restore this visit.'); }
    finally { setOccurrenceSaving(false); }
  };

  // A new visit shows up under its property's shift; an open visit list reloads to include it.
  const visitAdded = async (visit: { number: number; shiftId: string; startUtc: string; status: string }, propertyName: string) => {
    setVisitDialog(null);
    const outcome = visit.status === 'Assigned' ? 'The employee has it in My shifts.' : visit.status === 'Offered' ? 'An offer was sent to the employee.' : 'It has no employee yet.';
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

  const visibleRows = pageRows.filter(s => {
    const q = propertySearch.trim().toLowerCase();
    if (!q) return true;
    const branch = propertyById[s.branchId];
    return [s.name, branch?.street, branch?.city].some(v => (v ?? '').toLowerCase().includes(q));
  });
  const shownVisits = occurrences.filter(v => visitMatchesFilter(v, expandedFilter));
  return (
    <>
      <Group justify="space-between" align="flex-start" mb="md" wrap="wrap" gap="sm">
        <div>
          <Title order={2} component="h1">Work orders</Title>
          <Text c="dimmed">One work order per property. Its visits are the cleanings. {pageRows.length} of {pageTotal} shown.</Text>
        </div>
        <Group gap="xs" wrap="wrap">
          <Button variant="subtle" onClick={() => setShiftNotice('Export is not connected in this prototype. Nothing was exported.')}>Export</Button>
          <Button leftSection={<Plus size={16} />} disabled={!schedulingReady} onClick={openCreate}>New work order</Button>
        </Group>
      </Group>

      {shiftNotice && <Text role="status" aria-live="polite" mb="md">{shiftNotice}</Text>}
      {listError && <Text role="alert" c="red" mb="md">{listError}</Text>}
      {countError && <Text role="alert" c="red" mb="md">{countError}</Text>}

      <Group grow align="flex-end" mb="sm">
        <Select label="Filter by company" placeholder="All companies" clearable searchable data={companyChoices} value={filterCompany}
          onChange={(value) => { setFilterCompany(value); setShiftPage(1); }} />
        <Select label="Filter by type" placeholder="All types" clearable data={SCHEDULE_TYPES} value={filterType}
          onChange={(value) => { setFilterType(value); setShiftPage(1); }} />
      </Group>
      <LifecycleTabs value={lifecycle} onChange={changeLifecycle} label="Work order activity" disabled={assignBusy || templateSaving}
        activeCount={activeCount} inactiveCount={inactiveCount}>
      <TextInput placeholder="Search property" aria-label="Search work orders" mb="sm" value={propertySearch}
        onChange={(e) => setPropertySearch(e.currentTarget.value)} />
      {!loading && pageTotal > 0 && visibleRows.length === 0 && <Text c="dimmed" mb="sm">No work orders match this view.</Text>}
      {loading ? <Loader /> : pageTotal === 0 ? (
        <Text c="dimmed">{(tabCounts?.active ?? 0) + (tabCounts?.completed ?? 0) + (tabCounts?.inactive ?? 0) === 0 ? 'No shifts yet.' : `No ${lifecycle} shifts. Check the ${lifecycle === 'active' ? 'Inactive' : 'Active'} tab.`}</Text>
      ) : (<>
        <Text size="xs" c="dimmed" hiddenFrom="sm" mb={6}>Scroll sideways to see all columns.</Text>
        <Table.ScrollContainer minWidth={600} tabIndex={0} role="region" aria-label="Work orders table">
          <Table striped highlightOnHover withTableBorder>
            <Table.Thead className="bg-brand-500 text-accent-950">
              <Table.Tr className="!bg-brand-500 !text-accent-950"><Table.Th fw={700} className="!bg-brand-500 !text-accent-950">Property</Table.Th><Table.Th fw={700} className="!bg-brand-500 !text-accent-950" aria-label="Missing details" /><Table.Th fw={700} className="!bg-brand-500 !text-accent-950">Visits</Table.Th><Table.Th w={200} fw={700} className="!bg-brand-500 !text-accent-950"><span className="sr-only">Actions</span></Table.Th></Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {visibleRows.map((s) => (
                <Fragment key={s.id}>
                  <Table.Tr>
                    <Table.Td>
                      {(() => {
                        const branch = propertyById[s.branchId];
                        const address = branch ? [branch.street?.trim(), [branch.postalCode?.trim(), branch.city?.trim()].filter(Boolean).join(' ')].filter(Boolean).join(', ') : '';
                        const size = branch?.sizeM2 != null ? `${branch.sizeM2} m²` : null;
                        const company = companyChoices.find(c => c.value === s.companyId)?.label;
                        const time = s.schedule.dailyStart && s.schedule.dailyEnd ? `${s.schedule.dailyStart.slice(0, 5)} to ${s.schedule.dailyEnd.slice(0, 5)}` : null;
                        const checklist = templates.find(t => t.id === s.qualityCycleTemplateId)?.title;
                        return (
                          <Stack gap={4}>
                            <strong className="text-accent-950">{address || s.name}</strong>
                            {address && <span className="sr-only">{s.name}</span>}
                            {size && <Text size="sm" c="dimmed">{size}</Text>}
                            {company && <Text size="sm" c="dimmed">{company}</Text>}
                            <Group gap={6}>
                              <Badge variant="subtle">{scheduleLabel(s)}</Badge>
                              {time && <Badge variant="subtle">{time}</Badge>}
                            </Group>
                            <Text size="sm" c="dimmed">{checklist ? `Checklist template: ${checklist}` : 'Checklist template: none'}</Text>
                          </Stack>
                        );
                      })()}
                    </Table.Td>
                    <Table.Td>
                      {(() => {
                        const branch = propertyById[s.branchId];
                        const missing = [
                          assignmentCounts && assignmentCounts[s.id] === 0 ? 'Missing cleaner' : null,
                          !s.qualityCycleTemplateId ? 'Missing Quality Cycle' : null,
                          !branch?.pricingReady && s.isBranchMain !== false ? 'Missing price' : null,
                          // A one-time shift whose day has passed still needs its completion checked.
                          s.schedule.type === 6 && !s.isCompleted && s.schedule.oneTimeDate && s.schedule.oneTimeDate.slice(0, 10) < localInputValue(new Date().toISOString(), timeZoneId).slice(0, 10) ? 'Check completion' : null,
                        ].filter((m): m is string => !!m);
                        return <Group gap={6}>{missing.map(m => <Badge key={m} variant="gold">{m}</Badge>)}</Group>;
                      })()}
                    </Table.Td>
                    <Table.Td>
                      {(() => {
                        const pr = progressById[s.id];
                        if (!pr) return <Text size="sm" c="dimmed">—</Text>;
                        const doneDeg = pr.total ? Math.round(pr.completed / pr.total * 360) : 0;
                        const ongoingDeg = pr.total ? Math.round((pr.completed + pr.ongoing) / pr.total * 360) : 0;
                        const label = `${pr.completed} of ${pr.total} visits completed, ${pr.ongoing} ongoing, ${pr.scheduled} scheduled${pr.cancelled ? `, ${pr.cancelled} cancelled` : ''}`;
                        return (
                          <Group gap="xs" wrap="nowrap" className="group relative" tabIndex={0} role="img" aria-label={label}>
                            <div style={{ width: 44, height: 44, borderRadius: 999, background: pr.total ? `conic-gradient(var(--mantine-color-accent-9, #0a1741) 0 ${doneDeg}deg, #c9a340 ${doneDeg}deg ${ongoingDeg}deg, #c6cee3 ${ongoingDeg}deg 360deg)` : '#e4e8f2', position: 'relative', flex: '0 0 44px' }}>
                              <div style={{ position: 'absolute', inset: 8, borderRadius: 999, background: 'white' }} />
                            </div>
                            <div className="hidden group-hover:block group-focus:block absolute bottom-full left-0 z-10 mb-2 rounded-lg bg-accent-950 px-3 py-2 text-sm text-white shadow-lg whitespace-nowrap">
                              <strong>{pr.completed} of {pr.total} completed</strong>
                              <div>{pr.ongoing} ongoing · {pr.scheduled} scheduled{pr.cancelled ? ` · ${pr.cancelled} cancelled` : ''}</div>
                            </div>
                          </Group>
                        );
                      })()}
                    </Table.Td>
                    <Table.Td>
                      <Group gap={6} wrap="nowrap">
                        <Button variant="light" size="xs" aria-expanded={expandedId === s.id} onClick={() => void toggleExpanded(s)}>Visits</Button>
                        <Menu shadow="md" width={230} position="bottom-end" withinPortal>
                          <Menu.Target>
                            <ActionIcon variant="light" size="md" aria-label={`Actions for ${s.name}`}><MoreHorizontal size={16} /></ActionIcon>
                          </Menu.Target>
                          <Menu.Dropdown>
                            <Menu.Item leftSection={<Eye size={13} />} onClick={() => setWorkspaceId(s.id)}>View details</Menu.Item>
                            {isAdmin() && !s.isBranchMain && <Menu.Item leftSection={<Receipt size={13} />} onClick={() => { setPricingShift(s); setPricingOccurrence(undefined); }}>Pricing</Menu.Item>}
                            {isAdmin() && <Menu.Item leftSection={<Pencil size={13} />} onClick={() => openEdit(s)}>Edit schedule</Menu.Item>}
                            {s.isActive && !s.isBranchMain && <Menu.Item disabled={assignBusy} leftSection={<UserPlus size={13} />} onClick={() => openAssign(s.id)}>Assign employees</Menu.Item>}
                            {s.isActive && s.isBranchMain && <Menu.Item leftSection={<UserPlus size={13} />} onClick={() => { setDefaultCleanerError(''); setDefaultCleanerPick(s.defaultEmployeeId ?? null); setDefaultCleanerFor(s); }}>Assign employees</Menu.Item>}
                            {s.isActive && <Menu.Item leftSection={<CalendarDays size={13} />} onClick={() => void openOccurrences(s)}>Visits</Menu.Item>}
                            {s.isActive && s.isBranchMain && <Menu.Item leftSection={<Plus size={13} />} onClick={() => setVisitDialog({ preset: { companyId: s.companyId, branchId: s.branchId, propertyName: s.name } })}>Add visit</Menu.Item>}
                            {s.isActive && !s.isBranchMain && s.schedule.type === 4 && <Menu.Item leftSection={<CalendarPlus size={13} />} onClick={() => { void openOccurrences(s); openOccurrenceEditor(undefined, '4', s); }}>Add visit</Menu.Item>}
                            {isAdmin() && s.isActive && <Menu.Item leftSection={<ListChecks size={13} />} onClick={() => { setTemplateShift(s); setAssignedTemplate(s.qualityCycleTemplateId ?? null); setTemplateError(''); }}>Checklist template</Menu.Item>}
                            {s.isActive && <Menu.Item color="red" leftSection={<PauseCircle size={13} />} disabled={assignBusy} onClick={() => setConfirmAction({ title: `Deactivate “${s.name}”?`, body: 'It stops taking new visits. You can reactivate it later from the Inactive tab.', confirmLabel: 'Deactivate', color: 'red', run: () => deactivate(s.id) })}>Deactivate</Menu.Item>}
                            {!s.isActive && <Menu.Item leftSection={<PlayCircle size={13} />} disabled={!!restoring} onClick={() => setConfirmAction({ title: `Reactivate “${s.name}”?`, body: 'It returns to the Active tab and can take visits again.', confirmLabel: 'Reactivate', run: () => reactivate(s.id) })}>Reactivate</Menu.Item>}
                            {!s.isActive && <Menu.Item color="red" leftSection={<Trash2 size={13} />} disabled={!!restoring} onClick={() => setConfirmAction({ title: `Delete “${s.name}”?`, body: 'Permanently delete this inactive shift and remove its employee assignments. Completed checklist records, photos and reports will remain in history.', confirmLabel: 'Delete', color: 'red', run: () => deleteShift(s) })}>Delete</Menu.Item>}
                          </Menu.Dropdown>
                        </Menu>
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                </Fragment>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
        <ListPager label="Work orders" total={pageTotal} page={shiftPage} pageCount={shiftPageCount} onPage={setShiftPage} /></>
      )}
      </LifecycleTabs>

      {(() => {
        const drawerShift = expandedId ? pageRows.find(r => r.id === expandedId) ?? null : null;
        if (!drawerShift) return null;
        const title = propertyById[drawerShift.branchId] ? [propertyById[drawerShift.branchId].street?.trim(), [propertyById[drawerShift.branchId].postalCode?.trim(), propertyById[drawerShift.branchId].city?.trim()].filter(Boolean).join(' ')].filter(Boolean).join(', ') || drawerShift.name : drawerShift.name;
        return (
          <>
            <div className="fixed inset-0 z-[190] bg-accent-950/40" aria-hidden onClick={() => void toggleExpanded(drawerShift)} />
            <aside role="dialog" aria-modal="true" aria-label={`Visits of ${drawerShift.name}`} data-testid="row-visits" className="fixed inset-y-0 right-0 z-[191] flex w-full max-w-[560px] flex-col gap-4 overflow-y-auto bg-white p-6 shadow-2xl">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <h2 className="text-xl font-bold text-accent-950">{title}</h2>
                  <p className="text-sm text-gray-600">{companyChoices.find(c => c.value === drawerShift.companyId)?.label ?? ''}</p>
                  <p className="text-sm text-gray-600">{scheduleLabel(drawerShift)}</p>
                </div>
                <div className="flex items-center gap-2">
                  {drawerShift.isActive && <Button onClick={() => { if (drawerShift.isBranchMain) setVisitDialog({ preset: { companyId: drawerShift.companyId, branchId: drawerShift.branchId, propertyName: drawerShift.name } }); else { void openOccurrences(drawerShift); openOccurrenceEditor(undefined, '4', drawerShift); } }}>Add visit</Button>}
                  <DsButton variant="ghost" aria-label="Close visits" disabled={occurrenceSaving} onClick={() => { if (!occurrenceSaving) void toggleExpanded(drawerShift); }}>×</DsButton>
                </div>
              </div>
              {drawerShift.isBranchMain && <>
                <Select label="Default employee" description="Each new visit is offered to this employee. Visits already added keep their employee." placeholder="None: new visits start unassigned"
                  clearable searchable value={drawerShift.defaultEmployeeId ?? null} onChange={value => { void saveDefaultCleaner(value); }}
                  data={employees.filter(e => e.isActive && e.role !== 'partner').map(e => ({ value: e.id, label: employeeLabel(e, employees) }))} />
                {defaultCleanerError && <Text role="alert" c="red">{defaultCleanerError}</Text>}
              </>}
              <div className="flex flex-wrap items-center justify-between gap-3">
                <strong className="text-accent-950">{monthLabelOf(expandedMonth)}</strong>
                <select aria-label="Month" value={expandedMonth} onChange={(e) => changeExpandedMonth(e.currentTarget.value)}
                  className="min-h-11 rounded-xl border border-gray-300 bg-white px-3 text-sm text-gray-900 focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20">
                  {panelMonthChoices().map(choice => <option key={choice.value} value={choice.value}>{choice.label}</option>)}
                </select>
              </div>
              <div role="group" aria-label="Show visits" className="flex flex-wrap gap-2">
                {VISIT_FILTERS.map(([key, label]) => (
                  <Button key={key} size="xs" variant={expandedFilter === key ? 'filled' : 'light'} aria-pressed={expandedFilter === key} onClick={() => setExpandedFilter(key)}>{label}</Button>
                ))}
              </div>
              {occurrenceLoading && <p className="text-sm text-gray-500">Loading visits…</p>}
              {occurrenceError && <p role="alert" className="text-sm text-red-600">{occurrenceError}</p>}
              {!occurrenceLoading && !occurrenceError && shownVisits.length === 0 && <p className="text-sm text-gray-500">No visits match. Pick another month or status, or add a visit.</p>}
              {shownVisits.length > 0 && (
                <div className="overflow-x-auto rounded-xl border border-accent-100">
                  <table className="w-full text-left text-sm text-gray-900">
                    <thead className="bg-brand-500 text-accent-950">
                      <tr>
                        <th className="px-3 py-2 font-bold">Date and time</th>
                        <th className="px-3 py-2 font-bold">Cleaner</th>
                        <th className="px-3 py-2 font-bold">Type</th>
                        <th className="px-3 py-2 font-bold"><span className="sr-only">Actions</span></th>
                      </tr>
                    </thead>
                    <tbody>
                      {shownVisits.map((item) => (
                        <tr key={item.exceptionId ?? item.startUtc} className="border-t border-accent-100 align-top">
                          <td className="px-3 py-2">{visitLabel(item, timeZoneId) && <span data-testid="visit-name" className="mb-1 block text-sm font-semibold text-accent-950">{visitLabel(item, timeZoneId)}</span>}<span className="block font-semibold">{new Date(item.startUtc).toLocaleDateString(undefined, { timeZone: timeZoneId, day: 'numeric', month: 'short' })}</span><span className="block text-gray-600">{new Date(item.startUtc).toLocaleTimeString(undefined, { timeZone: timeZoneId, hour: '2-digit', minute: '2-digit' })} to {new Date(item.endUtc).toLocaleTimeString(undefined, { timeZone: timeZoneId, hour: '2-digit', minute: '2-digit' })}</span>{highlightUtc && Date.parse(item.startUtc) === Date.parse(highlightUtc) && <span className="block text-xs font-semibold text-orange-700">Overlapping visit</span>}</td>
                          <td className="px-3 py-2"><span className="block">{(item.assigneeIds ?? []).map(employeeName).join(', ') || (item.offeredEmployeeId ? `Offer sent to ${employeeName(item.offeredEmployeeId)}` : 'Not assigned')}</span><span className="block text-xs text-gray-500">{item.visitNumber ? (item.isPartnerBooking ? 'Partner booking' : item.offeredEmployeeId ? 'Waiting for the employee to accept' : (item.assigneeIds ?? []).length ? 'Accepted' : 'No employee yet') : item.assignmentSource ?? 'Recurring'}</span>{item.isPartnerBooking && <span className="block text-xs text-gray-500">Managed under Partner requests</span>}{item.visitNumber && item.offeredEmployeeId && Date.parse(item.endUtc) < Date.now() && <span data-testid="past-offer-note" className="block text-xs text-orange-700">This visit has already ended. The employee can still accept the offer. To change the employee, delete this visit and add it again.</span>}</td>
                          <td className="px-3 py-2 text-sm text-gray-600">{item.visitCleaningType ?? 'Normal'}<span data-testid="occurrence-status" className="block text-xs text-gray-500">{item.visitNumber && item.workStatus ? WORK_STATUS_LABEL[item.workStatus] ?? item.workStatus : item.status ? OCCURRENCE_STATUSES.find(x => x.value === String(item.status))?.label : 'Scheduled'}</span></td>
                          <td className="px-3 py-2">
                            <Menu shadow="md" width={230} position="bottom-end" withinPortal>
                              <Menu.Target>
                                <ActionIcon variant="light" size="md" aria-label={`Actions for visit ${item.visitNumber ?? ''}`}><MoreHorizontal size={16} /></ActionIcon>
                              </Menu.Target>
                              <Menu.Dropdown>
                                <Menu.Item disabled={item.canAssign === false || visitStatusOf(item) === 'completed' || visitStatusOf(item) === 'cancelled' || item.isPartnerBooking} onClick={() => { if (drawerShift.isBranchMain) { setCleanerMode('employee'); setCleanerVisit(item); setCleanerChoice(item.assigneeIds?.[0] ?? item.offeredEmployeeId ?? null); setCleanerError(''); setCleaningTypeChoice(null); setCleaningTypeError(''); } else setAssigningVisit(item); }}>Assign employees</Menu.Item>
                                <Menu.Item disabled={visitStatusOf(item) !== 'scheduled'} onClick={() => openOccurrenceEditor(item, '1', drawerShift)}>Change time</Menu.Item>
                                <Menu.Item onClick={() => { setCleanerMode('type'); setCleanerVisit(item); setCleaningTypeChoice(null); setCleaningTypeError(''); }}>Change cleaning type</Menu.Item>
                                <Menu.Item onClick={() => { setPricingShift(drawerShift); setPricingOccurrence(item); }}>Visit price</Menu.Item>
                                {!item.isCancelledManualVisit && <Menu.Item color="red" disabled={visitStatusOf(item) === 'completed' || visitStatusOf(item) === 'ongoing'} onClick={() => { if (item.exceptionId) { openCancelVisit(item); return; } openOccurrenceEditor(item, '3', drawerShift); setOccurrenceStatus('3'); }}>Cancel visit</Menu.Item>}
                                {item.isCancelledManualVisit && <Menu.Item onClick={() => void restoreManualVisit(item)}>Restore visit</Menu.Item>}
                                {item.exceptionId && item.status === 4 && <Menu.Item color="red" onClick={() => void restoreOccurrence(item)}>Delete visit</Menu.Item>}
                                {item.exceptionId && item.status !== 4 && !item.isCancelledManualVisit && <Menu.Item onClick={() => void restoreOccurrence(item)}>Restore original time</Menu.Item>}
                                {visitStatusOf(item) !== 'scheduled' && <span className="block px-3 py-2 text-xs text-gray-500">{VISIT_NOTES[visitStatusOf(item)]}</span>}
                              </Menu.Dropdown>
                            </Menu>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </aside>
          </>
        );
      })()}
      {workspaceShift && <ShiftWorkspace shift={workspaceShift}
        employeeCount={assignmentCounts ? (assignmentCounts[workspaceShift.id] ?? 0) : assignments[workspaceShift.id] ? assignments[workspaceShift.id].filter(a => a.isActive).length : undefined}
        templateName={templates.find(t => t.id === workspaceShift.qualityCycleTemplateId)?.title ?? null}
        onClose={() => setWorkspaceId(null)} />}
      {defaultCleanerFor && <Modal opened onClose={() => { if (!defaultCleanerSaving) setDefaultCleanerFor(null); }} closeButtonProps={{ disabled: defaultCleanerSaving }} title={`Assign employees — ${defaultCleanerFor.name}`} size="md" radius="md">
        <Stack>
          <Select label="Default employee" description="New visits are offered to this employee. Visits already added keep their employee." clearable searchable
            placeholder="None: new visits start unassigned" value={defaultCleanerPick} onChange={setDefaultCleanerPick}
            data={employees.filter(e => e.isActive).map(e => ({ value: e.id, label: `${e.firstName} ${e.lastName}` }))} />
          {defaultCleanerError && <Text role="alert" c="red">{defaultCleanerError}</Text>}
          <Group justify="flex-end">
            <Button variant="default" disabled={defaultCleanerSaving} onClick={() => setDefaultCleanerFor(null)}>Cancel</Button>
            <Button loading={defaultCleanerSaving} onClick={async () => {
              if (defaultCleanerSaving) return;
              setDefaultCleanerSaving(true); setDefaultCleanerError('');
              const result = await adminBranchVisits.setDefaultEmployee(defaultCleanerFor.id, defaultCleanerPick);
              setDefaultCleanerSaving(false);
              if (!result.ok) { setDefaultCleanerError(result.message); return; }
              setDefaultCleanerFor(null);
              await load();
            }}>Save</Button>
          </Group>
        </Stack>
      </Modal>}
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
      <AddVisitDialog opened={Boolean(visitDialog)} preset={visitDialog?.preset ?? null} employees={employees} timeZoneId={timeZoneId}
        onClose={() => setVisitDialog(null)} onAdded={(visit, propertyName) => { void visitAdded(visit, propertyName); }} />
      <Modal opened={Boolean(cleanerVisit)} onClose={() => { if (!cleanerSaving && !cleaningTypeSaving) setCleanerVisit(null); }} closeButtonProps={{ disabled: cleanerSaving || cleaningTypeSaving }} title={cleanerMode === 'employee' ? 'Change employee' : 'Cleaning type'}>
        <Stack>
          <Text size="sm">{cleanerVisit && visitLabel(cleanerVisit, timeZoneId)}</Text>
          {cleanerMode === 'employee' && !cleanerVisit?.isPartnerBooking && <>
          <Select label="Employee" placeholder="Unassigned" clearable searchable value={cleanerChoice} onChange={setCleanerChoice}
            description="The employee gets an offer and holds the visit once accepted. An employee who already holds it is released."
            data={employees.filter(e => e.isActive && e.role !== 'partner').map(e => ({ value: e.id, label: employeeLabel(e, employees) }))} />
          {cleanerError && <Text role="alert" c="red">{cleanerError}</Text>}
          <Group justify="flex-end"><Button variant="default" disabled={cleanerSaving} onClick={() => setCleanerVisit(null)}>Cancel</Button><Button loading={cleanerSaving} onClick={saveCleaner}>Save employee</Button></Group>
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
      <Modal opened={Boolean(editingOccurrence)} onClose={() => { if (!occurrenceSaving) setEditingOccurrence(null); }} closeButtonProps={{ disabled: occurrenceSaving }} closeOnEscape={!occurrenceSaving} closeOnClickOutside={!occurrenceSaving} title="Change visit">
        <Stack>
          <Select label="Action" value={occurrenceStatus} onChange={value => setOccurrenceStatus(value ?? '1')} data={OCCURRENCE_STATUSES}
            disabled={Boolean(editingOccurrence?.exceptionId)} />
          <TextInput label={`Start (${timeZoneId})`} type="datetime-local" required value={occurrenceStart} onChange={e => setOccurrenceStart(e.target.value)} />
          <TextInput label={`End (${timeZoneId})`} type="datetime-local" required value={occurrenceEnd} onChange={e => setOccurrenceEnd(e.target.value)} />
          {/* A main shift's visit gets its employee through its offer (Change employee), so no replacement is set here. */}
          {!occurrenceShift?.isBranchMain && <Select label="Replacement employee (optional)" clearable searchable value={occurrenceEmployee} onChange={setOccurrenceEmployee}
            data={employees.filter(e => e.isActive).map(e => ({ value: e.id, label: `${e.firstName} ${e.lastName}` }))} />}
          <TextInput label="Reason" required maxLength={500} value={occurrenceReason} onChange={e => setOccurrenceReason(e.target.value)} />
          <Textarea label="Notes for the employee (optional)" description="Shown to the employee for this visit only." maxLength={4000} autosize minRows={2} value={occurrenceNotes} onChange={e => setOccurrenceNotes(e.target.value)} />
          {occurrenceEditError && <Text role="alert" c="red">{occurrenceEditError}</Text>}
          <Group justify="flex-end"><Button variant="default" disabled={occurrenceSaving} onClick={() => setEditingOccurrence(null)}>Cancel</Button><Button loading={occurrenceSaving} onClick={saveOccurrence}>Save visit</Button></Group>
        </Stack>
      </Modal>

      <Modal opened={Boolean(cancellingVisit)} onClose={() => { if (!occurrenceSaving) setCancellingVisit(null); }} closeButtonProps={{ disabled: occurrenceSaving }} title="Cancel visit">
        <Stack>
          <Text size="sm">{cancellingVisit && (visitLabel(cancellingVisit, timeZoneId) || new Date(cancellingVisit.startUtc).toLocaleString(undefined, { timeZone: timeZoneId }))}</Text>
          <Text size="sm" c="dimmed">The visit leaves the employee's schedule and is not invoiced. It stays in this list so you can restore it.</Text>
          <Select label="Cancelled by" allowDeselect={false} value={cancelVisitStatus} onChange={value => setCancelVisitStatus(value ?? '3')}
            data={[{ value: '2', label: 'Customer cancelled' }, { value: '3', label: 'Company cancelled' }]} />
          <TextInput label="Reason" required maxLength={500} value={cancelVisitReason} onChange={e => setCancelVisitReason(e.target.value)} />
          {cancelVisitError && <Text role="alert" c="red">{cancelVisitError}</Text>}
          <Group justify="flex-end"><Button variant="default" disabled={occurrenceSaving} onClick={() => setCancellingVisit(null)}>Keep visit</Button><Button color="orange" loading={occurrenceSaving} onClick={() => { void confirmCancelVisit(); }}>Cancel visit</Button></Group>
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
      <Modal opened={modalOpen} onClose={closeModal} title={editingShift ? 'Edit schedule' : 'New work order'} size="lg" radius="md">
        <Stack gap="sm">
          <Text size="sm" c="dimmed">Schedule times use the tenant time zone: {timeZoneId}.</Text>
          <CompanyPicker allowInactive={false} allowedInactiveCompanyId={editingShift?.companyId} value={companyId} onChange={(v) => { setBranches([]); setCompanyId(v); setBranchId(null); setQualityCycleTemplateId(null); }} />
          <Select label="Branch" data={branches.filter(b => b.isActive !== false || b.id === editingShift?.branchId).map((b) => ({ value: b.id, label: `${b.name}${b.isActive === false ? ' (inactive)' : ''}` }))} value={branchId} onChange={v => { setBranchId(v); setPriceFrom(null); setQualityCycleTemplateId(null); }} searchable disabled={!companyId} />
          {editingShift && <Group align="flex-end" gap="xs" wrap="nowrap">
            <TextInput label="Name" readOnly style={{ flex: 1 }} value={name} description="Generated from the property: cleaning type, address, size, rooms and beds." />
          </Group>}
          {!editingShift && (branches.find(b => b.id === branchId)?.priceVersions?.length ?? 0) > 1 && <Select label="Price version" description="The property price this shift is billed at. Defaults to the price in force today." allowDeselect={false} value={priceFrom && (branches.find(b => b.id === branchId)?.priceVersions ?? []).some(v => v.effectiveFrom === priceFrom) ? priceFrom : defaultPriceVersion(branches.find(b => b.id === branchId)?.priceVersions)} onChange={setPriceFrom}
            data={(branches.find(b => b.id === branchId)?.priceVersions ?? []).map(v => ({ value: v.effectiveFrom, label: `From ${v.effectiveFrom} · €${v.priceNet.toFixed(2)} excl. VAT` }))} />}
          <Select label="Price for this shift" description="Property price uses the property's price. A shift price applies to this shift only." allowDeselect={false} value={pricingChoice}
            onChange={value => setPricingChoice((value ?? 'branch') as 'branch' | 'Monthly' | 'PerVisit')}
            data={[{ value: 'branch', label: 'Property price' }, { value: 'Monthly', label: 'Monthly fee' }, { value: 'PerVisit', label: 'Price per completed visit' }]} />
          {pricingChoice !== 'branch' && <Group grow>
            <TextInput label={pricingChoice === 'Monthly' ? 'Monthly fee excl. VAT (€)' : 'Price per visit excl. VAT (€)'} type="number" min={0} step="0.01" value={contractRate} onChange={e => setContractRate(e.target.value)} />
            <TextInput label="VAT (%)" type="number" min={0} max={100} step="0.1" value={contractVat} onChange={e => setContractVat(e.target.value)} />
          </Group>}
          {scheduleType !== '6' && <Group grow>
            <TextInput label="Start date" type="date" required={!editingShift || Boolean(editingShift.validFrom)} value={validFrom} onChange={e => setValidFrom(e.target.value)} />
            <TextInput label="End date (optional)" type="date" min={validFrom || undefined} value={validUntil} onChange={e => setValidUntil(e.target.value)} />
          </Group>}
          <Select label="Schedule" required withAsterisk={false} placeholder="Choose a schedule type" allowDeselect={false} data={SCHEDULE_TYPES.filter(type => type.value !== '6' || editingShift?.schedule.type === 6)} value={scheduleType || null} onChange={(v) => { if (v) setScheduleType(v); }} />
          {scheduleType === '4' && <Text size="sm">On-call is a reusable shift without scheduled dates. After creating it, enter the date, time and employee for a one-time visit. Only dated visits appear on calendars.</Text>}

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
