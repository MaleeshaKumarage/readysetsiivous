import { Suspense } from 'react';
import SignPageClient from './SignPageClient';

export default function SignPage() {
  return (
    <Suspense fallback={null}>
      <SignPageClient />
    </Suspense>
  );
}
