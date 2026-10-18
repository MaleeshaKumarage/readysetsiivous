import { authorizedFetch, invalidateAdminCache } from './adminApi';

export type BillingAddress = { street: string; postalCode: string; city: string; country: string };
export type InvoiceIssuer = { companyName: string; businessId: string; companyAddress: BillingAddress; email: string; phone: string; bankAccountIBAN: string; bankBic: string; paymentTermsDays: number; defaultVatRatePercent: number; isSampleData?: boolean };
export type PricingPlan = { mode: 'Monthly' | 'Hourly' | 'PerOccurrence'; rateNet: number; vatRatePercent: number; effectiveFrom: string };
export type OccurrencePrice = { occurrenceStartUtc: string; rateNet: number; reason: string };
export type ShiftPricing = { plans: PricingPlan[]; overrides: OccurrencePrice[] };
export type MonthlyOverride = { shiftId: string; month: string; amountNet: number; reason: string };
export type ShiftInvoiceInput = { companyId: string; shiftIds: string[]; from: string; to: string; billingAddress: BillingAddress; language: string; monthlyOverrides: MonthlyOverride[] };
export type ShiftInvoiceLine = { lineKey: string; description: string; shiftId?: string; shiftName?: string; branchName?: string; hours?: number; occurrences?: number; pricingMode?: string; periodFrom?: string; periodTo?: string; month?: string; quantity: number; unitPriceNet: number; netAmount: number; vatRatePercent: number; vatAmount: number; total: number; overrideReason?: string };
export type ShiftInvoice = { id: string; invoiceNumber: string; customer: { name: string }; issuer: InvoiceIssuer; lines: ShiftInvoiceLine[]; total: { net: number; vat: number; gross: number }; issueDate: string; dueDate: string; status: string; companyId?: string; periodFrom?: string; periodTo?: string; language?: string; previewToken?: string; issuerIsSample?: boolean };

async function request<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const response = await authorizedFetch(path, { method, body: body === undefined ? undefined : JSON.stringify(body), cache: 'no-store' });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    const errors = problem.errors && typeof problem.errors === 'object' ? Object.values(problem.errors).flat().join(' ') : '';
    throw new Error(errors || problem.detail || problem.title || `Request failed (${response.status}). Please retry.`);
  }
  if (method !== 'GET') invalidateAdminCache();
  return response.status === 204 ? undefined as T : response.json();
}
const invoices = '/api/v1/admin/invoices';
export const invoiceBillingApi = {
  list: () => request<ShiftInvoice[]>(invoices),
  issuer: () => request<InvoiceIssuer>(`${invoices}/issuer`),
  saveIssuer: (issuer: InvoiceIssuer) => request<InvoiceIssuer>(`${invoices}/issuer`, 'PUT', issuer),
  preview: (input: ShiftInvoiceInput) => request<ShiftInvoice>(`${invoices}/shift-preview`, 'POST', input),
  issue: (input: ShiftInvoiceInput, previewToken: string) => request<ShiftInvoice>(`${invoices}/shifts`, 'POST', { ...input, previewToken }),
  markPaid: (id: string) => request<void>(`${invoices}/${id}/mark-paid`, 'POST', { paidAtUtc: new Date().toISOString() }),
  void: (id: string) => request<void>(`${invoices}/${id}/void`, 'POST'),
  pricing: (id: string) => request<ShiftPricing>(`/api/v1/admin/shifts/${id}/pricing`),
  savePricing: (id: string, plan: PricingPlan) => request<ShiftPricing>(`/api/v1/admin/shifts/${id}/pricing`, 'PUT', plan),
  saveOccurrencePrice: (id: string, value: OccurrencePrice) => request<ShiftPricing>(`/api/v1/admin/shifts/${id}/occurrence-price`, 'PUT', value),
  download: async (id: string) => {
    const response = await authorizedFetch(`${invoices}/${id}/pdf`, { cache: 'no-store' });
    if (!response.ok) throw new Error('Could not download invoice PDF. Please retry.');
    const url = URL.createObjectURL(await response.blob()); const a = document.createElement('a');
    a.href = url; a.download = `invoice-${id}.pdf`; a.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
  },
};
