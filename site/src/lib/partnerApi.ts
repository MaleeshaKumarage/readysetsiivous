import { authorizedFetch, type Branch, type ShiftSchedule } from './adminApi';

export interface PartnerIdentity { realm: string; person: { id: string; firstName: string; lastName: string; role: string; isActive: boolean }; company: { id: string; name: string; isActive: boolean; isPartnerEnabled: boolean }; timeZoneId: string }
export interface CleaningRequest { id: string; version: number; companyId: string; branchId: string; branchName: string; fromDate: string; toDate: string; exactStartLocalTime: string | null; notes: string | null; cleaningType?: string; priceNet: number; vatRatePercent: number; estimatedDurationMinutes: number; status: string; firstAcceptedAtUtc: string | null; linkedShiftId: string | null; plannedStartUtc: string | null; plannedEndUtc: string | null; acceptedCleanerName: string | null; actualStartUtc: string | null; actualEndUtc: string | null; offeredEmployeeId?: string | null; offerId?: string | null }
// One-time portal setup. Photo sections are the photos every cleaner must attach before a visit can end.
// Account details: billing and invoice facts, a contact person, and how the cleaner gets in.
export interface PartnerAccount { billingName?: string | null; invoiceAddress?: string | null; businessId?: string | null; invoiceReference?: string | null; contactName?: string | null; contactPhone?: string | null; contactEmail?: string | null; accessInstructions?: string | null }
// General instructions are read by the cleaner before every visit; a visit's own instructions are appended after them.
export interface PartnerSettings { photoLabels: string[]; generalInstructions?: string | null; account?: PartnerAccount }
// What the owner asks for on a cleaning request. Structured actions can be applied by staff; "Message" needs a reply.
export type PartnerChangeAction = 'Message' | 'Reschedule' | 'Instructions' | 'Cancel';
export interface PartnerRequestChangeInput { message: string; action: PartnerChangeAction; fromDate?: string; toDate?: string; exactStartLocalTime?: string | null; notes?: string | null }
export interface PartnerChange { id: string; companyId: string; branchId: string | null; requestId: string | null; kind: string; message: string; createdUtc: string; resolvedUtc: string | null; resolution: string | null; action?: string; proposedFromDate?: string | null; proposedToDate?: string | null; proposedExactStartLocalTime?: string | null; proposedNotes?: string | null }
export interface CleanerOffer { id: string; requestId: string; version: number; branchName: string; street: string | null; postalCode: string | null; city: string | null; fromDate: string; toDate: string; exactStartLocalTime: string | null; plannedStartUtc: string | null; estimatedDurationMinutes: number; notes: string | null; timeZoneId: string }
export interface PartnerShift { id: string; branchId: string; branchName: string; name: string; isActive: boolean; schedule: ShiftSchedule; validFrom: string | null; validUntil: string | null }
// Partner-safe completed-visit summary. The cleaner is masked to an initial; no employee ids or full names.
export interface PartnerCompletionTask { text: string; done: boolean }
export interface PartnerCompletionPhoto { url: string; note: string }
export interface PartnerCompletionReport { templateTitle: string; startedAtUtc: string | null; endedAtUtc: string | null; tasks: PartnerCompletionTask[]; cleanerNotes: string | null; photos: PartnerCompletionPhoto[] }
export interface PartnerCompletion { requestId: string; branchName: string; fromDate: string; toDate: string; plannedStartUtc: string | null; plannedEndUtc: string | null; actualStartUtc: string | null; actualEndUtc: string | null; reports: PartnerCompletionReport[]; messages: PartnerChange[] }
// Key pickup or handover at one property. The fee is the amount frozen when the request was submitted, excl. VAT.
export interface KeyRequestPhoto { id: string; sizeBytes: number; uploadedUtc: string }
// Assignee, assignee name and photos are staff-only: partners never receive them.
export interface KeyRequest { id: string; revision: number; companyId: string; branchId: string; branchName: string; kind: string; date: string; notes: string | null; feeNet: number; vatRatePercent: number; status: string; cancellationReason: string | null; completedUtc?: string | null; assignedEmployeeId?: string | null; assignedEmployeeName?: string | null; photos?: KeyRequestPhoto[] | null }
export interface KeyRequestList { feeNet: number; vatRatePercent: number; items: KeyRequest[] }
export interface KeyRequestInput { clientRequestId: string; branchId: string; kind: 'Pickup' | 'Handover'; date: string; notes: string | null }
export interface RequestInput { clientRequestId?: string; version?: number; branchId?: string; fromDate: string; toDate: string; exactStartLocalTime: string | null; notes: string; cleaningType?: string }
export async function partnerSend<T>(path: string, method = 'GET', body?: unknown, signal?: AbortSignal): Promise<T> {
  const response = await authorizedFetch(`/api/v1/${path}`, { method, cache: 'no-store', signal, ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    // Newline-separated so the UI can translate each message on its own.
    const errors = Object.values(problem.errors ?? {}).flat().join('\n');
    throw new Error(errors || problem.detail || problem.title || `Request failed (${response.status}). Refresh to check whether your change was saved before retrying.`);
  }
  return response.status === 204 ? undefined as T : response.json();
}
// Photos need the signed-in token, so they are fetched and shown through an object URL.
async function photoObjectUrl(path: string): Promise<string> {
  const response = await authorizedFetch(`/api/v1/${path}`, { cache: 'no-store' });
  if (!response.ok) throw new Error('The photo could not be loaded.');
  return URL.createObjectURL(await response.blob());
}
// Multipart upload, so the JSON helper is not used. Errors use the same problem-details wording.
async function uploadKeyRequestPhoto(path: string, file: File): Promise<KeyRequest> {
  const body = new FormData();
  body.append('file', file);
  const response = await authorizedFetch(`/api/v1/${path}`, { method: 'POST', cache: 'no-store', body });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(Object.values(problem.errors ?? {}).flat().join('\n') || problem.detail || problem.title || 'The photo could not be uploaded. Check the file and try again.');
  }
  return response.json();
}
export const partnerApi = {
  me: () => partnerSend<PartnerIdentity>('partner/me'),
  branches: () => partnerSend<Branch[]>('partner/branches'),
  createBranch: (body: unknown) => partnerSend<Branch>('partner/branches', 'POST', body),
  quote: (id: string) => partnerSend<Branch>(`partner/branches/${id}/quote-request`, 'POST'),
  withdrawQuote: (id: string) => partnerSend<Branch>(`partner/branches/${id}/quote-request/withdraw`, 'POST'),
  updateBranch: (id: string, body: unknown) => partnerSend<Branch>(`partner/branches/${id}`, 'PUT', body),
  branchChange: (id: string, message: string) => partnerSend<PartnerChange>(`partner/branches/${id}/change-requests`, 'POST', { message }),
  requests: () => partnerSend<CleaningRequest[]>('partner/requests'),
  completion: (id: string) => partnerSend<PartnerCompletion>(`partner/requests/${id}/completion`),
  changes: () => partnerSend<PartnerChange[]>('partner/change-requests'),
  createRequest: (body: RequestInput) => partnerSend<CleaningRequest>('partner/requests', 'POST', body),
  updateRequest: (id: string, body: RequestInput) => partnerSend<CleaningRequest>(`partner/requests/${id}`, 'PUT', body),
  cancel: (request: CleaningRequest, reason: string) => partnerSend<CleaningRequest>(`partner/requests/${request.id}/cancel`, 'POST', { version: request.version, reason }),
  settings: () => partnerSend<PartnerSettings>('partner/settings'),
  saveSettings: (body: { photoLabels: string[]; generalInstructions: string; account?: PartnerAccount }) => partnerSend<PartnerSettings>('partner/settings', 'PUT', body),
  requestChange: (id: string, body: PartnerRequestChangeInput) => partnerSend<PartnerChange>(`partner/requests/${id}/change-requests`, 'POST', body),
  shifts: () => partnerSend<PartnerShift[]>('partner/shifts'),
  keyRequests: () => partnerSend<KeyRequestList>('partner/key-requests'),
  createKeyRequest: (body: KeyRequestInput) => partnerSend<KeyRequest>('partner/key-requests', 'POST', body),
  cancelKeyRequest: (request: KeyRequest, reason: string) => partnerSend<KeyRequest>(`partner/key-requests/${request.id}/cancel`, 'POST', { version: request.revision, reason }),
  keyRequestPhotoUrl: (requestId: string, photoId: string) => photoObjectUrl(`partner/key-requests/${requestId}/photos/${photoId}`),
};
export const offerApi = {
  list: () => partnerSend<CleanerOffer[]>('me/partner-offers'),
  accept: (offer: CleanerOffer, plannedStartLocal?: string) => partnerSend<Pick<CleaningRequest, 'id' | 'version' | 'status' | 'linkedShiftId' | 'plannedStartUtc' | 'plannedEndUtc'>>(`me/partner-offers/${offer.id}/accept`, 'POST', { version: offer.version, plannedStartLocal: plannedStartLocal || null }),
  decline: (offer: CleanerOffer, reason?: string) => partnerSend<void>(`me/partner-offers/${offer.id}/decline`, 'POST', { version: offer.version, reason: reason || null }),
};
export function staffPartnerApi(role: 'admin' | 'manager') {
  const base = `${role}/partner`;
  return {
    requests: (signal?: AbortSignal) => partnerSend<CleaningRequest[]>(`${base}/requests`, 'GET', undefined, signal), quotes: (signal?: AbortSignal) => partnerSend<Branch[]>(`${base}/quotes`, 'GET', undefined, signal), changes: (signal?: AbortSignal) => partnerSend<PartnerChange[]>(`${base}/change-requests`, 'GET', undefined, signal),
    branches: () => partnerSend<Branch[]>(`${base}/branches`),
    createBranch: (body: unknown) => partnerSend<Branch>(`${base}/branches`, 'POST', body),
    updateBranch: (id: string, body: unknown) => partnerSend<Branch>(`${base}/branches/${id}`, 'PUT', body),
    price: (id: string, body: unknown) => partnerSend<Branch>(`${base}/branches/${id}/pricing`, 'PUT', body),
    rejectQuote: (branch: Branch, reason: string) => partnerSend<Branch>(`${base}/branches/${branch.id}/quote-rejection`, 'POST', { reason }),
    offer: (request: CleaningRequest, employeeId: string, plannedStartLocal?: string) => partnerSend<CleaningRequest>(`${base}/requests/${request.id}/offer`, 'POST', { version: request.version, employeeId, plannedStartLocal: plannedStartLocal || null }),
    update: (request: CleaningRequest, body: RequestInput) => partnerSend<CleaningRequest>(`${base}/requests/${request.id}`, 'PUT', { ...body, version: request.version }),
    cancel: (request: CleaningRequest, reason: string) => partnerSend<CleaningRequest>(`${base}/requests/${request.id}/cancel`, 'POST', { version: request.version, reason }),
    resolve: (id: string, resolution: string) => partnerSend<PartnerChange>(`${base}/change-requests/${id}/resolve`, 'POST', { resolution }),
    // Applies a structured request to the request at the version staff reviewed, then resolves the change.
    apply: (id: string, version: number, resolution: string) => partnerSend<PartnerChange>(`${base}/change-requests/${id}/apply`, 'POST', { version, resolution }),
    keyRequests: (signal?: AbortSignal) => partnerSend<KeyRequestList>(`${base}/key-requests`, 'GET', undefined, signal),
    // One fee for every new key request. Existing requests keep the fee they were submitted with.
    setKeyRequestFee: (feeNet: number) => partnerSend<KeyRequestList>(`${base}/key-request-fee`, 'PUT', { feeNet }),
    assignKeyRequest: (request: KeyRequest, employeeId: string) => partnerSend<KeyRequest>(`${base}/key-requests/${request.id}/assignment`, 'POST', { revision: request.revision, employeeId }),
    completeKeyRequest: (request: KeyRequest) => partnerSend<KeyRequest>(`${base}/key-requests/${request.id}/complete`, 'POST', { revision: request.revision }),
    // Multipart upload, so the JSON helper is not used. Errors use the same problem-details wording.
    addKeyRequestPhoto: (request: KeyRequest, file: File) => uploadKeyRequestPhoto(`${base}/key-requests/${request.id}/photos`, file),
    keyRequestPhotoUrl: (requestId: string, photoId: string) => photoObjectUrl(`${base}/key-requests/${requestId}/photos/${photoId}`),
  };
}

// Key requests assigned to the signed-in employee. Only the assignee can complete them or add photos.
export const myKeyRequestApi = {
  assigned: () => partnerSend<KeyRequest[]>('me/key-requests'),
  complete: (request: KeyRequest) => partnerSend<KeyRequest>(`me/key-requests/${request.id}/complete`, 'POST', { revision: request.revision }),
  addPhoto: (request: KeyRequest, file: File) => uploadKeyRequestPhoto(`me/key-requests/${request.id}/photos`, file),
  photoUrl: (requestId: string, photoId: string) => photoObjectUrl(`me/key-requests/${requestId}/photos/${photoId}`),
};
