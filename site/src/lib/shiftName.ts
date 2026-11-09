// Automatic shift name: address | area | rooms | beds | schedule, e.g. "Kauppakatu 1, Imatra | 45m2 | 2 H | 3 V | - Daily".
// The area segment is left out when the property has no area. Shared by the admin Add shift dialog and its tests.
export function autoShiftName(branch: { street?: string | null; city?: string | null; roomCount?: number | null; bedCount?: number | null; areaSquareMeters?: number | null } | undefined, scheduleLabel: string) {
  const address = [branch?.street, branch?.city].filter(Boolean).join(', ');
  const area = branch?.areaSquareMeters != null ? `${branch.areaSquareMeters}m2` : null;
  return [address, area, `${branch?.roomCount ?? 0} H`, `${branch?.bedCount ?? 0} V`, `- ${scheduleLabel}`].filter(part => part !== null).join(' | ').trim();
}
