'use client';

import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import Link from 'next/link';
import type { Branch } from '@/lib/adminApi';
import { staffPartnerApi } from '@/lib/partnerApi';

const pendingQuote = (branch: Branch) => branch.isActive && Boolean(branch.quoteRequestedUtc) && !branch.pricingReady;
const QuoteContext = createContext<{ branches: Branch[]; requestCount: number; changeCount: number; actionsError: boolean; loaded: boolean; error: boolean; refresh: () => Promise<void> } | null>(null);
export const useQuoteRequests = () => useContext(QuoteContext);

export function QuoteRequestProvider({ pathname, children }: { pathname: string; children: ReactNode }) {
  const [branches, setBranches] = useState<Branch[]>([]);
  const [requestCount, setRequestCount] = useState(0), [changeCount, setChangeCount] = useState(0);
  const [loaded, setLoaded] = useState(false), [error, setError] = useState(false);
  const [actionsError, setActionsError] = useState(false);
  const controller = useRef<AbortController | null>(null);
  const refresh = useCallback(async () => {
    controller.current?.abort();
    const request = new AbortController(); controller.current = request;
    try {
      const api = staffPartnerApi('admin');
      const [quotes, requests, changes] = await Promise.allSettled([api.quotes(request.signal), api.requests(request.signal), api.changes(request.signal)]);
      if (request.signal.aborted) return;
      if (requests.status === 'fulfilled' && Array.isArray(requests.value)) setRequestCount(requests.value.filter(item => item.status === 'Requested').length);
      if (changes.status === 'fulfilled' && Array.isArray(changes.value)) setChangeCount(changes.value.filter(item => !item.resolvedUtc).length);
      setActionsError(requests.status === 'rejected' || changes.status === 'rejected' || (requests.status === 'fulfilled' && !Array.isArray(requests.value)) || (changes.status === 'fulfilled' && !Array.isArray(changes.value)));
      if (quotes.status === 'rejected') throw quotes.reason;
      const result = quotes.value;
      if (request.signal.aborted) return;
      if (!Array.isArray(result)) throw new Error('Invalid quote queue');
      setBranches(result.filter(pendingQuote)); setLoaded(true); setError(false);
    } catch {
      if (!request.signal.aborted) setError(true);
    }
  }, []);
  useEffect(() => {
    const refreshVisible = () => { if (document.visibilityState === 'visible') void refresh(); };
    refreshVisible();
    const timer = window.setInterval(refreshVisible, 30_000);
    window.addEventListener('focus', refreshVisible);
    document.addEventListener('visibilitychange', refreshVisible);
    return () => {
      window.clearInterval(timer); controller.current?.abort();
      window.removeEventListener('focus', refreshVisible);
      document.removeEventListener('visibilitychange', refreshVisible);
    };
  }, [pathname, refresh]);
  return <QuoteContext.Provider value={{ branches, requestCount, changeCount, actionsError, loaded, error, refresh }}>{children}</QuoteContext.Provider>;
}

export function QuoteRequestBanner() {
  const queue = useQuoteRequests();
  if (!queue || (!queue.branches.length && !queue.error && !queue.actionsError)) return null;
  const count = queue.branches.length;
  return <section aria-label="Pending quote requests" role="status" className="mb-5 rounded-lg border border-red-200 bg-red-50 p-4 text-red-900">
    <div className="flex flex-wrap items-center justify-between gap-3">
      {count > 0 && <><p className="font-semibold">{count} {count === 1 ? 'branch awaiting a quote' : 'branches awaiting quotes'}</p><Link className="inline-flex min-h-11 items-center font-semibold underline" href="/admin/partner-requests/?tab=quotes">Review quotes</Link></>}
      {queue.actionsError && <div className="flex flex-wrap items-center gap-3"><p role="alert">Partner action counts could not be checked. Displayed counts may be out of date.</p><button type="button" className="min-h-11 font-semibold underline" onClick={() => void queue.refresh()}>Retry partner counts</button></div>}
      {queue.error && <div className="flex flex-wrap items-center gap-3"><p>Quote requests could not be checked. {queue.loaded && 'Showing the last checked requests.'}</p><button type="button" className="min-h-11 font-semibold underline" onClick={() => void queue.refresh()}>Retry quote requests</button></div>}
    </div>
  </section>;
}

export function QuoteRequestedBadge({ branch, label = 'Quote requested' }: { branch: Branch; label?: string }) {
  const queue = useQuoteRequests();
  const pending = branch.isActive && !branch.pricingReady
    && (queue?.loaded ? queue.branches.some(item => item.id === branch.id) : Boolean(branch.quoteRequestedUtc));
  if (!pending) return null;
  return <span className="inline-flex w-fit items-center rounded-full border border-red-200 bg-red-50 px-2.5 py-1 text-xs font-semibold text-red-800">{label}</span>;
}

export function PartnerPendingBadge() {
  const queue = useQuoteRequests();
  const count = queue ? queue.branches.length + queue.requestCount + queue.changeCount : 0;
  if (!count) return null;
  return <span aria-label={`${count} pending partner actions`} className="inline-flex min-w-6 items-center justify-center rounded-full bg-red-50 px-2 py-1 text-xs font-semibold text-red-800 border border-red-200">{count}</span>;
}
