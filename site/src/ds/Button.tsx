import { forwardRef } from 'react';
import type { ButtonHTMLAttributes } from 'react';
import { cx } from './utils';

export type ButtonVariant = 'primary' | 'gold' | 'whatsapp' | 'outline' | 'ghost';
export type ButtonSize = 'sm' | 'md' | 'lg';

export const buttonVariants: Record<ButtonVariant, string> = {
  // Deep navy from the logo — the default call to action.
  primary:
    'bg-accent-900 text-white hover:bg-accent-800 shadow-lg shadow-accent-900/20 hover:shadow-xl focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent-500',
  // Gold from the logo — navy text keeps the contrast the logo itself uses.
  gold:
    'bg-brand-500 text-accent-950 hover:bg-brand-400 shadow-lg shadow-brand-500/25 hover:shadow-xl focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500',
  // WhatsApp green — the site's established CTA accent.
  whatsapp:
    'bg-whatsapp text-white hover:bg-whatsapp-dark shadow-lg shadow-green-200 dark:shadow-green-900/30 hover:shadow-xl hover:shadow-green-300 dark:hover:shadow-green-900/40',
  outline:
    'bg-white dark:bg-transparent border border-gray-300 dark:border-gray-600 text-gray-700 dark:text-gray-200 hover:border-brand-300 dark:hover:border-brand-500 hover:text-brand-700 dark:hover:text-brand-400 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500',
  ghost:
    'text-gray-700 dark:text-gray-200 hover:bg-gray-100 dark:hover:bg-gray-800 hover:text-brand-700 dark:hover:text-brand-400 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500',
};

export const buttonSizes: Record<ButtonSize, string> = {
  sm: 'px-4 py-2 text-sm gap-1.5',
  md: 'px-6 py-3 text-base gap-2',
  lg: 'px-8 py-3.5 text-lg gap-2.5',
};

/** Pill button classes — exported so links can wear the exact same styles. */
export function buttonClasses(variant: ButtonVariant = 'primary', size: ButtonSize = 'md') {
  return cx(
    'inline-flex items-center justify-center rounded-full font-semibold transition-all',
    'hover:-translate-y-0.5 active:translate-y-0 disabled:pointer-events-none disabled:opacity-50',
    buttonVariants[variant],
    buttonSizes[size],
  );
}

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  fullWidth?: boolean;
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { variant = 'primary', size = 'md', fullWidth = false, className, type = 'button', ...rest },
  ref,
) {
  return (
    <button
      ref={ref}
      type={type}
      className={cx(buttonClasses(variant, size), fullWidth && 'w-full', className)}
      {...rest}
    />
  );
});
