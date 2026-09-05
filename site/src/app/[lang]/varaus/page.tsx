'use client';

import Navbar from '@/components/Navbar';
import Footer from '@/components/Footer';
import FloatingWhatsApp from '@/components/FloatingWhatsApp';
import BookingFlow from './BookingFlow';
import { type Language, DEFAULT_LANGUAGE } from '@/i18n';

export default function VarausPage({ params }: { params: { lang: string } }) {
  const lang = (params.lang as Language) ?? DEFAULT_LANGUAGE;

  return (
    <>
      <Navbar lang={lang} />
      <main className="min-h-screen bg-white dark:bg-accent-950 pt-24 pb-16">
        <BookingFlow lang={lang} />
      </main>
      <Footer />
      <FloatingWhatsApp />
    </>
  );
}
