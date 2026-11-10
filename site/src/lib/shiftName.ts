// Automatic shift name: address | area | rooms | beds | schedule, e.g. "Kauppakatu 1, Imatra | 45m2 | 2 H | 3 V | - Daily".
// Empty address and area parts are left out, so a bare property never starts with a separator.
// Shared by the admin Add shift dialog and its tests.
export function autoShiftName(branch: { street?: string | null; city?: string | null; roomCount?: number | null; bedCount?: number | null; areaSquareMeters?: number | null } | undefined, scheduleLabel: string) {
  const address = [branch?.street, branch?.city].map(part => part?.trim() ?? '').filter(Boolean).join(', ');
  const area = branch?.areaSquareMeters != null ? `${branch.areaSquareMeters}m2` : '';
  return [address, area, `${branch?.roomCount ?? 0} H`, `${branch?.bedCount ?? 0} V`, `- ${scheduleLabel}`].filter(Boolean).join(' | ');
}
