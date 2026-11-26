'use client';

import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react';
import { cx } from './utils';

export interface MultiSelectOption {
  value: string;
  label: string;
}

export interface MultiSelectProps {
  /** Visible label above the control. */
  label: string;
  options: MultiSelectOption[];
  /** Selected values. In single mode it holds at most one value. */
  value: string[];
  onChange: (value: string[]) => void;
  /** false: pick exactly one (radio style), and the list closes after a pick. */
  multiple?: boolean;
  /** Shown when nothing is selected. */
  placeholder?: string;
  /** Small helper text under the control. */
  hint?: string;
  disabled?: boolean;
  className?: string;
}

/** Text on the closed control: the one name, or how many are selected. */
export function multiSelectSummary(options: MultiSelectOption[], value: string[], placeholder: string) {
  const chosen = options.filter(option => value.includes(option.value));
  if (chosen.length === 0) return placeholder;
  if (chosen.length === 1) return chosen[0].label;
  return `${chosen.length} selected`;
}

/**
 * Dropdown list for choosing one or several options, styled like the DS field and card.
 * Keyboard: Enter, Space or Arrow Down opens; arrows, Home and End move; Enter or Space picks; Escape or Tab closes.
 */
export function MultiSelect({
  label, options, value, onChange, multiple = true, placeholder = 'Choose', hint, disabled = false, className,
}: MultiSelectProps) {
  const id = useId();
  const labelId = `${id}-label`;
  const listId = `${id}-list`;
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const root = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const list = useRef<HTMLUListElement>(null);

  // A click anywhere else closes the list.
  useEffect(() => {
    if (!open) return;
    const onPointer = (event: MouseEvent) => {
      if (root.current && !root.current.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', onPointer);
    return () => document.removeEventListener('mousedown', onPointer);
  }, [open]);

  useEffect(() => { if (open) list.current?.focus(); }, [open]);

  const show = () => {
    if (disabled || options.length === 0) return;
    const first = options.findIndex(option => value.includes(option.value));
    setActive(first >= 0 ? first : 0);
    setOpen(true);
  };
  const close = (returnFocus: boolean) => {
    setOpen(false);
    if (returnFocus) trigger.current?.focus();
  };
  const pick = (option: MultiSelectOption) => {
    if (!multiple) {
      onChange([option.value]);
      close(true);
      return;
    }
    onChange(value.includes(option.value) ? value.filter(item => item !== option.value) : [...value, option.value]);
  };

  const onTriggerKey = (event: KeyboardEvent<HTMLButtonElement>) => {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') { event.preventDefault(); show(); }
  };
  const onListKey = (event: KeyboardEvent<HTMLUListElement>) => {
    const last = options.length - 1;
    if (event.key === 'ArrowDown') { event.preventDefault(); setActive(index => Math.min(last, index + 1)); }
    else if (event.key === 'ArrowUp') { event.preventDefault(); setActive(index => Math.max(0, index - 1)); }
    else if (event.key === 'Home') { event.preventDefault(); setActive(0); }
    else if (event.key === 'End') { event.preventDefault(); setActive(last); }
    else if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); if (options[active]) pick(options[active]); }
    else if (event.key === 'Escape') { event.preventDefault(); close(true); }
    else if (event.key === 'Tab') close(false);
  };

  return (
    <div ref={root} className={cx('relative', className)}>
      <span id={labelId} className="block mb-1.5 text-sm font-semibold text-gray-700 dark:text-gray-200">{label}</span>
      <button
        ref={trigger}
        type="button"
        disabled={disabled}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={open ? listId : undefined}
        aria-labelledby={`${labelId} ${id}-value`}
        onClick={() => (open ? close(false) : show())}
        onKeyDown={onTriggerKey}
        className="flex w-full min-h-11 items-center justify-between gap-2 rounded-xl border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-900 px-4 py-2.5 text-left text-base text-gray-900 dark:text-gray-100 focus:outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20 disabled:opacity-50 disabled:pointer-events-none"
      >
        <span id={`${id}-value`} className="truncate">{multiSelectSummary(options, value, placeholder)}</span>
        <span aria-hidden="true" className="text-xs text-gray-500">▾</span>
      </button>
      {hint && <span className="mt-1.5 block text-sm text-gray-500 dark:text-gray-400">{hint}</span>}
      {open && (
        <ul
          ref={list}
          id={listId}
          role="listbox"
          tabIndex={-1}
          aria-labelledby={labelId}
          aria-multiselectable={multiple}
          aria-activedescendant={options[active] ? `${id}-option-${active}` : undefined}
          onKeyDown={onListKey}
          className="absolute left-0 right-0 top-full z-30 mt-1.5 max-h-72 overflow-y-auto rounded-2xl border border-gray-100 dark:border-gray-700 bg-white dark:bg-gray-800 p-1.5 shadow-xl shadow-accent-900/10 focus:outline-none"
        >
          {options.map((option, index) => {
            const selected = value.includes(option.value);
            return (
              <li
                key={option.value}
                id={`${id}-option-${index}`}
                role="option"
                aria-selected={selected}
                onMouseDown={event => event.preventDefault()}
                onClick={() => { setActive(index); pick(option); }}
                className={cx(
                  'flex min-h-11 cursor-pointer items-center gap-3 rounded-xl px-3 text-sm text-gray-900 dark:text-gray-100',
                  index === active ? 'bg-gray-100 dark:bg-gray-700' : 'hover:bg-gray-50 dark:hover:bg-gray-700/60',
                )}
              >
                <span
                  aria-hidden="true"
                  className={cx(
                    'flex shrink-0 items-center justify-center border text-[11px] leading-none',
                    multiple ? 'rounded' : 'rounded-full',
                    selected ? 'border-accent-900 bg-accent-900 text-white' : 'border-accent-300 bg-white dark:bg-gray-900',
                  )}
                  style={{ width: 18, height: 18 }}
                >
                  {selected ? (multiple ? '✓' : '●') : ''}
                </span>
                {option.label}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
