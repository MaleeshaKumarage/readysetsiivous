import './globals.css';
import { MotionConfig } from 'framer-motion';
import { ThemeProvider } from '@/context/ThemeContext';
import type { Metadata } from 'next';

const SITE_NAME = 'ReadySetSiivous';
const SITE_URL = 'https://readysetsiivous.fi';
const SITE_TITLE = 'ReadySetSiivous — Professional Cleaning Services | Helsinki, Espoo, Vantaa';
const SITE_DESCRIPTION =
  'Professional home cleaning, deep cleaning, move-out cleaning, and office cleaning in Helsinki, Espoo & Vantaa. Insured, vetted staff. Book via WhatsApp in seconds.';

export const metadata: Metadata = {
  metadataBase: new URL(SITE_URL),
  title: SITE_TITLE,
  description: SITE_DESCRIPTION,
  openGraph: {
    type: 'website',
    url: SITE_URL,
    siteName: SITE_NAME,
    title: SITE_TITLE,
    description: SITE_DESCRIPTION,
    images: [
      {
        url: '/og-image.png',
        width: 1200,
        height: 630,
        alt: SITE_NAME,
      },
    ],
  },
  twitter: {
    card: 'summary_large_image',
    title: SITE_TITLE,
    description: SITE_DESCRIPTION,
    images: ['/og-image.png'],
  },
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="fi" suppressHydrationWarning>
      <body className="min-h-screen flex flex-col bg-white dark:bg-accent-950 text-gray-900 dark:text-gray-100 transition-colors">
        <MotionConfig reducedMotion="user">
          <ThemeProvider>{children}</ThemeProvider>
        </MotionConfig>
        {/* No-JS / crawler fallback: motion entrance states are opacity:0 in the static HTML;
            without JavaScript the animated content would be permanently invisible. */}
        <noscript>
          <style>{`[data-motion-reveal]{opacity:1 !important;transform:none !important;}`}</style>
        </noscript>
      </body>
    </html>
  );
}
