'use client';

import Link from 'next/link';
import { InventoryPanel } from '@/components/InventoryPanel';

export default function InventoryPage() {
  return (
    <main className="mx-auto w-full max-w-2xl px-4 py-6 text-gray-900 dark:text-gray-100">
      <div className="mb-6 flex items-center justify-between gap-4">
        <h1 className="text-2xl font-bold">Inventory</h1>
        <Link className="text-sm underline" href="/me/shifts/">My shifts</Link>
      </div>
      <InventoryPanel />
    </main>
  );
}
