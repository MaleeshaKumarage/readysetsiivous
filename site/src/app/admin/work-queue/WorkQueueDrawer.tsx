'use client';

import { useEffect, useRef, useState } from 'react';
import { adminBookings, adminBranchVisits, adminShifts, type Booking, type Employee, type WorkQueueVisit } from '@/lib/adminApi';
import { staffPartnerApi, type CleaningRequest } from '@/lib/partnerApi';
import { AssignCard, type Plan, type Slot } from './AssignCard';
import { Info } from './Info';
import {
  SOURCE_CHIP, SOURCE_LABEL, TYPE_LABEL, addDays, dateText, dayName, daysBetween, hh, lenText, localOf, personName, timeOf, windowText, zonedToUtc, type Row,
} from './model';

const STATUS_MOVED = 1, STATUS_EXTRA = 4, STATUS_COMPANY_CANCELLED = 3, MODE_REPLACE = 1;
const CANCEL_LABEL: Record<string, string> = { shift: 'Cancel visit', booking: 'Cancel booking', request: 'Cancel request', quote: 'Reject quote' };

export interface DrawerProps {
  row: Row;
  employees: Employee[];
  zone: string;
  onClose: () => void;
  /** A finished action: shows a success toast and reloads the queue. */
  onDone: (message: string) => void;
  /** A failed action: shows an error toast. */
  onFail: (message: string) => void;
}

const errorText = (error: unknown) => error instanceof Error && error.message ? error.message : 'The change could not be saved. Refresh and try again.';

