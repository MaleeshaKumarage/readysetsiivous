import { Suspense } from 'react';
import AccountClient from './AccountClient';
export default function AccountPage() {
  return <Suspense fallback={<p>Checking your account...</p>}><AccountClient /></Suspense>;
}
