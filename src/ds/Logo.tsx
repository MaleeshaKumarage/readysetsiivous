import type { HTMLAttributes } from 'react';
import { cx } from './utils';

export type LogoSize = 'sm' | 'md' | 'lg';

const logoSizes: Record<LogoSize, string> = {
  sm: 'w-8 h-8',
  md: 'w-10 h-10',
  lg: 'w-14 h-14',
};

const wordmarkSizes: Record<LogoSize, string> = {
  sm: 'text-base',
  md: 'text-lg sm:text-xl',
  lg: 'text-2xl',
};

export interface LogoProps extends HTMLAttributes<HTMLDivElement> {
  size?: LogoSize;
  /** Render the ReadySetSiivous wordmark next to the mark. */
  withWordmark?: boolean;
}

/**
 * Brand logo — the round navy/gold mark from public/logo.png plus the
 * wordmark with its gold "Siivous" accent.
 */
export function Logo({ size = 'md', withWordmark = true, className, ...rest }: LogoProps) {
  return (
    <div className={cx('flex items-center gap-2', className)} {...rest}>
      {/* The logo mark itself sits on a light disc so it reads on dark navy too. */}
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img
        src="/logo.png"
        alt="ReadySetSiivous logo"
        className={cx(
          'rounded-full bg-white p-1 ring-1 ring-gray-200 dark:ring-gray-700 shrink-0',
          logoSizes[size],
        )}
      />
      {withWordmark && (
        <span
          translate="no"
          className={cx(
            'font-extrabold text-gray-900 dark:text-gray-50 tracking-tight whitespace-nowrap',
            wordmarkSizes[size],
          )}
        >
          ReadySet<span className="text-brand-600 dark:text-brand-400">Siivous</span>
        </span>
      )}
    </div>
  );
}
