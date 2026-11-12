'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { staffInboxApi, type StaffInboxEntry } from '@/lib/staffInboxApi';

/** Events an admin or manager should act on, kept in the app so they are seen even when push is off. */
export function StaffInboxPanel() {
  const [items, setItems] = useState<StaffInboxEntry[] | null>(null);
  const [unread, setUnread] = useState(0);
  const [error, setError] = useState('');

  const load = () => staffInboxApi.list()
    .then(page => { setItems(page.items); setUnread(page.unread); setError(''); })
    .catch(failure => setError(failure instanceof Error ? failure.message : 'Could not load notifications.'));

  useEffect(() => { void load(); }, []);

  const open = async (entry: StaffInboxEntry) => {
    if (!entry.read) {
      try { await staffInboxApi.markRead(entry.id); } catch { /* the item stays unread and can be opened again */ }
    }
    await load();
  };

  return (
    <section aria-labelledby="staff-inbox-title" style={{ marginBottom: '1.5rem' }}>
      <h2 id="staff-inbox-title" style={{ fontSize: '1.1rem', fontWeight: 600 }}>
        Inbox{unread > 0 ? <span role="status" data-testid="inbox-unread"> · {unread} unread</span> : null}
      </h2>
      {error && <p role="alert">{error}</p>}
      {items && items.length === 0 && <p>Nothing needs your attention right now.</p>}
      {items && items.length > 0 && <ul style={{ listStyle: 'none', padding: 0 }}>
        {items.map(entry => <li key={entry.id} style={{ padding: '0.5rem 0', borderBottom: '1px solid #e5e7eb', fontWeight: entry.read ? 400 : 600 }}>
          <Link href={entry.link} onClick={() => { void open(entry); }}>{entry.title}</Link>
          <div style={{ fontSize: '0.9rem', fontWeight: 400 }}>{entry.body}</div>
          <div style={{ fontSize: '0.8rem', color: '#6b7280' }}>{new Date(entry.createdUtc).toLocaleString()}{entry.read ? ' · read' : ''}</div>
        </li>)}
      </ul>}
    </section>
  );
}
