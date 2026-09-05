'use client';

import { useEffect, useState, type ReactNode } from 'react';
import { initAuth, isAuthenticated, isAdmin, login, logout } from '@/lib/auth';
import { baseUrl } from '@/lib/whatsapp';

const LINKS = [
  { href: 'services', label: 'Services' },
  { href: 'bookings', label: 'Bookings' },
  { href: 'schedule', label: 'Schedule' },
  { href: 'invoices', label: 'Invoices' },
  { href: 'employees', label: 'Employees' },
  { href: 'settings', label: 'Settings' },
] as const;

export default function AdminShell({ lang, active, children }: { lang: string; active: string; children: ReactNode }) {
  const [ready, setReady] = useState(false);
  const [authed, setAuthed] = useState(false);
  const [admin, setAdmin] = useState(false);

  useEffect(() => {
    initAuth().then(() => {
      setAuthed(isAuthenticated());
      setAdmin(isAdmin());
      setReady(true);
    });
  }, []);

  if (!ready) {
    return <p className="p-8 text-center text-muted-foreground">Checking login…</p>;
  }

  if (!authed || !admin) {
    return (
      <div className="mx-auto max-w-md px-4 py-16 text-center">
        <h1 className="mb-4 text-2xl font-bold">Admin login</h1>
        <p className="mb-8 text-sm text-muted-foreground">
          Admin access requires the admin role in the ReadysetSiivous Keycloak realm.
        </p>
        <button
          onClick={() => login()}
          className="rounded-full bg-brand px-6 py-3 font-semibold text-background"
        >
          Sign in with Keycloak
        </button>
      </div>
    );
  }

  return (
    <div className="flex min-h-screen flex-col lg:flex-row">
      <aside className="border-b border-border bg-card lg:w-56 lg:border-b-0 lg:border-r">
        <nav className="flex gap-1 overflow-x-auto p-3 lg:flex-col">
          {LINKS.map((link) => (
            <a
              key={link.href}
              href={baseUrl(`/${lang}/admin/${link.href}/`)}
              className={
                'whitespace-nowrap rounded-lg px-4 py-2 text-sm font-medium ' +
                (active === link.href
                  ? 'bg-brand text-background'
                  : 'text-muted-foreground hover:bg-brand/10')
              }
            >
              {link.label}
            </a>
          ))}
          <button
            onClick={() => logout()}
            className="whitespace-nowrap rounded-lg px-4 py-2 text-left text-sm text-red-500 hover:bg-red-500/10"
          >
            Sign out
          </button>
        </nav>
      </aside>
      <main className="flex-1 p-6">{children}</main>
    </div>
  );
}
