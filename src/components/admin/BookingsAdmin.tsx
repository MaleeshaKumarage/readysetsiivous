'use client';

import { useCallback, useEffect, useState } from 'react';
import { adminBookings, adminEmployees, type Booking, type Employee } from '@/lib/adminApi';

export default function BookingsAdmin() {
  const [bookings, setBookings] = useState<Booking[] | null>(null);
  const [employees, setEmployees] = useState<Employee[] | null>(null);
  const [status, setStatus] = useState('');
  const [message, setMessage] = useState('');

  const load = useCallback(async () => {
    const query = status ? `?status=${encodeURIComponent(status)}` : '';
    setBookings(await adminBookings.list(query));
    setEmployees(await adminEmployees.list());
  }, [status]);

  useEffect(() => {
    load();
  }, [load]);

  async function action(id: string, fn: () => Promise<boolean>, label: string) {
    setMessage((await fn()) ? `${label} ok` : `${label} failed`);
    load();
  }

  async function assign(id: string, employeeId: string) {
    if (!employeeId) return;
    setMessage((await adminBookings.assign(id, employeeId)) ? 'Assigned' : 'Assign failed');
    load();
  }

  return (
    <div>
      <h1 className="mb-6 text-2xl font-bold">Bookings</h1>
      {message && <p className="mb-4 text-sm text-brand">{message}</p>}

      <label className="mb-6 block">
        <span className="mb-1 block text-sm font-medium">Filter by status</span>
        <select
          value={status}
          onChange={(e) => setStatus(e.target.value)}
          className="rounded-lg border border-border bg-card px-3 py-2 text-sm"
        >
          <option value="">All</option>
          <option value="New">New</option>
          <option value="Confirmed">Confirmed</option>
          <option value="Cancelled">Cancelled</option>
          <option value="Completed">Completed</option>
        </select>
      </label>

      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-border text-left text-muted-foreground">
              <th className="py-2 pr-2">Number</th>
              <th className="py-2 pr-2">When</th>
              <th className="py-2 pr-2">Customer</th>
              <th className="py-2 pr-2">Service</th>
              <th className="py-2 pr-2">Status</th>
              <th className="py-2 pr-2">Employee</th>
              <th className="py-2">Actions</th>
            </tr>
          </thead>
          <tbody>
            {bookings?.map((b) => (
              <tr key={b.id} className="border-b border-border/50 align-top">
                <td className="py-2 pr-2 font-mono">{b.bookingNumber}</td>
                <td className="py-2 pr-2 whitespace-nowrap">
                  {b.startLocalDate} {b.startLocalTime}
                </td>
                <td className="py-2 pr-2">
                  {b.customer.name}
                  <br />
                  <span className="text-muted-foreground">{b.customer.phone}</span>
                </td>
                <td className="py-2 pr-2">{b.service.nameFi}</td>
                <td className="py-2 pr-2">
                  <span
                    className={
                      'rounded-full px-2 py-0.5 text-xs ' +
                      (b.status === 'New'
                        ? 'bg-blue-500/15 text-blue-500'
                        : b.status === 'Confirmed'
                          ? 'bg-brand/15 text-brand'
                          : b.status === 'Completed'
                            ? 'bg-green-500/15 text-green-500'
                            : 'bg-red-500/15 text-red-500')
                    }
                  >
                    {b.status}
                  </span>
                </td>
                <td className="py-2 pr-2">
                  <select
                    value={''}
                    onChange={(e) => assign(b.id, e.target.value)}
                    className="rounded-lg border border-border bg-card px-2 py-1 text-xs"
                  >
                    <option value="">{b.employeeName ?? '—'}</option>
                    {employees?.map((emp) => (
                      <option key={emp.id} value={emp.id}>
                        {emp.firstName} {emp.lastName}
                      </option>
                    ))}
                    {b.employeeName && <option value="">Unassign</option>}
                  </select>
                </td>
                <td className="py-2">
                  <div className="flex flex-wrap gap-1">
                    {b.status === 'New' && (
                      <button
                        onClick={() => action(b.id, () => adminBookings.confirm(b.id), 'Confirm')}
                        className="rounded bg-brand px-2 py-1 text-xs font-semibold text-background"
                      >
                        confirm
                      </button>
                    )}
                    {b.status !== 'Cancelled' && b.status !== 'Completed' && (
                      <>
                        <button
                          onClick={() => action(b.id, () => adminBookings.complete(b.id), 'Complete')}
                          className="rounded bg-green-600 px-2 py-1 text-xs font-semibold text-white"
                        >
                          complete
                        </button>
                        <button
                          onClick={() => action(b.id, () => adminBookings.cancel(b.id), 'Cancel')}
                          className="rounded bg-red-600 px-2 py-1 text-xs font-semibold text-white"
                        >
                          cancel
                        </button>
                      </>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
