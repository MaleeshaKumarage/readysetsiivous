import type { Page } from '@playwright/test';

// Deterministic synthetic tenant data, built in dependency order: companies -> branches -> shifts -> invoices.
// Every shift points at a branch of its own company, so the fixture cannot describe an impossible tenant.
export const SCALE = { companies: 60, branchesMin: 2, branchesMax: 4 } as const;

export type ScaleCompany = { id: string; name: string; businessId: string; isActive: boolean; isPartnerEnabled: boolean; contactName: string; contactEmail: string; contactPhone: string; notes: null };
export type ScaleBranch = { id: string; companyId: string; name: string; street: string; postalCode: string; city: string; isActive: boolean };
export type ScaleShift = { id: string; companyId: string; branchId: string; name: string; isActive: boolean; schedule: { type: number; weeklyDays: number[]; weeklyStart: string; weeklyEnd: string }; validFrom: string; validUntil: null; notes: null };
export type ScaleInvoice = { id: string; invoiceNumber: string; customer: { name: string }; issueDate: string; dueDate: string; total: { net: number; vat: number; gross: number }; status: 'Issued' | 'Paid' | 'Void'; issuerIsSample: boolean };

export type ScaleData = { companies: ScaleCompany[]; branches: ScaleBranch[]; shifts: ScaleShift[]; invoices: ScaleInvoice[] };

const pad = (n: number, width = 2) => String(n).padStart(width, '0');

// Gross is always computed from net and VAT, so the fixture's totals add up like real invoices.
const VAT_RATE = 0.255;
function totalFor(net: number): { net: number; vat: number; gross: number } {
  const vat = Number((net * VAT_RATE).toFixed(2));
  return { net, vat, gross: Number((net + vat).toFixed(2)) };
}

export function buildScaleData(): ScaleData {
  const companies: ScaleCompany[] = Array.from({ length: SCALE.companies }, (_, c) => ({
    id: `00000000-0000-4000-8000-${pad(c + 1, 12)}`, name: `Company ${pad(c + 1)} Ltd`, businessId: `${1000000 + c}-${c % 9}`,
    isActive: c % 5 !== 0, isPartnerEnabled: c % 4 === 0,
    contactName: `Contact ${c + 1}`, contactEmail: `contact${c + 1}@example.test`, contactPhone: '+358 00 000 0000', notes: null,
  }));
  const branches: ScaleBranch[] = [];
  for (const company of companies) {
    const count = SCALE.branchesMin + (Number(company.id.slice(-3)) % (SCALE.branchesMax - SCALE.branchesMin + 1));
    for (let b = 0; b < count; b++) {
      branches.push({ id: `${company.id}-br-${b + 1}`, companyId: company.id, name: `Branch ${b + 1}`, street: `Street ${b + 1}`, postalCode: `00${pad(b + 1, 2)}0`, city: 'Helsinki', isActive: true });
    }
  }
  const shifts: ScaleShift[] = branches.map((branch, index) => ({
    id: `${branch.id}-shift`, companyId: branch.companyId, branchId: branch.id, name: `${branch.name} cleaning`,
    isActive: index % 6 !== 0, schedule: { type: 2, weeklyDays: [1, 3, 5], weeklyStart: '08:00', weeklyEnd: '12:00' },
    validFrom: '2026-10-01', validUntil: null, notes: null,
  }));
  const statuses: ScaleInvoice['status'][] = ['Issued', 'Paid', 'Void'];
  const invoices: ScaleInvoice[] = companies.map((company, i) => ({
    id: `inv-${pad(i + 1, 3)}`, invoiceNumber: `2026-${pad(i + 1, 4)}`, customer: { name: company.name },
    issueDate: `2026-10-${pad((i % 28) + 1)}T00:00:00Z`, dueDate: `2026-11-${pad((i % 28) + 1)}T00:00:00Z`,
    total: totalFor(100 + i), status: statuses[i % 3], issuerIsSample: i % 7 === 0,
  }));
  return { companies, branches, shifts, invoices };
}

