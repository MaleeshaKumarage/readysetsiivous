'use client';
import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import Link from 'next/link';
import Image from 'next/image';
import { LogOut } from 'lucide-react';
import { Button, Card } from '@/ds';
import { isPartner, logout } from '@/lib/auth';
import { NotificationsPanel } from '@/components/NotificationsPanel';
import { NotificationEventSettings } from '@/components/NotificationEventSettings';
import { meApi, supportsWorkPreferences } from '@/lib/meApi';
import { TENANT_SLUG } from '@/lib/api';
import { accountName, accountRoles, useAccountSession } from '../useAccountSession';
import { accountTheme } from '../AccountClient';
import '../../partner/partner.css';
import '../account.css';

// Profile page: the signed-in person's details, work preferences and notification settings. Reached from the Profile tile on the account page.
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

  return <main lang="en" className="partner-portal" style={accountTheme}>
    <header className="partner-header"><div className="partner-shell partner-header-inner">
      <div className="partner-brand">
        <Image src="/brand/readyset-primary-logo.png" alt="ReadySetSiivous" width={56} height={56} priority />
        <div>
          <h1>Profile</h1>
          <p><Link className="account-link" href={backHref}>Back to your account</Link></p>
        </div>
      </div>
      <div className="partner-header-actions">
        {authed && <Button variant="ghost" size="sm" className="partner-quiet" onClick={() => logout().catch(() => setError('Sign-out failed. Please retry.'))}><LogOut size={17} aria-hidden="true" />Sign out</Button>}
      </div>
    </div></header>

    <div className="partner-shell partner-main">
      {loading && <p role="status" className="partner-alert">Checking your account...</p>}
      {error && <p role="alert" className="partner-alert partner-alert-error">{error}</p>}
      {!loading && !authed && !error && <Card className="partner-surface">
        <p>Sign in from your account page to see your profile.</p>
        <Link className="account-link" href={backHref}>Go to your account</Link>
      </Card>}

      {account && <div className="account-grid">
        <div className="account-section">
          <Card className="partner-surface account-section" aria-labelledby="profile-details-heading">
            <h2 id="profile-details-heading">Details</h2>
            <p><strong>{accountName(account)}</strong></p>
            <p>Tenant: {account.realm}</p>
            {account.person?.role === 'partner' && <p>Company: {account.company?.name}</p>}
            {roles.length > 0 && <p>Roles: {roles.join(', ')}</p>}
          </Card>

          {showPreferences && <Card className="partner-surface account-section" aria-labelledby="preferences-heading">
            <h2 id="preferences-heading">Work preferences</h2>
            {preferenceBusy && <p role="status">{preference === null ? 'Loading work preferences...' : 'Saving work preferences...'}</p>}
            {preferenceError && <><p role="alert" className="partner-alert partner-alert-error">{preferenceError}</p><Button variant="ghost" size="sm" disabled={preferenceBusy} onClick={() => preference === null ? void loadPreference() : void savePreference()}>Retry work preferences</Button></>}
            {preference !== null && <>
              <label className="account-link" style={{ gap: 12 }}>
                <input type="checkbox" style={{ width: 20, height: 20 }} checked={selected} disabled={preferenceBusy} onChange={event => { setSelected(event.target.checked); setSaved(false); }} />
                <span>Automatically accept cleaning offers</span>
              </label>
              <p>Automatically scheduled work appears in My shifts. Resolve pending offers before enabling. Turning this off affects future offers only.</p>
              <Button disabled={preferenceBusy || selected === preference} onClick={() => void savePreference()}>Save work preferences</Button>
              {saved && <p role="status">Work preferences saved.</p>}
            </>}
          </Card>}
        </div>

        {!isPartner() && <Card className="partner-surface account-section">
          {/* The event settings belong to an active employee record; the API refuses them for any other login. */}
          {account?.employee?.isActive && <NotificationEventSettings />}
          <NotificationsPanel />
        </Card>}
      </div>}
    </div>
  </main>;
}
