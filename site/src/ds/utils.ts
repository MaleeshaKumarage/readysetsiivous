/** Join truthy class names — tiny `clsx` stand-in so the DS stays dependency-free. */
export function cx(...classes: Array<string | false | null | undefined>): string {
  return classes.filter(Boolean).join(' ');
}
