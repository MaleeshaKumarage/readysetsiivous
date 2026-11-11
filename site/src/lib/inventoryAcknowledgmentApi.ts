import { partnerSend } from './partnerApi';

export interface InventoryAcknowledgment {
  id: string; batchId: string; companyId: string; companyName: string;
  message: string; itemName: string; locationName: string; quantity: number; unit: string;
  note: string; takenUtc: string; photoIds: string[];
  status: 'Pending' | 'Acknowledged' | 'IssueReported'; responseNote: string;
  respondedByName: string | null; respondedUtc: string | null; createdUtc: string; version: string;
}
export interface AcknowledgmentPage { items: InventoryAcknowledgment[]; pendingCount: number; hasMore: boolean }
export const inventoryAcknowledgments = {
  partners: (search: string, signal?: AbortSignal) => partnerSend<{ id: string; name: string }[]>(`admin/inventory/acknowledgments/partners?search=${encodeURIComponent(search)}`, 'GET', undefined, signal),
  lookup: (ids: string[]) => partnerSend<InventoryAcknowledgment[]>(`admin/inventory/acknowledgments?${new URLSearchParams(ids.map(id => ['usageIds', id]))}`),
  create: (body: { batchId: string; companyId: string; usageIds: string[]; message: string }) => partnerSend<InventoryAcknowledgment[]>('admin/inventory/acknowledgments', 'POST', body),
  list: (history: boolean, skip = 0, signal?: AbortSignal) => partnerSend<AcknowledgmentPage>(`partner/inventory-acknowledgments?history=${history}&skip=${skip}`, 'GET', undefined, signal),
  respond: (row: InventoryAcknowledgment, status: 'Acknowledged' | 'IssueReported', note: string) => partnerSend<InventoryAcknowledgment>(`partner/inventory-acknowledgments/${row.id}/response`, 'POST', { version: row.version, status, note }),
  photoPath: (id: string, photoId: string) => `/api/v1/partner/inventory-acknowledgments/${id}/photos/${photoId}`,
};
