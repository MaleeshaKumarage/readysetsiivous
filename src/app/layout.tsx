import type { Metadata } from 'next';
import './globals.css';
import { ThemeProvider } from '@/context/ThemeContext';
import { LanguageProvider } from '@/context/LanguageContext';

export const metadata: Metadata = {
  title: 'ReadySetSiivous — Professional Cleaning Services',
  description:
    'Professional residential and commercial cleaning services in Helsinki, Espoo, Vantaa. Insured, vetted staff. Book via WhatsApp in seconds.',
  keywords: [
    'cleaning',
    'siivous',
    'städning',
    'Helsinki',
    'Espoo',
    'Vantaa',
    'home cleaning',
    'office cleaning',
    'move-out cleaning',
    'deep cleaning',
  ],
  openGraph: {
    title: 'ReadySetSiivous — Professional Cleaning Services',
    description:
      'Insured, vetted, and trusted cleaners. Get your instant quote and book in under 60 seconds.',
    type: 'website',
    locale: 'en_FI',
  },
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="fi" suppressHydrationWarning>
      <body className="min-h-screen flex flex-col bg-white dark:bg-gray-950 text-gray-900 dark:text-gray-100 transition-colors">
        <ThemeProvider>
          <LanguageProvider>{children}</LanguageProvider>
        </ThemeProvider>
      </body>
    </html>
  );
}
