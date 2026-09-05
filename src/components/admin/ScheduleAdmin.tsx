'use client';

import { useCallback, useEffect, useState } from 'react';
import { adminGet, adminEmployees, type Booking, type Employee } from '@/lib/adminApi';

export default function ScheduleAdmin() {
  const [employees, setEmployees] = useState<Employee[] | null>(null);
  const [employeeId, setEmployeeId] = useState('');
  const [date, setDate] = useState('');
  const [bookings, setBookings] = useState<Booking[] | null>(null);

  const load = useCallback(async () => {
    if (!employeeId || !date) {
      setBookings(null);
      return;
    }
    setBookings(
      await adminGet<Booking[]>(
        `/api/v1/admin/schedule?date=${encodeURIComponent(date)}&employeeId=${encodeURIComponent(employeeId)}`
      )
    );
  }, [employeeId, date]);

  useEffect(() => {
    adminEmployees.list().then((emps) => {
      setEmployees(emps);
      if (emps && emps.length > 0) setEmployeeId(emps[0].id);
    });
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  return (
    <div>
      <h1 className="mb-6 text-2xl font-bold">Schedule</h1>
      <div className="mb-6 flex flex-wrap items-end gap-3">
        <label className="block">
          <span className="mb-1 block text-sm font-medium">Employee</span>
          <select
            value={employeeId}
            onChange={(e) => setEmployeeId(e.target.value)}
            className="rounded-lg border border-border bg-card px-3 py-2 text-sm"
          >
            <option value="">Select…</option>
            {employees?.map((e) => (
              <option key={e.id} value={e.id}>
                {e.firstName} {e.lastName}
              </option>
            ))}
          </select>
        </label>
        <label className="block">
          <span className="mb-1 block text-sm font-medium">Date</span>
          <input
            type="date"
            value={date}
            onChange={(e) => setDate(e.target.value)}
            className="rounded-lg border border-border bg-card px-3 py-2 text-sm"
          />
        </label>
      </div>

      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-border text-left text-muted-foreground">
              <th className="py-2 pr-2">Time</th>
              <th className="py-2 pr-2">Booking</th>
              <th className="py-2 pr-2">Customer</th>
              <th className="py-2 pr-2">Service</th>
              <th className="py-2">Status</th>
            </tr>
          </thead>
          <tbody>
            {bookings?.map((b) => (
              <tr key={b.id} className="border-b border-border/50">
                <td className="py-2 pr-2">{b.startLocalTime}</td>
                <td className="py-2 pr-2 font-mono">{b.bookingNumber}</td>
                <td className="py-2 pr-2">{b.customer.name}</td>
                <td className="py-2 pr-2">{b.service.nameFi}</td>
                <td className="py-2">{b.status}</td>
              </tr>
            ))}
            {bookings !== null && bookings.length === 0 && (
              <tr>
                <td colSpan={5} className="py-4 text-muted-foreground">
                  No bookings for this day.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
