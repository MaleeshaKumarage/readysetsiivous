import type { Metadata } from 'next';
export const metadata: Metadata = {
  manifest: '/employee-manifest.json',
  appleWebApp: { capable: true, statusBarStyle: 'black-translucent', title: 'ReadySetSiivous My shifts' },
};
export default function EmployeeLayout({ children }: { children: React.ReactNode }) { return children; }
