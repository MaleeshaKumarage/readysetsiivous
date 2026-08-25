import type { HTMLAttributes } from 'react';
import { cx } from './utils';

export type BadgeVariant = 'gold' | 'navy' | 'green' | 'outline' | 'subtle';
export type BadgeSize = 'sm' | 'md';

export const badgeVariants: Record<BadgeVariant, string> = {
  // Solid gold — logo accent.
  gold: 'bg-brand-500 text-accent-950',
  // Solid navy — logo ink.
  navy: 'bg-accent-900 text-white dark:bg-accent-800',
  // WhatsApp green.
  green: 'bg-whatsapp text-white',
  // Hairline outline.
  outline:
    'border border-gray-300 dark:border-gray-600 text-gray-700 dark:text-gray-200',
  // Soft gold wash — for inline labels on white.
  subtle:
    'bg-brand-50 text-brand-800 dark:bg-brand-500/10 dark:text-brand-400 border border-brand-200/60 dark:border-brand-500/20',
};

export const badgeSizes: Record<BadgeSize, string> = {
  sm: 'px-2.5 py-0.5 text-xs gap-1',
  md: 'px-3.5 py-1 text-sm gap-1.5',
};

export interface BadgeProps extends HTMLAttributes<HTMLSpanElement> {
  variant?: BadgeVariant;
  size?: BadgeSize;
}

/** Small pill label. */
export function Badge({ variant = 'subtle', size = 'sm', className, ...rest }: BadgeProps) {
  return (
    <span
      className={cx(
        'inline-flex items-center rounded-full font-semibold whitespace-nowrap',
        badgeVariants[variant],
        badgeSizes[size],
        className,
      )}
      {...rest}
    />
  );
}
