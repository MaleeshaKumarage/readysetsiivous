import type { Metadata } from 'next';
import { localizedAlternates, NOINDEX } from '@/lib/seo';

export function generateMetadata({ params }: { params: { lang: string } }): Metadata {
  return { alternates: localizedAlternates(params.lang, '/sign/'), robots: NOINDEX };
}

export default function SignLayout({ children }: { children: React.ReactNode }) {
  return children;
}
