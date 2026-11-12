'use client';
import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import Link from 'next/link';
import { isPartner, logout } from '@/lib/auth';
import { meApi, supportsWorkPreferences } from '@/lib/meApi';
import { TENANT_SLUG } from '@/lib/api';
import { accountName, accountRoles, useAccountSession } from '../useAccountSession';

// Profile page: the signed-in person's details and work preferences. Reached from the Profile tile on the account page.
export default function ProfileClient() {
  const search = useSearchParams();
  const tenant = search.get('tenant') || TENANT_SLUG;
  const { loading, authed, account, error, setError } = useAccountSession(tenant);
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

  const showPreferences = !!account && !isPartner() && supportsWorkPreferences(account);
  useEffect(() => {
    if (!showPreferences) return;
    void loadPreference();
    return () => { preferenceVersion.current++; };
    // Load once the account is known; reloading on every render would overwrite unsaved choices.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [showPreferences]);

  const roles = accountRoles(account);
  const backHref = `/account/?tenant=${encodeURIComponent(tenant)}`;

  return <main className="min-h-screen bg-gray-50 text-gray-900">
    <header className="border-b border-gray-200 bg-white">
      <div className="mx-auto flex max-w-3xl flex-wrap items-center justify-between gap-4 px-4 py-4 sm:px-6">
        <div className="min-w-0">
          <Link href={backHref} className="inline-flex min-h-[44px] items-center text-sm font-medium underline focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-blue-500">Back to your account</Link>
          <h1 className="text-xl font-semibold">Profile</h1>
        </div>
        {authed && <button className="min-h-[44px] rounded-full border border-gray-400 px-4 text-sm font-medium hover:bg-gray-100 focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-blue-500" onClick={() => logout().catch(() => setError('Sign-out failed. Please retry.'))}>Sign out</button>}
      </div>
    </header>

    <div className="mx-auto max-w-3xl space-y-6 px-4 py-6 sm:px-6 sm:py-8">
      {loading && <p role="status" className="text-gray-700">Checking your account...</p>}
      {error && <p role="alert" className="rounded-lg border border-red-300 bg-red-50 p-4 text-red-800">{error}</p>}
      {!loading && !authed && !error && <section className="rounded-xl bg-white p-6 shadow-sm">
        <p>Sign in from your account page to see your profile.</p>
        <Link href={backHref} className="mt-4 inline-flex min-h-[44px] items-center underline">Go to your account</Link>
      </section>}

      {account && <>
        <section aria-labelledby="profile-details-heading" className="space-y-2 rounded-xl bg-white p-5 shadow-sm sm:p-6">
          <h2 id="profile-details-heading" className="text-lg font-semibold">Details</h2>
          <p className="font-medium">{accountName(account)}</p>
          <p className="text-sm text-gray-700">Tenant: {account.realm}</p>
          {account.person?.role === 'partner' && <p className="text-sm text-gray-700">Company: {account.company?.name}</p>}
          {roles.length > 0 && <p className="text-sm text-gray-700">Roles: {roles.join(', ')}</p>}
        </section>

        {showPreferences && <section aria-labelledby="preferences-heading" className="space-y-3 rounded-xl bg-white p-5 shadow-sm sm:p-6">
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
      </>}
    </div>
  </main>;
}
