'use client';

import { useCallback, useEffect, useState } from 'react';
import { adminEmployees, type Employee } from '@/lib/adminApi';

const EMPTY = { email: '', firstName: '', lastName: '', phone: '', role: 'employee' };

export default function EmployeesAdmin() {
  const [employees, setEmployees] = useState<Employee[] | null>(null);
  const [form, setForm] = useState(EMPTY);
  const [invite, setInvite] = useState<{ email: string; password: string } | null>(null);
  const [message, setMessage] = useState('');

  const load = useCallback(async () => {
    setEmployees(await adminEmployees.list());
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  async function create() {
    const ok = await adminEmployees.create({
      email: form.email,
      firstName: form.firstName,
      lastName: form.lastName,
      phone: form.phone,
      role: form.role,
      colorHex: null,
      defaultHours: {},
    });
    setMessage(ok ? 'Created' : 'Failed');
    setForm(EMPTY);
    load();
  }

  async function sendInvite(id: string) {
    const result = await adminEmployees.invite(id);
    if (result) setInvite({ email: result.email, password: result.temporaryPassword });
    else setMessage('Invite failed');
  }

  return (
    <div>
      <h1 className="mb-6 text-2xl font-bold text-gray-900 dark:text-gray-50">Employees</h1>
      {message && <p className="mb-4 text-sm text-brand">{message}</p>}

      {invite && (
        <div className="mb-6 rounded-xl border border-brand bg-brand/10 p-4">
          <p className="mb-1 font-semibold">Invite created for {invite.email}</p>
          <p>
            Temporary password (shown once):{' '}
            <span className="font-mono font-bold">{invite.password}</span>
          </p>
          <button onClick={() => setInvite(null)} className="mt-2 text-sm underline">
            dismiss
          </button>
        </div>
      )}

      <div className="mb-8 grid gap-3 rounded-2xl border border-gray-100 dark:border-gray-700 bg-white dark:bg-gray-800 shadow-sm p-5 md:grid-cols-3">
        <input
          placeholder="email"
          value={form.email}
          onChange={(e) => setForm((f) => ({ ...f, email: e.target.value }))}
          className="rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 px-3 py-2 text-sm focus:border-brand-400 focus:ring-1 focus:ring-brand-400 outline-none transition-colors"
        />
        <input
          placeholder="first name"
          value={form.firstName}
          onChange={(e) => setForm((f) => ({ ...f, firstName: e.target.value }))}
          className="rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 px-3 py-2 text-sm focus:border-brand-400 focus:ring-1 focus:ring-brand-400 outline-none transition-colors"
        />
        <input
          placeholder="last name"
          value={form.lastName}
          onChange={(e) => setForm((f) => ({ ...f, lastName: e.target.value }))}
          className="rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 px-3 py-2 text-sm focus:border-brand-400 focus:ring-1 focus:ring-brand-400 outline-none transition-colors"
        />
        <input
          placeholder="phone"
          value={form.phone}
          onChange={(e) => setForm((f) => ({ ...f, phone: e.target.value }))}
          className="rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 px-3 py-2 text-sm focus:border-brand-400 focus:ring-1 focus:ring-brand-400 outline-none transition-colors"
        />
        <select
          value={form.role}
          onChange={(e) => setForm((f) => ({ ...f, role: e.target.value }))}
          className="rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 px-3 py-2 text-sm focus:border-brand-400 focus:ring-1 focus:ring-brand-400 outline-none transition-colors"
        >
          <option value="employee">employee</option>
          <option value="admin">admin</option>
        </select>
        <button
          onClick={create}
          className="rounded-full bg-brand px-5 py-2.5 text-sm font-semibold text-accent-950 hover:bg-brand-400 transition-all"
        >
          Create employee
        </button>
      </div>

      <div className="overflow-x-auto rounded-2xl border border-gray-100 dark:border-gray-700 bg-white dark:bg-gray-800 shadow-sm px-5 py-2">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-100 dark:border-gray-700 text-left text-xs uppercase tracking-wide text-gray-500 dark:text-gray-400">
              <th className="py-2 pr-2">Name</th>
              <th className="py-2 pr-2">Email</th>
              <th className="py-2 pr-2">Phone</th>
              <th className="py-2 pr-2">Role</th>
              <th className="py-2 pr-2">Active</th>
              <th className="py-2">Action</th>
            </tr>
          </thead>
          <tbody>
            {employees?.map((e) => (
              <tr key={e.id} className="border-b border-gray-100 dark:border-gray-700/60">
                <td className="py-2 pr-2">
                  {e.firstName} {e.lastName}
                </td>
                <td className="py-2 pr-2">{e.email}</td>
                <td className="py-2 pr-2">{e.phone}</td>
                <td className="py-2 pr-2">{e.role}</td>
                <td className="py-2 pr-2">{e.isActive ? 'yes' : 'no'}</td>
                <td className="py-2">
                  <button onClick={() => sendInvite(e.id)} className="text-sm underline">
                    invite
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
