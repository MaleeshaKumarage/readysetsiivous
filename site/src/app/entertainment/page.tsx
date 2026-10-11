import { Suspense } from 'react';
import EntertainmentClient from './EntertainmentClient';

export default function EntertainmentPage() {
  return (
    <Suspense fallback={<div className="p-4 text-center">Loading...</div>}>
      <EntertainmentClient />
    </Suspense>
  );
}
