# Versioned release compatibility groups

User approved replacing per-release rollback SHA maintenance with a reviewed
compatibility group, retaining immutable-image and deployed-version checks.

Schema-2 release policy declares approved=true, backward-compatible-reviewed,
compatibility_group=readyset-api-v1, and a legacy bootstrap allowlist containing
only verified production7ea8a064a95836e6cf3f62b65c70f76eec6a9fc7. Normal production
builds validate this policy before image build and bake the group into
APP_COMPATIBILITY_GROUP beside APP_RELEASE_SHA. Policy bytes remain authenticated
by the existing release manifest.

The controller binds the running container's release/group markers to its actual
immutable image metadata. Marked images must match the candidate policy group;
a SHA allowlist cannot override a group mismatch. Unmarked legacy images are
accepted only by the explicit bootstrap SHA. After pulling, candidate image group
must equal policy group; after replacement, container group must still match.
Rollback restores the exact prior image/config/env and verifies its prior marker
(and release health). Cleanup preserves both current and rollback images.

No compatibility-group value is accepted from a deployment environment variable.
Only validated checked-in policy feeds the build argument, and image metadata is
checked again at deployment. A malformed, missing, duplicate or mismatched marker
fails before replacement, or triggers verified rollback if observed after swap.

Breaking schema or identity changes require a new group and a separately approved
migration/cutover plan. Normal CD never bridges groups automatically. Keep narrow
schema-1 support for historical/manual controller tests, while normal new builds
require schema2. Dev's generic wrapper keeps its existing image/health verification;
production compatibility epochs are not silently imposed on test deployments.

Validate same-group deployments across different SHAs, legacy bootstrap, rejection
of other legacy SHAs/group mismatches/invalid policies/marker spoofing, build-output
binding, post-swap failures and real SIGTERM rollback. Include new group checks in
the existing synthetic Docker/curl Linux harness. No live DB/identity changes.
