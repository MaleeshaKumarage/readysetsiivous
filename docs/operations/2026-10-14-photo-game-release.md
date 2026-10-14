# Photo appendix and game fallback release

Photo appendix PR #104 merged as `bb82ad4f69c45eb2705acad5e1b52f9e66313fb8`.
It includes game sample fallback PR #106 (`d36967f81a87e9ab48886eb3cf6f53f41f1360eb`).
Combined CI run 38516508661 passed: backend 432 passed, five existing fixture
skips; frontend 196 passed. API image build 38519313711 and website deployment
38519313520 passed. The four-page sample PDF was rendered and visually checked.

On 2026-10-14 at 06:15 UTC, production API container inspection confirmed
`ghcr.io/maleeshakumarage/readysetsiivous-api:sha-bb82ad4f69c45eb2705acad5e1b52f9e66313fb8`
running, with health status `ok`. Deployment run 38520897321 succeeded after cache
recovery. Public report links remain summary-only; private notes/photos are not
published by this release.

The prior game deployment reported success despite a failed image extraction:
root disk was full from unused release images. The old healthy API stayed running.
Recovery cleared downloaded apt archives and unused API image tags after protecting
all container image IDs, the running release and two rollback releases. No
containers, volumes, database records, keys or uploaded customer files were removed.
Root usage fell from 100% to 38%, with roughly 28 GiB free.

Issue #107 adds fail-fast API-only deployment, original environment restoration
on failure, bounded health checks and exact container/image verification. Environment
restoration does not roll back an already replaced container. Manual deployment
accepts a full SHA of an already built image. Full compose reconciliation remains
#90; bounded image-cache retention remains follow-up work.
