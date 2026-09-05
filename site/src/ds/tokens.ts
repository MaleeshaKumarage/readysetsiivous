/**
 * ReadySetSiivous design tokens — extracted from the brand logo.
 *
 * The logo's palette is deep navy ink (~#0A1741) with warm gold accents
 * (#E7CD70 / #DDC067). These are codified in tailwind.config.js as the
 * `accent` (navy) and `brand` (gold) scales; this file mirrors them as
 * plain values so docs, Storybook stories, and non-Tailwind contexts
 * have a single source of truth.
 */

export const palette = {
  /** Deep navy — the logo's dominant ink. Base scale: `accent` in Tailwind. */
  navy: {
    50: '#f3f5fa',
    100: '#e4e8f2',
    200: '#c6cee3',
    300: '#9ca9cc',
    400: '#6e7fae',
    500: '#46548b',
    600: '#32406f',
    700: '#232e54',
    800: '#18203e',
    900: '#0d1428',
    950: '#070b1a',
    /** True navy sampled from the logo. */
    logo: '#0a1741',
  },
  /** Warm gold — the logo's accent strokes. Base scale: `brand` in Tailwind. */
  gold: {
    50: '#fbf7ec',
    100: '#f5edcf',
    200: '#ebdba0',
    300: '#e0c56e',
    400: '#d9b95c',
    500: '#c9a340',
    600: '#9c7c26',
    700: '#7d6220',
    800: '#67501d',
    900: '#58441b',
    950: '#33270c',
    /** Light gold sampled from the logo. */
    logoLight: '#e7cd70',
    /** Base gold sampled from the logo. */
    logo: '#ddc067',
  },
  /** WhatsApp green — the site's CTA color. */
  whatsapp: {
    DEFAULT: '#25D366',
    dark: '#128C7E',
    light: '#DCF8C6',
  },
} as const;

export const fontFamily = {
  sans: "'Inter', system-ui, -apple-system, sans-serif",
} as const;

export const fontSize = {
  /** 4xl → 6xl extrabold, tracking-tight (`.heading-xl`) */
  xl: { size: '3.75rem', lineHeight: 1.1, weight: 800, className: 'heading-xl' },
  /** 3xl → 4xl bold (`.heading-lg`) */
  lg: { size: '2.25rem', lineHeight: 1.15, weight: 700, className: 'heading-lg' },
  /** 2xl → 3xl bold (`.heading-md`) */
  md: { size: '1.875rem', lineHeight: 1.25, weight: 700, className: 'heading-md' },
  /** xl semibold */
  sm: { size: '1.25rem', lineHeight: 1.4, weight: 700, className: 'text-xl font-bold' },
} as const;

export const radius = {
  sm: '0.75rem', // rounded-xl
  md: '1rem', // rounded-2xl
  lg: '1.5rem', // rounded-3xl
  full: '9999px', // rounded-full — pill buttons, badges
} as const;

export const shadow = {
  sm: '0 1px 2px 0 rgb(0 0 0 / 0.05)',
  md: '0 4px 6px -1px rgb(0 0 0 / 0.1), 0 2px 4px -2px rgb(0 0 0 / 0.1)',
  lg: '0 10px 15px -3px rgb(0 0 0 / 0.1), 0 4px 6px -4px rgb(0 0 0 / 0.1)',
  /** Brand-tinted elevation used by primary CTA buttons. */
  'navy-glow': '0 10px 25px -5px rgb(13 20 40 / 0.25)',
  'gold-glow': '0 10px 25px -5px rgb(201 163 64 / 0.35)',
} as const;
