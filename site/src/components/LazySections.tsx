'use client';

import { lazy, Suspense, type ComponentType } from 'react';

// Heavy or below-the-fold sections load on demand. Each one keeps its height while loading so the page
// does not jump when the section arrives.
function deferred<P extends object>(load: () => Promise<{ default: ComponentType<P> }>, minHeight: string, anchorId?: string) {
  const Section = lazy(load) as unknown as ComponentType<P>;
  return function DeferredSection(props: P) {
    return (
      <Suspense fallback={<div id={anchorId} aria-hidden="true" className={minHeight} />}>
        <Section {...props} />
      </Suspense>
    );
  };
}

function named<P extends object, K extends string>(load: () => Promise<Record<K, ComponentType<P>>>, key: K) {
  return () => load().then(module => ({ default: module[key] }));
}

export const LazyAbout = deferred(() => import('@/components/About'), 'min-h-64', 'about');

export const LazyDashboardCharts = deferred(named(() => import('@/components/admin/DashboardCharts'), 'DashboardCharts'), 'min-h-96');
export const LazyTodayKpis = deferred(named(() => import('@/components/admin/TodayKpis'), 'TodayKpis'), 'min-h-32');
export const LazyWordComparison = deferred(named(() => import('@/app/entertainment/WordComparison'), 'WordComparison'), 'min-h-24');
