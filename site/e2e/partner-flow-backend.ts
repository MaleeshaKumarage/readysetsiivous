import { devices, type Browser, type Page, type Route, type TestInfo } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// In-process stand-in for the partner API, shared by every browser context in one test.
// Rules mirror api/CleaningSuite.Application/Partners/PartnerService.cs so the UI is exercised
// against the same version, offer, acceptance and window behaviour the server enforces.

export const CORS = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
export const TENANT_TIME_ZONE = 'Europe/Helsinki';
// Every date used by these flows is in December, when Helsinki is on UTC+02:00.
const HELSINKI_WINTER_OFFSET = '+02:00';

export type ActorKey = 'owner' | 'admin' | 'manager' | 'cleaner' | 'autoCleaner' | 'newCleaner' | 'unlinked';
interface Person { id: string; firstName: string; lastName: string; role: string; isActive: boolean; autoAcceptWork?: boolean; keycloakUserId?: string | null }
const ROLE_FOR_ACTOR: Record<ActorKey, string[]> = { owner: ['partner'], admin: ['admin'], manager: ['manager'], cleaner: ['employee'], autoCleaner: ['employee'], newCleaner: ['employee'], unlinked: ['employee'] };
const PEOPLE: Record<ActorKey, Person> = {
  owner: { id: 'owner', firstName: 'Olivia', lastName: 'Owner', role: 'partner', isActive: true },
  admin: { id: 'admin', firstName: 'Ada', lastName: 'Admin', role: 'admin', isActive: true },
  manager: { id: 'manager', firstName: 'Mika', lastName: 'Manager', role: 'manager', isActive: true },
  cleaner: { id: 'cleaner', firstName: 'Test', lastName: 'Cleaner', role: 'employee', isActive: true, autoAcceptWork: false, keycloakUserId: 'kc-cleaner' },
  // No stored preference: the server default applies, which is automatic acceptance.
  newCleaner: { id: 'new-cleaner', firstName: 'Nina', lastName: 'Newstart', role: 'employee', isActive: true, keycloakUserId: 'kc-new' },
  // Added but not yet signed in: automatic acceptance cannot apply.
  unlinked: { id: 'unlinked', firstName: 'Ola', lastName: 'Pending', role: 'employee', isActive: true, keycloakUserId: null },
  autoCleaner: { id: 'auto-cleaner', firstName: 'Auto', lastName: 'Cleaner', role: 'employee', isActive: true, autoAcceptWork: true, keycloakUserId: 'kc-auto' },
};

export interface BranchRecord { id: string; companyId: string; name: string; street: string | null; postalCode: string | null; city: string | null; country: string | null; contactPhone: string | null; contactEmail: string | null; roomCount: number | null; bedCount: number | null; isActive: boolean; pricingReady: boolean; priceNet: number | null; vatRatePercent: number | null; estimatedDurationMinutes: number | null; quoteRequestedUtc: string | null }
export interface RequestRecord { id: string; companyId: string; branchId: string; clientRequestId: string; version: number; fromDate: string; toDate: string; exactStartLocalTime: string | null; notes: string | null; priceNet: number; vatRatePercent: number; estimatedDurationMinutes: number; status: string; firstAcceptedAtUtc: string | null; linkedShiftId: string | null; plannedStartUtc: string | null; plannedEndUtc: string | null; acceptedEmployeeId: string | null; offeredEmployeeId: string | null; offerId: string | null; offerPlannedStartUtc: string | null; cancellationReason: string | null }
export interface ChangeRecord { id: string; companyId: string; branchId: string | null; requestId: string | null; kind: string; message: string; resolvedUtc: string | null; resolution: string | null }
export interface WriteRecord { actor: ActorKey; method: string; path: string; body: any; status: number }

export class ApiError extends Error { constructor(readonly status: number, message: string) { super(message); } }

export const OWNER_COMPANY = { id: 'company-one', name: 'Owner company', isActive: true, isPartnerEnabled: true };
export const OTHER_COMPANY = { id: 'company-two', name: 'Other company', isActive: true, isPartnerEnabled: false };

export function helsinkiLocalToUtc(local: string): string {
  return new Date(`${local}:00${HELSINKI_WINTER_OFFSET}`).toISOString();
}

