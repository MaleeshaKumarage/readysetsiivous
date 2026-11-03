'use client';

import { useId, useRef, type ReactNode, type KeyboardEvent } from 'react';

export type LifecycleStatus = 'active' | 'inactive' | 'completed';

export function inLifecycle(record: { isActive: boolean }, status: LifecycleStatus): boolean {
  if (status === 'completed') return false;
  return record.isActive === (status === 'active');
}

/**
 * Active / Inactive tabs. Pass `completedCount` to add a Completed tab between them (used where finished work
 * is kept separate from live records). Arrow keys, Home and End move between the visible tabs.
 */
export function LifecycleTabs({ value, onChange, label = 'Record activity', activeCount, inactiveCount, completedCount, disabled = false, children }: {
  value: LifecycleStatus;
  onChange: (value: LifecycleStatus) => void;
  label?: string;
  activeCount?: number;
  inactiveCount?: number;
  completedCount?: number;
  disabled?: boolean;
  children: ReactNode;
}) {
  const id = useId();
  const tabs = useRef<Partial<Record<LifecycleStatus, HTMLButtonElement | null>>>({});
  const statuses: LifecycleStatus[] = completedCount === undefined ? ['active', 'inactive'] : ['active', 'completed', 'inactive'];
  const keyboard = (event: KeyboardEvent<HTMLButtonElement>) => {
    if (disabled) return;
    const index = statuses.indexOf(value);
    let next: LifecycleStatus;
    if (event.key === 'Home') next = statuses[0];
    else if (event.key === 'End') next = statuses[statuses.length - 1];
    else if (event.key === 'ArrowLeft') next = statuses[(index - 1 + statuses.length) % statuses.length];
    else if (event.key === 'ArrowRight') next = statuses[(index + 1) % statuses.length];
    else return;
    event.preventDefault();
    if (next !== value) onChange(next);
    tabs.current[next]?.focus();
  };
  const countFor = (status: LifecycleStatus) => status === 'active' ? activeCount : status === 'completed' ? completedCount : inactiveCount;
  const nameFor = (status: LifecycleStatus) => status === 'active' ? 'Active' : status === 'completed' ? 'Completed' : 'Inactive';
  return <div>
    <div role="tablist" aria-label={label} className="flex gap-1 border-b border-gray-200 dark:border-gray-700">
      {statuses.map(status => {
        const count = countFor(status);
        return <button key={status} ref={element => { tabs.current[status] = element; }}
          type="button" role="tab" id={`${id}-${status}-tab`} aria-controls={`${id}-${status}-panel`}
          aria-selected={value === status} tabIndex={value === status ? 0 : -1} disabled={disabled}
          onKeyDown={keyboard} onClick={() => { if (status !== value) onChange(status); }}
          className={`min-h-10 px-4 py-2 border-b-2 font-medium focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-600 disabled:opacity-50 ${value === status ? 'border-blue-600 text-blue-700 dark:text-blue-300' : 'border-transparent text-gray-600 dark:text-gray-300'}`}>
          {nameFor(status)}{count === undefined ? '' : ` (${count})`}
        </button>;
      })}
    </div>
    {statuses.map(status => <section key={status} role="tabpanel"
      id={`${id}-${status}-panel`} aria-labelledby={`${id}-${status}-tab`} hidden={value !== status} className="pt-4">
      {value === status ? children : null}
    </section>)}
  </div>;
}
