import type { Metadata } from 'next';
import { localizedAlternates } from '@/lib/seo';

export function generateMetadata({ params }: { params: { lang: string } }): Metadata {
  return { alternates: localizedAlternates(params.lang, '/varaus/') };
}

export default function VarausLayout({ children }: { children: React.ReactNode }) {
  return children;
}
