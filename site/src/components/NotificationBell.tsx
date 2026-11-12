'use client';

import { useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { Bell, ChevronRight, X } from 'lucide-react';
import { staffInboxApi, type StaffInboxEntry } from '@/lib/staffInboxApi';

/**
 * Bell with an unread count. Opens a side drawer listing staff notifications.
 * Each notification links to the place it is about and is marked read when opened.
 */
export function NotificationBell({ settingsHref }: { settingsHref: string }) {
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState<StaffInboxEntry[] | null>(null);
  const [unread, setUnread] = useState(0);
  const [error, setError] = useState('');
  const bellRef = useRef<HTMLButtonElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);

  const load = () => staffInboxApi.list()
    .then(page => { setItems(page.items); setUnread(page.unread); setError(''); })
    .catch(failure => setError(failure instanceof Error ? failure.message : 'Could not load notifications.'));

  useEffect(() => { void load(); }, []);

  useEffect(() => {
    if (!open) return;
    closeRef.current?.focus();
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { setOpen(false); bellRef.current?.focus(); }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open]);

  const close = () => { setOpen(false); bellRef.current?.focus(); };

  // Opening a notification marks it read here; the link itself takes the person to the relevant page.
  const openEntry = (entry: StaffInboxEntry) => {
    if (entry.read) return;
    setItems(current => current?.map(item => item.id === entry.id ? { ...item, read: true } : item) ?? null);
    setUnread(count => Math.max(0, count - 1));
    staffInboxApi.markRead(entry.id).catch(() => { /* stays unread on the server; it is shown again next time */ });
  };

  return <>
    <button ref={bellRef} type="button" className="notify-bell" aria-controls="notify-drawer" aria-expanded={open}
      aria-label={unread > 0 ? `Notifications, ${unread} unread` : 'Notifications'}
      onClick={() => { setOpen(true); void load(); }}>
      <Bell size={22} aria-hidden="true" />
      {unread > 0 && <span className="notify-badge" aria-hidden="true">{unread > 99 ? '99+' : unread}</span>}
    </button>

    {open && <>
      <div className="notify-overlay" onClick={close} aria-hidden="true" />
      <aside id="notify-drawer" role="dialog" aria-modal="true" aria-labelledby="notify-title" className="notify-drawer">
        <div className="notify-drawer-head">
          <div>
            <h2 id="notify-title">Notifications</h2>
            {unread > 0 && <p className="notify-count" role="status">{unread} unread</p>}
          </div>
          <button ref={closeRef} type="button" className="notify-close" aria-label="Close notifications" onClick={close}><X size={22} aria-hidden="true" /></button>
        </div>

        {error && <p role="alert" className="notify-empty">{error}</p>}
        {items && items.length === 0 && <p className="notify-empty">Nothing needs your attention right now.</p>}
        {items && items.length > 0 && <ul className="notify-list">
          {items.map(entry => <li key={entry.id}>
            <Link href={entry.link} className={`notify-item${entry.read ? '' : ' notify-unread'}`} onClick={() => openEntry(entry)}>
              <span className="notify-title">{entry.title}</span>
              <span className="notify-body">{entry.body}</span>
              <span className="notify-time">{new Date(entry.createdUtc).toLocaleString()}{entry.read ? ' · read' : ''}</span>
              <ChevronRight size={18} aria-hidden="true" className="notify-open" />
            </Link>
          </li>)}
        </ul>}

        <div className="notify-foot">
          <Link className="account-link" href={settingsHref} onClick={() => setOpen(false)}>Notification settings</Link>
        </div>
      </aside>
    </>}
  </>;
}
