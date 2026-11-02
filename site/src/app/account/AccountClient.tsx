'use client';
import { useEffect, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import Link from 'next/link';
import { PartnerRequestPreferences } from '@/components/PartnerRequestPreferences';
import { NotificationsPanel } from '@/components/NotificationsPanel';
import { initAuth, isAuthenticated, isAdmin, isManager, isPartner, login, logout, refreshToken, selectAuthTenant } from '@/lib/auth';
import { API_URL, TENANT_SLUG } from '@/lib/api';

interface Account { realm: string; person?: { id: string; firstName: string; lastName: string; role: string; isActive: boolean }; company?: { id: string; name: string; isActive: boolean; isPartnerEnabled: boolean }; employee?: { id: string; firstName: string; lastName: string; role: string; isActive: boolean } | null; }
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
        const response = await fetch(`${API_URL}/api/v1/${isPartner() ? 'partner/me' : 'me'}`, { headers: { Authorization: `Bearer ${jwt}` }, cache: 'no-store' });
        if (!response.ok) {
          const problem = await response.json().catch(() => ({})) as { detail?: string; title?: string };
          throw new Error(problem.detail || problem.title || 'Could not verify your account. Please try again.');
        }
        const identity = await response.json() as Account;
        if (identity.realm !== tenant) throw new Error('This login belongs to a different tenant. Sign out and open your invitation again.');
        if (isPartner() && (!identity.person?.isActive || identity.person.role !== 'partner' || !identity.company?.isActive || !identity.company.isPartnerEnabled)) throw new Error('Partner access is unavailable. Contact your tenant administrator.');
        if (!isPartner() && !identity.employee && !isAdmin()) throw new Error('Your login is not linked to an employee. Contact your tenant administrator.');
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
          <p>{account.person ? `${account.person.firstName} ${account.person.lastName}` : account.employee ? `${account.employee.firstName} ${account.employee.lastName}` : 'Tenant administrator'} · {account.realm}</p>
          {account.person?.role === 'partner' && <><p>{account.company?.name}</p><Link className="block rounded bg-gray-900 text-white px-4 py-2 text-center" href="/partner/">Open partner portal</Link></>}
          {account.employee?.isActive && !isPartner() && <Link className="block rounded bg-gray-900 text-white px-4 py-2 text-center" href="/me/shifts/">My shifts</Link>}
          {account.employee?.isActive && !isPartner() && <PartnerRequestPreferences /> }
          {!isPartner() && <><a className="block underline" href="#notifications">Notification settings</a><NotificationsPanel /></>}
          {!account.employee && isAdmin() && <p>To receive shifts, add an Administrator profile in Employees using your verified login email, then use Invite to link that profile to your existing account.</p>}
          {account.employee?.role === 'manager' && isManager() && <Link className="block underline" href="/manager/">Manage tenant operations</Link>}
          {isPartner() ? <p>Open your partner portal to manage properties and request cleaning.</p> : isAdmin() ? <Link className="underline" href="/admin/">Continue to administration</Link>
            : <p>Open My shifts to start your shift, record the activities you did, and end it.</p>}
        </>}
        {authed && <button className="underline" onClick={() => logout().catch(() => setError('Sign-out failed. Please retry.'))}>Sign out</button>}
      </>}
    </section>
  </main>;
}
