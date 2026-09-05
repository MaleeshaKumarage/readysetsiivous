'use client';

import { useCallback, useEffect, useState } from 'react';
import { adminInvoices, adminBookings, downloadInvoicePdf, type Invoice, type Booking } from '@/lib/adminApi';

export default function InvoicesAdmin() {
  const [invoices, setInvoices] = useState<Invoice[] | null>(null);
  const [completedBookings, setCompletedBookings] = useState<Booking[] | null>(null);
  const [bookingId, setBookingId] = useState('');
  const [message, setMessage] = useState('');

  const load = useCallback(async () => {
    setInvoices(await adminInvoices.list());
    const all = await adminBookings.list('?status=Completed');
    if (all) {
      setCompletedBookings(all);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  async function create() {
    if (!bookingId) return;
    setMessage((await adminInvoices.create(bookingId)) ? 'Invoice created' : 'Create failed');
    load();
  }

  return (
    <div>
      <h1 className="mb-6 text-2xl font-bold">Invoices</h1>
      {message && <p className="mb-4 text-sm text-brand">{message}</p>}

      <div className="mb-8 flex flex-wrap items-end gap-3 rounded-xl border border-border bg-card p-4">
        <label className="block">
          <span className="mb-1 block text-sm font-medium">Completed booking</span>
          <select
            value={bookingId}
            onChange={(e) => setBookingId(e.target.value)}
            className="rounded-lg border border-border bg-card px-3 py-2 text-sm"
          >
            <option value="">Select…</option>
            {completedBookings?.map((b) => (
              <option key={b.id} value={b.id}>
                {b.bookingNumber} — {b.customer.name}
              </option>
            ))}
          </select>
        </label>
        <button
          onClick={create}
          disabled={!bookingId}
          className="rounded-lg bg-brand px-4 py-2 text-sm font-semibold text-background disabled:opacity-50"
        >
          Create invoice
        </button>
      </div>

      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-border text-left text-muted-foreground">
              <th className="py-2 pr-2">Number</th>
              <th className="py-2 pr-2">Booking</th>
              <th className="py-2 pr-2">Customer</th>
              <th className="py-2 pr-2">Total</th>
              <th className="py-2 pr-2">Due</th>
              <th className="py-2 pr-2">Status</th>
              <th className="py-2">Actions</th>
            </tr>
          </thead>
          <tbody>
            {invoices?.map((inv) => (
              <tr key={inv.id} className="border-b border-border/50">
                <td className="py-2 pr-2 font-mono">{inv.invoiceNumber}</td>
                <td className="py-2 pr-2 font-mono">{inv.bookingNumber}</td>
                <td className="py-2 pr-2">{inv.customer.name}</td>
                <td className="py-2 pr-2">{inv.total.gross.toFixed(2)} €</td>
                <td className="py-2 pr-2">{inv.dueDate.slice(0, 10)}</td>
                <td className="py-2 pr-2">{inv.status}</td>
                <td className="py-2">
                  <div className="flex flex-wrap gap-1">
                    <button
                      onClick={() => downloadInvoicePdf(inv.id)}
                      className="rounded bg-brand px-2 py-1 text-xs font-semibold text-background"
                    >
                      PDF
                    </button>
                    {inv.status === 'Issued' && (
                      <>
                        <button
                          onClick={async () => {
                            await adminInvoices.markPaid(inv.id);
                            load();
                          }}
                          className="rounded bg-green-600 px-2 py-1 text-xs font-semibold text-white"
                        >
                          paid
                        </button>
                        <button
                          onClick={async () => {
                            await adminInvoices.void(inv.id);
                            load();
                          }}
                          className="rounded bg-red-600 px-2 py-1 text-xs font-semibold text-white"
                        >
                          void
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