export class PartnerFlowBackend {
  readonly companies = [OWNER_COMPANY, OTHER_COMPANY];
  readonly people = PEOPLE;
  branches: BranchRecord[] = [];
  requests: RequestRecord[] = [];
  changes: ChangeRecord[] = [];
  preferences: Record<string, boolean> = {};
  shiftActive: Record<string, boolean> = {};
  writes: WriteRecord[] = [];
  private sequence = 0;
  private failures: { method: string; pattern: RegExp; status: number; detail: string; remaining: number }[] = [];

  constructor(options: { priced?: boolean } = {}) {
    if (options.priced) this.addBranch({ name: 'Lake apartment', companyId: OWNER_COMPANY.id, priceNet: 85, estimatedDurationMinutes: 90 });
  }

  // ----- seeding helpers (state set up directly, not through the UI) -----
  addBranch(input: { name: string; companyId: string; priceNet?: number | null; estimatedDurationMinutes?: number | null; isActive?: boolean; quoteRequestedUtc?: string | null }): BranchRecord {
    const priced = input.priceNet != null && input.estimatedDurationMinutes != null;
    const branch: BranchRecord = { id: this.nextId('branch'), companyId: input.companyId, name: input.name, street: 'Lake road 1', postalCode: '33100', city: 'Tampere', country: 'Finland', contactPhone: null, contactEmail: null, roomCount: 2, bedCount: 3, isActive: input.isActive ?? true, pricingReady: priced, priceNet: input.priceNet ?? null, vatRatePercent: priced ? 25.5 : null, estimatedDurationMinutes: input.estimatedDurationMinutes ?? null, quoteRequestedUtc: input.quoteRequestedUtc ?? null };
    this.branches.push(branch);
    return branch;
  }
  seedRequest(branch: BranchRecord, window: { fromDate: string; toDate: string; exactStartLocalTime?: string | null; notes?: string | null }): RequestRecord {
    const request: RequestRecord = { id: this.nextId('request'), companyId: branch.companyId, branchId: branch.id, clientRequestId: this.nextId('client'), version: 1, fromDate: window.fromDate, toDate: window.toDate, exactStartLocalTime: window.exactStartLocalTime ?? null, notes: window.notes ?? null, priceNet: branch.priceNet!, vatRatePercent: branch.vatRatePercent!, estimatedDurationMinutes: branch.estimatedDurationMinutes!, status: 'Requested', firstAcceptedAtUtc: null, linkedShiftId: null, plannedStartUtc: null, plannedEndUtc: null, acceptedEmployeeId: null, offeredEmployeeId: null, offerId: null, offerPlannedStartUtc: null, cancellationReason: null };
    this.requests.push(request);
    return request;
  }
  seedOffer(request: RequestRecord, employeeId: string, plannedStartLocal: string | null = null) {
    this.offerRequest(request, this.employeeById(employeeId), request.version, plannedStartLocal);
    return request;
  }
  seedAccepted(request: RequestRecord, employeeId: string, plannedStartLocal: string) {
    this.seedOffer(request, employeeId, plannedStartLocal);
    this.materialize(request, employeeId, request.offerPlannedStartUtc!);
    return request;
  }
  request(id: string) { return this.requests.find(item => item.id === id)!; }
  nextId(prefix: string) { return `${prefix}-${++this.sequence}`; }

  // Make the next matching call(s) fail with a server error, to exercise retry paths.
  failNext(method: string, pattern: RegExp, status: number, detail: string, times = 1) {
    this.failures.push({ method, pattern, status, detail, remaining: times });
  }

