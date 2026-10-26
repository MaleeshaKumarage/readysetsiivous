# Dependency advisory applicability follow-up

Checked 2026-10-24 against main a57c6bd and the unchanged lockfile on fix/game-retry-quota. This is a targeted disposition of the original feed hints, not a complete current dependency audit.

Refresh note, 2026-10-26: PR205 merged as d1a65ad with compatible frontend tooling dependency updates. The tables and JSON below preserve the original query evidence; they are not a post-update scan. Complete exact-version advisory disposition and dev/build/runtime reachability remain open under VERIFY-02. Immutable Actions/container pinning (HARD-03) and narrow Docker context/locked restore (HARD-04) also remain unfinished.

| Hint | Exact source evidence | Disposition |
|---|---|---|
| Next typed-array cache RCE | next14.2.35 in lockfile. Primary advisory GHSA-673q-mjm6-r5xr affects16.0.0–16.4.0 and requires cache-component behavior. | This specific advisory does not apply to the installed version. The feed bundled several Next issues under one broad range; do not treat that combined range as proof. |
| Next image optimization / sharp RCE | next14.2.35 is within the advisory's affected range; sharp0.33.5 is optional dev-only. next.config.js exports static files and sets images.unoptimized=true; deploy.yml publishes site/out. | A public image-optimization runtime is not part of the checked deployment path. No remote exploit demonstrated. Tooling/build reachability and dependency hygiene remain to be completed. |
| Next Server Actions DoS | The primary advisory requires at least one Server Action. No use-server directive was found in site/src; checked deployment is static export. | No affected server-action handler identified in the checked source/deployment path. Re-evaluate if hosting switches to a Next server or actions are added. |
| MCP SDK hints | @modelcontextprotocol/sdk1.30.0 is introduced by the shadcn4.21.0 CLI dependency. Upstream advisory list confirms reported OAuth/server/transport issues. | Not an observed application MCP server. Exact advisory applicability and unused CLI dependency removal still need verification; not a clean dependency scan. |
| undici hints | undici7.29.1 is introduced by shadcn and @dotenvx/dotenvx. Upstream list contains the reported TLS-option and cross-origin cache advisories. | No application-runtime import observed in this limited pass. Exact advisory ranges and build-tool call paths still need verification; not closed. |

Primary references:

- https://github.com/vercel/next.js/security/advisories/GHSA-673q-mjm6-r5xr
- https://github.com/vercel/next.js/security/advisories/GHSA-2464-fhr4-w9xw
- https://github.com/vercel/next.js/security/advisories/GHSA-p3m5-59mp-jv5r
- https://github.com/modelcontextprotocol/typescript-sdk/security/advisories
- https://github.com/nodejs/undici/security/advisories

The absence of a reachable public server path is a scoped source/deployment inference, not proof that every install/build environment is safe. NuGet/native transitive advisories and all four feed hints must receive final evidence before VERIFY-02 closes. Do not upgrade framework major versions solely to clear an incorrectly aggregated feed match.

## NuGet follow-up

On 2026-10-24 the pinned SDK ran `dotnet list api/CleaningSuite.Backend.sln package --vulnerable --include-transitive --format json --no-restore` against restored assets. It exited0 and reported no vulnerable package entries for all five projects. See security-nuget-advisory-evidence.json (local paths normalized; raw output retained outside the repository). This is bounded to NuGet advisory coverage and the restored dependency graph; native libraries and remaining npm/tooling cases are still open.

## Complete npm registry query

The locked graph query `npm audit --json --package-lock-only --ignore-scripts` exited1 and returned36 affected package entries (4critical,16high,10moderate,6low). See security-npm-advisory-evidence.json for every package, propagated dependency, advisory URL and proposed registry fix. This extends the original four feed hints, and keeps all entries open for triage. Some suggested fixes are major upgrades or even downgrades; do not run force-fix blindly. Storybook dev-server origin/network advisories also require local-development exposure review, even though the public website is static. No package manifests or lockfiles changed during these read-only queries.
