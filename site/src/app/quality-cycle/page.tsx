import { Suspense } from 'react';
import QualityCyclePublicClient from './QualityCyclePublicClient';

export default function QualityCyclePublicPage() {
  return (
    <Suspense fallback={<div className="p-4 text-center">Loading...</div>}>
      <QualityCyclePublicClient />
    </Suspense>
  );
}
