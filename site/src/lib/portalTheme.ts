import type { CSSProperties } from 'react';
import { fontFamily, palette } from '@/ds';

// The partner portal colours, shared by the partner, account and office screens so they look like one product.
export const portalTheme = {
  '--partner-ink': palette.navy.logo, '--partner-muted': palette.navy[600],
  '--partner-background': palette.navy[50], '--partner-border': palette.navy[100],
  '--partner-gold': palette.gold[500], '--partner-gold-light': palette.gold[50],
  '--partner-focus': palette.gold[700],
  fontFamily: fontFamily.sans,
} as CSSProperties;
