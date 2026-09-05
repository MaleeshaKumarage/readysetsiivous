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
    return <p className="p-8 text-center text-gray-500 dark:text-gray-400">Checking login…</p>;
  }

  if (!authed || !admin) {
    return (
      <div className="container-page py-16">
        <div className="mx-auto max-w-md rounded-2xl bg-white dark:bg-gray-800 border border-gray-100 dark:border-gray-700 shadow-sm p-8 text-center">
          <h1 className="mb-2 text-2xl font-bold text-gray-900 dark:text-gray-50">Admin login</h1>
          <p className="mb-6 text-sm text-gray-500 dark:text-gray-400">
            Admin access requires the admin role in the ReadySetSiivous Keycloak realm.
          </p>
          <button
            onClick={() => login()}
            className="inline-flex items-center gap-2 rounded-full bg-brand px-6 py-3 font-semibold text-accent-950 hover:bg-brand-400 transition-all"
          >
            Sign in with Keycloak
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="container-page py-8">
      <div className="mb-6 flex flex-wrap items-center gap-2 border-b border-gray-100 dark:border-accent-900 pb-4">
        <nav className="flex flex-wrap items-center gap-2">
          {LINKS.map((link) => (
            <a
              key={link.href}
              href={baseUrl(`/${lang}/admin/${link.href}/`)}
              className={
                'rounded-full px-4 py-2 text-sm font-semibold transition-colors ' +
                (active === link.href
                  ? 'bg-brand text-accent-950 hover:bg-brand-400'
                  : 'text-gray-600 dark:text-gray-300 hover:bg-brand-50 dark:hover:bg-brand-500/10 hover:text-brand-700 dark:hover:text-brand-400')
              }
            >
              {link.label}
            </a>
          ))}
        </nav>
        <button
          onClick={() => logout()}
          className="ml-auto rounded-full border border-gray-200 dark:border-gray-700 px-4 py-2 text-sm font-semibold text-gray-600 dark:text-gray-300 hover:border-red-300 hover:text-red-500 transition-colors"
        >
          Sign out
        </button>
      </div>
      {children}
    </div>
  );
}
