'use client';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { managerApi, type ManagerShift, type ManagerEmployee, type ManagerCompany, type ManagerBranch, type ManagerAssignment, type ManagerSchedule } from '@/lib/managerApi';
import { LifecycleTabs, inLifecycle, type LifecycleStatus } from '@/components/LifecycleTabs';
const DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const input = 'block w-full rounded border border-gray-300 bg-white p-2 mt-1 text-gray-900';
const button = 'rounded border border-gray-300 bg-white px-3 py-2 disabled:opacity-50';
const daily = (): ManagerSchedule => ({ type: 0, dailyStart: '08:00', dailyEnd: '10:00' });

export default function ManagerPage() {
  const [shifts, setShifts] = useState<ManagerShift[] | null>(null);
  const [employees, setEmployees] = useState<ManagerEmployee[]>([]);
  const [companies, setCompanies] = useState<ManagerCompany[]>([]);
  const [branches, setBranches] = useState<ManagerBranch[]>([]);
  const [error, setError] = useState(''); const [busy, setBusy] = useState(false);
  const [timeZoneId, setTimeZoneId] = useState('Europe/Helsinki');
  const [lifecycle, setLifecycle] = useState<LifecycleStatus>('active');
  const [editing, setEditing] = useState<ManagerShift | 'new' | null>(null);
  const [name, setName] = useState(''); const [company, setCompany] = useState(''); const [branch, setBranch] = useState('');
  const [schedule, setSchedule] = useState<ManagerSchedule>(daily());
  const [validFrom, setValidFrom] = useState(''); const [validUntil, setValidUntil] = useState('');
  const [assignmentShift, setAssignmentShift] = useState<string | null>(null);
  const [assignments, setAssignments] = useState<ManagerAssignment[] | null>(null); const [employee, setEmployee] = useState('');
  const generation = useRef(0); const assignmentGeneration = useRef(0);
  async function load() {
    const version = ++generation.current; setError('');
    try {
      const [work, roster, clients, timeZone] = await Promise.all([managerApi.shifts(true), managerApi.employees(), managerApi.companies(), managerApi.timeZone()]);
      if (version !== generation.current) return;
      setShifts(work); setEmployees(roster); setCompanies(clients); setTimeZoneId(timeZone.timeZoneId);
    } catch (e) { if (version === generation.current) setError(e instanceof Error ? e.message : 'Could not load operations.'); }
  }
  useEffect(() => { void load(); return () => { generation.current++; assignmentGeneration.current++; }; }, []);
  function closeAssignments() {
    assignmentGeneration.current++; setAssignmentShift(null); setAssignments(null); setEmployee('');
  }
  function changeLifecycle(status: LifecycleStatus) {
    if (status === lifecycle) return;
    closeAssignments(); setEditing(null); setError(''); setLifecycle(status);
  }
  const visibleShifts = shifts?.filter(s => inLifecycle(s, lifecycle)) ?? [];
  useEffect(() => {
    let active = true; setBranches([]);
    if (company) managerApi.branches(company).then(r => { if (active) setBranches(r); }).catch(e => { if (active) setError(e.message); });
    return () => { active = false; };
  }, [company]);
  function open(shift: ManagerShift | 'new') {
    setError(''); setEditing(shift); closeAssignments();
    if (shift === 'new') { setName(''); setCompany(''); setBranch(''); setSchedule(daily()); setValidFrom(''); setValidUntil(''); }
    else { setName(shift.name); setCompany(shift.companyId); setBranch(shift.branchId); setSchedule(structuredClone(shift.schedule)); setValidFrom(shift.validFrom?.slice(0, 10) ?? ''); setValidUntil(shift.validUntil?.slice(0, 10) ?? ''); }
  }
  async function action(fn: () => Promise<void>) {
    setBusy(true); setError(''); try { await fn(); } catch (e) { setError(e instanceof Error ? e.message : 'Operation failed. Please retry.'); } finally { setBusy(false); }
  }
  async function save(event: FormEvent) {
    event.preventDefault();
    if (!name.trim() || !company || !branch) { setError('Company, branch and name are required.'); return; }
    await action(async () => {
      const body = { name: name.trim(), schedule, validFrom: validFrom || null, validUntil: validUntil || null };
      if (editing === 'new') { await managerApi.create({ ...body, companyId: company, branchId: branch }); changeLifecycle('active'); }
      else if (editing) await managerApi.update(editing.id, { ...body, isActive: editing.isActive });
      setEditing(null); await load();
    });
  }
  async function openAssignments(id: string) {
    const version = ++assignmentGeneration.current; setAssignmentShift(id); setAssignments(null); setEmployee(''); setError('');
    try { const result = await managerApi.assignments(id); if (version === assignmentGeneration.current) setAssignments(result); }
    catch (e) { if (version === assignmentGeneration.current) setError(e instanceof Error ? e.message : 'Could not load assignments.'); }
  }
  async function deleteShift(shift: ManagerShift) {
    if (!window.confirm(`Permanently delete inactive shift “${shift.name}” and remove its employee assignments? Completed checklist records, photos and reports will remain in history.`)) return;
    await action(async () => { await managerApi.deleteInactive(shift.id); closeAssignments(); await load(); });
  }
  const type = schedule.type;
  const timeKeys = type === 0 ? ['dailyStart', 'dailyEnd'] : type === 2 ? ['weeklyStart', 'weeklyEnd'] : type === 3 ? ['biWeeklyStart', 'biWeeklyEnd'] : ['monthlyStart', 'monthlyEnd'];
  function changeType(value: number) {
    const t = value as ManagerSchedule['type'];
    setSchedule(t === 0 ? daily() : t === 1 ? { type: t, dailyTimes: { Monday: { start: '08:00', end: '10:00' } } } : t === 2 ? { type: t, weeklyDays: [1], weeklyStart: '08:00', weeklyEnd: '10:00' } : t === 3 ? { type: t, biWeeklyWeekParity: 1, biWeeklyDay: 1, biWeeklyStart: '08:00', biWeeklyEnd: '10:00' } : t === 5 ? { type: t, monthlyDay: 1, monthlyStart: '08:00', monthlyEnd: '10:00' } : { type: t });
  }
  return <section className="space-y-5">
    <div className="flex justify-between items-center"><h2 className="text-xl font-semibold">Shifts</h2><button className={button} disabled={busy} onClick={() => open('new')}>Add shift</button></div>
    {error && <p role="alert" className="text-red-700">{error}</p>}
    <button className={button} disabled={busy} onClick={load}>Refresh operations</button>
    <LifecycleTabs value={lifecycle} onChange={changeLifecycle} label="Shift activity" disabled={busy}
      activeCount={shifts?.filter(s => s.isActive).length} inactiveCount={shifts?.filter(s => !s.isActive).length}>
    {shifts === null ? <p role="status">{error ? 'Operations could not be loaded. Use Refresh operations to retry.' : 'Loading operations…'}</p> : visibleShifts.length === 0 ? <p>{shifts.length === 0 ? 'No shifts yet.' : `No ${lifecycle} shifts. Check the ${lifecycle === 'active' ? 'Inactive' : 'Active'} tab.`}</p> : visibleShifts.map(s => <article key={s.id} className="rounded-xl border bg-white p-4 space-y-3" aria-label={s.name}>
      <h3 className="font-bold">{s.name}</h3><p>{companies.find(c => c.id === s.companyId)?.name ?? 'Company'} · {['Daily', 'Daily times', 'Weekly', 'Biweekly', 'Flexible', 'Monthly'][s.schedule.type]}</p>
      <p>{s.isActive ? 'Active' : 'Deactivated'}</p>
      <div className="flex flex-wrap gap-2"><button className={button} disabled={busy} onClick={() => open(s)}>Edit shift</button>{s.isActive ? <><button className={button} disabled={busy} onClick={() => openAssignments(s.id)}>Employees</button><button className={button} disabled={busy} onClick={() => action(async () => { await managerApi.deactivate(s.id); if (assignmentShift === s.id) closeAssignments(); await load(); })}>Deactivate shift</button></> : <><button className={button} disabled={busy} onClick={() => action(async () => { await managerApi.reactivate(s.id); await load(); })}>Reactivate shift</button><button className={button} disabled={busy} onClick={() => deleteShift(s)}>Delete</button></>}</div>
      {assignmentShift === s.id && <div className="space-y-3">
        {assignments === null ? <><p>Loading assignments…</p><button className={button} onClick={() => openAssignments(s.id)}>Retry assignments</button></> : <>
          <label>Employee<select aria-label="Employee" className={input} value={employee} disabled={busy} onChange={e => setEmployee(e.target.value)}><option value="">Choose employee</option>{employees.filter(e => e.isActive && !assignments.some(a => a.employeeId === e.id)).map(e => <option key={e.id} value={e.id}>{e.firstName} {e.lastName}</option>)}</select></label>
          <button className={button} disabled={busy || !employee} onClick={() => action(async () => { await managerApi.assign(s.id, employee); await openAssignments(s.id); })}>Assign employee</button>
          {assignments.length === 0 && <p>No employees assigned.</p>}
          {assignments.map(a => { const person = employees.find(e => e.id === a.employeeId); return <div key={a.id} className="flex flex-wrap justify-between gap-2"><span>{person ? `${person.firstName} ${person.lastName}` : 'Inactive employee'}</span><button className={button} disabled={busy} onClick={() => action(async () => { await managerApi.unassign(s.id, a.employeeId); await openAssignments(s.id); })}>Remove assignment</button></div>; })}
        </>}
        <button className={button} disabled={busy} onClick={closeAssignments}>Close assignments</button>
      </div>}
    </article>)}
    </LifecycleTabs>
    {editing && <form onSubmit={save} className="rounded-xl border bg-white p-5 space-y-4" aria-label={editing === 'new' ? 'Create shift' : 'Edit shift'}>
      <h2 className="text-xl font-semibold">{editing === 'new' ? 'Create shift' : 'Edit shift'}</h2>
      <label>Shift name<input className={input} value={name} disabled={busy} onChange={e => setName(e.target.value)} required /></label>
      <label>Company<select aria-label="Company" className={input} value={company} disabled={busy || editing !== 'new'} onChange={e => { setCompany(e.target.value); setBranch(''); }} required><option value="">Choose company</option>{companies.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select></label>
      <label>Branch<select aria-label="Branch" className={input} value={branch} disabled={busy || editing !== 'new'} onChange={e => setBranch(e.target.value)} required><option value="">Choose branch</option>{branches.map(b => <option key={b.id} value={b.id}>{b.name}</option>)}</select></label>
      <label>Schedule<select aria-label="Schedule" className={input} value={type} disabled={busy} onChange={e => changeType(Number(e.target.value))}>{['Daily', 'Different times per day', 'Weekly', 'Biweekly', 'Flexible', 'Monthly'].map((v, i) => <option key={i} value={i}>{v}</option>)}</select></label>
      <p className="text-sm text-gray-600">Schedule times use the tenant time zone: {timeZoneId}.</p>
      {type !== 1 && type !== 4 && <div className="grid sm:grid-cols-2 gap-4">{timeKeys.map((key, i) => <label key={key}>{i ? 'End time' : 'Start time'}<input className={input} type="time" disabled={busy} value={(schedule[key as keyof ManagerSchedule] as string | undefined)?.slice(0, 5) ?? ''} onChange={e => setSchedule({ ...schedule, [key]: e.target.value })} required /></label>)}</div>}
      {type === 2 && <fieldset><legend>Days</legend><div className="flex flex-wrap gap-3">{DAYS.map((d, i) => <label key={d}><input type="checkbox" disabled={busy} checked={(schedule.weeklyDays?.length ? schedule.weeklyDays : [schedule.weeklyDay]).includes(i)} onChange={e => { const days = schedule.weeklyDays?.length ? schedule.weeklyDays : schedule.weeklyDay == null ? [] : [schedule.weeklyDay]; setSchedule({ ...schedule, weeklyDay: null, weeklyDays: e.target.checked ? [...days, i] : days.filter(v => v !== i) }); }} /> {d}</label>)}</div></fieldset>}
      {type === 3 && <><label>Week parity<select aria-label="Week parity" className={input} disabled={busy} value={schedule.biWeeklyWeekParity ?? 1} onChange={e => setSchedule({ ...schedule, biWeeklyWeekParity: Number(e.target.value) })}><option value={1}>Odd weeks</option><option value={2}>Even weeks</option></select></label><label>Day<select aria-label="Day" className={input} disabled={busy} value={schedule.biWeeklyDay ?? 1} onChange={e => setSchedule({ ...schedule, biWeeklyDay: Number(e.target.value) })}>{DAYS.map((d, i) => <option key={d} value={i}>{d}</option>)}</select></label></>}
      {type === 5 && <label>Day of month<input className={input} type="number" min={1} max={31} disabled={busy} value={schedule.monthlyDay ?? 1} onChange={e => setSchedule({ ...schedule, monthlyDay: Number(e.target.value) })} required /></label>}
      {type === 1 && <fieldset className="space-y-3"><legend>Daily times</legend>{DAYS.map((d, i) => { const range = schedule.dailyTimes?.[d] ?? schedule.dailyTimes?.[String(i)]; return <div key={d} className="flex flex-wrap gap-3 items-center"><label><input type="checkbox" checked={!!range} disabled={busy} onChange={e => { const ranges = { ...schedule.dailyTimes }; delete ranges[String(i)]; if (e.target.checked) ranges[d] = { start: '08:00', end: '10:00' }; else delete ranges[d]; setSchedule({ ...schedule, dailyTimes: ranges }); }} /> {d}</label>{range && ['start', 'end'].map(k => <label key={k}>{d} {k}<input className={input} type="time" value={range[k as 'start' | 'end'].slice(0, 5)} disabled={busy} onChange={e => { const ranges = { ...schedule.dailyTimes }; delete ranges[String(i)]; ranges[d] = { ...range, [k]: e.target.value }; setSchedule({ ...schedule, dailyTimes: ranges }); }} required /></label>)}</div>; })}</fieldset>}
      <div className="grid sm:grid-cols-2 gap-4"><label>Valid from<input className={input} type="date" disabled={busy} value={validFrom} onChange={e => setValidFrom(e.target.value)} /></label><label>Valid until<input className={input} type="date" disabled={busy} value={validUntil} onChange={e => setValidUntil(e.target.value)} /></label></div>
      <div className="flex gap-3"><button className={button} disabled={busy} type="submit">Save shift</button><button className={button} disabled={busy} type="button" onClick={() => setEditing(null)}>Cancel</button></div>
    </form>}
  </section>;
}
