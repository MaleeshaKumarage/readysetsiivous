'use client';
import { useSearchParams } from 'next/navigation';
import Link from 'next/link';
import Image from 'next/image';
import type { CSSProperties } from 'react';
import type { LucideIcon } from 'lucide-react';
import { Building2, CalendarClock, ClipboardList, Settings, UserRound, LogOut } from 'lucide-react';
import { NotificationBell } from '@/components/NotificationBell';
import { Button, Card, fontFamily, palette } from '@/ds';
import { isAdmin, isManager, isPartner, login, logout } from '@/lib/auth';
import { TENANT_SLUG } from '@/lib/api';
import { accountName, accountRoles, useAccountSession } from './useAccountSession';
import '../partner/partner.css';
import './account.css';

// Same theme as the partner portal, so the account screens look like the rest of the product.
export const accountTheme = {
  '--partner-ink': palette.navy.logo, '--partner-muted': palette.navy[600],
  '--partner-background': palette.navy[50], '--partner-border': palette.navy[100],
  '--partner-gold': palette.gold[500], '--partner-gold-light': palette.gold[50],
  '--partner-focus': palette.gold[700],
  fontFamily: fontFamily.sans,
} as CSSProperties;

// One portal or page the signed-in account can open. Tiles are large touch targets (44px+) with a visible focus ring.
function PortalTile({ href, label, icon: Icon }: { href: string; label: string; icon: LucideIcon }) {
  return <Link href={href} className="account-tile">
    <span className="account-tile-icon"><Icon size={22} aria-hidden="true" /></span>
    <span>{label}</span>
  </Link>;
}

export default function AccountClient() {
  const search = useSearchParams();
  const tenant = search.get('tenant') || TENANT_SLUG;
  const { loading, authed, account, error, setError } = useAccountSession(tenant);
  const roles = accountRoles(account);
  const profileHref = `/account/profile/?tenant=${encodeURIComponent(tenant)}`;
  const portals = account ? [
    account.person?.role === 'partner' && { href: '/partner/', label: 'Open partner portal', icon: Building2 },
    account.employee?.isActive && !isPartner() && { href: '/me/shifts/', label: 'My shifts', icon: CalendarClock },
    account.employee?.role === 'manager' && isManager() && { href: '/manager/', label: 'Manage tenant operations', icon: ClipboardList },
    !isPartner() && isAdmin() && { href: '/admin/', label: 'Continue to administration', icon: Settings },
    { href: profileHref, label: 'Profile', icon: UserRound },
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
        {!isPartner() && account && <NotificationBell settingsHref={`${profileHref}#notifications`} />}
        {roles.map(role => <span key={role} className="partner-count">{role}</span>)}
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
          <nav aria-label="Choose a section" className="partner-grid">
            {portals.map(portal => <PortalTile key={portal.href} href={portal.href} label={portal.label} icon={portal.icon} />)}
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
