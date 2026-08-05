import Navbar from '@/components/Navbar';
import Hero from '@/components/Hero';
import QuoteForm from '@/components/QuoteForm';
import Services from '@/components/Services';
import Checklist from '@/components/Checklist';
import About from '@/components/About';
import Footer from '@/components/Footer';
import FloatingWhatsApp from '@/components/FloatingWhatsApp';

export default function HomePage() {
  return (
    <>
      <Navbar />
      <main>
        <Hero />
        <QuoteForm />
        <Services />
        <Checklist />
        <About />
      </main>
      <Footer />
      <FloatingWhatsApp />
    </>
  );
}
