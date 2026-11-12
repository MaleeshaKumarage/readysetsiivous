'use client';

import Link from 'next/link';

/**
 * Two-way switch between the office (admin or manager) portal and the employee portal,
 * shown only to people who have both. One tap moves between them.
 */
export function PortalSwitch({ current, officeHref, officeLabel, tenant }: {
  current: 'office' | 'employee';
  officeHref: string;
  officeLabel: string;
  tenant: string;
}) {
  const tenantQuery = `?tenant=${encodeURIComponent(tenant)}`;
  const option = (active: boolean) => `min-h-11 inline-flex items-center justify-center rounded-full px-4 text-sm font-semibold transition focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-blue-500 ${active ? 'bg-gray-900 text-white' : 'text-gray-800 hover:bg-gray-100'}`;
  return <nav aria-label="Switch portal" className="inline-flex rounded-full border border-gray-300 bg-white p-1">
    <Link href={`${officeHref}${tenantQuery}`} aria-current={current === 'office' ? 'page' : undefined} className={option(current === 'office')}>{officeLabel}</Link>
    <Link href={`/me/shifts/${tenantQuery}`} aria-current={current === 'employee' ? 'page' : undefined} className={option(current === 'employee')}>Employee</Link>
  </nav>;
}