  async attach(page: Page, actor: ActorKey) {
    await page.route(/\/api\/v1\//, route => this.handle(actor, route));
  }

  private async handle(actor: ActorKey, route: Route) {
    const req = route.request();
    if (req.method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS });
    const path = new URL(req.url()).pathname.replace(/^\/api\/v1\//, '').replace(/\/$/, '');
    const method = req.method();
    const body = req.postData() ? req.postDataJSON() : undefined;
    const failure = this.failures.find(item => item.remaining > 0 && item.method === method && item.pattern.test(path));
    let status = 200; let json: unknown = {};
    if (failure) { failure.remaining--; status = failure.status; json = { detail: failure.detail }; }
    else {
      try { json = this.dispatch(actor, method, path, body) ?? {}; }
      catch (error) {
        if (!(error instanceof ApiError)) throw error;
        status = error.status; json = { detail: error.message };
      }
    }
    // Record what the caller actually received, so refused writes are visible in assertions.
    if (method !== 'GET') this.writes.push({ actor, method, path, body, status });
    return route.fulfill({ status, headers: CORS, json });
  }

  private dispatch(actor: ActorKey, method: string, path: string, body: any): unknown {
    const me = this.people[actor];
    if (method === 'GET' && path === 'me') {
      // Administrators have no employee row; managers and cleaners are employees whose role must match the token role.
      const roles = [JSON.stringify({ roles: ROLE_FOR_ACTOR[actor] })];
      if (actor === 'owner' || actor === 'admin') return { realm: 'readysetsiivous', roles, employee: null };
      return { realm: 'readysetsiivous', roles, employee: { id: me.id, firstName: me.firstName, lastName: me.lastName, role: me.role, isActive: me.isActive, keycloakUserId: me.keycloakUserId ?? null, autoAcceptWork: me.autoAcceptWork ?? true } };
    }
    if (method === 'GET' && path === 'admin/tenant') return { companyName: 'ReadySetSiivous', timeZoneId: TENANT_TIME_ZONE };
    if (method === 'GET' && path === 'admin/employees') return Object.values(this.people).map(person => ({ ...person, autoAcceptWork: this.effectiveAutoAccept(person), email: `${person.id}@example.invalid`, phone: '' }));
    if (method === 'GET' && (path === 'admin/companies' || path === 'manager/companies')) return { items: this.companies, total: this.companies.length };
    if (method === 'GET' && path === 'manager/employees') return Object.values(this.people).map(person => ({ ...person, autoAcceptWork: this.effectiveAutoAccept(person), phone: '' }));

    if (path.startsWith('partner/')) return this.partnerRoute(actor, method, path.slice('partner/'.length), body);
    if (path.startsWith('admin/partner/') || path.startsWith('manager/partner/')) return this.staffRoute(actor, method, path.replace(/^(admin|manager)\/partner\//, ''), body);
    if (path.startsWith('me/')) return this.employeeRoute(actor, method, path.slice('me/'.length), body);
    throw new ApiError(404, `No fake route for ${method} ${path}.`);
  }

  // ----- partner (owner) API -----
  private partnerRoute(actor: ActorKey, method: string, path: string, body: any): unknown {
    if (actor !== 'owner') throw new ApiError(403, 'Partner access is required.');
    const company = OWNER_COMPANY;
    if (method === 'GET' && path === 'me') return { realm: 'readysetsiivous', person: { ...PEOPLE.owner, isActive: true }, company, timeZoneId: TENANT_TIME_ZONE };
    if (method === 'GET' && path === 'branches') return this.branches.filter(branch => branch.companyId === company.id);
    if (method === 'POST' && path === 'branches') {
      if (!body?.name?.trim()) throw new ApiError(400, 'Property name is required and must be at most 200 characters.');
      if (body.companyId) throw new ApiError(400, 'The company is taken from your partner account.');
      return this.addBranch({ name: body.name, companyId: company.id });
    }
    let match = is(path, /^branches\/([^/]+)\/quote-request$/);
    if (method === 'POST' && match) {
      const branch = this.branches.find(item => item.id === match![1] && item.companyId === company.id);
      if (!branch) throw new ApiError(404, 'Branch not found.');
      if (branch.pricingReady) throw new ApiError(409, 'This property is already priced.');
      branch.quoteRequestedUtc = '2026-11-01T09:00:00Z';
      return branch;
    }
    match = is(path, /^branches\/([^/]+)\/change-requests$/);
    if (method === 'POST' && match) return this.addChange({ kind: 'Property', branchId: match[1], requestId: null, message: body?.message });
    if (method === 'GET' && path === 'requests') return this.requests.filter(item => item.companyId === company.id).map(item => this.dto(item, false));
    if (method === 'POST' && path === 'requests') {
      const existing = this.requests.find(item => item.clientRequestId === body?.clientRequestId);
      if (existing) return this.dto(existing, false);
      const branch = this.branches.find(item => item.id === body?.branchId && item.companyId === company.id);
      if (!branch) throw new ApiError(404, 'Branch not found.');
      if (!branch.pricingReady) throw new ApiError(409, 'Request a quote before booking this property.');
      const request: RequestRecord = { id: this.nextId('request'), companyId: company.id, branchId: branch.id, clientRequestId: body.clientRequestId, version: 1, fromDate: '', toDate: '', exactStartLocalTime: null, notes: null, priceNet: branch.priceNet!, vatRatePercent: branch.vatRatePercent!, estimatedDurationMinutes: branch.estimatedDurationMinutes!, status: 'Requested', firstAcceptedAtUtc: null, linkedShiftId: null, plannedStartUtc: null, plannedEndUtc: null, acceptedEmployeeId: null, offeredEmployeeId: null, offerId: null, offerPlannedStartUtc: null, cancellationReason: null };
      this.applyWindow(request, body);
      this.requests.push(request);
      return this.dto(request, false);
    }
    match = is(path, /^requests\/([^/]+)$/);
    if (method === 'PUT' && match) {
      const request = this.ownRequest(match[1], company.id);
      this.checkVersion(request, body?.version);
      if (request.firstAcceptedAtUtc || request.status === 'Cancelled') throw new ApiError(409, 'This request can no longer be edited directly. Ask staff for a change.');
      this.applyWindow(request, body);
      this.invalidateOffer(request);
      request.status = 'Requested'; request.version++;
      return this.dto(request, false);
    }
    match = is(path, /^requests\/([^/]+)\/cancel$/);
    if (method === 'POST' && match) {
      const request = this.ownRequest(match[1], company.id);
      this.checkVersion(request, body?.version);
      if (request.firstAcceptedAtUtc || request.status === 'Cancelled') {
        if (request.status === 'Cancelled') return this.dto(request, false);
        throw new ApiError(409, 'This request can no longer be edited directly. Ask staff for a change.');
      }
      this.cancel(request, null);
      return this.dto(request, false);
    }
    match = is(path, /^requests\/([^/]+)\/change-requests$/);
    if (method === 'POST' && match) { this.ownRequest(match[1], company.id); return this.addChange({ kind: 'CleaningRequest', branchId: this.request(match[1]).branchId, requestId: match[1], message: body?.message }); }
    if (method === 'GET' && path === 'change-requests') return this.changes.filter(item => item.companyId === company.id);
    if (method === 'GET' && path === 'shifts') {
      return this.requests.filter(item => item.companyId === company.id && item.linkedShiftId).map(item => ({ id: item.linkedShiftId, branchId: item.branchId, branchName: this.branchName(item.branchId), name: `Cleaning — ${this.branchName(item.branchId)}`, isActive: Boolean(this.shiftActive[item.linkedShiftId!]), schedule: { type: 'OnCallFlexible' }, validFrom: item.fromDate, validUntil: item.toDate }));
    }
    throw new ApiError(404, `No fake partner route for ${method} ${path}.`);
  }

  // ----- administrator and manager API (both use the same partner operations) -----
  private staffRoute(actor: ActorKey, method: string, path: string, body: any): unknown {
    if (actor !== 'admin' && actor !== 'manager') throw new ApiError(403, 'Administrator or manager access is required.');
    if (method === 'GET' && path === 'requests') return this.requests.map(item => this.dto(item, true));
    if (method === 'GET' && path === 'quotes') return this.branches.filter(item => item.quoteRequestedUtc && !item.pricingReady);
    if (method === 'GET' && path === 'change-requests') return this.changes;
    if (method === 'GET' && path === 'branches') return this.branches;
    if (method === 'POST' && path === 'branches') return this.addBranch({ name: body.name, companyId: body.companyId });
    let match = is(path, /^branches\/([^/]+)\/pricing$/);
    if (method === 'PUT' && match) {
      const branch = this.branches.find(item => item.id === match![1])!;
      Object.assign(branch, { priceNet: body.priceNet, vatRatePercent: body.vatRatePercent, estimatedDurationMinutes: body.estimatedDurationMinutes, pricingReady: true, quoteRequestedUtc: null });
      return branch;
    }
    match = is(path, /^branches\/([^/]+)$/);
    if (method === 'PUT' && match) { Object.assign(this.branches.find(item => item.id === match![1])!, body); return this.branches.find(item => item.id === match![1]); }
    match = is(path, /^requests\/([^/]+)\/offer$/);
    if (method === 'POST' && match) {
      const request = this.request(match[1]);
      return this.dto(this.offerRequest(request, this.employeeById(body.employeeId), body.version, body.plannedStartLocal ?? null), true);
    }
    match = is(path, /^requests\/([^/]+)\/cancel$/);
    if (method === 'POST' && match) {
      const request = this.request(match[1]);
      this.checkVersion(request, body?.version);
      if (!body?.reason?.trim() || body.reason.length > 1000) throw new ApiError(400, 'Cancellation reason is required and must be at most 1000 characters.');
      if (request.status === 'Cancelled') return this.dto(request, true);
      this.cancel(request, body.reason);
      return this.dto(request, true);
    }
    match = is(path, /^requests\/([^/]+)$/);
    if (method === 'PUT' && match) {
      const request = this.request(match[1]);
      this.checkVersion(request, body?.version);
      if (request.status === 'Cancelled') throw new ApiError(409, 'Cancelled requests cannot be rescheduled.');
      this.ensureUnstarted(request);
      this.applyWindow(request, body);
      this.dropShift(request);
      request.acceptedEmployeeId = null; request.plannedStartUtc = null; request.plannedEndUtc = null;
      this.invalidateOffer(request); request.status = 'Requested'; request.version++;
      return this.dto(request, true);
    }
    match = is(path, /^change-requests\/([^/]+)\/resolve$/);
    if (method === 'POST' && match) {
      const change = this.changes.find(item => item.id === match![1])!;
      if (change.resolvedUtc) throw new ApiError(409, 'This change request has already been resolved.');
      Object.assign(change, { resolution: body.resolution, resolvedUtc: '2026-11-03T12:00:00Z' });
      return change;
    }
    throw new ApiError(404, `No fake staff route for ${method} ${path}.`);
  }

  // ----- cleaner (employee) API -----
  private employeeRoute(actor: ActorKey, method: string, path: string, body: any): unknown {
    const me = this.people[actor];
    if (me.role !== 'employee') throw new ApiError(403, 'Employee access is required.');
    if (method === 'GET' && path === 'shifts/calendar') return { occurrences: [], timeZoneId: TENANT_TIME_ZONE, fromLocalDate: '2026-12-01', toLocalDate: '2026-12-01' };
    if (method === 'GET' && path === 'work-preferences') return { autoAcceptWork: this.preferences[me.id] ?? me.autoAcceptWork ?? true };
    if (method === 'PUT' && path === 'work-preferences') {
      if (body?.autoAcceptWork && this.pendingOffersFor(me.id).length) throw new ApiError(409, 'Accept or decline your pending offers before enabling automatic acceptance.');
      this.preferences[me.id] = Boolean(body?.autoAcceptWork);
      return { autoAcceptWork: this.preferences[me.id] };
    }
    if (method === 'GET' && path === 'partner-offers') {
      return this.pendingOffersFor(me.id).map(item => ({ id: item.offerId, requestId: item.id, version: item.version, branchName: this.branchName(item.branchId), street: this.branch(item.branchId).street, postalCode: this.branch(item.branchId).postalCode, city: this.branch(item.branchId).city, fromDate: item.fromDate, toDate: item.toDate, exactStartLocalTime: item.exactStartLocalTime, plannedStartUtc: item.offerPlannedStartUtc, estimatedDurationMinutes: item.estimatedDurationMinutes, notes: item.notes, timeZoneId: TENANT_TIME_ZONE }));
    }
    let match = is(path, /^partner-offers\/([^/]+)\/accept$/);
    if (method === 'POST' && match) {
      const request = this.offerFor(match[1], me.id);
      if (request.status === 'Accepted' && request.acceptedEmployeeId === me.id && body?.version === request.version - 1) return this.dto(request, false);
      this.checkVersion(request, body?.version);
      if (request.status !== 'Offered') throw new ApiError(409, 'This offer is no longer awaiting acceptance.');
      this.ensureUnstarted(request);
      let start: string | null;
      if (request.offerPlannedStartUtc) {
        start = request.offerPlannedStartUtc;
        if (body?.plannedStartLocal && this.planStart(request, body.plannedStartLocal) !== start) throw new ApiError(400, 'Accept the planned time set by your manager.');
      } else start = body?.plannedStartLocal ? this.planStart(request, body.plannedStartLocal) : null;
      this.materialize(request, me.id, start);
      return this.dto(request, false);
    }
    match = is(path, /^partner-offers\/([^/]+)\/decline$/);
    if (method === 'POST' && match) {
      const request = this.offerFor(match[1], me.id);
      this.checkVersion(request, body?.version);
      if (request.status !== 'Offered') throw new ApiError(409, 'This offer is no longer awaiting acceptance.');
      if (body?.reason?.length > 1000) throw new ApiError(400, 'Use at most 1000 characters for the reason.');
      this.invalidateOffer(request); request.status = 'Requested'; request.version++;
      return null;
    }
    throw new ApiError(404, `No fake employee route for ${method} ${path}.`);
  }

  // ----- domain rules, mirroring PartnerService -----
  private offerRequest(request: RequestRecord, employee: Person | undefined, version: number, plannedStartLocal: string | null): RequestRecord {
    this.checkVersion(request, version);
    if (request.status === 'Cancelled') throw new ApiError(409, 'Cancelled requests cannot be offered.');
    this.ensureUnstarted(request);
    if (!employee || !employee.isActive || !['employee', 'manager', 'admin'].includes(employee.role)) throw new ApiError(400, 'Select an active cleaner, manager or administrator.');
    const automatic = this.effectiveAutoAccept(employee);
    let planned: string | null = null;
    if (plannedStartLocal) planned = this.planStart(request, plannedStartLocal);
    else if (request.fromDate === request.toDate && request.exactStartLocalTime) planned = this.planStart(request, `${request.fromDate}T${request.exactStartLocalTime}`);
    this.dropShift(request);
    request.acceptedEmployeeId = null; request.plannedStartUtc = null; request.plannedEndUtc = null;
    request.version++;
    request.offerId = `offer-${request.id}-${request.version}`;
    request.offeredEmployeeId = employee.id; request.offerPlannedStartUtc = planned; request.status = 'Offered';
    if (automatic) this.materialize(request, employee.id, planned);
    return request;
  }

  // Automatic acceptance applies only to employees who can sign in; a flexible visit (no start) covers its whole window.
  private effectiveAutoAccept(person: Person | undefined): boolean {
    // Server default: automatic unless the employee has chosen otherwise.
    return Boolean((person?.autoAcceptWork ?? true) && person?.keycloakUserId);
  }

  private materialize(request: RequestRecord, employeeId: string, startUtc: string | null) {
    const end = startUtc ? new Date(new Date(startUtc).getTime() + request.estimatedDurationMinutes * 60_000).toISOString() : null;
    request.linkedShiftId ??= this.nextId('shift');
    this.shiftActive[request.linkedShiftId] = true;
    request.acceptedEmployeeId = employeeId; request.plannedStartUtc = startUtc; request.plannedEndUtc = end;
    request.firstAcceptedAtUtc ??= '2026-11-03T10:00:00Z';
    request.status = 'Accepted'; request.version++;
  }

  private planStart(request: RequestRecord, local: string | null | undefined): string {
    if (!local || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(local)) throw new ApiError(400, 'Choose the cleaning start date and time in the tenant time zone.');
    const start = new Date(helsinkiLocalToUtc(local));
    const lower = new Date(helsinkiLocalToUtc(`${request.fromDate}T00:00`));
    const upper = new Date(helsinkiLocalToUtc(`${addDays(request.toDate, 1)}T00:00`));
    if (start < lower || new Date(start.getTime() + request.estimatedDurationMinutes * 60_000) > upper) throw new ApiError(400, 'The entire visit must fit within the requested dates.');
    return start.toISOString();
  }

  private applyWindow(request: RequestRecord, body: any) {
    const exact = Boolean(body?.exactStartLocalTime);
    if (!body?.fromDate || !body?.toDate || body.toDate < body.fromDate) throw new ApiError(400, 'Choose a supported inclusive date range of at most 367 days.');
    if (exact && (body.fromDate !== body.toDate || !/^\d{2}:\d{2}$/.test(body.exactStartLocalTime))) throw new ApiError(400, 'An exact appointment requires one date and a time in HH:mm format.');
    if (typeof body.notes === 'string' && body.notes.length > 4000) throw new ApiError(400, 'Use at most 4000 characters for cleaning instructions.');
    request.fromDate = body.fromDate; request.toDate = body.toDate;
    request.exactStartLocalTime = exact ? `${body.exactStartLocalTime}:00` : null;
    request.notes = body.notes?.trim() ? body.notes : null;
  }

  private cancel(request: RequestRecord, reason: string | null) {
    this.ensureUnstarted(request);
    this.dropShift(request);
    request.status = 'Cancelled'; request.cancellationReason = reason?.trim() ?? null;
    this.invalidateOffer(request); request.version++;
  }

  private invalidateOffer(request: RequestRecord) {
    request.offerId = null; request.offeredEmployeeId = null; request.offerPlannedStartUtc = null;
  }

  private dropShift(request: RequestRecord) {
    if (request.linkedShiftId) this.shiftActive[request.linkedShiftId] = false;
  }

  private ensureUnstarted(request: RequestRecord) {
    if (request.status === 'InProgress' || request.status === 'Completed') throw new ApiError(409, 'Work has started. Use a separate staff correction process; this visit cannot be reassigned, moved or cancelled.');
  }

  private checkVersion(request: RequestRecord, version: unknown) {
    if (request.version !== version) throw new ApiError(409, 'This request changed. Reload it before making another change.');
  }

  private offerFor(offerId: string, employeeId: string): RequestRecord {
    const request = this.requests.find(item => item.offerId === offerId && item.offeredEmployeeId === employeeId)
      ?? this.requests.find(item => item.offerId === offerId && item.acceptedEmployeeId === employeeId);
    if (!request) throw new ApiError(404, 'Cleaning offer was not found.');
    return request;
  }

  private pendingOffersFor(employeeId: string): RequestRecord[] {
    return this.requests.filter(item => item.status === 'Offered' && item.offeredEmployeeId === employeeId
      && this.companies.find(company => company.id === item.companyId)?.isPartnerEnabled);
  }

  private ownRequest(id: string, companyId: string): RequestRecord {
    const request = this.requests.find(item => item.id === id && item.companyId === companyId);
    if (!request) throw new ApiError(404, 'Cleaning request was not found.');
    return request;
  }

  private addChange(input: { kind: string; branchId: string | null; requestId: string | null; message: string }): ChangeRecord {
    const change: ChangeRecord = { id: this.nextId('change'), companyId: OWNER_COMPANY.id, branchId: input.branchId, requestId: input.requestId, kind: input.kind, message: input.message, resolvedUtc: null, resolution: null };
    this.changes.push(change);
    return change;
  }

  private employeeById(id: string): Person | undefined {
    return Object.values(this.people).find(person => person.id === id);
  }

  private branch(id: string): BranchRecord {
    return this.branches.find(item => item.id === id)!;
  }

  private branchName(id: string): string {
    return this.branch(id).name;
  }

  dto(request: RequestRecord, staff: boolean) {
    const cleaner = request.acceptedEmployeeId ? this.employeeById(request.acceptedEmployeeId) : undefined;
    return {
      id: request.id, version: request.version, companyId: request.companyId, branchId: request.branchId, branchName: this.branchName(request.branchId),
      fromDate: request.fromDate, toDate: request.toDate, exactStartLocalTime: request.exactStartLocalTime, notes: request.notes,
      priceNet: request.priceNet, vatRatePercent: request.vatRatePercent, estimatedDurationMinutes: request.estimatedDurationMinutes,
      status: request.status, firstAcceptedAtUtc: request.firstAcceptedAtUtc, linkedShiftId: request.linkedShiftId,
      plannedStartUtc: request.plannedStartUtc, plannedEndUtc: request.plannedEndUtc,
      acceptedCleanerName: cleaner ? `${cleaner.firstName} ${cleaner.lastName}` : null,
      actualStartUtc: null, actualEndUtc: null,
      ...(staff ? { offeredEmployeeId: request.offeredEmployeeId, offerId: request.offerId } : {}),
    };
  }
}

function is(path: string, re: RegExp) { return path.match(re); }

function addDays(isoDate: string, days: number): string {
  const date = new Date(`${isoDate}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

// Each browser context gets the same mock session setup the other specs use, with the given role.
export async function prepareRolePage(page: Page, backend: PartnerFlowBackend, actor: ActorKey) {
  await setupAdminMocks(page);
  await page.addInitScript(roles => { (window as any).__MOCK_ROLES__ = roles; }, ROLE_FOR_ACTOR[actor]);
  await backend.attach(page, actor);
}

// Opens a second, independent browser session (its own cookies, storage and role) for the other actors.
export async function openActor(browser: Browser, testInfo: TestInfo, backend: PartnerFlowBackend, actor: ActorKey) {
  const device = testInfo.project.name === 'mobile-touch' ? devices['Pixel 5'] : devices['Desktop Chrome'];
  const context = await browser.newContext({ ...device, baseURL: 'http://localhost:3000', serviceWorkers: 'block' });
  const page = await context.newPage();
  await prepareRolePage(page, backend, actor);
  return { page, context };
}
