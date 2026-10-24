'use client';

import { useState, useCallback, memo } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useLanguage } from '@/hooks/useLanguage';

const FAQ_KEYS = ['supplies', 'keys', 'cancellation', 'pets', 'duration', 'frequency'];

interface FAQItemProps {
  faqKey: string;
  index: number;
  isOpen: boolean;
  onToggle: (index: number) => void;
  question: string;
  answer: string;
}

/**
 * Performance Optimization:
 * FAQItem is wrapped with React.memo to prevent unnecessary re-renders of non-toggled FAQ items.
 * When openIndex changes in the parent FAQ component, only the newly opened and previously opened
 * items re-render instead of re-rendering all items in the FAQ list.
 */
const FAQItem = memo(function FAQItem({
  index,
  isOpen,
  onToggle,
  question,
  answer,
}: FAQItemProps) {
  return (
    <div className="bg-white dark:bg-gray-800 rounded-xl border border-gray-100 dark:border-gray-700 shadow-sm overflow-hidden">
      <button
        type="button"
        onClick={() => onToggle(index)}
        className="w-full flex items-center justify-between px-5 py-4 text-left transition-colors hover:bg-gray-50/80 dark:hover:bg-gray-700/50 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:ring-inset rounded-xl"
        aria-expanded={isOpen}
        aria-controls={`faq-panel-${index}`}
      >
        <span id={`faq-question-${index}`} className="text-sm sm:text-base font-semibold text-gray-900 dark:text-gray-100 pr-4">
          {question}
        </span>
        <svg
          aria-hidden="true"
          className={`w-5 h-5 shrink-0 text-gray-400 transition-transform duration-200 ${isOpen ? 'rotate-180 text-brand-600 dark:text-brand-400' : ''}`}
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
        >
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
        </svg>
      </button>
      <AnimatePresence initial={false}>
        {isOpen && (
          <motion.div
            id={`faq-panel-${index}`}
            role="region"
            aria-labelledby={`faq-question-${index}`}
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: 'auto', opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.25, ease: 'easeOut' }}
            className="overflow-hidden"
          >
            <div className="px-5 pb-4">
              <p className="text-sm text-gray-500 dark:text-gray-400 leading-relaxed">
                {answer}
              </p>
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
});

export default function FAQ() {
  const { t } = useLanguage();
  const [openIndex, setOpenIndex] = useState<number | null>(null);

  const toggle = useCallback((i: number) => {
    setOpenIndex((prev) => (prev === i ? null : i));
  }, []);

  return (
    <section id="faq" className="bg-gray-50 dark:bg-accent-900 section-padding">
      <div className="container-page">
        <div className="text-center max-w-2xl mx-auto">
          <h2 className="heading-lg text-gray-900 dark:text-gray-50">{t('faq.title')}</h2>
          <p className="mt-3 text-gray-500 dark:text-gray-400 text-lg">{t('faq.subtitle')}</p>
        </div>

        <div className="mt-10 max-w-3xl mx-auto space-y-3">
          {FAQ_KEYS.map((key, i) => (
            <FAQItem
              key={key}
              faqKey={key}
              index={i}
              isOpen={openIndex === i}
              onToggle={toggle}
              question={t(`faq.questions.${key}.q`)}
              answer={t(`faq.questions.${key}.a`)}
            />
          ))}
        </div>
      </div>
    </section>
  );
}
