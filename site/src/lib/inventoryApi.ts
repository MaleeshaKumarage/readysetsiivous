import { authorizedFetch } from './adminApi';

export interface InventoryLocation {
  id: string;
  name: string;
  isActive: boolean;
  version: string;
}

export interface InventoryItem {
  id: string;
  name: string;
  unit: string;
  isVerified: boolean;
  isActive: boolean;
  version: string;
}

export interface InventoryUsage {
  id: string;
  employeeId: string;
  employeeName: string;
  locationId: string;
  locationName: string;
  itemId: string;
  itemName: string;
  unit: string;
  quantity: number;
  note: string;
  takenUtc: string;
  photoIds: string[];
  photosPurged: boolean;
  version: string;
}

async function readJson<T>(response: Response, fallback: string): Promise<T> {
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(problem.detail || problem.title || fallback);
  }
  return await response.json() as T;
}

/** Fetches a protected photo and returns a blob URL. The caller revokes it when done. */
export async function inventoryPhotoUrl(path: string): Promise<string> {
  const response = await authorizedFetch(path);
  if (!response.ok) throw new Error('This photo is no longer available.');
  return URL.createObjectURL(await response.blob());
}

export function takenLabel(takenUtc: string): string {
  return new Intl.DateTimeFormat('en-GB', { timeZone: 'Europe/Helsinki', dateStyle: 'medium', timeStyle: 'short' }).format(new Date(takenUtc));
}

export const meInventory = {
  locations: async (): Promise<InventoryLocation[]> =>
    readJson(await authorizedFetch('/api/v1/me/inventory/locations'), 'Could not load locations.'),
  items: async (): Promise<InventoryItem[]> =>
    readJson(await authorizedFetch('/api/v1/me/inventory/items'), 'Could not load items.'),
  addItem: async (name: string, unit?: string): Promise<InventoryItem> =>
    readJson(await authorizedFetch('/api/v1/me/inventory/items', { method: 'POST', body: JSON.stringify({ name, unit: unit || null }) }), 'Could not add the item.'),
  usage: async (take = 50): Promise<InventoryUsage[]> =>
    readJson(await authorizedFetch(`/api/v1/me/inventory/usage?take=${take}`), 'Could not load your history.'),
  record: async (form: FormData): Promise<InventoryUsage> =>
    readJson(await authorizedFetch('/api/v1/me/inventory/usage', { method: 'POST', body: form }), 'The entry was not saved. Check the details and try again.'),
  photoPath: (usageId: string, photoId: string) => `/api/v1/me/inventory/usage/${usageId}/photos/${photoId}`,
};

export const adminInventory = {
  locations: async (includeInactive = true): Promise<InventoryLocation[]> =>
    readJson(await authorizedFetch(`/api/v1/admin/inventory/locations?includeInactive=${includeInactive}`), 'Could not load locations.'),
  createLocation: async (name: string): Promise<InventoryLocation> =>
    readJson(await authorizedFetch('/api/v1/admin/inventory/locations', { method: 'POST', body: JSON.stringify({ name }) }), 'Could not create the location.'),
  updateLocation: async (id: string, body: { name: string; isActive: boolean; version: string }): Promise<InventoryLocation> =>
    readJson(await authorizedFetch(`/api/v1/admin/inventory/locations/${id}`, { method: 'PUT', body: JSON.stringify(body) }), 'Could not save the location.'),
  items: async (includeInactive = true): Promise<InventoryItem[]> =>
    readJson(await authorizedFetch(`/api/v1/admin/inventory/items?includeInactive=${includeInactive}`), 'Could not load items.'),
  createItem: async (name: string, unit?: string): Promise<InventoryItem> =>
    readJson(await authorizedFetch('/api/v1/admin/inventory/items', { method: 'POST', body: JSON.stringify({ name, unit: unit || null }) }), 'Could not create the item.'),
  updateItem: async (id: string, body: { name: string; unit: string | null; isActive: boolean; version: string }): Promise<InventoryItem> =>
    readJson(await authorizedFetch(`/api/v1/admin/inventory/items/${id}`, { method: 'PUT', body: JSON.stringify(body) }), 'Could not save the item.'),
  verifyItem: async (id: string, version: string): Promise<InventoryItem> =>
    readJson(await authorizedFetch(`/api/v1/admin/inventory/items/${id}/verify`, { method: 'POST', body: JSON.stringify({ version }) }), 'Could not verify the item.'),
  usage: async (filters: { employeeId?: string; from?: string; to?: string; skip?: number; take?: number }): Promise<InventoryUsage[]> => {
    const query = new URLSearchParams();
    if (filters.employeeId) query.set('employeeId', filters.employeeId);
    if (filters.from) query.set('from', filters.from);
    if (filters.to) query.set('to', filters.to);
    query.set('skip', String(filters.skip ?? 0));
    query.set('take', String(filters.take ?? 100));
    return readJson(await authorizedFetch(`/api/v1/admin/inventory/usage?${query}`), 'Could not load the inventory log.');
  },
  exportCsv: async (filters: { employeeId?: string; from?: string; to?: string }): Promise<Blob> => {
    const query = new URLSearchParams();
    if (filters.employeeId) query.set('employeeId', filters.employeeId);
    if (filters.from) query.set('from', filters.from);
    if (filters.to) query.set('to', filters.to);
    const response = await authorizedFetch(`/api/v1/admin/inventory/usage/export?${query}`);
    if (!response.ok) throw new Error('The export could not be created. Try again.');
    return await response.blob();
  },
  photoPath: (usageId: string, photoId: string) => `/api/v1/admin/inventory/usage/${usageId}/photos/${photoId}`,
};
