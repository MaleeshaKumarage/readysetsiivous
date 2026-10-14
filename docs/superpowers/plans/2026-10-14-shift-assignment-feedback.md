# Shift assignment feedback implementation

1. Add fresh, throwing shift-assignment requests scoped to this client flow.
2. Track pending/loading/error/success state and preserve failed batch choices.
3. Filter inactive/already assigned choices and guard stale responses/navigation.
4. Test desktop/mobile conflicts, retry, partial success and failure boundaries.
5. Merge passing CI and verify deployed UI. Preserve backend overlap policy.
