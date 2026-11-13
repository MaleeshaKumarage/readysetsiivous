'use client';

import Link from 'next/link';
import { useState } from 'react';
import { ChevronDown, LogOut, Menu } from 'lucide-react';
import { authTenant, logout } from '@/lib/auth';
import { Button } from '@/ds/Button';
import { Card } from '@/ds/Card';
import { PartnerOffers } from '@/components/PartnerOffers';
import { PwaInstallButton } from '@/components/PwaInstallButton';
import { InventoryPanel } from '@/components/InventoryPanel';

export type EmployeeSection = 'schedules' | 'offers' | 'keys' | 'inventory' | 'settings';

const SECTIONS: { id: EmployeeSection; label: string }[] = [
  { id: 'schedules', label: 'Schedules' },
  { id: 'offers', label: 'Offers' },
  { id: 'keys', label: 'Keys' },
  { id: 'inventory', label: 'Inventory' },
  { id: 'settings', label: 'Settings' },
];

/** Top-level sections of the employee portal. Arrow keys, Home and End move between tabs. */
export function EmployeeTabs({ section, onChange }: { section: EmployeeSection; onChange: (next: EmployeeSection) => void }) {
  const [menuOpen, setMenuOpen] = useState(false);
  const current = SECTIONS.find(item => item.id === section)?.label ?? '';
  return (
    <>
    {/* Phones: one button shows the current section and opens the full list. */}
    <div className="sm:hidden relative">
      <button type="button" id="employee-section-menu-button" aria-expanded={menuOpen} aria-controls="employee-section-menu"
        onClick={() => setMenuOpen(open => !open)}
        className="min-h-11 w-full flex items-center justify-between gap-3 rounded-xl bg-white/80 px-4 font-semibold text-gray-900 shadow-sm dark:bg-gray-900/60 dark:text-gray-100">
        <span className="flex items-center gap-2"><Menu size={20} aria-hidden="true" />{current}</span>
        <ChevronDown size={20} aria-hidden="true" className={menuOpen ? 'rotate-180 transition' : 'transition'} />
      </button>
      {menuOpen && <nav id="employee-section-menu" aria-label="My account sections" className="mt-2 grid gap-1 rounded-xl bg-white p-2 shadow-lg dark:bg-gray-900">
        {SECTIONS.map(item => {
          const selected = section === item.id;
          return <button key={item.id} type="button" aria-current={selected ? 'page' : undefined}
            onClick={() => { onChange(item.id); setMenuOpen(false); }}
            className={`min-h-11 rounded-lg px-4 text-left font-semibold ${selected ? 'bg-accent-900 text-white dark:bg-brand-400' : 'text-gray-900 hover:bg-gray-100 dark:text-gray-100 dark:hover:bg-gray-800'}`}>
            {item.label}
          </button>;
        })}
      </nav>}
    </div>
    {/* Larger screens: the tabs in one row. */}
    <div role="tablist" aria-label="My account sections" className="employee-tabs hidden sm:flex gap-2 rounded-xl bg-white/70 p-1 dark:bg-gray-900/60">
      {SECTIONS.map((item, index) => {
        const selected = section === item.id;
        return (
          <button
            key={item.id}
            id={`employee-tab-${item.id}`}
            type="button"
            role="tab"
            aria-selected={selected}
            aria-controls={`employee-panel-${item.id}`}
            tabIndex={selected ? 0 : -1}
            className={`min-h-11 min-w-0 whitespace-normal rounded-lg px-4 py-2 font-semibold focus-visible:outline focus-visible:outline-2 ${selected ? 'bg-accent-900 text-white dark:bg-brand-400 dark:text-accent-950' : 'text-gray-800 hover:bg-gray-100 dark:text-gray-100 dark:hover:bg-gray-800'}`}
            onClick={() => onChange(item.id)}
            onKeyDown={event => {
              let next = index;
              if (event.key === 'ArrowRight') next = (index + 1) % SECTIONS.length;
              else if (event.key === 'ArrowLeft') next = (index + SECTIONS.length - 1) % SECTIONS.length;
              else if (event.key === 'Home') next = 0;
              else if (event.key === 'End') next = SECTIONS.length - 1;
              else return;
              event.preventDefault();
              onChange(SECTIONS[next].id);
              document.getElementById(`employee-tab-${SECTIONS[next].id}`)?.focus();
            }}
          >
            {item.label}
          </button>
        );
      })}
    </div>
    </>
  );
}

export function OffersSection({ autoAcceptWork, preferenceError, onRetryPreference, onAccepted }: {
  autoAcceptWork: boolean | null;
  preferenceError: string;
  onRetryPreference: () => void;
  onAccepted: () => void;
}) {
  return (
    <div id="employee-panel-offers" role="tabpanel" aria-labelledby="employee-tab-offers" className="space-y-6">
      {preferenceError ? (
        <div className="space-y-2">
          <p role="alert" className="text-sm text-red-700 dark:text-red-300">{preferenceError}</p>
          <Button onClick={onRetryPreference}>Retry work preferences</Button>
        </div>
      ) : autoAcceptWork === null ? (
        <p role="status" className="text-sm text-gray-600 dark:text-gray-300">Checking cleaning offer preferences...</p>
      ) : null}
      {/* Offers stay visible until automatic acceptance is confirmed, so a failed preference check never hides a pending offer. */}
      {autoAcceptWork === true ? (
        <Card variant="elevated" padding="lg" className="text-center text-gray-600 dark:text-gray-300">
          Automatic acceptance is on, so offers are assigned to you directly. You can change this in Settings.
        </Card>
      ) : (
        <PartnerOffers onAccepted={onAccepted} />
      )}
    </div>
  );
}

export function InventorySection() {
  return (
    <div id="employee-panel-inventory" role="tabpanel" aria-labelledby="employee-tab-inventory" className="space-y-6">
      <InventoryPanel />
    </div>
  );
}

export function SettingsSection({ autoAcceptWork }: { autoAcceptWork: boolean | null }) {
  const account = `/account/?tenant=${encodeURIComponent(authTenant())}`;
  return (
    <div id="employee-panel-settings" role="tabpanel" aria-labelledby="employee-tab-settings" className="space-y-4">
      <Card variant="elevated" padding="lg" className="space-y-3">
        <h2 className="font-semibold text-gray-900 dark:text-gray-100">Cleaning offers</h2>
        <p className="text-sm text-gray-600 dark:text-gray-300">
          {autoAcceptWork === null ? 'Checking your preference…' : autoAcceptWork ? 'Automatic acceptance is on.' : 'Automatic acceptance is off. You review each offer yourself.'}
        </p>
        <Link className="text-sm underline text-gray-900 dark:text-gray-100" href={account}>Change work preferences in My account</Link>
      </Card>
      <Card variant="elevated" padding="lg" className="space-y-3">
        <h2 className="font-semibold text-gray-900 dark:text-gray-100">App</h2>
        <PwaInstallButton audience="employee" language="en" />
      </Card>
      <Card variant="elevated" padding="lg" className="space-y-3">
        <h2 className="font-semibold text-gray-900 dark:text-gray-100">Account</h2>
        <div className="flex flex-wrap gap-3">
          <Link className="rounded-lg border border-gray-900 px-4 py-2 text-sm text-gray-900 dark:border-gray-100 dark:text-gray-100" href={account}>My account</Link>
          <button type="button" className="notify-bell" aria-label="Sign out" title="Sign out" onClick={() => logout()}><LogOut size={22} aria-hidden="true" /></button>
        </div>
      </Card>
    </div>
  );
}
