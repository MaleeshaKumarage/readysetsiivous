import { type Language, DEFAULT_LANGUAGE } from '@/i18n';
import Navbar from '@/components/Navbar';
import Hero from '@/components/Hero';
import QuoteForm from '@/components/QuoteForm';
import Services from '@/components/Services';
import Kotitalousvahennys from '@/components/Kotitalousvahennys';
import Checklist from '@/components/Checklist';
import FAQ from '@/components/FAQ';
import About from '@/components/About';
import PaymentBanner from '@/components/PaymentBanner';
import Footer from '@/components/Footer';
import FloatingWhatsApp from '@/components/FloatingWhatsApp';

export function generateStaticParams() {
  return [{ lang: 'fi' }, { lang: 'en' }, { lang: 'sv' }];
}

export default function LangPage({
  params,
}: {
  params: { lang: string };
}) {
  const lang = (params.lang as Language) ?? DEFAULT_LANGUAGE;

  return (
    <>
      <Navbar lang={lang} />
      <main>
        <Hero />
        <QuoteForm />
        <Kotitalousvahennys />
        <Services />
        <Checklist />
        <FAQ />
        <About />
        <PaymentBanner />
      </main>
      <Footer />
      <FloatingWhatsApp />
    </>
  );
}
