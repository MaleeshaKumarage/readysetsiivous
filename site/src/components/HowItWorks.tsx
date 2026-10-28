'use client';
import { useLanguage } from '@/hooks/useLanguage';
import { publicUx } from '@/i18n/publicUx';

export default function HowItWorks() {
  const { lang } = useLanguage(), u = publicUx(lang);
  return <section className="section-padding"><div className="container-page">
    <h2 className="heading-lg text-center">{u.howTitle}</h2>
    <ol className="mt-8 grid gap-6 md:grid-cols-3">{u.howSteps.map((text,i) => <li key={text} className="rounded-xl border p-6"><span className="mb-3 inline-flex h-10 w-10 items-center justify-center rounded-full bg-brand-300 text-accent-950 font-bold">{i+1}</span><p>{text}</p></li>)}</ol>
  </div></section>;
}
