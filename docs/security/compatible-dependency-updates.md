# Compatible dependency security updates

Checked 2026-10-25. The first compatible remediation updates the complete Storybook 8.6 cohort to 8.6.19, shadcn to 4.22.1 and the existing webpack override to 5.104.1, then refreshes identified compatible transitive patches. Next 14, React 18 and Tailwind 3 remain unchanged. The shadcn CSS import remains intact.

The npm registry query reports 18 affected package entries (1 critical, 6 high, 6 moderate, 5 low), down from 36. Entries include propagated dependency chains and development/build tooling; this is not a clean scan or 18 demonstrated production vulnerabilities. All original entries are tracked below and in compatible-dependency-audit.json.

| Original package | Current locked versions | Registry disposition |
|---|---|---|
| `@modelcontextprotocol/sdk` | 1.32.2 | No longer reported by this query |
| `@storybook/addon-actions` | 8.6.19 | Still reported; see remaining work below |
| `@storybook/addon-essentials` | 8.6.19 | Still reported; see remaining work below |
| `@storybook/builder-webpack5` | 8.6.19 | Still reported; see remaining work below |
| `@storybook/core` | 8.6.19 | No longer reported by this query |
| `@storybook/nextjs` | 8.6.19 | Still reported; see remaining work below |
| `body-parser` | 2.4.0 | No longer reported by this query |
| `brace-expansion` | 5.0.12, 1.1.21 | No longer reported by this query |
| `browserify-sign` | 4.2.6 | Still reported; see remaining work below |
| `create-ecdh` | 4.0.4 | Still reported; see remaining work below |
| `crypto-browserify` | 3.12.1 | Still reported; see remaining work below |
| `elliptic` | 6.6.1 | Still reported; see remaining work below |
| `fast-uri` | 3.1.9 | No longer reported by this query |
| `hono` | 4.13.13 | No longer reported by this query |
| `image-size` | 1.2.1 | Still reported; see remaining work below |
| `ip-address` | 10.7.5 | No longer reported by this query |
| `js-yaml` | 4.3.2 | No longer reported by this query |
| `nanoid` | 3.3.20 | No longer reported by this query |
| `next` | 14.2.35 | Still reported; see remaining work below |
| `node-polyfill-webpack-plugin` | 2.0.1 | Still reported; see remaining work below |
| `pbkdf2` | 3.1.7 | No longer reported by this query |
| `postcss` | 8.4.31, 8.5.25 | Still reported; see remaining work below |
| `postcss-nested` | 6.2.0 | Still reported; see remaining work below |
| `postcss-selector-parser` | 7.1.7, 6.1.4, 7.1.6 | Still reported; see remaining work below |
| `proxy-addr` | 2.0.8 | No longer reported by this query |
| `qs` | 6.16.1 | No longer reported by this query |
| `shadcn` | 4.22.1 | No longer reported by this query |
| `sharp` | 0.33.5 | Still reported; see remaining work below |
| `source-map-js` | 1.2.2 | No longer reported by this query |
| `storybook` | 8.6.19 | No longer reported by this query |
| `tailwindcss` | 3.4.19 | Still reported; see remaining work below |
| `undici` | 7.30.1 | No longer reported by this query |
| `uuid` | 9.0.1 | Still reported; see remaining work below |
| `webpack` | 5.104.1 | No longer reported by this query |
| `webpack-dev-middleware` | 6.1.3 | Still reported; see remaining work below |
| `zod` | 4.6.7 | No longer reported by this query |

## Remaining work and exposure

- Next 14.2.35 remains reported, including its nested PostCSS 8.4.31. The checked deployment exports static files with image optimization disabled; no public Next server, Server Actions, rewrites or WebSocket upgrade handler was found. This bounds the observed production exposure, not development/build safety. A supported Next migration and a verified nested PostCSS fix remain separate work.
- Storybook 8.6.19 fixes its critical origin and local-network code injection issues. Remaining Storybook chains are image-size 1.x, sharp 0.33.x, webpack-dev-middleware 6.x, uuid 9.x, and browser crypto polyfills through elliptic. Patching these requires supported parent changes or compatibility-reviewed major changes; no blanket overrides were added. No patched elliptic release is identified by the recorded advisory.
- Tailwind 3 and postcss-nested still introduce postcss-selector-parser 6.x. The 7.x copies used by CSS modules were refreshed, but forcing 7.x into 6.x consumers is outside this patch. CSS is repository-controlled in the checked build path; no user CSS compilation endpoint was found.
- Registry severity is retained even where the deployed static website has no affected server path. Developer servers and CI processing remain in scope. Run a fresh full audit after each later migration rather than assuming the present residual list remains current.

