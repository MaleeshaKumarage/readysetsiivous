import { authorizedFetch, type Branch, type ShiftSchedule } from './adminApi';

export interface PartnerIdentity { realm: string; person: { id: string; firstName: string; lastName: string; role: string; isActive: boolean }; company: { id: string; name: string; isActive: boolean; isPartnerEnabled: boolean }; timeZoneId: string }
export interface CleaningRequest { id: string; version: number; companyId: string; branchId: string; branchName: string; fromDate: string; toDate: string; exactStartLocalTime: string | null; notes: string | null; priceNet: number; vatRatePercent: number; estimatedDurationMinutes: number; status: string; firstAcceptedAtUtc: string | null; linkedShiftId: string | null; plannedStartUtc: string | null; plannedEndUtc: string | null; acceptedCleanerName: string | null; actualStartUtc: string | null; actualEndUtc: string | null; offeredEmployeeId?: string | null; offerId?: string | null }
export interface PartnerChange { id: string; companyId: string; branchId: string | null; requestId: string | null; kind: string; message: string; createdUtc: string; resolvedUtc: string | null; resolution: string | null }
export interface CleanerOffer { id: string; requestId: string; version: number; branchName: string; street: string | null; postalCode: string | null; city: string | null; fromDate: string; toDate: string; exactStartLocalTime: string | null; plannedStartUtc: string | null; estimatedDurationMinutes: number; notes: string | null; timeZoneId: string }
export interface PartnerShift { id: string; branchId: string; branchName: string; name: string; isActive: boolean; schedule: ShiftSchedule; validFrom: string | null; validUntil: string | null }
export interface RequestInput { clientRequestId?: string; version?: number; branchId?: string; fromDate: string; toDate: string; exactStartLocalTime: string | null; notes: string }
export async function partnerSend<T>(path: string, method = 'GET', body?: unknown, signal?: AbortSignal): Promise<T> {
  const response = await authorizedFetch(`/api/v1/${path}`, { method, cache: 'no-store', signal, ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    const errors = Object.values(problem.errors ?? {}).flat().join(' ');
    throw new Error(errors || problem.detail || problem.title || `Request failed (${response.status}). Refresh to check whether your change was saved before retrying.`);
  }
  return response.status === 204 ? undefined as T : response.json();
}
export const partnerApi = {
  me: () => partnerSend<PartnerIdentity>('partner/me'),
  branches: () => partnerSend<Branch[]>('partner/branches'),
  createBranch: (body: unknown) => partnerSend<Branch>('partner/branches', 'POST', body),
  quote: (id: string) => partnerSend<Branch>(`partner/branches/${id}/quote-request`, 'POST'),
  branchChange: (id: string, message: string) => partnerSend<PartnerChange>(`partner/branches/${id}/change-requests`, 'POST', { message }),
  requests: () => partnerSend<CleaningRequest[]>('partner/requests'),
  changes: () => partnerSend<PartnerChange[]>('partner/change-requests'),
  createRequest: (body: RequestInput) => partnerSend<CleaningRequest>('partner/requests', 'POST', body),
  updateRequest: (id: string, body: RequestInput) => partnerSend<CleaningRequest>(`partner/requests/${id}`, 'PUT', body),
  cancel: (request: CleaningRequest) => partnerSend<CleaningRequest>(`partner/requests/${request.id}/cancel`, 'POST', { version: request.version }),
  requestChange: (id: string, message: string) => partnerSend<PartnerChange>(`partner/requests/${id}/change-requests`, 'POST', { message }),
  shifts: () => partnerSend<PartnerShift[]>('partner/shifts'),
};
export const offerApi = {
  list: () => partnerSend<CleanerOffer[]>('me/partner-offers'),
  accept: (offer: CleanerOffer, plannedStartLocal?: string) => partnerSend<Pick<CleaningRequest, 'id' | 'version' | 'status' | 'linkedShiftId' | 'plannedStartUtc' | 'plannedEndUtc'>>(`me/partner-offers/${offer.id}/accept`, 'POST', { version: offer.version, plannedStartLocal: plannedStartLocal || null }),
  decline: (offer: CleanerOffer, reason?: string) => partnerSend<void>(`me/partner-offers/${offer.id}/decline`, 'POST', { version: offer.version, reason: reason || null }),
};
export function staffPartnerApi(role: 'admin' | 'manager') {
  const base = `${role}/partner`;
  return {
    requests: () => partnerSend<CleaningRequest[]>(`${base}/requests`), quotes: (signal?: AbortSignal) => partnerSend<Branch[]>(`${base}/quotes`, 'GET', undefined, signal), changes: () => partnerSend<PartnerChange[]>(`${base}/change-requests`),
    branches: () => partnerSend<Branch[]>(`${base}/branches`),
    createBranch: (body: unknown) => partnerSend<Branch>(`${base}/branches`, 'POST', body),
    updateBranch: (id: string, body: unknown) => partnerSend<Branch>(`${base}/branches/${id}`, 'PUT', body),
    price: (id: string, body: unknown) => partnerSend<Branch>(`${base}/branches/${id}/pricing`, 'PUT', body),
    offer: (request: CleaningRequest, employeeId: string, plannedStartLocal?: string) => partnerSend<CleaningRequest>(`${base}/requests/${request.id}/offer`, 'POST', { version: request.version, employeeId, plannedStartLocal: plannedStartLocal || null }),
    update: (request: CleaningRequest, body: RequestInput) => partnerSend<CleaningRequest>(`${base}/requests/${request.id}`, 'PUT', { ...body, version: request.version }),
    cancel: (request: CleaningRequest, reason: string) => partnerSend<CleaningRequest>(`${base}/requests/${request.id}/cancel`, 'POST', { version: request.version, reason }),
    resolve: (id: string, resolution: string) => partnerSend<PartnerChange>(`${base}/change-requests/${id}/resolve`, 'POST', { resolution }),
  };
}
