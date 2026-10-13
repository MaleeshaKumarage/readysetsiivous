'use client';
import { useEffect, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import Link from 'next/link';
import { initAuth, isAuthenticated, isAdmin, login, logout, refreshToken, selectAuthTenant } from '@/lib/auth';
import { API_URL, TENANT_SLUG } from '@/lib/api';

interface Account { realm: string; employee?: { id: string; firstName: string; lastName: string; role: string; isActive: boolean } | null; }
export default function AccountClient() {
  const search = useSearchParams();
  const tenant = search.get('tenant') || TENANT_SLUG;
  const [loading, setLoading] = useState(true);
  const [authed, setAuthed] = useState(false);
  const [account, setAccount] = useState<Account | null>(null);
  const [error, setError] = useState('');
  useEffect(() => {
    let cancelled = false;
    setLoading(true); setAuthed(false); setAccount(null); setError('');
    async function load() {
      try {
        selectAuthTenant(tenant);
        await initAuth();
        if (cancelled) return;
        if (!isAuthenticated()) { setAuthed(false); return; }
        setAuthed(true);
        const jwt = await refreshToken();
        if (!jwt) throw new Error('Please sign in again.');
        const response = await fetch(`${API_URL}/api/v1/me`, { headers: { Authorization: `Bearer ${jwt}` } });
        if (!response.ok) {
          const problem = await response.json().catch(() => ({})) as { detail?: string; title?: string };
          throw new Error(problem.detail || problem.title || 'Could not verify your account. Please try again.');
        }
        const identity = await response.json() as Account;
        if (identity.realm !== tenant) throw new Error('This login belongs to a different tenant. Sign out and open your invitation again.');
        if (!identity.employee && !isAdmin()) throw new Error('Your login is not linked to an employee. Contact your tenant administrator.');
        if (!cancelled) setAccount(identity);
      } catch (error) { if (!cancelled) setError(error instanceof Error ? error.message : 'Account setup could not be confirmed.'); }
      finally { if (!cancelled) setLoading(false); }
    }
    load(); return () => { cancelled = true; };
  }, [tenant]);
  return <main className="min-h-screen flex items-center justify-center p-6 bg-gray-50 text-gray-900">
    <section className="w-full max-w-lg rounded-xl bg-white shadow p-6 space-y-4">
      <h1 className="text-2xl font-semibold">Your ReadySetSiivous account</h1>
      {loading ? <p role="status">Checking your account...</p> : <>
        {error && <p role="alert" className="text-red-700">{error}</p>}
        {!authed && !error && <>
          <p>After setting your password in the invitation email, sign in to confirm your account.</p>
          <button className="rounded bg-gray-900 text-white px-4 py-2" onClick={() => login().catch(() => setError('Sign-in could not be started. Please retry.'))}>Sign in</button>
        </>}
        {account && <>
          <h2 className="text-xl font-medium">Account ready</h2>
          <p>{account.employee ? `${account.employee.firstName} ${account.employee.lastName}` : 'Tenant administrator'} · {account.realm}</p>
          {isAdmin() ? <Link className="underline" href="/admin/">Continue to administration</Link>
            : <p>You can record your shift activities using the checklist links sent to your email.</p>}
        </>}
        {authed && <button className="underline" onClick={() => logout().catch(() => setError('Sign-out failed. Please retry.'))}>Sign out</button>}
      </>}
    </section>
  </main>;
}
