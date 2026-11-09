// Automatic shift name: address | rooms | beds | schedule, e.g. "Kauppakatu 1, Imatra | 2 H | 2 V | - One-time".
// Shared by the admin Add shift dialog and its tests so both build the same name.
export function autoShiftName(branch: { street?: string | null; city?: string | null; roomCount?: number | null; bedCount?: number | null } | undefined, scheduleLabel: string) {
  const address = [branch?.street, branch?.city].filter(Boolean).join(', ');
  return `${address} | ${branch?.roomCount ?? 0} H | ${branch?.bedCount ?? 0} V | - ${scheduleLabel}`.trim();
}
