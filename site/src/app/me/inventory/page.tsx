import { redirect } from 'next/navigation';

// Inventory now lives in the employee portal's Inventory tab; keep the old URL working.
export default function InventoryPage() {
  redirect('/me/shifts/');
}