## Installation boundary and validation

The lockfile was generated with `--package-lock-only --ignore-scripts`. All resolved tarballs use the public npm registry and integrity hashes. The changed/new packages have no install lifecycle scripts. Unchanged core-js-pure, esbuild, fsevents and sharp advertise install scripts; verification initially installs with `npm ci --ignore-scripts` into this worktree's own node_modules. No shared dependency junction, global npm configuration, package-manager force fix, or production API calls are used.

Validation on Windows with Node 24.19.0/npm 11.17.0:

- `npm ci --ignore-scripts --no-audit --no-fund`: 932 packages installed into a normal, independent directory; no lifecycle scripts enabled.
- `tsc --noEmit --incremental false`: passed.
- `npm run build`: passed; all 40 static pages generated. The initial sandbox attempt could not create its generated icon output; the elevated retry completed.
- `npm run build-storybook -- --disable-telemetry --quiet`: passed with the Storybook-only wrapper described below. Bundle-size warnings remain.
- Existing public accessibility and admin installed-mode theme cases: 31 of 32 passed initially; one case traversing seven admin routes exceeded the default 30-second budget during cold compilation. That single case passed with a 90-second budget and one worker. All 32 selected cases were exercised across desktop/mobile, English/Finnish/Swedish public pages, and light/dark system themes. No test source was changed.
- `git diff --check`: passed.

The registry query exits 1 because residual advisories remain; that result is expected and is not presented as success. All 985 locked package entries have public npm registry tarballs and integrity hashes. Deprecation warnings for existing inflight, rimraf, glob and uuid remain visible; this patch does not establish comprehensive publisher or abandonment coverage.

## Storybook webpack initialization

The first Storybook build failed because Next 14 initialized its embedded webpack while Storybook loaded plugins from the separately patched local webpack. The exact installed Next 14.2.35 source at `next/dist/compiled/webpack/webpack.js` selects `require('webpack')` inside `init()` when `NEXT_PRIVATE_LOCAL_WEBPACK` is set, otherwise it loads `bundle5()`. The installed Storybook 8.6.19 manifest declares `bin/index.cjs` as its CLI, which loads presets afterward.

The small `site/scripts/run-storybook.cjs` wrapper sets that switch before loading the CLI. Both Storybook npm scripts use the wrapper; Next public build/dev scripts remain unchanged. A process-only diagnostic build succeeded first, followed by a successful normal npm build command after the wrapper was added. This uses a private Next compatibility switch and should be rechecked when Next or Storybook changes.

## Primary evidence

- [Storybook origin validation advisory and 8.6.19 patch](https://github.com/storybookjs/storybook/security/advisories/GHSA-f2ph-cmg5-wxc8)
- [Storybook local-network injection and 8.6.19 patch](https://github.com/storybookjs/storybook/security/advisories/GHSA-px85-f4q5-jh77)
- [Webpack buildHttp advisory and 5.104.1 patch](https://github.com/webpack/webpack/security/advisories/GHSA-8fgc-7cc6-rx7x)
- [PostCSS source-map disclosure advisory and 8.5.23 patch](https://github.com/postcss/postcss/security/advisories/GHSA-fxqj-rqcc-2cmp)

Public npm metadata confirms Storybook 8.6.19 supports the current Next 14/React 18 versions. shadcn 4.22.1 declares SDK ^1.32.0 and Zod ^4.2.0; the resulting graph resolves SDK 1.32.2, Zod 4.6.7 and undici 7.30.1. These are parent-supported dependency changes, not forced overrides.
