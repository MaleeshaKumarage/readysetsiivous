import { redirect } from 'next/navigation';
import type { Metadata } from 'next';
import { NOINDEX } from '@/lib/seo';

// Legacy URL that only redirects: keep it out of the index.
export const metadata: Metadata = { robots: NOINDEX };

// Inventory now lives in the employee portal's Inventory tab; keep the old URL working.
export default function InventoryPage() {
  redirect('/me/shifts/');
}
