'use client';

import { useId, useRef, type ReactNode, type KeyboardEvent } from 'react';

export type LifecycleStatus = 'active' | 'inactive';

export function inLifecycle(record: { isActive: boolean }, status: LifecycleStatus): boolean {
  return record.isActive === (status === 'active');
}

export function LifecycleTabs({ value, onChange, label = 'Record activity', activeCount, inactiveCount, disabled = false, children }: {
  value: LifecycleStatus;
  onChange: (value: LifecycleStatus) => void;
  label?: string;
  activeCount?: number;
  inactiveCount?: number;
  disabled?: boolean;
  children: ReactNode;
}) {
  const id = useId();
  const active = useRef<HTMLButtonElement>(null);
  const inactive = useRef<HTMLButtonElement>(null);
  const keyboard = (event: KeyboardEvent<HTMLButtonElement>) => {
    if (disabled) return;
    let next: LifecycleStatus;
    if (event.key === 'Home') next = 'active';
    else if (event.key === 'End') next = 'inactive';
    else if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') next = value === 'active' ? 'inactive' : 'active';
    else return;
    event.preventDefault();
    if (next !== value) onChange(next);
    (next === 'active' ? active : inactive).current?.focus();
  };
  return <div>
    <div role="tablist" aria-label={label} className="flex gap-1 border-b border-gray-200 dark:border-gray-700">
      {(['active', 'inactive'] as const).map(status => {
        const count = status === 'active' ? activeCount : inactiveCount;
        return <button key={status} ref={status === 'active' ? active : inactive}
          type="button" role="tab" id={`${id}-${status}-tab`} aria-controls={`${id}-${status}-panel`}
          aria-selected={value === status} tabIndex={value === status ? 0 : -1} disabled={disabled}
          onKeyDown={keyboard} onClick={() => { if (status !== value) onChange(status); }}
          className={`min-h-10 px-4 py-2 border-b-2 font-medium focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-600 disabled:opacity-50 ${value === status ? 'border-blue-600 text-blue-700 dark:text-blue-300' : 'border-transparent text-gray-600 dark:text-gray-300'}`}>
          {status === 'active' ? 'Active' : 'Inactive'}{count === undefined ? '' : ` (${count})`}
        </button>;
      })}
    </div>
    {(['active', 'inactive'] as const).map(status => <section key={status} role="tabpanel"
      id={`${id}-${status}-panel`} aria-labelledby={`${id}-${status}-tab`} hidden={value !== status} className="pt-4">
      {value === status ? children : null}
    </section>)}
  </div>;
}
