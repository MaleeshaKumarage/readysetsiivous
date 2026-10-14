import { authorizedFetch, invalidateAdminCache } from './adminApi';

export async function lifecycleRequest(path: string): Promise<void> {
  const response = await authorizedFetch(path, { method: 'POST' });
  if (!response.ok) {
    let message = `Could not change activity (${response.status}). Please retry.`;
    try {
      const problem = await response.json() as { detail?: string; title?: string };
      message = problem.detail || problem.title || message;
    } catch { /* Keep the useful fallback for non-JSON responses. */ }
    throw new Error(message);
  }
  invalidateAdminCache();
}

export const companyLifecycle = {
  reactivate: (id: string) => lifecycleRequest(`/api/v1/admin/companies/${id}/reactivate`),
  deactivate: (id: string) => lifecycleRequest(`/api/v1/admin/companies/${id}/deactivate`),
};
export const branchLifecycle = {
  reactivate: (id: string) => lifecycleRequest(`/api/v1/admin/companies/branches/${id}/reactivate`),
  deactivate: (id: string) => lifecycleRequest(`/api/v1/admin/companies/branches/${id}/deactivate`),
};
