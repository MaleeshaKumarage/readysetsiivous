import { authorizedFetch } from './adminApi';

export type SupplyStatus = 'Requested' | 'Approved' | 'Fulfilled' | 'Declined';

export interface SupplyLine {
  itemId: string;
  itemName: string;
  unit: string;
  quantity: number;
}

export interface SupplyRequest {
  id: string;
  employeeId: string;
  employeeName: string;
  lines: SupplyLine[];
  note: string;
  status: SupplyStatus;
  decisionNote: string | null;
  decidedByName: string | null;
  decidedUtc: string | null;
  createdUtc: string;
  version: string;
}

export type SupplyDecision = 'approve' | 'decline' | 'fulfil';

async function readJson<T>(response: Response, failure: string): Promise<T> {
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(problem.detail || problem.title || failure);
  }
  return response.json();
}

export const supplyRequests = {
  own: async (): Promise<SupplyRequest[]> =>
    readJson(await authorizedFetch('/api/v1/me/supply-requests', { cache: 'no-store' }), 'Supply requests could not be loaded.'),
  create: async (lines: { itemId: string; quantity: number }[], note: string): Promise<SupplyRequest> =>
    readJson(await authorizedFetch('/api/v1/me/supply-requests', { method: 'POST', body: JSON.stringify({ lines, note }) }), 'The request could not be sent.'),
  review: async (status?: SupplyStatus): Promise<SupplyRequest[]> =>
    readJson(await authorizedFetch(`/api/v1/admin/supply-requests${status ? `?status=${status}` : ''}`, { cache: 'no-store' }), 'Supply requests could not be loaded.'),
  decide: async (id: string, version: string, decision: SupplyDecision, note?: string): Promise<SupplyRequest> =>
    readJson(await authorizedFetch(`/api/v1/admin/supply-requests/${id}/decision`, { method: 'POST', body: JSON.stringify({ decision, version, note: note ?? null }) }), 'The decision could not be saved.'),
};