// Returns a list of integrity problems; an empty array means the fixture is consistent.
export function integrityProblems(data: ScaleData): string[] {
  const problems: string[] = [];
  const companyIds = new Set(data.companies.map(c => c.id));
  const branchById = new Map(data.branches.map(b => [b.id, b]));
  for (const branch of data.branches) if (!companyIds.has(branch.companyId)) problems.push(`branch ${branch.id} has no company`);
  for (const shift of data.shifts) {
    const branch = branchById.get(shift.branchId);
    if (!branch) problems.push(`shift ${shift.id} has no branch`);
    else if (branch.companyId !== shift.companyId) problems.push(`shift ${shift.id} points at a branch of another company`);
  }
  for (const invoice of data.invoices) if (!data.companies.some(c => c.name === invoice.customer.name)) problems.push(`invoice ${invoice.invoiceNumber} has no customer`);
  for (const invoice of data.invoices) if (Math.abs(invoice.total.net + invoice.total.vat - invoice.total.gross) > 0.005) problems.push(`invoice ${invoice.invoiceNumber} gross does not equal net plus VAT`);
  return problems;
}

const CORS = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*' };

// Serves the fixture through the admin API. Must be registered after setupAdminMocks so these routes take precedence.
export async function installScaleRoutes(page: Page, data: ScaleData) {
  const branchesOf = (companyId: string) => data.branches.filter(b => b.companyId === companyId);
  await page.route('**/api/v1/admin/**', route => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    const q = url.searchParams;
    const json = (body: unknown) => route.fulfill({ headers: CORS, json: body });
    if (path.endsWith('/legacy-repairs')) return json({ agreements: [] });
    if (path.endsWith('/assignment-counts')) return json(Object.fromEntries(data.shifts.map(shift => [shift.id, 1])));
    if (path.endsWith('/leads')) return json({ items: [], total: 0 });
    if (path.endsWith('/shifts/calendar')) return json({ timeZoneId: 'Europe/Helsinki', occurrences: [], people: [] });
    if (path.endsWith('/work-queue')) return json({ view: q.get('view') ?? 'needs-action', source: q.get('source') ?? 'all', items: [], total: 0, counts: { 'needs-action': 0, scheduled: 0, 'in-progress': 0, completed: 0, cancelled: 0 } });
    if (path.endsWith('/issuer')) return json({ companyName: 'Supplier', businessId: '123', companyAddress: { street: 'Street', postalCode: '00100', city: 'Helsinki', country: 'FI' }, email: 'a@b.fi', phone: '1', bankAccountIBAN: 'FI00', bankBic: 'BIC', paymentTermsDays: 14, defaultVatRatePercent: 25.5, isSampleData: false });
    if (path.endsWith('/invoices')) return json(data.invoices);
    if (path.endsWith('/companies/summary')) return json({ all: data.companies.length, active: data.companies.filter(c => c.isActive).length, inactive: data.companies.filter(c => !c.isActive).length });
    if (path.endsWith('/companies')) {
      const search = (q.get('search') ?? '').toLowerCase();
      const status = q.get('status') ?? 'all';
      const matched = data.companies.filter(c => (status === 'all' || (status === 'active' ? c.isActive : !c.isActive))
        && (!search || c.name.toLowerCase().includes(search) || c.businessId.includes(search)));
      const skip = Number(q.get('skip') ?? 0), take = Number(q.get('take') ?? 25);
      return json({ items: matched.slice(skip, skip + take), total: matched.length });
    }
    const companyMatch = /\/companies\/([^/]+)$/.exec(path);
    if (companyMatch) {
      const company = data.companies.find(c => c.id === companyMatch[1]);
      return company ? json({ company, branches: branchesOf(company.id) }) : route.fulfill({ status: 404, headers: CORS, json: { title: 'Not found' } });
    }
    if (path.endsWith('/shifts')) {
      const companyId = q.get('companyId'), branchId = q.get('branchId'), includeInactive = q.get('includeInactive') === 'true';
      return json(data.shifts.filter(s => (!companyId || s.companyId === companyId) && (!branchId || s.branchId === branchId) && (includeInactive || s.isActive)));
    }
    return json([]);
  });
}
