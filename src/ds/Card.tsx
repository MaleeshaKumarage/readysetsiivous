import type { HTMLAttributes } from 'react';
import { cx } from './utils';

export type CardVariant = 'default' | 'elevated' | 'navy';
export type CardPadding = 'none' | 'sm' | 'md' | 'lg';

export const cardVariants: Record<CardVariant, string> = {
  // White surface with hairline border — the site's workhorse card.
  default: 'bg-white dark:bg-gray-800 border border-gray-100 dark:border-gray-700',
  // Floating card with soft brand-tinted shadow.
  elevated: 'bg-white dark:bg-gray-800 shadow-xl shadow-accent-900/10 dark:shadow-gray-950/50 border border-gray-100 dark:border-gray-700',
  // Navy ink card — matches the logo background.
  navy: 'bg-accent-900 dark:bg-accent-950 border border-accent-800 text-white',
};

export const cardPaddings: Record<CardPadding, string> = {
  none: '',
  sm: 'p-4',
  md: 'p-6',
  lg: 'p-8',
};

export interface CardProps extends HTMLAttributes<HTMLDivElement> {
  variant?: CardVariant;
  padding?: CardPadding;
  /** Lift on hover (works best with `elevated`). */
  hoverable?: boolean;
}

/** Rounded content surface. */
export function Card({
  variant = 'default',
  padding = 'md',
  hoverable = false,
  className,
  ...rest
}: CardProps) {
  return (
    <div
      className={cx(
        'rounded-2xl',
        cardVariants[variant],
        cardPaddings[padding],
        hoverable && 'transition-all hover:-translate-y-1 hover:shadow-2xl hover:shadow-accent-900/15',
        className,
      )}
      {...rest}
    />
  );
}
