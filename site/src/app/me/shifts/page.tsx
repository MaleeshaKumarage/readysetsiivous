'use client';

import { useEffect, useState } from 'react';
import {
  Calendar,
  Clock,
  Play,
  CheckCircle2,
  Loader2,
  AlertTriangle,
  ChevronDown,
  ChevronUp,
} from 'lucide-react';
import { initAuth, isAuthenticated, login, logout } from '@/lib/auth';
import { meApi, MyShiftOccurrence, QualityCycleFormItem } from '@/lib/meApi';
import { Logo } from '@/ds/Logo';
import { Button } from '@/ds/Button';
import { Card } from '@/ds/Card';

type Status = 'not-started' | 'in-progress' | 'submitted';

function statusOf(o: MyShiftOccurrence): Status {
  if (!o.form) return 'not-started';
  if (o.form.isSubmitted) return 'submitted';
  if (o.form.startedAtUtc) return 'in-progress';
  return 'not-started';
}

function statusLabel(s: Status): string {
  switch (s) {
    case 'not-started': return 'Not started';
    case 'in-progress': return 'In progress';
    case 'submitted': return 'Done';
  }
}

function statusClasses(s: Status): string {
  switch (s) {
    case 'not-started': return 'bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-300';
    case 'in-progress': return 'bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300';
    case 'submitted': return 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300';
  }
}

