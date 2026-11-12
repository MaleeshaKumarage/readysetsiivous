'use client';
import { useSearchParams } from 'next/navigation';
import Link from 'next/link';
import Image from 'next/image';
import type { LucideIcon } from 'lucide-react';
import { Building2, CalendarClock, ClipboardList, Settings, LogOut, UserRound } from 'lucide-react';
import { NotificationBell } from '@/components/NotificationBell';
import { Button, Card } from '@/ds';
import { isAdmin, isManager, isPartner, login, logout } from '@/lib/auth';
import { TENANT_SLUG } from '@/lib/api';
import { accountName, useAccountSession } from './useAccountSession';
import { portalTheme } from '@/lib/portalTheme';
import '../partner/partner.css';
import './account.css';

export const accountTheme = portalTheme;

// One portal or page the signed-in account can open. Tiles are large touch targets (44px+) with a visible focus ring.
function PortalCircle({ href, label, icon: Icon }: { href: string; label: string; icon: LucideIcon }) {
  return <Link href={href} className="account-circle">
    <span className="account-circle-icon"><Icon size={34} aria-hidden="true" /></span>
    <span>{label}</span>
  </Link>;
}

export default function AccountClient() {
  const search = useSearchParams();
  const tenant = search.get('tenant') || TENANT_SLUG;
  const { loading, authed, account, error, setError } = useAccountSession(tenant);
  const profileHref = `/account/profile/?tenant=${encodeURIComponent(tenant)}`;
  const portals = account ? [
    account.person?.role === 'partner' && { href: '/partner/', label: 'Partner portal', icon: Building2 },
    account.employee?.isActive && !isPartner() && { href: '/me/shifts/', label: 'Employee portal', icon: CalendarClock },
    account.employee?.role === 'manager' && isManager() && { href: '/manager/', label: 'Manager portal', icon: ClipboardList },
    !isPartner() && isAdmin() && { href: '/admin/', label: 'Office portal', icon: Settings },
  ].filter(Boolean) as { href: string; label: string; icon: LucideIcon }[] : [];

  return <main lang="en" className="partner-portal" style={accountTheme}>
    <header className="partner-header"><div className="partner-shell partner-header-inner">
      <div className="partner-brand">
        <Image src="/brand/readyset-primary-logo.png" alt="ReadySetSiivous" width={56} height={56} priority />
        <div>
          <h1>Your account</h1>
          <p>{account ? `${accountName(account)} · ${account.realm}` : tenant}</p>
        </div>
      </div>
      <div className="partner-header-actions">
        {account && <Link href={profileHref} className="notify-bell" aria-label="Profile" title="Profile"><UserRound size={22} aria-hidden="true" /></Link>}
        {!isPartner() && account && <NotificationBell settingsHref={`${profileHref}#notifications`} />}
        {authed && <Button variant="ghost" size="sm" className="partner-quiet" onClick={() => logout().catch(() => setError('Sign-out failed. Please retry.'))}><LogOut size={17} aria-hidden="true" />Sign out</Button>}
      </div>
    </div></header>

    <div className="partner-shell partner-main">
      {loading && <p role="status" className="partner-alert">Checking your account...</p>}
      {error && <p role="alert" className="partner-alert partner-alert-error">{error}</p>}
      {!loading && !authed && !error && <Card className="partner-surface">
        <p>After setting your password in the invitation email, sign in to confirm your account.</p>
        <Button onClick={() => login().catch(() => setError('Sign-in could not be started. Please retry.'))}>Sign in</Button>
      </Card>}

      {account && <>
        <section aria-label="Account ready" className="account-section">
          <h2>Account ready</h2>
          <nav aria-label="Choose a section" className="account-circles">
            {portals.map(portal => <PortalCircle key={portal.href} href={portal.href} label={portal.label} icon={portal.icon} />)}
          </nav>
        </section>

        <div className="account-grid">
          {!account.employee && isAdmin() && <Card className="partner-surface">
            <p>To receive shifts, add an Administrator profile in Employees using your verified login email, then use Invite to link that profile to your existing account.</p>
          </Card>}
        </div>
      </>}
    </div>
  </main>;
}
