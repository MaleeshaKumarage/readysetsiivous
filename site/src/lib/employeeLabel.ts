/**
 * The name shown for an employee in cleaner pickers and offer text. When two people share a name, the email is
 * added so the admin can tell them apart; otherwise the plain name is shown, as before.
 */
export function employeeLabel(employee: { firstName: string; lastName: string; email?: string }, all: { firstName: string; lastName: string }[]): string {
  const name = `${employee.firstName} ${employee.lastName}`;
  const sharesName = all.filter(other => `${other.firstName} ${other.lastName}` === name).length > 1;
  return sharesName && employee.email ? `${name} (${employee.email})` : name;
}
