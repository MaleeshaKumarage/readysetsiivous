'use client';

import { useCallback, useEffect, useState } from 'react';
import { adminServices } from '@/lib/adminApi';

interface ServiceRow {
  id: string;
  slug: string;
  name: { values: Record<string, string> };
  description: { values: Record<string, string> };
  durationMinutes: number;
  priceNet: number;
  vatRatePercent: number;
  isActive: boolean;
  isFeatured: boolean;
  sortOrder: number;
}

const EMPTY_FIELDS = {
  slug: '',
  category: 'home',
  nameFi: '',
  nameEn: '',
  nameSv: '',
  descriptionFi: '',
  descriptionEn: '',
  descriptionSv: '',
  durationMinutes: 120,
  priceNet: 0,
  vatRatePercent: 25.5,
  isFeatured: false,
  sortOrder: 0,
};

export default function ServicesAdmin() {
  const [services, setServices] = useState<ServiceRow[] | null>(null);
  const [form, setForm] = useState(EMPTY_FIELDS);
  const [message, setMessage] = useState('');

  const load = useCallback(async () => {
    setServices(await adminServices.list(true));
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  async function save() {
    const fields = {
      slug: form.slug,
      category: form.category,
      name: { fi: form.nameFi, en: form.nameEn, sv: form.nameSv },
      description: { fi: form.descriptionFi, en: form.descriptionEn, sv: form.descriptionSv },
      durationMinutes: Number(form.durationMinutes),
      priceNet: Number(form.priceNet),
      vatRatePercent: Number(form.vatRatePercent),
      currency: 'EUR',
      isFeatured: form.isFeatured,
      sortOrder: Number(form.sortOrder),
    };
    const ok = await adminServices.create(fields);
    setMessage(ok ? 'Created' : 'Failed');
    setForm(EMPTY_FIELDS);
    load();
  }

  async function toggleActive(s: ServiceRow) {
    const ok = await adminServices.update(
      s.id,
      {
        slug: s.slug,
        category: '',
        name: s.name.values,
        description: s.description.values,
        durationMinutes: s.durationMinutes,
        priceNet: s.priceNet,
        vatRatePercent: s.vatRatePercent,
        currency: 'EUR',
        isFeatured: s.isFeatured,
        sortOrder: s.sortOrder,
      },
      !s.isActive
    );
    setMessage(ok ? 'Updated' : 'Failed');
    load();
  }

  const input = (key: keyof typeof EMPTY_FIELDS, type = 'text') => (
    <input
      type={type}
      value={form[key] as string | number}
      onChange={(e) =>
        setForm((f) => ({
          ...f,
          [key]: type === 'number' ? Number(e.target.value) : e.target.value,
        }))
      }
      className="w-full rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 px-3 py-1.5 text-sm focus:border-brand-400 focus:ring-1 focus:ring-brand-400 outline-none transition-colors"
    />
  );

  return (
    <div>
      <h1 className="mb-6 text-2xl font-bold text-gray-900 dark:text-gray-50">Services</h1>
      {message && <p className="mb-4 text-sm text-brand">{message}</p>}

      <div className="mb-8 grid gap-3 rounded-2xl border border-gray-100 dark:border-gray-700 bg-white dark:bg-gray-800 shadow-sm p-5 md:grid-cols-3">
        {input('slug')}
        {input('category')}
        {input('nameFi')}
        {input('nameEn')}
        {input('nameSv')}
        {input('descriptionFi')}
        {input('descriptionEn')}
        {input('descriptionSv')}
        {input('durationMinutes', 'number')}
        {input('priceNet', 'number')}
        {input('vatRatePercent', 'number')}
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={form.isFeatured}
            onChange={(e) => setForm((f) => ({ ...f, isFeatured: e.target.checked }))}
          />
          Featured
        </label>
        <button
          onClick={save}
          className="rounded-full bg-brand px-5 py-2.5 text-sm font-semibold text-accent-950 hover:bg-brand-400 transition-all"
        >
          Create service
        </button>
      </div>

      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-gray-100 dark:border-gray-700 text-left text-xs uppercase tracking-wide text-gray-500 dark:text-gray-400">
            <th className="py-2 pr-2">Name (fi)</th>
            <th className="py-2 pr-2">Slug</th>
            <th className="py-2 pr-2">Duration</th>
            <th className="py-2 pr-2">Price net</th>
            <th className="py-2 pr-2">Active</th>
            <th className="py-2">Action</th>
          </tr>
        </thead>
        <tbody>
          {services?.map((s) => (
            <tr key={s.id} className="border-b border-gray-100 dark:border-gray-700/60">
              <td className="py-2 pr-2">{s.name.values.fi}</td>
              <td className="py-2 pr-2">{s.slug}</td>
              <td className="py-2 pr-2">{s.durationMinutes} min</td>
              <td className="py-2 pr-2">{s.priceNet} €</td>
              <td className="py-2 pr-2">{s.isActive ? 'yes' : 'no'}</td>
              <td className="py-2">
                <button onClick={() => toggleActive(s)} className="text-sm underline">
                  {s.isActive ? 'deactivate' : 'activate'}
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
