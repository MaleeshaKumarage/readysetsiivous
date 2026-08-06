import { type Language, DEFAULT_LANGUAGE } from '@/i18n';
import Navbar from '@/components/Navbar';
import Hero from '@/components/Hero';
import EmergencyCTA from '@/components/EmergencyCTA';
import QuoteForm from '@/components/QuoteForm';
import Kotitalousvahennys from '@/components/Kotitalousvahennys';
import Services from '@/components/Services';
import PricingTransparency from '@/components/PricingTransparency';
import Checklist from '@/components/Checklist';
import KeySecurity from '@/components/KeySecurity';
import EcoPetBadges from '@/components/EcoPetBadges';
import FAQ from '@/components/FAQ';
import About from '@/components/About';
import ResponsibleEmployer from '@/components/ResponsibleEmployer';
import QRSection from '@/components/QRSection';
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
        <EmergencyCTA />
        <QuoteForm />
        <Kotitalousvahennys />
        <Services />
        <PricingTransparency />
        <Checklist />
        <KeySecurity />
        <EcoPetBadges />
        <FAQ />
        <About />
        <ResponsibleEmployer />
        <QRSection />
        <PaymentBanner />
      </main>
      <Footer />
      <FloatingWhatsApp />
    </>
  );
}
