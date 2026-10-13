'use client';
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import Link from 'next/link';
import { initAuth, isAuthenticated, isAdmin, isManager, authTenant, refreshToken, login, logout } from '@/lib/auth';
import { API_URL } from '@/lib/api';

export default function ManagerLayout({ children }: { children: ReactNode }) {
  const [access, setAccess] = useState<'pending' | 'anonymous' | 'allowed' | 'denied' | 'error'>('pending');
  const [tenant, setTenant] = useState('');
  const version = useRef(0);
  const verify = useCallback(async () => {
    const attempt = ++version.current; setAccess('pending');
    try {
      await initAuth(); if (attempt !== version.current) return;
      setTenant(authTenant());
      if (!isAuthenticated()) { setAccess('anonymous'); return; }
      if (!isAdmin() && !isManager()) { setAccess('denied'); return; }
      const bearer = await refreshToken(); if (attempt !== version.current) return;
      if (!bearer) { setAccess('denied'); return; }
      const response = await fetch(`${API_URL}/api/v1/me`, { headers: { Authorization: `Bearer ${bearer}` }, cache: 'no-store' });
      if (attempt !== version.current) return;
      if ([401, 403].includes(response.status)) { setAccess('denied'); return; }
      if (!response.ok) throw new Error();
      const identity = await response.json();
      const tokenRoles = (identity.roles ?? []).flatMap((v: unknown) => { try { return typeof v === 'string' ? JSON.parse(v).roles ?? [] : []; } catch { return []; } });
      const role = identity.employee?.role;
      const allowed = identity.realm === authTenant() && (identity.employee
        ? identity.employee.isActive === true && ['admin', 'manager'].includes(role) && tokenRoles.includes(role)
        : tokenRoles.includes('admin') && isAdmin());
      if (attempt === version.current) setAccess(allowed ? 'allowed' : 'denied');
    } catch { if (attempt === version.current) setAccess('error'); }
  }, []);
  useEffect(() => { void verify(); return () => { version.current++; }; }, [verify]);
  return <main className="min-h-screen bg-gray-50 text-gray-900 p-4 sm:p-8">
    <div className="max-w-5xl mx-auto space-y-6">
      <header className="flex flex-wrap justify-between gap-4 items-center"><h1 className="text-2xl font-bold">Tenant operations</h1><nav className="flex gap-4"><Link className="underline" href={tenant ? `/account/?tenant=${encodeURIComponent(tenant)}` : '/account/'}>Account</Link><Link className="underline" href="/me/shifts/">My shifts</Link>{access !== 'anonymous' && <button className="underline" onClick={() => logout().catch(() => setAccess('error'))}>Sign out</button>}</nav></header>
      {access === 'pending' && <p role="status">Checking access…</p>}
      {access === 'anonymous' && <button className="rounded bg-gray-900 text-white px-4 py-2" onClick={() => login()}>Sign in</button>}
      {access === 'denied' && <p role="alert">Access denied. Tenant manager or administrator access is required.</p>}
      {access === 'error' && <div role="alert">Could not verify access. <button className="underline" onClick={verify}>Retry access check</button></div>}
      {access === 'allowed' && children}
    </div>
  </main>;
}