export default function MyShiftsPage() {
  const [ready, setReady] = useState(false);
  const [authed, setAuthed] = useState(false);
  const [occurrences, setOccurrences] = useState<MyShiftOccurrence[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [openId, setOpenId] = useState<string | null>(null);

  // Inline form state
  const [items, setItems] = useState<QualityCycleFormItem[]>([]);
  const [notes, setNotes] = useState('');
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    initAuth().then((ok) => {
      setReady(true);
      setAuthed(ok);
      if (!ok) return;
      loadShifts();
    });
  }, []);

  async function loadShifts() {
    setLoading(true);
    setError(null);
    try {
      const data = await meApi.myShifts();
      setOccurrences(data ?? []);
    } catch {
      setError('Failed to load your shifts.');
    } finally {
      setLoading(false);
    }
  }

  async function handleStart(o: MyShiftOccurrence) {
    setBusy(true);
    setError(null);
    try {
      const form = await meApi.startShift(o.shiftId, o.startUtc, o.endUtc);
      if (form) {
        setOccurrences((prev) => prev.map((x) => (x.shiftId === o.shiftId && x.startUtc === o.startUtc ? { ...x, form } : x)));
      } else {
        setError('Could not start shift.');
      }
    } catch {
      setError('Could not start shift.');
    } finally {
      setBusy(false);
    }
  }

  function openForm(o: MyShiftOccurrence) {
    setOpenId(openId === `${o.shiftId}-${o.startUtc}` ? null : `${o.shiftId}-${o.startUtc}`);
    if (o.form) {
      setItems(o.form.items.map((i) => ({ ...i })));
      setNotes(o.form.cleanerNotes ?? '');
    }
  }

  function toggleItem(index: number) {
    setItems((prev) => prev.map((it, i) => (i === index ? { ...it, isChecked: !it.isChecked } : it)));
  }

  async function handleEnd(o: MyShiftOccurrence) {
    if (!o.form) return;
    setSubmitting(true);
    setError(null);
    try {
      const result = await meApi.endForm(o.form.id, items, undefined, notes);
      if (result) {
        setOccurrences((prev) => prev.map((x) => (x.shiftId === o.shiftId && x.startUtc === o.startUtc ? { ...x, form: result } : x)));
        setOpenId(null);
      } else {
        setError('Could not submit form.');
      }
    } catch {
      setError('Could not submit form.');
    } finally {
      setSubmitting(false);
    }
  }

  if (!ready) {
    return (
      <div className="min-h-screen flex items-center justify-center p-4">
        <Loader2 className="w-6 h-6 animate-spin text-brand-500" />
      </div>
    );
  }

  if (!authed) {
    return (
      <div className="min-h-screen bg-gradient-to-br from-accent-50 via-white to-brand-50 dark:from-gray-950 dark:to-gray-900 flex flex-col items-center justify-center p-4 gap-4">
        <Logo size="lg" />
        <p className="text-sm text-gray-600 dark:text-gray-300">Sign in to view your shifts.</p>
        <Button onClick={() => login()}>Sign in</Button>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-gradient-to-br from-accent-50 via-white to-brand-50 dark:from-gray-950 dark:via-gray-950 dark:to-gray-900 py-8 px-4 sm:px-6">
      <div className="max-w-2xl mx-auto space-y-6">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            <Logo size="md" />
            <h1 className="text-xl font-bold text-gray-900 dark:text-gray-100">My Shifts</h1>
          </div>
          <Button variant="ghost" size="sm" onClick={() => logout()}>Sign out</Button>
        </div>

        {error && (
          <div className="flex items-center gap-2 p-3 rounded-xl bg-red-50 border border-red-200 text-red-700 text-sm">
            <AlertTriangle className="w-4 h-4 shrink-0" />
            <span>{error}</span>
          </div>
        )}

        {loading ? (
          <div className="flex items-center justify-center py-16 text-gray-500">
            <Loader2 className="w-6 h-6 animate-spin" />
          </div>
        ) : occurrences.length === 0 ? (
          <Card variant="elevated" padding="lg" className="text-center text-gray-500 dark:text-gray-400">
            No shifts scheduled.
          </Card>
        ) : (
          <div className="space-y-4">
            {occurrences.map((o) => {
              const key = `${o.shiftId}-${o.startUtc}`;
              const status = statusOf(o);
              const isOpen = openId === key;
              return (
                <Card key={key} variant="elevated" padding="none" className="overflow-hidden">
                  <div className="p-5 space-y-3">
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <h2 className="font-bold text-gray-900 dark:text-gray-100">{o.shiftName}</h2>
                        <div className="flex flex-wrap items-center gap-3 text-xs text-gray-500 dark:text-gray-400 pt-1">
                          <span className="flex items-center gap-1">
                            <Calendar className="w-3.5 h-3.5" />
                            {new Date(o.startUtc).toLocaleDateString()}
                          </span>
                          <span className="flex items-center gap-1">
                            <Clock className="w-3.5 h-3.5" />
                            {new Date(o.startUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                            {' – '}
                            {new Date(o.endUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                          </span>
                        </div>
                      </div>
                      <span className={`text-xs font-semibold px-2.5 py-1 rounded-full ${statusClasses(status)}`}>
                        {statusLabel(status)}
                      </span>
                    </div>

                    {status === 'not-started' && (
                      <Button size="sm" onClick={() => handleStart(o)} disabled={busy} className="gap-1.5">
                        <Play className="w-4 h-4" /> Start
                      </Button>
                    )}

                    {status === 'in-progress' && (
                      <Button variant="outline" size="sm" onClick={() => openForm(o)} className="gap-1.5">
                        {isOpen ? <ChevronUp className="w-4 h-4" /> : <ChevronDown className="w-4 h-4" />}
                        Fill form & end
                      </Button>
                    )}

                    {status === 'submitted' && (
                      <div className="flex items-center gap-2 text-xs text-emerald-600 dark:text-emerald-400">
                        <CheckCircle2 className="w-4 h-4" />
                        Submitted {o.form?.submittedUtc ? new Date(o.form.submittedUtc).toLocaleString() : ''}
                      </div>
                    )}

                    {isOpen && status === 'in-progress' && o.form && (
                      <div className="pt-3 border-t border-gray-100 dark:border-gray-700 space-y-4">
                        <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                          {o.form.templateTitle || 'Checklist'}
                        </h3>
                        <div className="space-y-2">
                          {items.map((item, index) => (
                            <div
                              key={index}
                              onClick={() => toggleItem(index)}
                              className={`flex items-center gap-2 p-3 rounded-lg border cursor-pointer transition-colors ${
                                item.isChecked
                                  ? 'bg-brand-50/60 border-brand-300 dark:bg-brand-950/30 dark:border-brand-800'
                                  : 'bg-white border-gray-200 hover:border-brand-200 dark:bg-gray-800 dark:border-gray-700'
                              }`}
                            >
                              <input type="checkbox" checked={item.isChecked} onChange={() => toggleItem(index)} className="accent-brand-500" />
                              <span className="text-sm text-gray-800 dark:text-gray-200">{item.itemText}</span>
                            </div>
                          ))}
                        </div>
                        <textarea
                          value={notes}
                          onChange={(e) => setNotes(e.target.value)}
                          placeholder="Notes (optional)"
                          rows={2}
                          className="w-full rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 p-3 text-sm text-gray-800 dark:text-gray-200 focus:outline-none focus:ring-2 focus:ring-brand-400"
                        />
                        <Button size="sm" onClick={() => handleEnd(o)} disabled={submitting} fullWidth className="gap-1.5">
                          {submitting ? <Loader2 className="w-4 h-4 animate-spin" /> : <CheckCircle2 className="w-4 h-4" />}
                          End shift & submit
                        </Button>
                      </div>
                    )}
                  </div>
                </Card>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
