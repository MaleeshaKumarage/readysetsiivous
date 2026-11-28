'use client';

import { useEffect } from 'react';
import { Loader } from '@mantine/core';

/** The shift calendar and the employee schedule are now one page, the Scheduler. Old links keep their date and employee. */
export function SchedulerRedirect() {
  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const kept = new URLSearchParams();
    for (const key of ['date', 'employee']) { const value = params.get(key); if (value) kept.set(key, value); }
    const query = kept.toString();
    window.location.replace(`/admin/shifts/scheduler/${query ? `?${query}` : ''}`);
  }, []);
  return <Loader aria-label="Opening the Scheduler" />;
}
