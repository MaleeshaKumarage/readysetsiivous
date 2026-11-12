'use client';
import { useSearchParams } from 'next/navigation';
import Link from 'next/link';
import type { LucideIcon } from 'lucide-react';
import { Building2, CalendarClock, ClipboardList, Settings, UserRound } from 'lucide-react';
import { NotificationsPanel } from '@/components/NotificationsPanel';
import { StaffInboxPanel } from '@/components/StaffInboxPanel';
import { isAdmin, isManager, isPartner, login, logout } from '@/lib/auth';
import { TENANT_SLUG } from '@/lib/api';
import { accountName, accountRoles, useAccountSession } from './useAccountSession';

// One portal or page the signed-in account can open. Tiles are large touch targets (44px+) with a visible focus ring.
function PortalTile({ href, label, icon: Icon }: { href: string; label: string; icon: LucideIcon }) {
  return <Link href={href} className="group flex min-h-[44px] items-center gap-4 rounded-xl border border-gray-200 bg-white p-5 shadow-sm transition hover:border-gray-400 hover:shadow focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-blue-500 motion-reduce:transition-none">
    <span className="flex h-12 w-12 shrink-0 items-center justify-center rounded-full bg-gray-900 text-white"><Icon size={22} aria-hidden="true" /></span>
    <span className="text-base font-semibold text-gray-900">{label}</span>
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

  return <main className="min-h-screen bg-gray-50 text-gray-900">
    <header className="border-b border-gray-200 bg-white">
      <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-4 px-4 py-4 sm:px-6">
        <div className="min-w-0">
          <p className="text-sm text-gray-600">{account ? `${accountName(account)} · ${account.realm}` : tenant}</p>
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
          <nav aria-label="Choose a section" className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {portals.map(portal => <PortalTile key={portal.href} href={portal.href} label={portal.label} icon={portal.icon} />)}
          </nav>
        </section>

        {!isPartner() && <div className="space-y-4 rounded-xl bg-white p-5 shadow-sm sm:p-6">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h2 id="notifications-heading" className="text-lg font-semibold">Your notifications</h2>
            <a className="inline-flex min-h-[44px] items-center text-sm font-medium underline focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-blue-500" href="#notifications">Notification settings</a>
          </div>
          <StaffInboxPanel />
          <NotificationsPanel />
        </div>}
        {!account.employee && isAdmin() && <p className="rounded-xl bg-white p-5 text-sm shadow-sm sm:p-6">To receive shifts, add an Administrator profile in Employees using your verified login email, then use Invite to link that profile to your existing account.</p>}
      </>}
    </div>
  </main>;
}
