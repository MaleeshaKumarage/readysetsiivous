'use client';
import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import Link from 'next/link';
import { NotificationsPanel } from '@/components/NotificationsPanel';
import { initAuth, isAuthenticated, isAdmin, isManager, isPartner, login, logout, refreshToken, selectAuthTenant } from '@/lib/auth';
import { meApi, supportsWorkPreferences } from '@/lib/meApi';
import { API_URL, TENANT_SLUG } from '@/lib/api';

interface Account { realm: string; person?: { id: string; firstName: string; lastName: string; role: string; isActive: boolean }; company?: { id: string; name: string; isActive: boolean; isPartnerEnabled: boolean }; employee?: { id: string; firstName: string; lastName: string; role: string; isActive: boolean; identitySyncPending?: boolean } | null; }
export default function AccountClient() {
  const search = useSearchParams();
  const tenant = search.get('tenant') || TENANT_SLUG;
  const [loading, setLoading] = useState(true);
  const [authed, setAuthed] = useState(false);
  const [account, setAccount] = useState<Account | null>(null);
  const [error, setError] = useState('');
  const [preference, setPreference] = useState<boolean | null>(null);
  const [selected, setSelected] = useState(false);
  const [preferenceBusy, setPreferenceBusy] = useState(false);
  const [preferenceError, setPreferenceError] = useState('');
  const [saved, setSaved] = useState(false);
  const preferenceVersion = useRef(0);
  async function loadPreference() {
    const version = ++preferenceVersion.current;
    setPreferenceBusy(true); setPreferenceError('');
    try { const result = await meApi.workPreferences(); if (version !== preferenceVersion.current) return; setPreference(result.autoAcceptWork); setSelected(result.autoAcceptWork); }
    catch (error) { if (version !== preferenceVersion.current) return; setPreferenceError(error instanceof Error ? error.message : 'Could not load work preferences.'); }
    finally { if (version === preferenceVersion.current) setPreferenceBusy(false); }
  }
  async function savePreference() {
    const version = ++preferenceVersion.current;
    setPreferenceBusy(true); setPreferenceError(''); setSaved(false);
    try { const result = await meApi.saveWorkPreferences(selected); if (version !== preferenceVersion.current) return; setPreference(result.autoAcceptWork); setSelected(result.autoAcceptWork); setSaved(true); }
    catch (error) { if (version !== preferenceVersion.current) return; setPreferenceError(error instanceof Error ? error.message : 'Could not save work preferences.'); }
    finally { if (version === preferenceVersion.current) setPreferenceBusy(false); }
  }
  useEffect(() => {
    let cancelled = false;
    preferenceVersion.current++; setPreference(null); setSaved(false); setPreferenceError('');
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
        if (!cancelled) { setAccount(identity); if (!isPartner() && supportsWorkPreferences(identity)) void loadPreference(); }
      } catch (error) { if (!cancelled) setError(error instanceof Error ? error.message : 'Account setup could not be confirmed.'); }
      finally { if (!cancelled) setLoading(false); }
    }
    load(); return () => { cancelled = true; preferenceVersion.current++; };
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
          {!isPartner() && supportsWorkPreferences(account) && <section aria-label="Work preferences" className="space-y-3">
            <h2 className="text-xl font-medium">Work preferences</h2>
            {preferenceBusy && <p role="status">{preference === null ? 'Loading work preferences...' : 'Saving work preferences...'}</p>}
            {preferenceError && <><p role="alert" className="text-red-700">{preferenceError}</p><button className="underline" disabled={preferenceBusy} onClick={() => preference === null ? void loadPreference() : void savePreference()}>Retry work preferences</button></>}
            {preference !== null && <><label className="flex gap-2 items-center"><input type="checkbox" checked={selected} disabled={preferenceBusy} onChange={event => { setSelected(event.target.checked); setSaved(false); }} />Automatically accept cleaning offers</label>
              <p>Automatically scheduled work appears in My shifts. Resolve pending offers before enabling. Turning this off affects future offers only.</p>
              <button className="rounded bg-gray-900 text-white px-4 py-2" disabled={preferenceBusy || selected === preference} onClick={() => void savePreference()}>Save work preferences</button>
              {saved && <p role="status">Work preferences saved.</p>}</>}
          </section>}
          {account.person?.role === 'partner' && <><p>{account.company?.name}</p><Link className="block rounded bg-gray-900 text-white px-4 py-2 text-center" href="/partner/">Open partner portal</Link></>}
          {account.employee?.isActive && !isPartner() && <Link className="block rounded bg-gray-900 text-white px-4 py-2 text-center" href="/me/shifts/">My shifts</Link>}
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
