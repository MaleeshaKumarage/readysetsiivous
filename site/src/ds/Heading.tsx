import type { HTMLAttributes } from 'react';
import { cx } from './utils';

export type HeadingLevel = 'h1' | 'h2' | 'h3' | 'h4';
export type HeadingSize = 'xl' | 'lg' | 'md' | 'sm';

/** Maps to the site's `.heading-xl/lg/md` component classes in globals.css. */
const sizeClasses: Record<HeadingSize, string> = {
  xl: 'heading-xl',
  lg: 'heading-lg',
  md: 'heading-md',
  sm: 'text-xl font-bold tracking-tight',
};

export interface HeadingProps extends HTMLAttributes<HTMLHeadingElement> {
  as?: HeadingLevel;
  size?: HeadingSize;
}

/** Brand heading — extrabold, tight tracking, Inter. */
export function Heading({
  as: Tag = 'h2',
  size = 'lg',
  className,
  ...rest
}: HeadingProps) {
  return (
    <Tag
      className={cx(
        'text-gray-900 dark:text-gray-50 text-balance',
        sizeClasses[size],
        className,
      )}
      {...rest}
    />
  );
}

export interface EyebrowProps extends HTMLAttributes<HTMLSpanElement> {
  /** Show the gold dot from the logo next to the text. */
  dot?: boolean;
}

/** Small gold kicker above a heading. */
export function Eyebrow({ dot = true, className, ...rest }: EyebrowProps) {
  return (
    <span
      className={cx(
        'inline-flex items-center gap-2 text-sm font-semibold uppercase tracking-widest text-brand-600 dark:text-brand-400',
        className,
      )}
      {...rest}
    >
      {dot && <span className="w-2 h-2 rounded-full bg-brand-500" aria-hidden="true" />}
      {rest.children}
    </span>
  );
}
