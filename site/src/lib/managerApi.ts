import { authorizedFetch, type ShiftSchedule } from './adminApi';
export interface ManagerSchedule extends ShiftSchedule { dailyTimes?: Record<string, { start: string; end: string }> }
export interface ManagerShift { id: string; companyId: string; branchId: string; name: string; schedule: ManagerSchedule; isActive: boolean; validFrom: string | null; validUntil: string | null }
export interface ManagerEmployee { id: string; firstName: string; lastName: string; phone: string; isActive: boolean }
export interface ManagerCompany { id: string; name: string; isActive: boolean }
export interface ManagerBranch { id: string; companyId: string; name: string; isActive: boolean }
export interface ManagerAssignment { id: string; shiftId: string; employeeId: string; assignedAtUtc: string; isActive: boolean }
async function send<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const response = await authorizedFetch(`/api/v1/manager${path}`, { method, cache: 'no-store', ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(problem.detail || problem.title || 'Operation failed. Please retry.');
  }
  return response.status === 204 ? undefined as T : await response.json() as T;
}
export const managerApi = {
  timeZone: () => send<{ timeZoneId: string }>('/time-zone'),
  shifts: (includeInactive = false) => send<ManagerShift[]>(`/shifts${includeInactive ? '?includeInactive=true' : ''}`),
  employees: () => send<ManagerEmployee[]>('/employees'),
  companies: async () => {
    const result: ManagerCompany[] = [];
    for (let skip = 0; ; skip += 100) {
      const page = await send<{ items: ManagerCompany[]; total: number }>(`/companies?skip=${skip}&take=100`);
      result.push(...page.items);
      if (skip + 100 >= page.total) return result;
    }
  },
  branches: (companyId: string) => send<ManagerBranch[]>(`/companies/${companyId}/branches`),
  create: (body: unknown) => send<ManagerShift>('/shifts', 'POST', body),
  update: (id: string, body: unknown) => send<ManagerShift>(`/shifts/${id}`, 'PUT', body),
  deactivate: (id: string) => send<void>(`/shifts/${id}/deactivate`, 'POST'),
  reactivate: (id: string) => send<ManagerShift>(`/shifts/${id}/reactivate`, 'POST'),
  deleteInactive: (id: string) => send<void>(`/shifts/${id}`, 'DELETE'),
  assignments: (id: string) => send<ManagerAssignment[]>(`/shifts/${id}/assignments`),
  assign: (id: string, employeeId: string) => send<ManagerAssignment>(`/shifts/${id}/assignments`, 'POST', { employeeId }),
  unassign: (id: string, employeeId: string) => send<void>(`/shifts/${id}/assignments/${employeeId}`, 'DELETE'),
};
