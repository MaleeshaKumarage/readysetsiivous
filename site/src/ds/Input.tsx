import { forwardRef } from 'react';
import type { InputHTMLAttributes, LabelHTMLAttributes, TextareaHTMLAttributes } from 'react';
import { cx } from './utils';

const fieldClasses =
  'w-full rounded-xl border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-900 ' +
  'px-4 py-2.5 text-base text-gray-900 dark:text-gray-100 placeholder:text-gray-400 ' +
  'focus:outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20 ' +
  'disabled:opacity-50 disabled:pointer-events-none transition-colors';

export const Input = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement>>(
  function Input({ className, ...rest }, ref) {
    return <input ref={ref} className={cx(fieldClasses, className)} {...rest} />;
  },
);

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaHTMLAttributes<HTMLTextAreaElement>>(
  function Textarea({ className, ...rest }, ref) {
    return <textarea ref={ref} className={cx(fieldClasses, 'min-h-28 resize-y', className)} {...rest} />;
  },
);

export interface FieldProps extends LabelHTMLAttributes<HTMLLabelElement> {
  /** Label text above the control. */
  label: string;
  /** Small helper text under the control. */
  hint?: string;
  /** Error text — turns the label and ring red. */
  error?: string;
}

/** Label + control wrapper with hint/error support. Pair with `<Input />` or `<Textarea />`. */
export function Field({ label, hint, error, className, children, ...rest }: FieldProps) {
  return (
    <label className={cx('block', className)} {...rest}>
      <span
        className={cx(
          'block mb-1.5 text-sm font-semibold',
          error ? 'text-red-600 dark:text-red-400' : 'text-gray-700 dark:text-gray-200',
        )}
      >
        {label}
      </span>
      {children}
      {error ? (
        <span className="mt-1.5 block text-sm text-red-600 dark:text-red-400">{error}</span>
      ) : hint ? (
        <span className="mt-1.5 block text-sm text-gray-500 dark:text-gray-400">{hint}</span>
      ) : null}
    </label>
  );
}
