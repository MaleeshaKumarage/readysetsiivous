# ReadySetSiivous — Cleaning Company Landing Page

Multi-language (EN / FI / SV), mobile-responsive, static landing page with WhatsApp lead generation. Built with **Next.js 14 + Tailwind CSS**.

## Quick Start

```bash
# Install dependencies
npm install

# Run development server
npm run dev
# → http://localhost:3000

# Build static export for production
npm run build
# → Output in ./out/ — ready for deployment
```

## Project Structure

```
src/
├── app/
│   ├── globals.css          # Tailwind + custom animations
│   ├── layout.tsx           # Root layout with LanguageProvider
│   └── page.tsx             # Main page (all sections)
├── components/
│   ├── Navbar.tsx           # Sticky nav + mobile menu + WhatsApp CTA
│   ├── Hero.tsx             # Hero section with CTAs + trust badges
│   ├── QuoteForm.tsx        # WhatsApp quote calculator
│   ├── Services.tsx         # Service cards grid
│   ├── Checklist.tsx        # Standard vs Deep comparison table
│   ├── About.tsx            # Trust & safety section
│   ├── Footer.tsx           # Areas, contact, links
│   ├── FloatingWhatsApp.tsx # Persistent floating WA button
│   ├── LanguageSwitcher.tsx # EN/FI/SV dropdown
│   └── TrustBadges.tsx      # Reusable trust badges
├── i18n/
│   ├── index.ts             # Translation engine (t, ta, to)
│   ├── en.json              # English dictionary
│   ├── fi.json              # Finnish dictionary
│   └── sv.json              # Swedish dictionary
├── context/
│   └── LanguageContext.tsx   # Language state (React Context)
├── hooks/
│   └── useLanguage.ts       # Hook: translate(), lang, setLang
└── lib/
    └── whatsapp.ts          # WhatsApp URL builder + opener
```

## Setup Before Deploy

### 1. Replace the WhatsApp phone number

Edit `src/lib/whatsapp.ts` and replace the placeholder:

```ts
const WHATSAPP_PHONE = '358468044231'; // ← current number
```

Use international format without `+` or leading zeros (e.g., `358401234567` for Finnish number `+358 40 123 4567`).

### 2. Replace contact email

Edit `src/components/Footer.tsx` and replace `info@readysetsiivous.fi` with your real email.

### 3. Customize the logo

The logo is text-based (ReadySetSiivous with green accent). To add an image logo, edit `src/components/Navbar.tsx` and `src/components/Footer.tsx`.

## Internationalization (i18n)

### Adding a new language

1. Copy `src/i18n/en.json` → `src/i18n/xx.json`
2. Translate all values (keep keys identical)
3. Register it in `src/i18n/index.ts`:

```ts
import xx from './xx.json';

export const LANGUAGES = [
  // ...
  { code: 'xx', label: 'Language Name', flag: '🇪🇺' },
];

const dictionaries = { en, fi, sv, xx };
```

### How translations work

```tsx
const { t } = useLanguage();
t('hero.headline')        // → "Professional Cleaning Services..."
t('services.home.price')  // → "From €35/h"
ta('footer.areas')        // → ["Helsinki", "Espoo", ...]
```

Pre-filled WhatsApp messages automatically switch to the site's active language.

## Free Deployment

### Vercel (recommended)

1. Push to GitHub
2. Go to [vercel.com](https://vercel.com) → Import Project
3. Vercel auto-detects Next.js — no config needed
4. **Cost:** €0/month on the Hobby plan (includes custom domain + SSL)

### Cloudflare Pages

```bash
npm run build
# Upload the ./out/ folder via Cloudflare Dashboard or Wrangler CLI
```

1. Go to [Cloudflare Pages](https://pages.cloudflare.com) → Create Project
2. Set build command: `npm run build`
3. Set output directory: `out`
4. **Cost:** €0/month (unlimited sites, custom domain + SSL)

### Manual / Any static host

After `npm run build`, the `out/` folder contains a fully static site (HTML + CSS + JS). Upload it to any static host — Netlify, GitHub Pages, S3 + CloudFront, etc.

## Tech Stack

| Layer | Choice |
|-------|--------|
| Framework | Next.js 14 (App Router) |
| Styling | Tailwind CSS 3.4 |
| Language | TypeScript |
| i18n | Custom JSON dictionary + React Context |
| Hosting | Static export → Vercel / Cloudflare Pages (€0) |
| Leads | WhatsApp Web (`wa.me` links, no backend) |

## Key Design Decisions

- **Zero backend** — no API routes, no database, no server. WhatsApp is the CRM.
- **Fully static** — `next.config.js` uses `output: 'export'`. Every page is plain HTML/CSS/JS.
- **Language-aware WhatsApp** — form messages include the user's selected language so you know which language to reply in.
- **Mobile-first** — every section adapts to mobile with full-width cards, responsive tables, and touch-friendly inputs.
