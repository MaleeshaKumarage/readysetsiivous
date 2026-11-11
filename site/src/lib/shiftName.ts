// Shift name, the same format the server builds for every shift:
// "{Deep|Normal} Cleaning - {Street}, {PostalCode} {City} |{SizeM2}m2|{Rooms}H|{Beds}V".
// Missing counts show "-". The server is the authority; this preview matches it for the admin form.
export function autoShiftName(branch: { street?: string | null; postalCode?: string | null; city?: string | null; roomCount?: number | null; bedCount?: number | null; sizeM2?: number | null; cleaningType?: string | null } | undefined): string {
  const address = [branch?.street?.trim() ?? '', `${branch?.postalCode?.trim() ?? ''} ${branch?.city?.trim() ?? ''}`.trim()].filter(Boolean).join(', ');
  const heading = address ? `${branch?.cleaningType ?? ''} Cleaning - ${address}`.trim() : `${branch?.cleaningType ?? ''} Cleaning`.trim();
  const count = (value: number | null | undefined) => value?.toString() ?? '-';
  return `${heading} |${branch?.sizeM2 ?? ''}m2|${count(branch?.roomCount)}H|${count(branch?.bedCount)}V`;
}