/** The side panel for one queue item. Every action the item allows happens here. */
export function WorkQueueDrawer({ row, employees, zone, onClose, onDone, onFail }: DrawerProps) {
  const api = staffPartnerApi('admin');
  const p = row.payload;
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState('');
  const [booking, setBooking] = useState<Booking | null>(null);
  const [reply, setReply] = useState('');
  const [notes, setNotes] = useState(p.t === 'change' ? p.change.proposedNotes ?? p.request?.notes ?? '' : '');
  const [price, setPrice] = useState(p.t === 'quote' ? p.branch.priceNet?.toString() ?? '' : '');
  const [vat, setVat] = useState(p.t === 'quote' ? p.branch.vatRatePercent?.toString() ?? '25.5' : '25.5');
  const [duration, setDuration] = useState(p.t === 'quote' ? p.branch.estimatedDurationMinutes?.toString() ?? '' : '');
  const [editing, setEditing] = useState(false);
  const [edit, setEdit] = useState(p.t === 'request' ? { from: p.request.fromDate, to: p.request.toDate, time: p.request.exactStartLocalTime?.slice(0, 5) ?? '', notes: p.request.notes ?? '' } : { from: '', to: '', time: '', notes: '' });
  const [cancelAsk, setCancelAsk] = useState(false);
  const [reason, setReason] = useState('');
  const [photos, setPhotos] = useState(p.t === 'key' ? p.key.photos?.length ?? 0 : 0);
  const photoInput = useRef<HTMLInputElement>(null);
  const closeButton = useRef<HTMLButtonElement>(null);

  useEffect(() => { closeButton.current?.focus(); }, [row.id]);
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey); return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);
  useEffect(() => {
    if (p.t !== 'booking') return;
    let live = true;
    void adminBookings.detail(p.item.id).then(found => { if (live) setBooking(found ?? null); });
    return () => { live = false; };
  }, [p]);

  const run = async (work: () => Promise<string>) => {
    if (busy) return;
    setBusy(true); setProblem('');
    try { onDone(await work()); } catch (error) { onFail(errorText(error)); } finally { setBusy(false); }
  };
  const placeOf = () => p.t === 'change' ? p.request?.branchName ?? row.customer : row.customer;
  const nameOf = (id: string) => { const found = employees.find(e => e.id === id); return found ? personName(found) : 'the employee'; };
  const latestRequest = async (id: string): Promise<CleaningRequest> => {
    const found = (await api.requests()).find(item => item.id === id);
    if (!found) throw new Error('The partner request was not found. Refresh the queue.');
    return found;
  };

  // ---- When the work happens, and what can be changed about it ----
  const plan: Plan | null = (() => {
    if (p.t === 'shift') {
      const v = p.item.visit;
      const [day, time] = p.item.scheduledLocal.split(' ');
      if (!v) return { days: [day], day, start: timeOf(time) ?? 8, len: 2, canMove: false, canResize: false };
      const from = localOf(v.startUtc, zone);
      const len = Math.max(0.25, (Date.parse(v.endUtc) - Date.parse(v.startUtc)) / 3_600_000);
      return { days: daysBetween(from.day, addDays(from.day, 13)), day: from.day, start: from.hour, len, canMove: true, canResize: true };
    }
    if (p.t === 'booking') {
      const [day, time] = p.item.scheduledLocal.split(' ');
      return { days: [day], day, start: timeOf(time) ?? 8, len: (p.item.durationMinutes ?? 120) / 60, canMove: false, canResize: false,
        fixedNote: 'A public booking keeps the day and time the customer booked.' };
    }
    if (p.t === 'request') {
      const r = p.request;
      const fixed = !!r.exactStartLocalTime && r.fromDate === r.toDate;
      return { days: daysBetween(r.fromDate, r.toDate), day: r.fromDate, start: timeOf(r.exactStartLocalTime) ?? 9, len: r.estimatedDurationMinutes / 60,
        canMove: !fixed, canResize: false,
        fixedNote: fixed ? 'The partner asked for this exact start. The length comes from the property’s estimate.' : 'The length comes from the property’s estimate.' };
    }
    if (p.t === 'change' && p.changeKind === 'reschedule' && p.request) {
      const r = p.request, c = p.change;
      const from = c.proposedFromDate ?? r.fromDate, to = c.proposedToDate ?? from;
      return { days: daysBetween(from, to), day: from, start: timeOf(c.proposedExactStartLocalTime) ?? timeOf(r.exactStartLocalTime) ?? 9,
        len: r.estimatedDurationMinutes / 60, canMove: true, canResize: false, fixedNote: 'The length comes from the property’s estimate.' };
    }
    if (p.t === 'key' && !p.key.assignedEmployeeId) return { days: [p.key.date], day: p.key.date, start: null, len: 0, canMove: false, canResize: false };
    return null;
  })();

  const whenOf = (slot: Slot) => `${dayName(slot.day)}${slot.start === null ? '' : ` ${hh(slot.start)}–${hh(slot.start + slot.len)}`}`;
  const moveShift = (v: WorkQueueVisit, slot: Slot, employeeId: string | null) => {
    const extra = v.status === STATUS_EXTRA ? v.extraExceptionId ?? v.exceptionId : null;
    const startUtc = zonedToUtc(slot.day, slot.start ?? 0, zone);
    const endUtc = new Date(Date.parse(startUtc) + slot.len * 3_600_000).toISOString();
    return adminShifts.saveOccurrenceException(v.shiftId, {
      id: extra ?? v.exceptionId ?? null, originalStartUtc: extra ? null : v.originalStartUtc ?? v.startUtc,
      startUtc, endUtc, status: extra ? STATUS_EXTRA : STATUS_MOVED, reason: 'Planned from the Work queue', employeeId,
    });
  };

  const assign = (employeeId: string, slot: Slot) => run(async () => {
    const name = nameOf(employeeId);
    const changed = !!plan && (slot.day !== plan.day || slot.start !== plan.start || slot.len !== plan.len);
    if (p.t === 'shift') {
      const v = p.item.visit;
      if (!v) throw new Error('This visit cannot be assigned here. Open it in the Scheduler.');
      if (v.isBranchMain && v.extraExceptionId) {
        if (changed) await moveShift(v, slot, v.visitEmployeeId);
        const result = await adminBranchVisits.setEmployee(v.shiftId, v.extraExceptionId, employeeId, v.offerRevision);
        if (!result.ok) throw new Error(result.message);
      } else if (changed) {
        await moveShift(v, slot, employeeId);
      } else {
        const extra = v.status === STATUS_EXTRA ? v.extraExceptionId ?? v.exceptionId : null;
        await adminShifts.setVisitAssignees(v.shiftId, {
          originalStartUtc: extra ? null : v.originalStartUtc ?? v.startUtc, extraExceptionId: extra, mode: MODE_REPLACE, employeeIds: [employeeId],
          reason: 'Assigned from the Work queue', rosterVersion: v.rosterVersion, assignmentVersion: v.assignmentVersion, fromThisDate: false, preview: false,
        });
      }
      return `${row.reference} is assigned to ${name} for ${whenOf(slot)} and now shows in the Scheduler.`;
    }
    if (p.t === 'booking') {
      if (!await adminBookings.assign(p.item.id, employeeId)) throw new Error('The booking could not be assigned. Refresh and check its status.');
      return `Booking ${p.item.reference} is assigned to ${name} for ${whenOf(slot)} and now shows in the Scheduler.`;
    }
    if (p.t === 'request') {
      const r = p.request;
      const planned = plan?.canMove && slot.start !== null ? `${slot.day}T${hh(slot.start)}` : undefined;
      const result = await api.offer(r, employeeId, planned);
      return result.status === 'Accepted'
        ? `${name} accepts work automatically, so ${r.branchName} (${whenOf(slot)}) is accepted and now shows in the Scheduler.`
        : `Offer for ${r.branchName}, ${whenOf(slot)}, sent to ${name}. It moves to the Scheduler when they accept.`;
    }
    if (p.t === 'change' && p.request) {
      await api.apply(p.change.id, p.request.version, reply.trim() || `Moved to ${whenOf(slot)}.`);
      const fresh = await latestRequest(p.request.id);
      await api.offer(fresh, employeeId, slot.start === null ? undefined : `${slot.day}T${hh(slot.start)}`);
      return `${fresh.branchName} is moved to ${whenOf(slot)} with ${name}. The partner ${reply.trim() ? 'gets your reply.' : 'is told about the new time.'}`;
    }
    if (p.t === 'key') {
      await api.assignKeyRequest(p.key, employeeId);
      return `${name} will handle the ${p.key.kind === 'Handover' ? 'key handover' : 'key pickup'} on ${dateText(p.key.date)}. Mark it completed when the keys have changed hands.`;
    }
    throw new Error('This item cannot be assigned.');
  });

  // ---- Other actions ----
  const needReply = (text: string) => { if (!reply.trim()) { setProblem(text); return false; } return true; };
  const savePrice = () => {
    if (p.t !== 'quote') return;
    if (!price || !duration || Number(price) < 0 || Number(duration) < 1) { setProblem('Enter the price and the estimated duration.'); return; }
    void run(async () => {
      await api.price(p.branch.id, { priceNet: Number(price), vatRatePercent: Number(vat), estimatedDurationMinutes: Number(duration) });
      return `${p.branch.name} is priced at €${Number(price).toFixed(2)} excl. VAT. The partner can now book cleaning there.`;
    });
  };
  const resolveChange = () => {
    if (p.t !== 'change') return;
    if (p.changeKind === 'message' && !needReply('Write a reply to the partner.')) return;
    void run(async () => {
      if (p.changeKind === 'instructions' && p.request) {
        const unchanged = notes.trim() === (p.change.proposedNotes ?? '').trim();
        if (unchanged) await api.apply(p.change.id, p.request.version, reply.trim() || 'Instructions updated.');
        else {
          const r = p.request;
          await api.update(r, { fromDate: r.fromDate, toDate: r.toDate, exactStartLocalTime: r.exactStartLocalTime, notes: notes.trim() });
          await api.resolve(p.change.id, reply.trim() || 'Instructions updated.');
        }
        return `The instructions for ${placeOf()} are updated and the request is resolved.`;
      }
      await api.resolve(p.change.id, reply.trim());
      return `The message from ${row.customer} is answered and resolved.`;
    });
  };
  
  const cancelVisit = () => {
    if (p.t !== 'change') return;
    void run(async () => {
      if (p.request) await api.apply(p.change.id, p.request.version, reply.trim() || 'Cancelled as you asked.');
      else await api.resolve(p.change.id, reply.trim() || 'Cancelled as you asked.');
      return `The visit at ${placeOf()} is cancelled and the partner is told.`;
    });
  };
  const decline = () => {
    if (p.t !== 'change' || !needReply('Write a reply so the partner knows why.')) return;
    void run(async () => { await api.resolve(p.change.id, reply.trim()); return `The request from ${row.customer} is declined and the partner sees your reply.`; });
  };
  const addPhoto = (file: File | undefined) => {
    if (p.t !== 'key' || !file) return;
    setBusy(true);
    api.addKeyRequestPhoto(p.key, file).then(() => setPhotos(n => n + 1)).catch(error => onFail(errorText(error))).finally(() => setBusy(false));
  };
  const completeKey = () => { if (p.t === 'key') void run(async () => { await api.completeKeyRequest(p.key); return `The ${p.key.kind === 'Handover' ? 'key handover' : 'key pickup'} for ${row.customer} is completed.`; }); };
  const confirmBooking = () => { if (p.t === 'booking') void run(async () => {
    if (!await adminBookings.confirm(p.item.id)) throw new Error('The booking could not be confirmed. Refresh and check its status.');
    return `Booking ${p.item.reference} is confirmed. Assign an employee next.`;
  }); };
  const saveEdit = () => {
    if (p.t !== 'request') return;
    if (!edit.from || !edit.to || edit.to < edit.from) { setProblem('The end date must be on or after the start date.'); return; }
    void run(async () => {
      await api.update(p.request, { fromDate: edit.from, toDate: edit.to, exactStartLocalTime: edit.time || null, notes: edit.notes });
      return `${p.request.branchName} is updated. Send a new employee offer.`;
    });
  };
  const needsReason = p.t === 'request' || p.t === 'quote';
  const cancelIt = () => {
    if (needsReason && !reason.trim()) { setProblem('Enter a reason for the partner.'); return; }
    void run(async () => {
      if (p.t === 'request') { await api.cancel(p.request, reason.trim()); return `${p.request.branchName} is cancelled. The partner sees your reason.`; }
      if (p.t === 'quote') { await api.rejectQuote(p.branch, reason.trim()); return `The quote for ${p.branch.name} is rejected and the partner sees your reason.`; }
      if (p.t === 'booking') {
        if (!await adminBookings.cancel(p.item.id)) throw new Error('The booking could not be cancelled. Refresh and check its status.');
        return `Booking ${p.item.reference} is cancelled.`;
      }
      if (p.t === 'shift' && p.item.visit) {
        const v = p.item.visit;
        const extra = v.status === STATUS_EXTRA ? v.extraExceptionId ?? v.exceptionId : null;
        const text = reason.trim() || 'Cancelled from the Work queue';
        if (extra) await adminShifts.cancelVisit(v.shiftId, extra, { status: STATUS_COMPANY_CANCELLED, reason: text });
        else await adminShifts.saveOccurrenceException(v.shiftId, { id: v.exceptionId, originalStartUtc: v.originalStartUtc ?? v.startUtc, startUtc: v.startUtc, endUtc: v.endUtc, status: STATUS_COMPANY_CANCELLED, reason: text });
        return `${row.reference} on ${row.when} is cancelled.`;
      }
      throw new Error('This item cannot be cancelled here.');
    });
  };

  // ---- What the panel shows ----
  const title = p.t === 'booking' ? `Booking ${p.item.reference}` : p.t === 'quote' ? `Price ${p.branch.name}` : row.reference;
  const facts: [string, string][] = (() => {
    if (p.t === 'shift') return [['Customer', row.customer], ['Status', row.state], ['Start', row.when], ['Cleaning type', row.cleanType ?? 'Normal'], ...(p.item.visit?.notes ? [['Notes', p.item.visit.notes] as [string, string]] : [])];
    if (p.t === 'booking') return [['Customer', row.customer], ['Status', booking?.status ?? row.state], ['Start', row.when],
      ['Contact', booking ? `${booking.customer.phone} · ${booking.customer.email}` : 'Loading…'], ['Service', `${p.item.service ?? booking?.service.nameFi ?? 'Cleaning'}${p.item.durationMinutes ? `, ${p.item.durationMinutes} min` : ''}`]];
    if (p.t === 'request') { const q = p.request; return [['Customer', row.customer], ['Status', row.state], ['Window', windowText(q.fromDate, q.toDate)],
      ['Start', q.exactStartLocalTime ? q.exactStartLocalTime.slice(0, 5) : 'Any time in the window'], ['Duration', lenText(q.estimatedDurationMinutes / 60)],
      ['Cleaning type', row.cleanType ?? 'Normal'], ['Price', `€${q.priceNet.toFixed(2)} excl. VAT (${q.vatRatePercent}%)`], ['Notes', q.notes || 'No notes']]; }
    if (p.t === 'quote') return [['Company', row.customer], ['Property', row.reference], ['Rooms', String(p.branch.roomCount ?? 'Not set')], ['Beds', String(p.branch.bedCount ?? 'Not set')], ['Requested', row.when.replace('Waiting since ', '')]];
    if (p.t === 'change') return [['Company', row.customer], ['About', row.reference], ['Sent', row.when.replace('Sent ', '')],
      ...(p.request ? [['Visit now', `${windowText(p.request.fromDate, p.request.toDate)}${p.request.exactStartLocalTime ? ` ${p.request.exactStartLocalTime.slice(0, 5)}` : ''}${p.request.acceptedCleanerName ? ` with ${p.request.acceptedCleanerName}` : ''}`] as [string, string]] : [])];
    const k = p.key;
    return [['Customer', row.customer], ['Request', k.kind === 'Handover' ? 'Key handover' : 'Key pickup'], ['Date', dateText(k.date)],
      ['Fee', `€${k.feeNet.toFixed(2)} excl. VAT`], ['Notes', k.notes || 'No notes'], ['Staff member', k.assignedEmployeeName ?? 'Not assigned']];
  })();
  const requested = p.t !== 'change' ? '' : p.changeKind === 'reschedule'
    ? `Asks to move the visit to ${p.change.proposedFromDate ? windowText(p.change.proposedFromDate, p.change.proposedToDate ?? p.change.proposedFromDate) : 'another time'}${p.change.proposedExactStartLocalTime ? ` at ${p.change.proposedExactStartLocalTime.slice(0, 5)}` : ''}.`
    : p.changeKind === 'cancel' ? 'Asks to cancel the visit.' : p.changeKind === 'instructions' ? 'Asks to change the cleaning instructions.' : 'Sent a message.';
  const changeHelp = p.t !== 'change' ? '' : p.changeKind === 'reschedule' ? 'Make the change below: the visit is already on the card at the requested days. Drag it to the time that suits, change the person if needed, then move it.'
    : p.changeKind === 'cancel' ? 'Cancelling removes the visit from the Scheduler, tells the assignee and tells the partner.'
    : p.changeKind === 'instructions' ? 'Update the instructions below so the assignee sees the change on the visit.' : 'Your reply is shown to the partner and the message is resolved.';
  const assignWords = p.t === 'request' ? { label: 'Offer to', verb: 'Send offer to', save: 'send the offer', help: 'The employee gets an offer to accept. Once accepted, it moves to the Scheduler; people with automatic acceptance take it straight away. ' }
    : p.t === 'change' ? { label: 'Move the visit', verb: 'Move with', save: 'move it', help: '' }
    : p.t === 'key' ? { label: 'Staff member who collects or hands over the keys', verb: 'Assign to', save: 'assign', help: '' }
    : { label: 'Assignee', verb: 'Assign to', save: 'assign', help: '' };
  const cardHelp = assignWords.help + (plan?.start === null
    ? 'Key requests have a day but no time. Pick the employee in the card; the list shows how busy each one is that day.'
    : 'Pick the employee in the card; the list shows every employee, free ones first. Drag the outlined block along the day, or tap the bar, to set the time.'
      + (plan?.canResize ? ' Drag its dark right edge to make it longer or shorter.' : '') + (plan && plan.days.length > 1 ? ' Use the arrows to change the day.' : ''));
  const original = p.t === 'request' ? `The partner asked for ${windowText(p.request.fromDate, p.request.toDate)}${p.request.exactStartLocalTime ? ` ${p.request.exactStartLocalTime.slice(0, 5)}` : ''}, ${lenText(p.request.estimatedDurationMinutes / 60)}.`
    : p.t === 'change' && p.request ? `Now ${windowText(p.request.fromDate, p.request.toDate)}.`
    : plan ? `Originally ${row.when}, ${lenText(plan.len)}.` : '';
  const canCancel = !!CANCEL_LABEL[p.t] && (p.t !== 'shift' || !!p.item.visit);
  const cancelLabel = CANCEL_LABEL[p.t] ?? 'Cancel';
  const keyAssigned = p.t === 'key' && !!p.key.assignedEmployeeId;

  return (
    <aside className="drawer" role="dialog" aria-modal="true" aria-label={title} aria-busy={busy}>
      <div className="drawer-head">
        <span style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
          <span className="tags" style={{ gap: 6 }}><span className={SOURCE_CHIP[row.source]}>{SOURCE_LABEL[row.source]}</span><span className="chip chip-type">{TYPE_LABEL[row.kind]}</span>{row.cleanType && <span className="chip chip-sub">{row.cleanType}</span>}</span>
          <strong>{title}</strong>
        </span>
        <button ref={closeButton} type="button" className="btn btn-ghost" aria-label="Close" onClick={onClose} style={{ minWidth: 44 }}>×</button>
      </div>
      <dl className="facts">{facts.map(([k, v]) => <div key={k} style={{ display: 'contents' }}><dt>{k}</dt><dd>{v}</dd></div>)}</dl>
      {p.t === 'change' && <p className="message">{p.change.message}</p>}

      {p.t === 'change' && <>
        <p style={{ margin: 0, fontWeight: 600, display: 'flex', alignItems: 'center', gap: 4, flexWrap: 'wrap' }}>{requested}<Info text={changeHelp} /></p>
        {p.changeKind === 'instructions' && <label className="field">Cleaning instructions, shown to the assignee<textarea className="input" rows={3} value={notes} onChange={event => setNotes(event.target.value)} /></label>}
        <label className="field">{p.changeKind === 'message' ? 'Reply to the partner' : 'Reply to the partner (optional when you make the change)'}<textarea className="input" rows={2} value={reply} onChange={event => { setReply(event.target.value); setProblem(''); }} /></label>
        {(p.changeKind === 'instructions' || p.changeKind === 'message') && <div className="row"><button type="button" className="btn btn-primary" disabled={busy} onClick={resolveChange}>{p.changeKind === 'instructions' ? 'Save instructions and resolve' : 'Send reply and resolve'}</button></div>}
        {p.changeKind === 'cancel' && <div className="row"><button type="button" className="btn btn-primary" disabled={busy} onClick={cancelVisit}>Cancel the visit</button></div>}
        {p.changeKind === 'reschedule' && !p.request && <p className="note">The visit this request is about could not be found. Reply to the partner instead.</p>}
      </>}

      {plan && <AssignCard key={row.id} plan={plan} employees={employees} zone={zone} pickLabel={assignWords.label} help={cardHelp} verb={assignWords.verb}
        autoAcceptNote={p.t === 'request'} preselect={p.t === 'request' ? p.request.offeredEmployeeId : p.t === 'change' ? employees.find(e => personName(e) === p.request?.acceptedCleanerName)?.id : null} original={original} saveWord={assignWords.save} busy={busy} onAssign={assign} />}

      {p.t === 'change' && p.changeKind !== 'message' && <div className="row"><button type="button" className="btn btn-ghost" disabled={busy} onClick={decline}>Decline the request</button></div>}

      {p.t === 'quote' && <>
        <span className="row" style={{ gap: 4 }}><strong style={{ fontSize: 15 }}>Price for this property</strong><Info text="New requests for this property use this price. Existing requests keep their agreed price." /></span>
        <div className="two">
          <label className="field">Price excl. VAT (€)<input className="input" type="number" min="0" step="0.01" value={price} onChange={event => { setPrice(event.target.value); setProblem(''); }} /></label>
          <label className="field">VAT (%)<input className="input" type="number" min="0" max="100" step="0.01" value={vat} onChange={event => setVat(event.target.value)} /></label>
        </div>
        <label className="field">Estimated duration (minutes)<input className="input" type="number" min="1" step="1" value={duration} onChange={event => { setDuration(event.target.value); setProblem(''); }} /></label>
        <div className="row"><button type="button" className="btn btn-primary" disabled={busy} onClick={savePrice}>Save price</button></div>
      </>}

      {keyAssigned && p.t === 'key' && <>
        <p className="note">{p.key.assignedEmployeeName ?? 'The staff member'} is handling it. Add a photo of the keys if you have one, then mark it completed.</p>
        <input ref={photoInput} type="file" accept="image/*" hidden aria-label="Photo of the keys" onChange={event => { addPhoto(event.target.files?.[0]); event.target.value = ''; }} />
        <button type="button" className="btn btn-ghost" disabled={busy} onClick={() => photoInput.current?.click()}>Add a photo ({photos} added)</button>
        <button type="button" className="btn btn-primary" disabled={busy} onClick={completeKey}>Mark completed</button>
      </>}

      {problem && <p role="alert" className="problem">{problem}</p>}

      {editing && p.t === 'request' && (
        <div className="box">
          <span className="row" style={{ gap: 4 }}><strong>Edit or reschedule</strong><Info text="Changes withdraw any offer, so the request needs a new employee offer." /></span>
          <div className="two">
            <label className="field">From<input className="input" type="date" value={edit.from} onChange={event => setEdit({ ...edit, from: event.target.value })} /></label>
            <label className="field">To<input className="input" type="date" value={edit.to} min={edit.from} onChange={event => setEdit({ ...edit, to: event.target.value })} /></label>
          </div>
          <label className="field">Exact start (optional, one day only)<input className="input" type="time" value={edit.time} onChange={event => setEdit({ ...edit, time: event.target.value })} /></label>
          <label className="field">Cleaning instructions<textarea className="input" rows={2} value={edit.notes} onChange={event => setEdit({ ...edit, notes: event.target.value })} /></label>
          <div className="row"><button type="button" className="btn btn-ghost" onClick={() => setEditing(false)}>Cancel</button><button type="button" className="btn btn-primary" disabled={busy} onClick={saveEdit}>Save changes</button></div>
        </div>
      )}

      {(p.t === 'booking' && booking?.status === 'New' || p.t === 'request' && !editing || canCancel) && (
        <div className="foot">
          {p.t === 'booking' && booking?.status === 'New' && <button type="button" className="btn btn-ghost" disabled={busy} onClick={confirmBooking}>Confirm booking</button>}
          {p.t === 'request' && !editing && <button type="button" className="btn btn-ghost" disabled={busy} onClick={() => { setEditing(true); setCancelAsk(false); }}>Edit or reschedule</button>}
          {canCancel && <button type="button" className="btn btn-ghost" disabled={busy} onClick={() => { setCancelAsk(true); setReason(''); setProblem(''); }}>{cancelLabel}</button>}
          {cancelAsk && (
            <div role="alertdialog" aria-label={cancelLabel} className="ask">
              <strong>{cancelLabel}?</strong>
              {needsReason && <label className="field">Reason, shown to the partner<input className="input" type="text" maxLength={500} value={reason} onChange={event => { setReason(event.target.value); setProblem(''); }} /></label>}
              {p.t === 'shift' && <label className="field">Reason (optional)<input className="input" type="text" maxLength={500} value={reason} onChange={event => setReason(event.target.value)} /></label>}
              <div className="row"><button type="button" className="btn btn-ghost" onClick={() => setCancelAsk(false)}>Keep</button><button type="button" className="btn btn-primary" disabled={busy} onClick={cancelIt}>{cancelLabel}</button></div>
            </div>
          )}
        </div>
      )}
    </aside>
  );
}
