'use client';

import { useCallback, useEffect, useState } from 'react';
import { adminGet, adminSend } from '@/lib/adminApi';

interface TenantProfile {
  companyName: string;
  businessId: string;
  email: string;
  phone: string;
  bankAccountIBAN: string;
  bankBic: string;
  timeZoneId: string;
  defaultLocale: string;
  defaultVatRatePercent: number;
  paymentTermsDays: number;
  minHoursBeforeBooking: number;
  allowUnstaffedBookings: boolean;
  companyAddress: { street: string; postalCode: string; city: string; country: string | null };
}

export default function SettingsAdmin() {
  const [profile, setProfile] = useState<TenantProfile | null>(null);
  const [message, setMessage] = useState('');
  const [pageKey, setPageKey] = useState('home');
  const [pageFi, setPageFi] = useState('');
  const [pageEn, setPageEn] = useState('');
  const [pageSv, setPageSv] = useState('');

  const load = useCallback(async () => {
    setProfile(await adminGet<TenantProfile>('/api/v1/admin/tenant'));
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  async function saveProfile() {
    if (!profile) return;
    setMessage(
      (await adminSend('/api/v1/admin/tenant', 'PUT', profile)) ? 'Saved' : 'Save failed'
    );
  }

  async function savePage() {
    setMessage(
      (await adminSend(`/api/v1/admin/tenant/pages/${pageKey}`, 'PUT', {
        fi: pageFi,
        en: pageEn,
        sv: pageSv,
      }))
        ? 'Page saved'
        : 'Page save failed'
    );
  }

  if (!profile) return <p className="text-gray-500 dark:text-gray-400">Loading…</p>;

  const set = (key: keyof TenantProfile, value: unknown) =>
    setProfile((p) => (p ? { ...p, [key]: value } : p));

  return (
    <div>
      <h1 className="mb-6 text-2xl font-bold text-gray-900 dark:text-gray-50">Tenant settings</h1>
      {message && <p className="mb-4 text-sm text-brand">{message}</p>}

      <div className="mb-8 grid gap-3 rounded-2xl border border-gray-100 dark:border-gray-700 bg-white dark:bg-gray-800 shadow-sm p-5 md:grid-cols-2">
        <Field label="Company name" value={profile.companyName} onChange={(v) => set('companyName', v)} />
        <Field label="Business id (Y-tunnus)" value={profile.businessId} onChange={(v) => set('businessId', v)} />
        <Field label="Email" value={profile.email} onChange={(v) => set('email', v)} />
        <Field label="Phone" value={profile.phone} onChange={(v) => set('phone', v)} />
        <Field label="IBAN" value={profile.bankAccountIBAN} onChange={(v) => set('bankAccountIBAN', v)} />
        <Field label="BIC" value={profile.bankBic} onChange={(v) => set('bankBic', v)} />
        <Field
          label="Street"
          value={profile.companyAddress.street}
          onChange={(v) => set('companyAddress', { ...profile.companyAddress, street: v })}
        />
        <Field
          label="Postal code"
          value={profile.companyAddress.postalCode}
          onChange={(v) => set('companyAddress', { ...profile.companyAddress, postalCode: v })}
        />
        <Field
          label="City"
          value={profile.companyAddress.city}
          onChange={(v) => set('companyAddress', { ...profile.companyAddress, city: v })}
        />
        <Field
          label="Time zone"
          value={profile.timeZoneId}
          onChange={(v) => set('timeZoneId', v)}
        />
        <Field
          label="VAT %"
          value={String(profile.defaultVatRatePercent)}
          onChange={(v) => set('defaultVatRatePercent', Number(v))}
        />
        <Field
          label="Payment terms (days)"
          value={String(profile.paymentTermsDays)}
          onChange={(v) => set('paymentTermsDays', Number(v))}
        />
        <Field
          label="Min hours before booking"
          value={String(profile.minHoursBeforeBooking)}
          onChange={(v) => set('minHoursBeforeBooking', Number(v))}
        />
        <button
          onClick={saveProfile}
          className="rounded-full bg-brand px-5 py-2.5 text-sm font-semibold text-accent-950 hover:bg-brand-400 transition-all"
        >
          Save settings
        </button>
      </div>

      <h2 className="mb-4 text-lg font-semibold text-gray-900 dark:text-gray-50">Public page content</h2>
      <div className="grid gap-3 rounded-2xl border border-gray-100 dark:border-gray-700 bg-white dark:bg-gray-800 shadow-sm p-5 md:grid-cols-2">
        <select
          value={pageKey}
          onChange={(e) => setPageKey(e.target.value)}
          className="rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 px-3 py-2 text-sm focus:border-brand-400 focus:ring-1 focus:ring-brand-400 outline-none transition-colors"
        >
          {['home', 'services', 'about', 'faq', 'footer'].map((k) => (
            <option key={k} value={k}>
              {k}
            </option>
          ))}
        </select>
        <Field label="fi" value={pageFi} onChange={setPageFi} />
        <Field label="en" value={pageEn} onChange={setPageEn} />
        <Field label="sv" value={pageSv} onChange={setPageSv} />
        <button
          onClick={savePage}
          className="rounded-full bg-brand px-5 py-2.5 text-sm font-semibold text-accent-950 hover:bg-brand-400 transition-all"
        >
          Save page
        </button>
      </div>
    </div>
  );
}

function Field({
  label,
  value,
  onChange,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
}) {
  return (
    <label className="block">
      <span className="mb-1 block text-sm font-medium">{label}</span>
      <input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="w-full rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 px-3 py-2 text-sm focus:border-brand-400 focus:ring-1 focus:ring-brand-400 outline-none transition-colors"
      />
    </label>
  );
}
