'use client';

import { useEffect } from 'react';
import { Loader } from '@mantine/core';

/** Partner operations moved into the Work queue. Old links open it, and the old quotes tab opens the quotes. */
export default function PartnerRequestsPage() {
  useEffect(() => {
    const tab = new URLSearchParams(window.location.search).get('tab');
    window.location.replace(`/admin/work-queue/${tab === 'quotes' ? '?type=quote' : '?source=partner'}`);
  }, []);
  return <Loader aria-label="Opening the Work queue" />;
}
