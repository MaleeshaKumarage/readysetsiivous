import { authorizedFetch, invalidateAdminCache } from './adminApi';

export async function setServiceActivity(id: string, isActive: boolean): Promise<void> {
  const response = await authorizedFetch(`/api/v1/admin/services/${encodeURIComponent(id)}/${isActive ? 'reactivate' : 'deactivate'}`, { method: 'POST' });
  if (!response.ok) throw new Error(isActive ? 'Could not reactivate service. Please retry.' : 'Could not deactivate service. Please retry.');
  invalidateAdminCache();
}
