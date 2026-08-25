import { type Language, DEFAULT_LANGUAGE } from '@/i18n';
import Navbar from '@/components/Navbar';
import Hero from '@/components/Hero';
import EmergencyCTA from '@/components/EmergencyCTA';
import QuoteForm from '@/components/QuoteForm';
// import Kotitalousvahennys from '@/components/Kotitalousvahennys'; // HIDDEN 2026-08-25: "Our Prices"
import Services from '@/components/Services';
// import PricingTransparency from '@/components/PricingTransparency'; // HIDDEN 2026-08-25: "What's Included in Our Prices"
// import Checklist from '@/components/Checklist'; // HIDDEN 2026-08-25: "What's Included?"
import KeySecurity from '@/components/KeySecurity';
import EcoPetBadges from '@/components/EcoPetBadges';
import FAQ from '@/components/FAQ';
import About from '@/components/About';
import Testimonials from '@/components/Testimonials';
// import ResponsibleEmployer from '@/components/ResponsibleEmployer'; // HIDDEN 2026-08-25: "Cleaning You Can Feel Good About"
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
        {/* <Kotitalousvahennys /> */} {/* HIDDEN 2026-08-25: "Our Prices" */}
        <Services />
        {/* <PricingTransparency /> */} {/* HIDDEN 2026-08-25: "What's Included in Our Prices" */}
        {/* <Checklist /> */} {/* HIDDEN 2026-08-25: "What's Included?" */}
        <KeySecurity />
        <EcoPetBadges />
        <FAQ />
        <About />
        <Testimonials />
        {/* <ResponsibleEmployer /> */} {/* HIDDEN 2026-08-25: "Cleaning You Can Feel Good About" */}
        <QRSection />
        <PaymentBanner />
      </main>
      <Footer />
      <FloatingWhatsApp />
    </>
  );
}
