## 2025-05-18 - Optimized ShiftScheduleCalculator.HasOverlap algorithm
**Learning:** Shift occurrence overlap calculations are called during shift assignment validation. Replaced the $O(N \times M)$ nested loop with an $O(N + M)$ two-pointer interval scan after ensuring chronological sorting. `EnsureSortedByStart` scans in $O(N)$ time first to avoid unnecessary allocations if occurrences are already ordered.
**Action:** Always check if list inputs are already sorted before invoking $O(N \log N)$ sorting routines in interval arithmetic.
