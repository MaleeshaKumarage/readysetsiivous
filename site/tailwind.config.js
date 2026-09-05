/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    './src/**/*.{js,ts,jsx,tsx,mdx}',
  ],
  theme: {
    extend: {
      colors: {
        // Gold — primary accent (from the logo's #DDC067)
        brand: {
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
        },
        // Navy — secondary/base (from the logo's #0A0E1A)
        accent: {
          DEFAULT: 'hsl(var(--accent))',
          foreground: 'hsl(var(--accent-foreground))',
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
        },
        whatsapp: {
          DEFAULT: '#25D366',
          dark: '#128C7E',
          light: '#DCF8C6',
        },
        background: 'hsl(var(--background))',
        foreground: 'hsl(var(--foreground))',
        card: {
          DEFAULT: 'hsl(var(--card))',
          foreground: 'hsl(var(--card-foreground))',
        },
        popover: {
          DEFAULT: 'hsl(var(--popover))',
          foreground: 'hsl(var(--popover-foreground))',
        },
        primary: {
          DEFAULT: 'hsl(var(--primary))',
          foreground: 'hsl(var(--primary-foreground))',
        },
        secondary: {
          DEFAULT: 'hsl(var(--secondary))',
          foreground: 'hsl(var(--secondary-foreground))',
        },
        muted: {
          DEFAULT: 'hsl(var(--muted))',
          foreground: 'hsl(var(--muted-foreground))',
        },
        destructive: {
          DEFAULT: 'hsl(var(--destructive))',
          foreground: 'hsl(var(--destructive-foreground))',
        },
        border: 'hsl(var(--border))',
        input: 'hsl(var(--input))',
        ring: 'hsl(var(--ring))',
      },
      fontFamily: {
        sans: ['var(--font-inter)', 'Inter', 'system-ui', '-apple-system', 'sans-serif'],
      },
    },
  },
  darkMode: 'class',
  plugins: [],
};
