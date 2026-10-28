// Cleaning Suite API client. Public endpoints only — admin uses the token path.
// Static export must never call this at build time; fetch happens client-side.

export const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "https://api.readysetsiivous.fi";
export const TENANT_SLUG = process.env.NEXT_PUBLIC_TENANT_SLUG ?? "readysetsiivous";

export interface PublicService {
  id: string;
  slug: string;
  category: string;
  name: string;
  description: string;
  additionalInfo: string | null;
  icon: string;
  imageUrl: string;
  sortOrder: number;
  priceNet: number;
  vatRatePercent: number;
  currency: string;
  durationMinutes?: number;
  priceGross?: number;
}

export interface AvailabilitySlot {
  startTime: string;
  endTime: string;
}

export interface BookingRequest {
  localDate: string;
  startTime: string;
  serviceId: string;
  customerName: string;
  customerPhone: string;
  customerEmail: string;
  street: string;
  postalCode: string;
  city: string;
  notes?: string | null;
  expectedPriceGross?: number;
}

export interface BookingResult {
  bookingId: string;
  bookingNumber: string;
  customerReference: string;
  managementKey: string;
  priceGross?: number;
  currency?: string;
}

export type PublicFailure = 'validation' | 'conflict' | 'priceChanged' | 'rateLimit' | 'unavailable' | 'uncertain' | 'notFound';
export type PublicResult<T> = { value: T; error?: never } | { value?: never; error: PublicFailure; fields?: string[] };

export async function publicPost<T>(path: string, body: unknown): Promise<PublicResult<T>> {
  try {
    const response = await fetch(`${API_URL}/api/v1/public/${TENANT_SLUG}${path}`, {
      method: 'POST', cache: 'no-store', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body), signal: AbortSignal.timeout(10000),
    });
    if (response.ok) return { value: response.status === 204 ? undefined as T : await response.json() as T };
    const problem = await response.json().catch(() => ({}));
    const fields = problem.errors && typeof problem.errors === 'object' ? Object.keys(problem.errors) : [];
    return { error: response.status === 404 ? (path === '/bookings' ? 'priceChanged' : 'notFound') : response.status === 429 ? 'rateLimit'
      : response.status === 409 ? (/price/i.test(String(problem.detail ?? problem.message ?? '')) ? 'priceChanged' : 'conflict')
      : response.status === 400 ? 'validation' : path.endsWith('/lookup') ? 'unavailable' : 'uncertain', fields };
  } catch {
    // A response can be lost after a successful write. Never automatically retry.
    return { error: path.endsWith('/lookup') ? 'unavailable' : 'uncertain' };
  }
}

export interface ManagedBooking {
  bookingNumber: string; status: string; startLocalDate: string; startLocalTime: string;
  serviceNameFi: string; serviceNameEn?: string; serviceNameSv?: string;
  durationMinutes?: number; priceGross?: number; currency?: string;
}

async function getJson<T>(path: string, timeoutMs = 8000): Promise<T | null> {
  try {
    const response = await fetch(`${API_URL}${path}`, {
      headers: { Accept: "application/json" },
      signal: AbortSignal.timeout(timeoutMs),
    });
    if (!response.ok) return null;
    return (await response.json()) as T;
  } catch {
    return null;
  }
}

export async function fetchServices(lang: string): Promise<PublicService[] | null> {
  return getJson<PublicService[]>(
    `/api/v1/public/${TENANT_SLUG}/services?lang=${encodeURIComponent(lang)}`
  );
}

export async function fetchAvailability(
  localDate: string,
  serviceId: string
): Promise<AvailabilitySlot[] | null> {
  return getJson<AvailabilitySlot[]>(
    `/api/v1/public/${TENANT_SLUG}/availability?date=${encodeURIComponent(localDate)}&serviceId=${encodeURIComponent(serviceId)}`
  );
}

export async function createBooking(body: BookingRequest): Promise<BookingResult | null> {
  try {
    const response = await fetch(`${API_URL}/api/v1/public/${TENANT_SLUG}/bookings`, {
      method: "POST",
      cache: "no-store",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
      signal: AbortSignal.timeout(10000),
    });
    if (!response.ok) return null;
    return (await response.json()) as BookingResult;
  } catch {
    return null;
  }
}
