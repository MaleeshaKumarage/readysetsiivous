import { Suspense } from 'react';
import ProfileClient from './ProfileClient';
export default function ProfilePage() {
  return <Suspense fallback={<p>Checking your account...</p>}><ProfileClient /></Suspense>;
}
