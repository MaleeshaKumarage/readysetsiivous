import { Suspense } from 'react';
import SignPageClient from './SignPageClient';

// Static route: the signer token arrives via the query string (`?token=...`),
// so no dynamic segment or generateStaticParams is needed for `output: export`.
export default function SignPage() {
  return (
    <Suspense fallback={null}>
      <SignPageClient />
    </Suspense>
  );
}
