import { Suspense } from 'react';
import ReportRecipient from './ReportRecipient';

export const metadata = { title: 'ReadySetSiivous — Quality report', robots: { index: false, follow: false }, referrer: 'no-referrer' };
export default function QualityReportPage() {
  return <Suspense fallback={<p className="p-8">Loading…</p>}><ReportRecipient /></Suspense>;
}
