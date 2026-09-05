'use client';

import { motion } from 'framer-motion';
import type { ReactNode } from 'react';

/**
 * Shared framer-motion primitives.
 *
 * All entrance states are deterministic (opacity 0 → 1) so server and client
 * render identical HTML — safe for hydration. MotionConfig reducedMotion="user"
 * in the root layout disables these for users who prefer reduced motion.
 */

/** Fade-in + slide-up when the element scrolls into view. */
export function Reveal({
  children,
  className,
  delay = 0,
  y = 24,
}: {
  children: ReactNode;
  className?: string;
  delay?: number;
  y?: number;
}) {
  return (
    <motion.div
      className={className}
      data-motion-reveal
      initial={{ opacity: 0, y }}
      whileInView={{ opacity: 1, y: 0 }}
      viewport={{ once: true, margin: '-60px' }}
      transition={{ duration: 0.6, ease: 'easeOut', delay }}
    >
      {children}
    </motion.div>
  );
}

/** Grid/list wrapper that staggers its <StaggerItem> children into view. */
export function StaggerGroup({
  children,
  className,
  delay = 0,
  stagger = 0.08,
}: {
  children: ReactNode;
  className?: string;
  delay?: number;
  stagger?: number;
}) {
  return (
    <motion.div
      className={className}
      data-motion-reveal
      initial="hidden"
      whileInView="show"
      viewport={{ once: true, margin: '-60px' }}
      variants={{
        hidden: {},
        show: { transition: { staggerChildren: stagger, delayChildren: delay } },
      }}
    >
      {children}
    </motion.div>
  );
}

/** Child of <StaggerGroup>. Optionally lifts on hover. */
export function StaggerItem({
  children,
  className,
  hover = false,
}: {
  children: ReactNode;
  className?: string;
  hover?: boolean;
}) {
  return (
    <motion.div
      className={className}
      data-motion-reveal
      variants={{
        hidden: { opacity: 0, y: 20 },
        show: { opacity: 1, y: 0, transition: { duration: 0.5, ease: 'easeOut' } },
      }}
      whileHover={hover ? { y: -6 } : undefined}
    >
      {children}
    </motion.div>
  );
}
