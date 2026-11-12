'use client';
import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import Link from 'next/link';
import type { LucideIcon } from 'lucide-react';
import { Building2, CalendarClock, ClipboardList, Settings } from 'lucide-react';
import { NotificationsPanel } from '@/components/NotificationsPanel';
import { StaffInboxPanel } from '@/components/StaffInboxPanel';
import { initAuth, isAuthenticated, isAdmin, isManager, isPartner, login, logout, refreshToken, selectAuthTenant } from '@/lib/auth';
import { meApi, supportsWorkPreferences } from '@/lib/meApi';
import { API_URL, TENANT_SLUG } from '@/lib/api';

interface Account { realm: string; person?: { id: string; firstName: string; lastName: string; role: string; isActive: boolean }; company?: { id: string; name: string; isActive: boolean; isPartnerEnabled: boolean }; employee?: { id: string; firstName: string; lastName: string; role: string; isActive: boolean; identitySyncPending?: boolean } | null; }

// One portal the signed-in account can open. Tiles are large touch targets (44px+) with a visible focus ring.
function PortalTile({ href, label, icon: Icon }: { href: string; label: string; icon: LucideIcon }) {
  return <Link href={href} className="group flex min-h-[44px] items-center gap-4 rounded-xl border border-gray-200 bg-white p-5 shadow-sm transition hover:border-gray-400 hover:shadow focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-blue-500 motion-reduce:transition-none">
    <span className="flex h-12 w-12 shrink-0 items-center justify-center rounded-full bg-gray-900 text-white"><Icon size={22} aria-hidden="true" /></span>
    <span className="text-base font-semibold text-gray-900">{label}</span>
  </Link>;
}

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

  const who = account?.person ? `${account.person.firstName} ${account.person.lastName}` : account?.employee ? `${account.employee.firstName} ${account.employee.lastName}` : 'Tenant administrator';
  const roles = account ? [isAdmin() && 'Administrator', isManager() && 'Manager', isPartner() ? 'Partner' : account.employee && 'Cleaner'].filter(Boolean) as string[] : [];
  const portals = account ? [
    account.person?.role === 'partner' && { href: '/partner/', label: 'Open partner portal', icon: Building2 },
    account.employee?.isActive && !isPartner() && { href: '/me/shifts/', label: 'My shifts', icon: CalendarClock },
    account.employee?.role === 'manager' && isManager() && { href: '/manager/', label: 'Manage tenant operations', icon: ClipboardList },
    !isPartner() && isAdmin() && { href: '/admin/', label: 'Continue to administration', icon: Settings },
  ].filter(Boolean) as { href: string; label: string; icon: LucideIcon }[] : [];

  return <main className="min-h-screen bg-gray-50 text-gray-900">
    <header className="border-b border-gray-200 bg-white">
      <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-4 px-4 py-4 sm:px-6">
        <div className="min-w-0">
          <p className="text-sm text-gray-600">{account ? `${who} · ${account.realm}` : tenant}</p>
          <h1 className="text-xl font-semibold">Your ReadySetSiivous account</h1>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {roles.map(role => <span key={role} className="rounded-full border border-gray-300 px-3 py-1 text-xs font-medium text-gray-800">{role}</span>)}
          {authed && <button className="min-h-[44px] rounded-full border border-gray-400 px-4 text-sm font-medium hover:bg-gray-100 focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-blue-500" onClick={() => logout().catch(() => setError('Sign-out failed. Please retry.'))}>Sign out</button>}
        </div>
      </div>
    </header>

    <div className="mx-auto max-w-6xl space-y-8 px-4 py-6 sm:px-6 sm:py-8">
      {loading && <p role="status" className="text-gray-700">Checking your account...</p>}
      {error && <p role="alert" className="rounded-lg border border-red-300 bg-red-50 p-4 text-red-800">{error}</p>}
      {!loading && !authed && !error && <section className="rounded-xl bg-white p-6 shadow-sm sm:p-8">
        <p className="mb-4">After setting your password in the invitation email, sign in to confirm your account.</p>
        <button className="min-h-[44px] rounded-full bg-gray-900 px-6 text-white focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-blue-500" onClick={() => login().catch(() => setError('Sign-in could not be started. Please retry.'))}>Sign in</button>
      </section>}

      {account && <>
        <section aria-label="Account ready" className="space-y-4">
          <h2 className="text-xl font-medium">Account ready</h2>
          {portals.length > 0 && <nav aria-label="Choose a section" className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {portals.map(portal => <PortalTile key={portal.href} href={portal.href} label={portal.label} icon={portal.icon} />)}
          </nav>}
        </section>

        <div className="grid gap-6 lg:grid-cols-3 lg:gap-8">
          {/* Notifications come first and take the wide column, so they are the first thing staff see. */}
          <div className="order-1 space-y-6 lg:col-span-2">
            {!isPartner() && <div className="space-y-4 rounded-xl bg-white p-5 shadow-sm sm:p-6">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <h2 id="notifications-heading" className="text-lg font-semibold">Your notifications</h2>
                <a className="inline-flex min-h-[44px] items-center text-sm font-medium underline focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-blue-500" href="#notifications">Notification settings</a>
              </div>
              <StaffInboxPanel />
              <NotificationsPanel />
            </div>}
            {!account.employee && isAdmin() && <p className="rounded-xl bg-white p-5 text-sm shadow-sm sm:p-6">To receive shifts, add an Administrator profile in Employees using your verified login email, then use Invite to link that profile to your existing account.</p>}
          </div>

          {/* Profile and preferences sit beside notifications on wide screens and below them on phone. */}
          <aside aria-labelledby="profile-heading" className="order-2 space-y-6">
            <section className="space-y-2 rounded-xl bg-white p-5 shadow-sm sm:p-6">
              <h2 id="profile-heading" className="text-lg font-semibold">Profile</h2>
              <p className="font-medium">{who}</p>
              <p className="text-sm text-gray-700">Tenant: {account.realm}</p>
              {account.person?.role === 'partner' && <p className="text-sm text-gray-700">Company: {account.company?.name}</p>}
              {roles.length > 0 && <p className="text-sm text-gray-700">Roles: {roles.join(', ')}</p>}
            </section>

            {!isPartner() && supportsWorkPreferences(account) && <section aria-labelledby="preferences-heading" className="space-y-3 rounded-xl bg-white p-5 shadow-sm sm:p-6">
              <h2 id="preferences-heading" className="text-lg font-semibold">Work preferences</h2>
              {preferenceBusy && <p role="status" className="text-gray-700">{preference === null ? 'Loading work preferences...' : 'Saving work preferences...'}</p>}
              {preferenceError && <><p role="alert" className="text-red-700">{preferenceError}</p><button className="min-h-[44px] underline focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-blue-500" disabled={preferenceBusy} onClick={() => preference === null ? void loadPreference() : void savePreference()}>Retry work preferences</button></>}
              {preference !== null && <>
                <label className="flex min-h-[44px] items-center gap-3">
                  <input type="checkbox" className="h-5 w-5" checked={selected} disabled={preferenceBusy} onChange={event => { setSelected(event.target.checked); setSaved(false); }} />
                  <span>Automatically accept cleaning offers</span>
                </label>
                <p className="text-sm text-gray-700">Automatically scheduled work appears in My shifts. Resolve pending offers before enabling. Turning this off affects future offers only.</p>
                <button className="min-h-[44px] rounded-full bg-gray-900 px-5 text-white disabled:opacity-50 focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-blue-500" disabled={preferenceBusy || selected === preference} onClick={() => void savePreference()}>Save work preferences</button>
                {saved && <p role="status">Work preferences saved.</p>}
              </>}
            </section>}
          </aside>
        </div>
      </>}
    </div>
  </main>;
}
