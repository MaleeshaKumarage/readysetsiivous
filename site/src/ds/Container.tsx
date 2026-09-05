import type { HTMLAttributes } from 'react';
import { cx } from './utils';

export interface ContainerProps extends HTMLAttributes<HTMLDivElement> {}

/** Page-width wrapper — mirrors the site's `.container-page` (max-w-7xl). */
export function Container({ className, ...rest }: ContainerProps) {
  return <div className={cx('container-page', className)} {...rest} />;
}
