# Deployment verification implementation

1. Implement private environment backup, API-only update, bounded health and final identity verification.
2. Forward credentials through SSH envs and check out trusted main.
3. Test the actual script with isolated fake Docker/curl, including environment restoration on failures.
4. Merge after CI, deploy the built release and verify production image and health.

Full compose reconciliation remains #90. Cache retention is follow-up; no container or volume cleanup.
