import type { Viewport } from 'next';

export const viewport: Viewport = { viewportFit: 'cover' };

export default function PartnerLayout({ children }: { children: React.ReactNode }) {
  return children;
}
